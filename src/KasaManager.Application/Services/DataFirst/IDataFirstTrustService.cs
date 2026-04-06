using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Domain.Calculation.Data;

namespace KasaManager.Application.Services.DataFirst;

public interface IDataFirstTrustService
{
    Task<DataFirstTrustSnapshot> CalculateAsync(DateOnly date, string kasaScope, CancellationToken ct = default);
    Task<DataFirstTrustSnapshot> CalculateAndSaveAsync(DateOnly date, string kasaScope, CancellationToken ct = default);

    /// <summary>
    /// FAZ 12: Bulk trust hesaplama — N scope için tek DB roundtrip.
    /// </summary>
    Task<Dictionary<(DateOnly, string), DataFirstTrustSnapshot>> CalculateBulkAsync(
        IEnumerable<(DateOnly Date, string Scope)> scopes, CancellationToken ct = default);
}
