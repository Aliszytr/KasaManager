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
/// FAZ 12: Threshold'lar IOptions ile config'den okunur. Hardcoded const kaldırıldı.
/// </summary>
public class SwitchReadinessPolicyService : ISwitchReadinessPolicyService
{
    private readonly SwitchPolicyOptions _opts;

    public SwitchReadinessPolicyService(IOptions<SwitchPolicyOptions> options)
    {
        _opts = options.Value;
    }

    public Task<PolicyResult> EvaluateAsync(PolicyInput input, CancellationToken ct = default)
    {
        return Task.FromResult(EvaluateInternal(input));
    }

    public Task<List<PolicyResult>> EvaluateBulkAsync(List<PolicyInput> inputs, CancellationToken ct = default)
    {
        var results = new List<PolicyResult>(inputs.Count);
        foreach (var input in inputs)
        {
            results.Add(EvaluateInternal(input));
        }
        return Task.FromResult(results);
    }

    private PolicyResult EvaluateInternal(PolicyInput input)
    {
        var result = new PolicyResult
        {
            TargetDate = input.TargetDate,
            KasaType = input.KasaType,
            TrustScore = input.TrustScore,
            DriftAmount = input.DriftAmount
        };

        if (!input.IsDataComplete)
        {
            result.Status = PolicyStatus.Blocked;
            result.Reason = "Data is incomplete";
            return result;
        }

        var absDrift = Math.Abs(input.DriftAmount);

        // --- BLOCKED CONDITIONS (config'den okunur) ---
        if (input.TrustScore < _opts.MinTrustScoreBlocked)
        {
            result.Status = PolicyStatus.Blocked;
            result.Reason = $"Low Trust Score ({input.TrustScore})";
            return result;
        }

        if (absDrift > _opts.MaxMinorDrift)
        {
            result.Status = PolicyStatus.Blocked;
            result.Reason = $"Major Drift detected (Abs Diff: {absDrift:N2})";
            return result;
        }

        // --- ELIGIBLE CONDITIONS (config'den okunur) ---
        if (input.TrustScore >= _opts.MinTrustScoreEligible && absDrift == 0m)
        {
            result.Status = PolicyStatus.Eligible;
            result.Reason = "High trust and exact match";
            return result;
        }

        // --- REVIEW STATUS ---
        result.Status = PolicyStatus.Review;
        
        if (absDrift > 0)
        {
            result.Reason = $"Requires review due to minor drift (Abs Diff: {absDrift:N2})";
        }
        else
        {
            result.Reason = $"Requires review due to medium trust ({input.TrustScore})";
        }

        return result;
    }
}
