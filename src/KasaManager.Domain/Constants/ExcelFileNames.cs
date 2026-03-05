#nullable enable
namespace KasaManager.Domain.Constants;

/// <summary>
/// Projenin kullandığı Excel dosya adlarını merkezi olarak tanımlar.
/// Magic string kullanımını ortadan kaldırarak typo riskini sıfırlar
/// ve dosya adı değişikliklerini tek bir noktadan yönetmeyi sağlar.
/// </summary>
public static class ExcelFileNames
{
    // ── Ana Kasa Rapor Dosyaları ──
    public const string KasaUstRapor = "KasaUstRapor.xlsx";
    public const string BankaTahsilat = "BankaTahsilat.xlsx";
    public const string BankaHarc = "BankaHarc.xlsx";
    public const string MasrafVeReddiyat = "MasrafveReddiyat.xlsx";

    // ── Online Dosyalar ──
    public const string OnlineMasraf = "onlineMasraf.xlsx";
    public const string OnlineHarc = "onlineHarc.xlsx";
    public const string OnlineReddiyat = "OnlineReddiyat.xlsx";

    // ── Karşılaştırma Modülü Dosyaları ──
    /// <summary>
    /// Karşılaştırma modülünün kullandığı tüm dosya adları.
    /// ComparisonArchiveService arşivleme ve yükleme işlemlerinde kullanır.
    /// </summary>
    public static readonly string[] ComparisonFiles =
    {
        BankaTahsilat,
        BankaHarc,
        OnlineMasraf,
        OnlineHarc,
        OnlineReddiyat
    };
}
