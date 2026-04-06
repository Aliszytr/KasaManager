using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Domain.Calculation.Data;

namespace KasaManager.Application.Services.DataFirst;

public interface ISwitchSimulationService
{
    Task<SwitchSimulationResult> SimulateAsync(DateOnly date, string kasaType, CancellationToken ct = default);
    Task<List<SwitchSimulationResult>> SimulateBulkAsync(IEnumerable<(DateOnly Date, string KasaScope)> scopes, CancellationToken ct = default);
}
