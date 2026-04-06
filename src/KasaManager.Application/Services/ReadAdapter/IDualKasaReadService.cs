using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Domain.Calculation.Data;

namespace KasaManager.Application.Services.ReadAdapter;

public interface IDualKasaReadService
{
    Task<DualKasaResult> GetDualResultAsync(DateOnly date, string kasaType, CancellationToken ct = default);
    Task<List<DualKasaResult>> GetDualResultsBulkAsync(IEnumerable<(DateOnly Date, string KasaScope)> scopes, CancellationToken ct = default);
}
