using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Domain.Calculation.Data;

namespace KasaManager.Application.Services.DataFirst;

/// <summary>
/// FAZ 12: DB isolation — TrustService artık DbContext yerine bu interface üzerinden veri okur.
/// Bulk operasyonlar desteklenir (N+1 sorgu eliminasyonu).
/// </summary>
public interface IDataFirstTrustReadRepository
{
    /// <summary>
    /// Tek bir (date, scope) çifti için drift ve stale metriklerini getirir.
    /// </summary>
    Task<TrustMetrics> GetMetricsAsync(DateOnly date, string kasaScope, CancellationToken ct = default);

    /// <summary>
    /// Birden fazla (date, scope) çifti için drift ve stale metriklerini tek sorguda getirir.
    /// N+1 sorgu problemi yerine bulk preload sağlar.
    /// </summary>
    Task<Dictionary<(DateOnly Date, string Scope), TrustMetrics>> GetMetricsBulkAsync(
        IEnumerable<(DateOnly Date, string Scope)> scopes, CancellationToken ct = default);

    /// <summary>
    /// Trust snapshot'ı upsert eder (idempotent).
    /// </summary>
    Task<DataFirstTrustSnapshot> UpsertSnapshotAsync(DataFirstTrustSnapshot snapshot, CancellationToken ct = default);
}

/// <summary>
/// Trust hesaplaması için gerekli ham metrikler. 
/// DB erişimi olmadan pure hesaplama yapılabilmesini sağlar.
/// </summary>
public class TrustMetrics
{
    public int TotalDriftCount { get; set; }
    public int ExactMatchCount { get; set; }
    public int DriftCount { get; set; }
    public bool IsStale { get; set; }
}
