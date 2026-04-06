using System;

namespace KasaManager.Domain.Calculation.Data;

public class DataFirstTrustSnapshot
{
    public Guid Id { get; set; }
    public DateOnly TargetDate { get; set; }
    public string KasaType { get; set; } = string.Empty;
    
    public int TotalCount { get; set; }
    public int ExactMatchCount { get; set; }
    public int DriftCount { get; set; }
    public int StaleCount { get; set; }
    
    public decimal ConfidenceScore { get; set; } // 0-100
    public TrustLevel TrustLevel { get; set; }
    public DateTime CalculatedAt { get; set; }
}
