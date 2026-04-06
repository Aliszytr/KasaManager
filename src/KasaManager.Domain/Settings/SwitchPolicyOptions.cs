namespace KasaManager.Domain.Settings;

/// <summary>
/// FAZ 12: Switch Policy threshold'ları — artık appsettings.json'dan okunur.
/// Hardcoded magic number'lar kaldırıldı.
/// </summary>
public class SwitchPolicyOptions
{
    public const string SectionName = "SwitchPolicy";

    // ── Policy Layer Thresholds ──

    /// <summary>Eligible için minimum trust score (default: 95).</summary>
    public int MinTrustScoreEligible { get; set; } = 95;

    /// <summary>Blocked altında kalan trust score (default: 80).</summary>
    public int MinTrustScoreBlocked { get; set; } = 80;

    /// <summary>Minor drift üst sınırı (default: 1.0m). Üstü → Blocked.</summary>
    public decimal MaxMinorDrift { get; set; } = 1.0m;

    // ── Gate Layer Thresholds ──

    /// <summary>Safe Window için minimum trust score (default: 98).</summary>
    public int MinTrustScoreGate { get; set; } = 98;

    /// <summary>Safe Window için maksimum absolute drift (default: 0.0001m).</summary>
    public decimal MaxAbsoluteDriftGate { get; set; } = 0.0001m;

    // ── Simulation Layer Thresholds ──

    /// <summary>Minor diff kabul yüzdesi (default: 0.1%).</summary>
    public decimal SimulationSafeDiffPercent { get; set; } = 0.1m;
}
