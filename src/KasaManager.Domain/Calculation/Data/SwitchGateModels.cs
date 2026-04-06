using System;

namespace KasaManager.Domain.Calculation.Data;

public class SwitchGateResult
{
    public DateOnly TargetDate { get; set; }
    public string KasaType { get; set; } = string.Empty;

    public bool WouldSwitch { get; set; }
    public bool IsSafeWindow { get; set; }
    public PolicyStatus PolicyStatus { get; set; }
    public int TrustScore { get; set; }
    public decimal DriftAmount { get; set; }
    public string Reason { get; set; } = string.Empty;
}
