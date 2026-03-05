namespace KasaManager.Domain.Reports.Snapshots;

/// <summary>
/// Alt bölüm textbox girdileri snapshot'ı.
/// Esnek tutmak için JSON ana-değer yapısı kullanılır.
/// </summary>
public sealed class KasaRaporSnapshotInputs
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SnapshotId { get; set; }
    public KasaRaporSnapshot? Snapshot { get; set; }

    /// <summary>
    /// Rapora aktarılmış + manuel girilmiş tüm inputlar.
    /// JSON objesi: { "field": "value", ... }
    /// Örn: { "TahsilatToplam": "1234,50", "KasayiYapan": "Ali" }
    /// </summary>
    public string ValuesJson { get; set; } = "{}";
}
