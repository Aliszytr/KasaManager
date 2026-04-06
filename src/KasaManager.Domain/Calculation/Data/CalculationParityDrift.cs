using System;

namespace KasaManager.Domain.Calculation.Data;

public enum DriftSeverity
{
    ExactMatch = 0,
    MinorDrift = 1,
    MajorDrift = 2
}

public enum DriftResolutionStatus
{
    Open = 0,
    Investigating = 1,
    Resolved = 2,
    AcceptedDifference = 3,
    FalsePositive = 4
}

public class CalculationParityDrift
{
    public Guid Id { get; set; }
    public DateOnly TargetDate { get; set; }
    public string KasaScope { get; set; } = string.Empty;
    public string FieldKey { get; set; } = string.Empty;
    
    public decimal? LegacyValue { get; set; }
    public decimal? DataFirstValue { get; set; }
    public decimal? AbsoluteDifference { get; set; }
    
    public DriftSeverity Severity { get; set; }
    public string? ReasonHint { get; set; } // Parity specific sub-category
    public string? RootCauseCategory { get; set; }
    public DateTime DetectedAt { get; set; }
    
    // FAZ 4: Drift Resolution Alanları
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DriftResolutionStatus ResolutionStatus { get; set; } = DriftResolutionStatus.Open;
    public string? ResolutionNote { get; set; }
}
