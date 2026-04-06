using System;
using System.Collections.Generic;
using KasaManager.Domain.Calculation.Data;

namespace KasaManager.Web.Models;

public enum PilotReadiness
{
    Unknown = 0,
    Ready = 1,
    Blocked = 2
}

public class DataPilotIndexViewModel
{
    // KPIs
    public int TotalDaysAnalyzed { get; set; }
    public int EligibleDays { get; set; }
    public int NotEligibleDays { get; set; }
    public int OpenDrifts { get; set; }
    public int InvestigatingDrifts { get; set; }
    public int ResolvedDrifts { get; set; }
    public int AcceptedDifference { get; set; }
    public int FalsePositive { get; set; }

    // Filters
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? KasaType { get; set; }
    public bool? IsEligibleFilter { get; set; }

    // Data
    public List<ReadinessGridItem> GridItems { get; set; } = new();

    // Summary Text
    public string PilotSummaryHtml { get; set; } = string.Empty;

    // Phase 7: Trend
    public List<DataFirstTrustSnapshot> RecentTrustTrend { get; set; } = new();
}

public class ReadinessGridItem
{
    public DateOnly TargetDate { get; set; }
    public string KasaScope { get; set; } = string.Empty;
    public PilotReadiness ReadinessStatus { get; set; }
    public string BlockReason { get; set; } = string.Empty;
    public int OpenDriftsCount { get; set; }
    public int ExactUnresolvedCount { get; set; }
    
    // Phase 7: Trust Score Snapshot
    public DataFirstTrustSnapshot? TrustSnapshot { get; set; }
    
    // Phase 8: Dual Result Snapshot
    public DualKasaResult? DualResult { get; set; }
    
    // Phase 9: Switch Simulation Result
    public SwitchSimulationResult? SimulationResult { get; set; }

    // Phase 10: Policy Layer Result
    public PolicyResult? PolicyResult { get; set; }

    // Phase 11: Switch Gate Result
    public SwitchGateResult? GateResult { get; set; }
    
    public bool IsEligible { get; set; } // Internal logic helper
    public bool IsStale { get; set; }
    public bool IsLocked { get; set; }
    public List<CalculationParityDrift> Drifts { get; set; } = new();
}
