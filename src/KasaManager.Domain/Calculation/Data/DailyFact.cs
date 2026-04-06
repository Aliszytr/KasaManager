using System;

namespace KasaManager.Domain.Calculation.Data;

/// <summary>
/// Normalize edilmiş günlük doğrulanmış faktörler (tekil veri kalemleri).
/// Veri Okuma ve Formül katmanı arasında yeni Truth kaynak.
/// </summary>
public class DailyFact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly ForDate { get; set; }
    public Guid ImportBatchId { get; set; }
    
    // Yabancı anahtar (Foreign Key) navigasyon özelliği istenirse eklenebilir.
    // public ImportBatch ImportBatch { get; set; }
    
    public string CanonicalKey { get; set; } = string.Empty; // Örn: "sabah_kredi_karti_tahsilat"
    
    // Değer ve Metin İçerikleri
    public string? RawValue { get; set; }
    public decimal? NumericValue { get; set; }
    public string? TextValue { get; set; }
    public DateTime? DateValue { get; set; }
    
    // Traceability ve Audit
    public string SourceFileName { get; set; } = string.Empty;
    public int? SourceRowNo { get; set; }
    public int? SourceColumnNo { get; set; }
    public decimal Confidence { get; set; } = 1.0m; // 0.0 - 1.0 arası eşleşme güveni
}
