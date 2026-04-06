using System;

namespace KasaManager.Domain.Calculation.Data;

public class SwitchSimulationResult
{
    public DateOnly TargetDate { get; set; }
    public string KasaType { get; set; } = string.Empty;

    public decimal LegacyValue { get; set; }
    public decimal DataFirstValue { get; set; }
    public decimal DifferenceAmount { get; set; }
    public decimal DifferencePercent { get; set; }
    
    public decimal TrustScore { get; set; }
    public TrustLevel TrustLevel { get; set; }
    
    public bool IsSimulationSafe { get; set; }
    public string SimulationNote { get; set; } = string.Empty;
}
