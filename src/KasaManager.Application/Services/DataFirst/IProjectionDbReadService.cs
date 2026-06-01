using KasaManager.Domain.Calculation.Data;

namespace KasaManager.Application.Services.DataFirst;

/// <summary>
/// Read-only accessor for projection resolver DB inputs.
/// </summary>
public interface IProjectionDbReadService
{
    Task<ProjectionDbReadSnapshot> GetSnapshotAsync(
        DateOnly date,
        string preferredScope,
        CancellationToken ct = default);
}

public sealed record ProjectionDbReadSnapshot(
    DailyCalculationResult? DailyResult,
    IReadOnlyList<DailyFact> DailyFacts,
    IReadOnlyList<DailyOverride> DailyOverrides,
    string ScopeUsed
);
