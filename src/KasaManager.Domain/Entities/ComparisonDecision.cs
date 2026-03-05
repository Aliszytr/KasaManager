#nullable enable
using KasaManager.Domain.Reports;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KasaManager.Domain.Entities;

/// <summary>
/// Karşılaştırma kısmi eşleşmeleri için kullanıcı kararı.
/// Onay → Eşleşenlere, Red → Eşleşmeyenlere taşınır.
/// </summary>
[Table("ComparisonDecisions")]
public class ComparisonDecision
{
    [Key]
    public int Id { get; set; }

    /// <summary>Karşılaştırma türü (TahsilatMasraf, HarcamaHarc, ReddiyatCikis)</summary>
    public ComparisonType ComparisonType { get; set; }

    // ── Kaydı benzersiz tanımlayan alanlar ──

    /// <summary>UYAP Dosya numarası (örn: "2026/36")</summary>
    [MaxLength(50)]
    public string OnlineDosyaNo { get; set; } = "";

    /// <summary>UYAP Yatırılan miktar</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal OnlineMiktar { get; set; }

    /// <summary>UYAP Birim adı (örn: "Ankara 22. İdare Mahkemesi")</summary>
    [MaxLength(300)]
    public string? OnlineBirimAdi { get; set; }

    // ── Eşleşen banka kaydı bilgileri ──

    /// <summary>Banka işlem tutarı</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? BankaTutar { get; set; }

    /// <summary>Banka açıklama (kısa)</summary>
    [MaxLength(500)]
    public string? BankaAciklamaSummary { get; set; }

    // ── Karar ──

    /// <summary>"approved" veya "rejected"</summary>
    [MaxLength(20)]
    public string Decision { get; set; } = "";

    /// <summary>Kararın verildiği zaman (UTC)</summary>
    public DateTime DecidedAtUtc { get; set; }

    /// <summary>Kararı veren kullanıcı adı</summary>
    [MaxLength(100)]
    public string? DecidedBy { get; set; }

    // ── Orijinal eşleşme bilgileri (görüntüleme için) ──

    /// <summary>Orijinal güven puanı (0.0 - 1.0)</summary>
    public double OriginalConfidence { get; set; }

    /// <summary>Orijinal eşleşme/eşleşmeme nedeni</summary>
    [MaxLength(500)]
    public string? OriginalMatchReason { get; set; }
}
