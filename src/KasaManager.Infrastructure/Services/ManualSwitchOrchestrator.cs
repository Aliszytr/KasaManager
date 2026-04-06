using System;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Services.DataFirst;
using KasaManager.Domain.Calculation.Data;

namespace KasaManager.Infrastructure.Services;

public class ManualSwitchOrchestrator : IManualSwitchOrchestrator
{
    public Task<ManualSwitchResult> EvaluateAsync(
        PolicyResult policy,
        SwitchGateResult gate,
        decimal legacyValue,
        decimal dataFirstValue,
        CancellationToken ct = default)
    {
        var result = new ManualSwitchResult
        {
            TargetDate = policy.TargetDate,
            KasaType = policy.KasaType,
            LegacyValue = legacyValue,
            DataFirstValue = dataFirstValue,
            Diff = dataFirstValue - legacyValue,
            WouldSwitch = gate.WouldSwitch,
            IsSafeWindow = gate.IsSafeWindow
        };

        if (policy.Status != PolicyStatus.Eligible)
        {
            result.IsAllowed = false;
            result.FinalDecision = "BLOCKED";
            result.Reason = $"Policy is not Eligible. Current status: {policy.Status}";
        }
        else if (!gate.WouldSwitch)
        {
            result.IsAllowed = false;
            result.FinalDecision = "BLOCKED";
            result.Reason = "Switch Gate evaluated WouldSwitch = false.";
        }
        else if (!gate.IsSafeWindow)
        {
            result.IsAllowed = false;
            result.FinalDecision = "BLOCKED";
            result.Reason = "Simulation is NOT in a Safe Window (Drift/Trust thresholds unmet).";
        }
        else
        {
            result.IsAllowed = true;
            result.FinalDecision = "SIMULATED_SWITCH";
            result.Reason = "All policy and gate checks passed. Simulated successful switch.";
        }

        return Task.FromResult(result);
    }
}
