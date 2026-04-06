using System;

namespace KasaManager.Domain.Calculation.Data;

public enum PolicyStatus
{
    Eligible,
    Review,
    Blocked
}

public class PolicyInput
{
    public DateOnly TargetDate { get; set; }
    public string KasaType { get; set; } = string.Empty;

    public int TrustScore { get; set; }
    public decimal DriftAmount { get; set; }
    public bool IsDataComplete { get; set; }
}

public class PolicyResult
{
    public DateOnly TargetDate { get; set; }
    public string KasaType { get; set; } = string.Empty;

    public PolicyStatus Status { get; set; }
    public int TrustScore { get; set; }
    public decimal DriftAmount { get; set; }
    public string Reason { get; set; } = string.Empty;
}
