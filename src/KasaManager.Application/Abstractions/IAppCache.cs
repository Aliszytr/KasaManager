namespace KasaManager.Application.Abstractions;

/// <summary>
/// MS4: Uygulama cache soyutlama katmanı.
/// Config ile provider seçilebilir: Memory (default) | Redis | Db.
/// </summary>
public interface IAppCache
{
    /// <summary>Bir değeri cache'e yazar.</summary>
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Cache'ten bir değeri okur. Bulunamazsa default döner.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>Cache'ten bir anahtarı siler.</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>Süresi dolmuş kayıtları temizler.</summary>
    Task EvictExpiredAsync(CancellationToken ct = default);

    // ─── OB-1 FIX: Senkron overload'lar ───
    // Sync caller'lar (IImportOrchestrator) deadlock riski olmadan kullanır.
    // Default impl: async çağrısı yaparak geriye uyumluluk sağlar.
    // MemoryAppCache gibi native sync provider'lar override eder.

    /// <summary>Senkron cache okuma. Native sync provider varsa override edin.</summary>
    T? Get<T>(string key) => GetAsync<T>(key).GetAwaiter().GetResult();

    /// <summary>Senkron cache yazma. Native sync provider varsa override edin.</summary>
    void Set<T>(string key, T value, TimeSpan ttl) => SetAsync(key, value, ttl).GetAwaiter().GetResult();

    /// <summary>Senkron eviction. Native sync provider varsa override edin.</summary>
    void EvictExpired() => EvictExpiredAsync().GetAwaiter().GetResult();
}
