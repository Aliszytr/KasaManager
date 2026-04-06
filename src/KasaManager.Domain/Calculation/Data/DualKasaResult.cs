using System;

namespace KasaManager.Domain.Calculation.Data;

public class DualKasaResult
{
    public DateOnly TargetDate { get; set; }
    public string KasaType { get; set; } = string.Empty;

    public decimal LegacyResult { get; set; }
    public decimal DataFirstResult { get; set; }
    public decimal DifferenceAmount { get; set; }
    public decimal DifferenceAbs { get; set; }
    public decimal DifferencePercent { get; set; }
    
    public bool IsExactMatch { get; set; }
    public DriftSeverity DriftCategory { get; set; }
    
    public decimal TrustScore { get; set; }
    public TrustLevel TrustLevel { get; set; }
}
