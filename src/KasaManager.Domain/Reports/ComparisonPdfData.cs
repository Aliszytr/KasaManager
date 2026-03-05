#nullable enable
namespace KasaManager.Domain.Reports;

/// <summary>
/// Karşılaştırma PDF çıktısı için veri modeli.
/// <see cref="ComparisonReport"/> verilerinden türetilir; kullanıcı kararları uygulandıktan sonra
/// olağan/olağandışı kategorilere ayrılmış halidir.
/// </summary>
public sealed class ComparisonPdfData
{
    // ─────────────────────────────────────────────────────────────
    // Başlık bilgileri
    // ─────────────────────────────────────────────────────────────

    public required ComparisonType Type { get; init; }
    public required DateTime GeneratedAt { get; init; }
    public DateOnly? ReportDate { get; init; }

    // ─────────────────────────────────────────────────────────────
    // Genel istatistikler
    // ─────────────────────────────────────────────────────────────

    public int TotalOnlineRecords { get; init; }
    public int TotalBankaAlacakRecords { get; init; }
    public int MatchedCount { get; init; }
    public int PartialMatchCount { get; init; }
    public int NotFoundCount { get; init; }
    public decimal TotalOnlineAmount { get; init; }
    public decimal TotalMatchedAmount { get; init; }
    public decimal UnmatchedAmount { get; init; }

    // ─────────────────────────────────────────────────────────────
    // Kullanıcı kararları sonrası istatistikler
    // ─────────────────────────────────────────────────────────────

    /// <summary>Kullanıcı tarafından onaylanan kısmi eşleşme sayısı</summary>
    public int ApprovedCount { get; init; }

    /// <summary>Kullanıcı tarafından reddedilen kısmi eşleşme sayısı</summary>
    public int RejectedCount { get; init; }

    // ─────────────────────────────────────────────────────────────
    // Fazla Gelenler (Bankaya Fazla Giren)
    // ─────────────────────────────────────────────────────────────

    /// <summary>EFT, Otomatik İade, Gelen Havale, Virman gibi olağan fazla kayıtlar</summary>
    public List<UnmatchedBankaRecord> OlaganFazlaGelenler { get; init; } = [];

    /// <summary>Bilinmeyen veya beklendik dışı fazla kayıtlar</summary>
    public List<UnmatchedBankaRecord> OlagandisiFazlaGelenler { get; init; } = [];

    // ─────────────────────────────────────────────────────────────
    // Gelmeyenler (Bankaya Gelmeyen)
    // ─────────────────────────────────────────────────────────────

    /// <summary>Online'da olup bankada karşılığı bulunamayan kayıtlar</summary>
    public List<MissingBankaRecord> Gelmeyenler { get; init; } = [];

    /// <summary>Kullanıcı tarafından reddedilerek gelmeyenler arasına eklenen eski kısmi eşleşmeler</summary>
    public List<ComparisonMatchResult> ReddedilenEslesmeler { get; init; } = [];

    // ─────────────────────────────────────────────────────────────
    // Tutar dengesi
    // ─────────────────────────────────────────────────────────────

    public decimal SurplusAmount { get; init; }
    public decimal MissingAmount { get; init; }
    public decimal NetAmountDifference { get; init; }
    public string? BalanceSummary { get; init; }

    // ─────────────────────────────────────────────────────────────
    // Reddiyat / Stopaj (Sadece ReddiyatCikis türü)
    // ─────────────────────────────────────────────────────────────

    public decimal TotalGelirVergisi { get; init; }
    public decimal TotalDamgaVergisi { get; init; }
    public decimal TotalStopaj { get; init; }
    public decimal TotalOdenecekMiktar { get; init; }
    public decimal TotalNetOdenecek { get; init; }
    public decimal TotalBankaCikis { get; init; }
    public string? StopajStatus { get; init; }
}
