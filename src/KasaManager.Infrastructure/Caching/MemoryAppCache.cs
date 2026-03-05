using System.Collections.Concurrent;
using KasaManager.Application.Abstractions;

namespace KasaManager.Infrastructure.Caching;

/// <summary>
/// MS4: In-memory cache implementasyonu (varsayılan).
/// ConcurrentDictionary ile thread-safe.
/// Uygulama restart'ında cache kaybolur — bu beklenen davranıştır.
/// </summary>
public sealed class MemoryAppCache : IAppCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _store = new();
    private DateTime _lastEviction = DateTime.UtcNow;
    private static readonly TimeSpan EvictionInterval = TimeSpan.FromMinutes(15);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        _store[key] = new CacheEntry(value, DateTime.UtcNow, ttl);
        return Task.CompletedTask;
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (_store.TryGetValue(key, out var entry) && !entry.IsExpired)
            return Task.FromResult((T?)entry.Value);

        if (entry != null && entry.IsExpired)
            _store.TryRemove(key, out _);

        return Task.FromResult(default(T?));
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task EvictExpiredAsync(CancellationToken ct = default)
    {
        if (DateTime.UtcNow - _lastEviction < EvictionInterval)
            return Task.CompletedTask;

        _lastEviction = DateTime.UtcNow;

        var keysToRemove = _store
            .Where(kvp => kvp.Value.IsExpired)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
            _store.TryRemove(key, out _);

        return Task.CompletedTask;
    }

    private sealed class CacheEntry
    {
        public object? Value { get; }
        public DateTime CachedAtUtc { get; }
        public TimeSpan Ttl { get; }
        public bool IsExpired => DateTime.UtcNow - CachedAtUtc > Ttl;

        public CacheEntry(object? value, DateTime cachedAtUtc, TimeSpan ttl)
        {
            Value = value;
            CachedAtUtc = cachedAtUtc;
            Ttl = ttl;
        }
    }
}
