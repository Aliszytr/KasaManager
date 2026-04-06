using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Services.DataFirst;
using KasaManager.Domain.Calculation.Data;
using Microsoft.Extensions.Logging;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// FAZ 12: DataFirstTrustService — DB erişimi IDataFirstTrustReadRepository'e taşındı.
/// Bu servis artık PURE hesaplama + repository üzerinden okuma/yazma yapar.
/// Bulk operasyonlar desteklenir (N+1 eliminasyonu).
/// </summary>
public class DataFirstTrustService : IDataFirstTrustService
{
    private readonly IDataFirstTrustReadRepository _repository;
    private readonly ILogger<DataFirstTrustService> _logger;

    public DataFirstTrustService(IDataFirstTrustReadRepository repository, ILogger<DataFirstTrustService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<DataFirstTrustSnapshot> CalculateAsync(DateOnly date, string kasaScope, CancellationToken ct = default)
    {
        var metrics = await _repository.GetMetricsAsync(date, kasaScope, ct);
        return ComputeSnapshot(date, kasaScope, metrics);
    }

    /// <summary>
    /// FAZ 12: Bulk hesaplama — N scope için tek DB roundtrip.
    /// DualKasaReadService tarafından N+1 yerine 1 çağrı ile kullanılır.
    /// </summary>
    public async Task<Dictionary<(DateOnly, string), DataFirstTrustSnapshot>> CalculateBulkAsync(
        IEnumerable<(DateOnly Date, string Scope)> scopes, CancellationToken ct = default)
    {
        var scopeList = scopes.ToList();
        var allMetrics = await _repository.GetMetricsBulkAsync(scopeList, ct);

        var result = new Dictionary<(DateOnly, string), DataFirstTrustSnapshot>();
        foreach (var scope in scopeList)
        {
            var metrics = allMetrics.TryGetValue((scope.Date, scope.Scope), out var m) ? m : new TrustMetrics();
            result[(scope.Date, scope.Scope)] = ComputeSnapshot(scope.Date, scope.Scope, metrics);
        }

        return result;
    }

    public async Task<DataFirstTrustSnapshot> CalculateAndSaveAsync(DateOnly date, string kasaScope, CancellationToken ct = default)
    {
        var rawResult = await CalculateAsync(date, kasaScope, ct);
        return await _repository.UpsertSnapshotAsync(rawResult, ct);
    }

    /// <summary>
    /// PURE hesaplama — DB erişimi yok, deterministik.
    /// </summary>
    private static DataFirstTrustSnapshot ComputeSnapshot(DateOnly date, string kasaScope, TrustMetrics metrics)
    {
        decimal confidenceScore = 0m;
        var trustLevel = TrustLevel.Unknown;
        var staleCount = metrics.IsStale ? 1 : 0;

        if (metrics.TotalDriftCount > 0)
        {
            decimal baseScore = ((decimal)metrics.ExactMatchCount / metrics.TotalDriftCount) * 100m;
            decimal penalty = 0m;

            if (metrics.DriftCount > 0) penalty += 10m;
            if (metrics.IsStale) penalty += 5m;

            confidenceScore = Math.Clamp(baseScore - penalty, 0m, 100m);

            if (confidenceScore >= 90m) trustLevel = TrustLevel.High;
            else if (confidenceScore >= 70m) trustLevel = TrustLevel.Medium;
            else trustLevel = TrustLevel.Low;
        }

        return new DataFirstTrustSnapshot
        {
            TargetDate = date,
            KasaType = kasaScope,
            TotalCount = metrics.TotalDriftCount,
            ExactMatchCount = metrics.ExactMatchCount,
            DriftCount = metrics.DriftCount,
            StaleCount = staleCount,
            ConfidenceScore = confidenceScore,
            TrustLevel = trustLevel,
            CalculatedAt = DateTime.UtcNow
        };
    }
}
