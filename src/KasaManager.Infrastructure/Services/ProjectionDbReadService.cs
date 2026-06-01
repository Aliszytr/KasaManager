using KasaManager.Application.Services.DataFirst;
using KasaManager.Domain.Calculation.Data;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// Read-only DB provider used by projection resolver.
/// </summary>
public sealed class ProjectionDbReadService : IProjectionDbReadService
{
    private static readonly string[] ScopeFallbackOrder = ["Sabah", "Aksam", "Genel"];
    private readonly KasaManagerDbContext _dbContext;

    public ProjectionDbReadService(KasaManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ProjectionDbReadSnapshot> GetSnapshotAsync(
        DateOnly date,
        string preferredScope,
        CancellationToken ct = default)
    {
        var scopes = new List<string>();
        if (!string.IsNullOrWhiteSpace(preferredScope))
            scopes.Add(preferredScope);
        scopes.AddRange(ScopeFallbackOrder.Where(s => !scopes.Contains(s, StringComparer.OrdinalIgnoreCase)));

        DailyCalculationResult? result = null;
        var usedScope = preferredScope;
        foreach (var scope in scopes)
        {
            result = await _dbContext.DailyCalculationResults
                .AsNoTracking()
                .Where(x => x.ForDate == date && x.KasaTuru == scope)
                .OrderByDescending(x => x.CalculatedVersion)
                .FirstOrDefaultAsync(ct);

            if (result != null)
            {
                usedScope = scope;
                break;
            }
        }

        var latestBatchId = await _dbContext.ImportBatches
            .AsNoTracking()
            .Where(x => x.TargetDate == date)
            .OrderByDescending(x => x.ImportedAt)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(ct);

        List<DailyFact> facts;
        if (latestBatchId != Guid.Empty)
        {
            facts = await _dbContext.DailyFacts
                .AsNoTracking()
                .Where(x => x.ForDate == date && x.ImportBatchId == latestBatchId)
                .ToListAsync(ct);
        }
        else
        {
            facts = await _dbContext.DailyFacts
                .AsNoTracking()
                .Where(x => x.ForDate == date)
                .ToListAsync(ct);
        }

        var overrides = await _dbContext.DailyOverrides
            .AsNoTracking()
            .Where(x => x.ForDate == date)
            .ToListAsync(ct);

        return new ProjectionDbReadSnapshot(result, facts, overrides, usedScope ?? preferredScope);
    }
}
