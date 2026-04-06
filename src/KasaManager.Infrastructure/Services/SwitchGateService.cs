using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Services.DataFirst;
using KasaManager.Domain.Calculation.Data;
using KasaManager.Domain.Settings;
using Microsoft.Extensions.Options;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// FAZ 12: Gate threshold'ları IOptions ile config'den okunur. Hardcoded const kaldırıldı.
/// </summary>
public class SwitchGateService : ISwitchGateService
{
    private readonly SwitchPolicyOptions _opts;

    public SwitchGateService(IOptions<SwitchPolicyOptions> options)
    {
        _opts = options.Value;
    }

    public Task<SwitchGateResult> EvaluateAsync(PolicyResult input, CancellationToken ct = default)
    {
        return Task.FromResult(EvaluateInternal(input));
    }

    public Task<List<SwitchGateResult>> EvaluateBulkAsync(List<PolicyResult> inputs, CancellationToken ct = default)
    {
        var results = new List<SwitchGateResult>(inputs.Count);
        foreach (var input in inputs)
        {
            results.Add(EvaluateInternal(input));
        }
        return Task.FromResult(results);
    }

    private SwitchGateResult EvaluateInternal(PolicyResult input)
    {
        var result = new SwitchGateResult
        {
            TargetDate = input.TargetDate,
            KasaType = input.KasaType,
            PolicyStatus = input.Status,
            TrustScore = input.TrustScore,
            DriftAmount = input.DriftAmount,
            Reason = input.Reason
        };

        result.WouldSwitch = input.Status == PolicyStatus.Eligible;

        if (input.Status == PolicyStatus.Eligible && 
            input.TrustScore >= _opts.MinTrustScoreGate && 
            Math.Abs(input.DriftAmount) <= _opts.MaxAbsoluteDriftGate)
        {
            result.IsSafeWindow = true;
            result.Reason = "Optimal safety parameters met for switch.";
        }
        else
        {
            result.IsSafeWindow = false;
            // Retain original policy reason to inform why it's not a safe window
        }

        return result;
    }
}
