#nullable enable
namespace KasaManager.Domain.Validation;

/// <summary>
/// Kullanıcı tarafından "Çözüldü / Tamamlandı" olarak işaretlenen uyarı kaydı.
/// DB'ye persist edilir, böylece aynı günde tekrar gösterilmez.
/// </summary>
public sealed class DismissedValidation
{
    public int Id { get; set; }

    /// <summary>Hangi güne ait uyarı</summary>
    public DateOnly RaporTarihi { get; set; }

    /// <summary>Hangi kasa tipi (Sabah/Aksam/Genel)</summary>
    public string KasaTuru { get; set; } = string.Empty;

    /// <summary>Dismiss edilen kuralın kodu (örn: MUTABAKAT_FARK_YUKSEK)</summary>
    public string RuleCode { get; set; } = string.Empty;

    /// <summary>Kim dismiss etti</summary>
    public string? DismissedBy { get; set; }

    /// <summary>Ne zaman dismiss edildi (UTC)</summary>
    public DateTime DismissedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Kullanıcı notu (opsiyonel)</summary>
    public string? Note { get; set; }
}
