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
}
