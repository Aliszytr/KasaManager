using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Domain.Calculation.Data;

namespace KasaManager.Application.Services.DataFirst;

public interface ISwitchReadinessPolicyService
{
    Task<PolicyResult> EvaluateAsync(PolicyInput input, CancellationToken ct = default);
    Task<List<PolicyResult>> EvaluateBulkAsync(List<PolicyInput> inputs, CancellationToken ct = default);
}
