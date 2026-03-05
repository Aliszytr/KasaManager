namespace KasaManager.Domain.Reports.Snapshots;

/// <summary>
/// Üst grid satırı snapshot kaydı.
/// KasaUstRapor'dan gelen tüm kolonlar "ColumnsJson" içinde saklanır.
/// </summary>
public sealed class KasaRaporSnapshotRow
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SnapshotId { get; set; }
    public KasaRaporSnapshot? Snapshot { get; set; }

    /// <summary>
    /// Grid satırında görünen veznedar adı.
    /// </summary>
    public string? Veznedar { get; set; }

    /// <summary>
    /// UI checkbox karşılığı.
    /// </summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// UI'da görünen/hesaplanan bakiye.
    /// </summary>
    public decimal? Bakiye { get; set; }

    /// <summary>
    /// KasaUstRapor'dan gelen tüm kolon değerleri.
    /// JSON objesi: { "canonicalName": "value", ... }
    /// Not: string olarak saklıyoruz (sayı/date vb. dönüşümleri UI/Service tarafında yapılır).
    /// </summary>
    public string ColumnsJson { get; set; } = "{}";

    /// <summary>
    /// Aynı satırın kullanıcıya gösterilen orijinal başlık bilgisi gerekiyorsa tutulabilir.
    /// JSON objesi: { "canonicalName": "originalHeader" }
    /// </summary>
    public string? HeadersJson { get; set; }

    /// <summary>
    /// KasaUstRapor özel satır bilgisi (örn: TOPLAMLAR satırı)
    /// </summary>
    public bool IsSummaryRow { get; set; }
}
