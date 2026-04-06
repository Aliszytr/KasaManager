using System;

namespace KasaManager.Domain.Calculation.Data;

/// <summary>
/// Bir hesablama sonucunun zaman içindeki değişmez (immutable) versiyon geçmişidir.
/// Her kesinleştirmede (Save) yeni bir History kaydı atılır.
/// </summary>
public class DailyCalculationHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DailyCalculationResultId { get; set; }
    public DateOnly ForDate { get; set; }
    public string KasaTuru { get; set; } = string.Empty;
    
    public int VersionNumber { get; set; }
    
    // DailyCalculationResult snapshot data clonlanmış alanlar
    public string ResultsJson { get; set; } = "{}";
    public string InputsFingerprint { get; set; } = string.Empty;
    public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;
    public string ArchivedBy { get; set; } = string.Empty;
}
