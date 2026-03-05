namespace KasaManager.Domain.Reports.Snapshots;

/// <summary>
/// "Hesapla" sonrası türetilmiş sonuç alanları snapshot'ı.
/// Sorgulamada yeniden hesap yapılmaz.
/// </summary>
public sealed class KasaRaporSnapshotResults
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SnapshotId { get; set; }
    public KasaRaporSnapshot? Snapshot { get; set; }

    /// <summary>
    /// Hesapla sonucunda oluşan tüm sonuçlar.
    /// JSON objesi: { "field": "value", ... }
    /// Örn: { "ToplamTahsilat": "...", "Fark": "..." }
    /// </summary>
    public string ValuesJson { get; set; } = "{}";
}
