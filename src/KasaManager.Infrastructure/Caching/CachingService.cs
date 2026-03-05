#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace KasaManager.Infrastructure.Caching;

/// <summary>
/// R19: Uygulama içi caching servisi için interface.
/// FieldCatalog, GlobalDefaults gibi sık kullanılan verilerin cache'lenmesini sağlar.
/// </summary>
public interface ICachingService
{
    /// <summary>
    /// Cache'den değer alır veya yoksa factory fonksiyonunu çalıştırıp sonucu cache'ler.
    /// </summary>
    T GetOrCreate<T>(string key, Func<T> factory, TimeSpan? expiration = null);
    
    /// <summary>
    /// Async versiyon - Cache'den değer alır veya yoksa factory fonksiyonunu çalıştırıp sonucu cache'ler.
    /// </summary>
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken ct = default);
    
    /// <summary>
    /// Belirtilen anahtarı cache'den siler.
    /// </summary>
    void Invalidate(string key);
    
    /// <summary>
    /// Belirtilen prefix ile başlayan tüm anahtarları cache'den siler.
    /// </summary>
    void InvalidatePrefix(string prefix);
    
    /// <summary>
    /// Tüm cache'i temizler.
    /// </summary>
    void Clear();
}

/// <summary>
/// R19: InMemory cache implementasyonu.
/// IMemoryCache üzerine kurulu, thread-safe, performans odaklı.
/// </summary>
public sealed class InMemoryCachingService : ICachingService, IDisposable
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, byte> _keys;
    private readonly object _lock = new();
    
    // Varsayılan cache süresi
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(5);
    
    // Uzun süreli cache (FieldCatalog gibi değişmeyen veriler için)
    public static readonly TimeSpan LongExpiration = TimeSpan.FromHours(1);
    
    // Kısa süreli cache (sık değişen veriler için)
    public static readonly TimeSpan ShortExpiration = TimeSpan.FromMinutes(1);

    public InMemoryCachingService(IMemoryCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _keys = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
    }
    
    public T GetOrCreate<T>(string key, Func<T> factory, TimeSpan? expiration = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentNullException(nameof(key));
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));
        
        return _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = expiration ?? DefaultExpiration;
            entry.RegisterPostEvictionCallback((evictedKey, _, _, _) =>
            {
                _keys.TryRemove(evictedKey.ToString()!, out _);
            });
            
            _keys.TryAdd(key, 0);
            return factory();
        })!;
    }
    
    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentNullException(nameof(key));
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));
        
        // Önce cache'den kontrol et (sync)
        if (_cache.TryGetValue(key, out T? cachedValue) && cachedValue != null)
        {
            return cachedValue;
        }
        
        // Cache'de yoksa factory çalıştır
        var value = await factory().ConfigureAwait(false);
        
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(expiration ?? DefaultExpiration)
            .RegisterPostEvictionCallback((evictedKey, _, _, _) =>
            {
                _keys.TryRemove(evictedKey.ToString()!, out _);
            });
        
        _cache.Set(key, value, options);
        _keys.TryAdd(key, 0);
        
        return value;
    }
    
    public void Invalidate(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;
            
        _cache.Remove(key);
        _keys.TryRemove(key, out _);
    }
    
    public void InvalidatePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return;
        
        var keysToRemove = _keys.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        
        foreach (var key in keysToRemove)
        {
            _cache.Remove(key);
            _keys.TryRemove(key, out _);
        }
    }
    
    public void Clear()
    {
        foreach (var key in _keys.Keys.ToList())
        {
            _cache.Remove(key);
        }
        _keys.Clear();
    }
    
    public void Dispose()
    {
        // IMemoryCache'in dispose'unu çağırmıyoruz çünkü DI container yönetiyor
        _keys.Clear();
    }
}

/// <summary>
/// R19: Cache key sabitleri.
/// Tutarlı key kullanımı için merkezi tanımlar.
/// </summary>
public static class CacheKeys
{
    // FieldCatalog
    public const string FieldCatalogAll = "FieldCatalog:All";
    public const string FieldCatalogByCategory = "FieldCatalog:ByCategory";
    public const string FieldCatalogBySource = "FieldCatalog:BySource";
    
    // GlobalDefaults
    public const string GlobalDefaults = "GlobalDefaults:Current";
    
    // Excel dosyaları (tarih bazlı)
    public static string ExcelData(string fileName, DateOnly date) 
        => $"Excel:{fileName}:{date:yyyyMMdd}";
    
    // Snapshot (tarih ve tür bazlı)
    public static string Snapshot(DateOnly date, string kasaTuru) 
        => $"Snapshot:{date:yyyyMMdd}:{kasaTuru}";
    
    // UnifiedPool (tarih bazlı)
    public static string UnifiedPool(DateOnly date) 
        => $"UnifiedPool:{date:yyyyMMdd}";
}
