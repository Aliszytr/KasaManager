using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Services.DataFirst;
using KasaManager.Domain.Calculation.Data;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// FAZ 12: DB erişimini TrustService'den izole eden repository.
/// Bulk operasyonlar ile N+1 sorgu problemi çözülür.
/// </summary>
public sealed class DataFirstTrustReadRepository : IDataFirstTrustReadRepository
{
    private readonly KasaManagerDbContext _dbContext;

    public DataFirstTrustReadRepository(KasaManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TrustMetrics> GetMetricsAsync(DateOnly date, string kasaScope, CancellationToken ct = default)
    {
        var bulk = await GetMetricsBulkAsync(new[] { (date, kasaScope) }, ct);
        return bulk.TryGetValue((date, kasaScope), out var m) ? m : new TrustMetrics();
    }

    public async Task<Dictionary<(DateOnly Date, string Scope), TrustMetrics>> GetMetricsBulkAsync(
        IEnumerable<(DateOnly Date, string Scope)> scopes, CancellationToken ct = default)
    {
        var scopeList = scopes.ToList();
        if (!scopeList.Any())
            return new Dictionary<(DateOnly Date, string Scope), TrustMetrics>();

        var minDate = scopeList.Min(x => x.Date);
        var maxDate = scopeList.Max(x => x.Date);

        // 1 SORGU: Tüm ilgili drift kayıtlarını tek seferde çek
        // FAZ 13.1: FieldKey + RootCauseCategory eklendi — structural drift filtreleme için
        var allDrifts = await _dbContext.CalculationParityDrifts
            .Where(d => d.TargetDate >= minDate && d.TargetDate <= maxDate)
            .Select(d => new { d.TargetDate, d.KasaScope, d.Severity, d.FieldKey, d.RootCauseCategory })
            .ToListAsync(ct);

        // 1 SORGU: Tüm ilgili daily calculation results (stale kontrolü)
        var allResults = await _dbContext.DailyCalculationResults
            .Where(r => r.ForDate >= minDate && r.ForDate <= maxDate)
            .GroupBy(r => new { r.ForDate, r.KasaTuru })
            .Select(g => new
            {
                g.Key.ForDate,
                g.Key.KasaTuru,
                IsStale = g.OrderByDescending(x => x.CalculatedVersion).First().IsStale
            })
            .ToListAsync(ct);

        var result = new Dictionary<(DateOnly Date, string Scope), TrustMetrics>();

        foreach (var scope in scopeList)
        {
            var scopeDrifts = allDrifts
                .Where(d => d.TargetDate == scope.Date && d.KasaScope == scope.Scope)
                .ToList();

            // FAZ 13.1: Structural drift'leri trust hesaplamasından çıkar.
            // Structural = DataFirst'e özgü carry-over alanları (prev_*) veya
            // Legacy'de hiç bulunmayan Extra Fact'ler.
            // Bu kayıtlar DB'de kalır (görünürlük), sadece trust SCORING'den exclude edilir.
            var financialDrifts = scopeDrifts
                .Where(d => !IsStructuralDrift(d.FieldKey, d.RootCauseCategory))
                .ToList();

            var staleRecord = allResults
                .FirstOrDefault(r => r.ForDate == scope.Date && r.KasaTuru == scope.Scope);

            result[(scope.Date, scope.Scope)] = new TrustMetrics
            {
                TotalDriftCount = financialDrifts.Count,
                ExactMatchCount = financialDrifts.Count(d => d.Severity == DriftSeverity.ExactMatch),
                DriftCount = financialDrifts.Count(d => d.Severity == DriftSeverity.MinorDrift || d.Severity == DriftSeverity.MajorDrift),
                IsStale = staleRecord?.IsStale ?? false
            };
        }

        return result;
    }

    public async Task<DataFirstTrustSnapshot> UpsertSnapshotAsync(DataFirstTrustSnapshot snapshot, CancellationToken ct = default)
    {
        var existing = await _dbContext.DataFirstTrustSnapshots
            .FirstOrDefaultAsync(s => s.TargetDate == snapshot.TargetDate && s.KasaType == snapshot.KasaType, ct);

        if (existing == null)
        {
            snapshot.Id = Guid.NewGuid();
            _dbContext.DataFirstTrustSnapshots.Add(snapshot);
        }
        else
        {
            existing.TotalCount = snapshot.TotalCount;
            existing.ExactMatchCount = snapshot.ExactMatchCount;
            existing.DriftCount = snapshot.DriftCount;
            existing.StaleCount = snapshot.StaleCount;
            existing.ConfidenceScore = snapshot.ConfidenceScore;
            existing.TrustLevel = snapshot.TrustLevel;
            existing.CalculatedAt = snapshot.CalculatedAt;
            _dbContext.DataFirstTrustSnapshots.Update(existing);
            snapshot = existing; // Return the tracked entity
        }

        await _dbContext.SaveChangesAsync(ct);
        return snapshot;
    }

    /// <summary>
    /// FAZ 13.1: Structural drift = DataFirst mimarisinin ürettiği ama Legacy'de hiç olmayan alanlar.
    /// Bu driftler beklenen yapısal farklardır, finansal sapma değildir.
    /// Trust scoring'den exclude edilir, drift kayıtları DB'de görünür kalır.
    /// </summary>
    private static bool IsStructuralDrift(string? fieldKey, string? rootCauseCategory)
    {
        // Kriter 1: prev_* prefix — carry-over chain verileri (prev_kasa_toplam, prev_devreden_kasa)
        if (!string.IsNullOrEmpty(fieldKey) && 
            fieldKey.Contains("prev_", StringComparison.OrdinalIgnoreCase))
            return true;

        // Kriter 2: "Missing in Legacy (Extra Fact)" — DataFirst-only alanlar
        if (string.Equals(rootCauseCategory, "Missing in Legacy (Extra Fact)", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
