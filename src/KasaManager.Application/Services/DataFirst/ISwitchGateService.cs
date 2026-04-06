using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Domain.Calculation.Data;

namespace KasaManager.Application.Services.DataFirst;

public interface ISwitchGateService
{
    Task<SwitchGateResult> EvaluateAsync(PolicyResult input, CancellationToken ct = default);
    Task<List<SwitchGateResult>> EvaluateBulkAsync(List<PolicyResult> inputs, CancellationToken ct = default);
}
