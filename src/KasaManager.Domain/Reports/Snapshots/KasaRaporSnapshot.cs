using KasaManager.Domain.Reports;

namespace KasaManager.Domain.Reports.Snapshots;

/// <summary>
/// R6: Kasa raporunun DB'ye yazılan tek kaydı "snapshot"tır.
/// Sorgulamada yeniden hesap yapılmaz; aynen geri okunur.
/// </summary>
public sealed class KasaRaporSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateOnly RaporTarihi { get; set; }

    public KasaRaporTuru RaporTuru { get; set; }

    /// <summary>
    /// UI/logic versiyonlaması için. (Örn: 1,2,3...)
    /// </summary>
    public int Version { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string? CreatedBy { get; set; }

    /// <summary>
    /// Üst grid'de seçilen kişilerin bakiyelerinden oluşan seçim toplamı.
    /// </summary>
    public decimal SelectionTotal { get; set; }

    /// <summary>
    /// Uyarılar/validasyon notları (JSON string). (Örn: tarih tutarsızlığı, atlanan gün uyarısı)
    /// Not: JsonDocument EF Core tarafında entity sanılabildiği için DB'de string saklıyoruz.
    /// </summary>
    public string? WarningsJson { get; set; }

    public ICollection<KasaRaporSnapshotRow> Rows { get; set; } = new List<KasaRaporSnapshotRow>();

    public KasaRaporSnapshotInputs? Inputs { get; set; }

    public KasaRaporSnapshotResults? Results { get; set; }
}
