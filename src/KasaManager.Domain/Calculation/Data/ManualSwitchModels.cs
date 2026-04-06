using System;

namespace KasaManager.Domain.Calculation.Data;

public class ManualSwitchResult
{
    public DateOnly TargetDate { get; set; }
    public string KasaType { get; set; } = string.Empty;

    public bool IsAllowed { get; set; }
    public bool WouldSwitch { get; set; }
    public bool IsSafeWindow { get; set; }
    public string FinalDecision { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;

    public decimal LegacyValue { get; set; }
    public decimal DataFirstValue { get; set; }
    public decimal Diff { get; set; }
}
