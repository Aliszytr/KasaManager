using System.Threading;
using System.Threading.Tasks;
using KasaManager.Domain.Calculation.Data;

namespace KasaManager.Application.Services.DataFirst;

public interface IManualSwitchOrchestrator
{
    Task<ManualSwitchResult> EvaluateAsync(
        PolicyResult policy,
        SwitchGateResult gate,
        decimal legacyValue,
        decimal dataFirstValue,
        CancellationToken ct = default);
}
