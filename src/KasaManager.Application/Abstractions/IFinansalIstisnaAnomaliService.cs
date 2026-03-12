#nullable enable
using KasaManager.Domain.FinancialExceptions;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// Faz 3: Anomaly Suggestion sistemi.
/// Operatöre istisna önerisi sunar — otomatik kayıt oluşturmaz.
/// ARCHITECTURE LOCK: Yalnızca öneri üretir, otomatik istisna oluşturma YASAKTIR.
/// </summary>
public interface IFinansalIstisnaAnomaliService
{
    /// <summary>
    /// Verilen tarih için anomali önerilerini üretir.
    /// Her öneri operatörün karar vermesi gereken bir durumu temsil eder.
    /// </summary>
    Task<IReadOnlyList<AnomaliOnerisi>> AnalyzeAsync(DateOnly tarih, CancellationToken ct = default);
}

/// <summary>
/// Bir anomali önerisi — operatöre sunulan, kesinleşmemiş tespit.
/// </summary>
public sealed record AnomaliOnerisi
{
    /// <summary>Kısa başlık.</summary>
    public required string Baslik { get; init; }

    /// <summary>Detaylı açıklama.</summary>
    public required string Aciklama { get; init; }

    /// <summary>Önerilen istisna türü.</summary>
    public IstisnaTuru OnerilenTur { get; init; }

    /// <summary>Önerilen hesap türü.</summary>
    public Domain.Reports.HesapKontrol.BankaHesapTuru OnerilenHesapTuru { get; init; }

    /// <summary>Önerilen tutar.</summary>
    public decimal OnerilenTutar { get; init; }

    /// <summary>Güven seviyesi (0.0 - 1.0).</summary>
    public double GuvenSeviyesi { get; init; }

    /// <summary>Anomali kaynağı (HesapKontrol, BankaKarşılaştırma, vb.).</summary>
    public string Kaynak { get; init; } = "Sistem";

    /// <summary>Kaynak HesapKontrol kaydının ID'si (varsa). UI'da doğrudan işlem yapabilmek için.</summary>
    public Guid? KaynakKayitId { get; init; }
}
