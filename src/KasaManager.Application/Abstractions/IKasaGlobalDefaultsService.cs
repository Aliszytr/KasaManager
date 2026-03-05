using KasaManager.Domain.Settings;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// R9/R9.1/R10.4: Global kasa varsayılanları.
/// - Vergi Kasa checkbox seçimleri
/// - Varsayılan Nakit / Bozuk para
/// - (R10) Kasada Eksik/Fazla (varsayılan)
/// - (R10.4) Genel Kasa Devreden (Seed)
/// </summary>
public interface IKasaGlobalDefaultsService
{
    Task<KasaGlobalDefaultsSettings> GetAsync(CancellationToken ct);

    /// <summary>
    /// Geriye dönük uyumluluk için alias.
    /// Not: GetAsync zaten "yoksa oluştur" mantığıyla tek satır (Id=1) üretir.
    /// </summary>
    Task<KasaGlobalDefaultsSettings> GetOrCreateAsync(CancellationToken ct);

    Task SaveVergiKasaSelectedAsync(IReadOnlyCollection<string> selectedVeznedarlar, string? updatedBy, CancellationToken ct);

    /// <summary>
    /// Global varsayılan parasal değerleri kaydeder.
    /// Not: Bu değerler tarih bazlı değildir; değiştiği anda gelecek tüm kasalara uygulanır.
    /// </summary>
    Task SaveDefaultCashAsync(
        decimal? defaultNakitPara,
        decimal? defaultBozukPara,
        decimal? defaultKasaEksikFazla,
        decimal? defaultGenelKasaDevredenSeed,
        DateTime? defaultGenelKasaBaslangicTarihiSeed,
        decimal? defaultKaydenTahsilat,
        decimal? defaultDundenDevredenKasaNakit,
        string? updatedBy,
        CancellationToken ct);

    /// <summary>
    /// Banka hesap IBAN bilgilerini kaydeder.
    /// IBAN'lar otomatik normalize edilir (boşluk/tire silinir, UPPER).
    /// </summary>
    Task SaveIbanSettingsAsync(
        string? hesapAdiStopaj, string? ibanStopaj,
        string? hesapAdiMasraf, string? ibanMasraf,
        string? hesapAdiHarc, string? ibanHarc,
        string? ibanPostaPulu,
        string? updatedBy,
        CancellationToken ct);

    /// <summary>
    /// Vergide Biriken seed değerini kaydeder.
    /// Genel Kasa'dan aktarma veya Sabah/Akşam kasa kaydetme sonrası carry-forward için kullanılır.
    /// </summary>
    Task SaveVergideBirikenSeedAsync(decimal seed, string? updatedBy, CancellationToken ct);
}
