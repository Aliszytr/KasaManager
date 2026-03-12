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

    // ─── Async API (interface uyumu) ───

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        Set(key, value, ttl);
        return Task.CompletedTask;
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        => Task.FromResult(Get<T>(key));

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task EvictExpiredAsync(CancellationToken ct = default)
    {
        EvictExpired();
        return Task.CompletedTask;
    }

    // ─── OB-1 FIX: Native sync API ───
    // ConcurrentDictionary zaten senkron — gereksiz async wrapping yok.

    public T? Get<T>(string key)
    {
        if (_store.TryGetValue(key, out var entry) && !entry.IsExpired)
            return (T?)entry.Value;

        if (entry != null && entry.IsExpired)
            _store.TryRemove(key, out _);

        return default;
    }

    public void Set<T>(string key, T value, TimeSpan ttl)
    {
        _store[key] = new CacheEntry(value, DateTime.UtcNow, ttl);
    }

    public void EvictExpired()
    {
        if (DateTime.UtcNow - _lastEviction < EvictionInterval)
            return;

        _lastEviction = DateTime.UtcNow;

        var keysToRemove = _store
            .Where(kvp => kvp.Value.IsExpired)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
            _store.TryRemove(key, out _);
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
