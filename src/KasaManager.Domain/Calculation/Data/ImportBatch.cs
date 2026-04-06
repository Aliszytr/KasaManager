using System;

namespace KasaManager.Domain.Calculation.Data;

/// <summary>
/// Excel/Source okunduğunda içeri alma oturumu için izleme nesnesi.
/// </summary>
public class ImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly TargetDate { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public string ImportedBy { get; set; } = string.Empty;
    public string ImportProfileVersion { get; set; } = string.Empty;
}
