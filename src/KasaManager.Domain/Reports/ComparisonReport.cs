#nullable enable
namespace KasaManager.Domain.Reports;

/// <summary>
/// Karşılaştırma raporu.
/// Tüm eşleşme sonuçlarını ve istatistikleri içerir.
/// </summary>
public sealed class ComparisonReport
{
    /// <summary>Karşılaştırma türü (Tahsilat-Masraf veya Harcama-Harc)</summary>
    public required ComparisonType Type { get; init; }
    
    /// <summary>Raporun oluşturulma zamanı</summary>
    public required DateTime GeneratedAt { get; init; }
    
    /// <summary>Filtrelenen rapor tarihi (null ise tüm tarihler)</summary>
    public DateOnly? ReportDate { get; init; }
    
    // ─────────────────────────────────────────────────────────────
    // İstatistikler
    // ─────────────────────────────────────────────────────────────
    
    /// <summary>Online dosyasındaki toplam kayıt sayısı</summary>
    public int TotalOnlineRecords { get; init; }
    
    /// <summary>Banka dosyasındaki toplam kayıt sayısı</summary>
    public int TotalBankaRecords { get; init; }
    
    /// <summary>Banka dosyasındaki Alacak (+) kayıt sayısı</summary>
    public int TotalBankaAlacakRecords { get; init; }
    
    /// <summary>Tam eşleşme bulunan kayıt sayısı</summary>
    public int MatchedCount { get; init; }
    
    /// <summary>Kısmi eşleşme bulunan kayıt sayısı</summary>
    public int PartialMatchCount { get; init; }
    
    /// <summary>Eşleşme bulunamayan kayıt sayısı</summary>
    public int NotFoundCount { get; init; }
    
    /// <summary>Online dosyasındaki toplam tutar</summary>
    public decimal TotalOnlineAmount { get; init; }
    
    /// <summary>Eşleşen kayıtların toplam tutarı</summary>
    public decimal TotalMatchedAmount { get; init; }
    
    /// <summary>Eşleşmeyen kayıtların toplam tutarı</summary>
    public decimal UnmatchedAmount { get; init; }
    
    // ─────────────────────────────────────────────────────────────
    // Fazla/Eksik Kayıt İstatistikleri
    // ─────────────────────────────────────────────────────────────
    
    /// <summary>Banka dosyasında fazla giren kayıt sayısı (online karşılığı yok)</summary>
    public int SurplusBankaCount { get; init; }
    
    /// <summary>Fazla giren kayıtların toplam tutarı</summary>
    public decimal SurplusAmount { get; init; }
    
    /// <summary>Online'da olup bankada karşılığı olmayan kayıt sayısı</summary>
    public int MissingBankaCount { get; init; }
    
    /// <summary>Bankada karşılığı olmayan kayıtların toplam tutarı</summary>
    public decimal MissingAmount { get; init; }
    
    /// <summary>
    /// Net tutar farkı: (Banka Toplam) - (Online Toplam)
    /// Pozitif: Bankada fazla para var
    /// Negatif: Bankada eksik para var
    /// </summary>
    public decimal NetAmountDifference { get; init; }
    
    /// <summary>Tutar dengesi durumu özet mesajı</summary>
    public string? BalanceSummary { get; init; }
    
    // ─────────────────────────────────────────────────────────────
    // Reddiyat / Stopaj Özellikleri (Sadece ReddiyatCikis türü için)
    // ─────────────────────────────────────────────────────────────
    
    /// <summary>Toplam Gelir Vergisi (onlineReddiyat)</summary>
    public decimal TotalGelirVergisi { get; init; }
    
    /// <summary>Toplam Damga Vergisi (onlineReddiyat)</summary>
    public decimal TotalDamgaVergisi { get; init; }
    
    /// <summary>Toplam Stopaj = Gelir Vergisi + Damga Vergisi</summary>
    public decimal TotalStopaj { get; init; }
    
    /// <summary>Toplam Ödenecek Miktar (Brüt = Stopaj dahil)</summary>
    public decimal TotalOdenecekMiktar { get; init; }
    
    /// <summary>Toplam Net Ödenecek (Stopaj kesildikten sonra)</summary>
    public decimal TotalNetOdenecek { get; init; }
    
    /// <summary>Banka çıkış toplamı (negatif değerlerin mutlak değeri)</summary>
    public decimal TotalBankaCikis { get; init; }
    
    /// <summary>
    /// Stopaj durumu mesajı:
    /// - Virman yapılmadan önce: Fark ≈ Stopaj (normal)
    /// - Virman yapıldıktan sonra: Fark ≈ 0 (normal)
    /// - Diğer durumlar: Eksik/Fazla çıkış uyarısı
    /// </summary>
    public string? StopajStatus { get; init; }
    
    // ─────────────────────────────────────────────────────────────
    // Detaylı sonuçlar
    // ─────────────────────────────────────────────────────────────
    
    /// <summary>Tüm eşleşme sonuçları</summary>
    public List<ComparisonMatchResult> Results { get; init; } = new();
    
    /// <summary>Banka dosyasında fazla giren kayıtlar (online karşılığı yok)</summary>
    public List<UnmatchedBankaRecord> SurplusBankaRecords { get; init; } = new();
    
    /// <summary>Online'da olup bankada karşılığı olmayan kayıtlar</summary>
    public List<MissingBankaRecord> MissingBankaRecords { get; init; } = new();
    
    /// <summary>İşlem sırasında oluşan uyarılar/hatalar</summary>
    public List<string> Issues { get; init; } = new();
}
