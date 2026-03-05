#nullable enable
namespace KasaManager.Domain.Reports;

/// <summary>
/// Tek bir karşılaştırma eşleşme sonucunu temsil eder.
/// Online kayıt ile Banka kaydı arasındaki eşleşme bilgilerini içerir.
/// </summary>
public sealed class ComparisonMatchResult
{
    // ─────────────────────────────────────────────────────────────
    // Online kayıt bilgileri (onlineMasraf.xlsx / onlineHarc.xlsx)
    // ─────────────────────────────────────────────────────────────
    
    /// <summary>Online Excel'deki satır indeksi (0-based)</summary>
    public int OnlineRowIndex { get; init; }
    
    /// <summary>Dosya numarası (örn: "2026/36")</summary>
    public string? OnlineDosyaNo { get; init; }
    
    /// <summary>Birim adı (örn: "Ankara 22. İdare Mahkemesi")</summary>
    public string? OnlineBirimAdi { get; init; }
    
    /// <summary>Yatırılan miktar</summary>
    public decimal OnlineMiktar { get; init; }
    
    /// <summary>İşlem tarihi</summary>
    public DateTime? OnlineTarih { get; init; }
    
    // ─────────────────────────────────────────────────────────────
    // Banka kayıt bilgileri (BankaTahsilat.xlsx / BankaHarc.xlsx)
    // ─────────────────────────────────────────────────────────────
    
    /// <summary>Banka Excel'deki satır indeksi (eşleşme varsa)</summary>
    public int? BankaRowIndex { get; init; }
    
    /// <summary>Banka açıklama metni</summary>
    public string? BankaAciklama { get; init; }
    
    /// <summary>Banka işlem tutarı</summary>
    public decimal? BankaTutar { get; init; }
    
    /// <summary>Banka işlem tarihi</summary>
    public DateTime? BankaTarih { get; init; }
    
    /// <summary>Borç/Alacak durumu (+/-)</summary>
    public string? BankaBorcAlacak { get; init; }
    
    // ─────────────────────────────────────────────────────────────
    // Parse edilmiş banka bilgileri
    // ─────────────────────────────────────────────────────────────
    
    /// <summary>Açıklamadan çıkarılan il (örn: "Ankara")</summary>
    public string? ParsedIl { get; init; }
    
    /// <summary>Açıklamadan çıkarılan mahkeme (örn: "22. İdare Mahkemesi")</summary>
    public string? ParsedMahkeme { get; init; }
    
    /// <summary>Açıklamadan çıkarılan esas no (örn: "2026/36")</summary>
    public string? ParsedEsasNo { get; init; }
    
    /// <summary>Eşleşmede kullanılan anahtar kelime</summary>
    public string? ParsedKeyword { get; init; }
    
    // ─────────────────────────────────────────────────────────────
    // Eşleşme kalitesi
    // ─────────────────────────────────────────────────────────────
    
    /// <summary>Eşleşme durumu</summary>
    public MatchStatus Status { get; init; }
    
    /// <summary>Güven puanı (0.0 - 1.0 arası)</summary>
    public double ConfidenceScore { get; init; }
    
    /// <summary>Eşleşme veya eşleşmeme nedeni açıklaması</summary>
    public string? MatchReason { get; init; }
}
