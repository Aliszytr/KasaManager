#nullable enable
namespace KasaManager.Domain.Projection;

/// <summary>
/// P2: EksikFazla Projection Engine — günlük veri kaynağı bilgisi.
/// </summary>
public enum ProjectionDaySource
{
    /// <summary>Kaydedilmiş hesaplanmış snapshot'tan geldi</summary>
    CalculatedSnapshot,

    /// <summary>Canlı pool + FormulaEngine ile hesaplandı</summary>
    LiveCalculation,

    /// <summary>Snapshot + Excel birleşimi — tam veri</summary>
    ExcelAndSnapshot,

    /// <summary>Veri bulunamadı — tüm değerler sıfır</summary>
    NoData,

    /// <summary>Mock / Test verisi</summary>
    TestData
}

/// <summary>
/// P2: Bir gün için projection engine'in ihtiyaç duyduğu minimum veri seti.
/// </summary>
public sealed record ProjectionDayInput(
    DateOnly Date,

    // Banka verileri
    decimal BankaGirenTahsilat,
    decimal BankaGirenHarc,

    // UstRapor verileri
    decimal ToplamTahsilat,
    decimal OnlineTahsilat,
    decimal ToplamHarc,

    // Online verileri
    decimal OnlineReddiyat,
    decimal OnlineHarc,

    // Hesaplanan ara değerler (FormulaEngine / Legacy output)
    decimal BankayaYatirilacakNakit,
    decimal NormalHarc,

    // Kaynak bilgisi
    ProjectionDaySource Source
);

/// <summary>
/// P2: Zincirdeki her günün detayı — tam audit trail.
/// </summary>
public sealed record ProjectionDayNode(
    DateOnly Date,
    int Depth,
    ProjectionDaySource Source,

    // Raw hesaplanan değerler (düzeltme öncesi)
    decimal RawGuneTahsilat,
    decimal RawGuneHarc,

    // Carry-forward sonrası
    decimal OncekiTahsilat,
    decimal DundenTahsilat,
    decimal OncekiHarc,
    decimal DundenHarc,

    // Bu gün için veri var mıydı
    bool HasData
);

/// <summary>
/// P2.1: Günlük veri sağlayıcı delegate.
/// KasaDraftService'in private Excel reader'larını engine'e geçirmek için.
/// </summary>
public delegate Task<ProjectionDayInput?> DayInputProvider(DateOnly date, CancellationToken ct);

/// <summary>
/// P2: Projection engine istek modeli.
/// </summary>
public sealed record ProjectionRequest(
    DateOnly TargetDate,
    string UploadFolderAbsolute,
    int MaxLookbackDays = 14,

    /// <summary>
    /// P2.1: Opsiyonel dış veri sağlayıcı.
    /// Set edilmişse engine bu delegate'i kullanır (Excel + snapshot hybrid).
    /// Set edilmemişse engine kendi snapshot-only resolver'ını kullanır.
    /// </summary>
    DayInputProvider? InputProvider = null
);

/// <summary>
/// P2: Projection engine sonuç modeli.
/// Legacy EksikFazlaChain ile 1:1 karşılaştırılabilir çıktılar üretir.
/// </summary>
public sealed record ProjectionResult(
    DateOnly TargetDate,
    bool Ok,

    // Legacy parity çıktıları
    decimal OncekiTahsilat,
    decimal DundenTahsilat,
    decimal GuneTahsilat,
    decimal OncekiHarc,
    decimal DundenHarc,
    decimal GuneHarc,

    // HesapKontrol düzeltmeleri uygulandı mı
    bool HesapKontrolApplied,

    // Audit trail
    List<ProjectionDayNode> Chain,
    string? ErrorMessage = null
);
