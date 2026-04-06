using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Services.DataFirst;
using KasaManager.Application.Services.ReadAdapter;
using KasaManager.Domain.Calculation.Data;
using KasaManager.Domain.Settings;
using Microsoft.Extensions.Options;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// FAZ 12: Simulation threshold'ları IOptions ile config'den okunur.
/// </summary>
public class SwitchSimulationService : ISwitchSimulationService
{
    private readonly IDualKasaReadService _dualKasaReadService;
    private readonly SwitchPolicyOptions _opts;

    public SwitchSimulationService(IDualKasaReadService dualKasaReadService, IOptions<SwitchPolicyOptions> options)
    {
        _dualKasaReadService = dualKasaReadService;
        _opts = options.Value;
    }

    public async Task<SwitchSimulationResult> SimulateAsync(DateOnly date, string kasaType, CancellationToken ct = default)
    {
        var results = await SimulateBulkAsync(new[] { (date, kasaType) }, ct);
        return results.FirstOrDefault() ?? new SwitchSimulationResult
        {
            TargetDate = date,
            KasaType = kasaType,
            IsSimulationSafe = false,
            SimulationNote = "Unknown error - missing dual read"
        };
    }

    public async Task<List<SwitchSimulationResult>> SimulateBulkAsync(IEnumerable<(DateOnly Date, string KasaScope)> scopes, CancellationToken ct = default)
    {
        var scopeList = scopes.ToList();
        if (!scopeList.Any()) return new List<SwitchSimulationResult>();

        // 1) Fetch base data heavily optimized without roundtripping exactly once per request.
        var dualResults = await _dualKasaReadService.GetDualResultsBulkAsync(scopeList, ct);

        // 2) Deterministic mapping to Simulation Results
        var simulationResults = new List<SwitchSimulationResult>();

        foreach (var req in scopeList)
        {
            var dualRes = dualResults.FirstOrDefault(d => d.TargetDate == req.Date && d.KasaType == req.KasaScope);
            
            if (dualRes == null)
            {
                simulationResults.Add(new SwitchSimulationResult
                {
                    TargetDate = req.Date,
                    KasaType = req.KasaScope,
                    IsSimulationSafe = false,
                    SimulationNote = "Missing read data - NOT SAFE"
                });
                continue;
            }

            bool isSafe = false;
            string note = string.Empty;

            // Rule (config'den okunur):
            // TRUE if: ExactMatch == true OR (DifferencePercent < threshold AND TrustLevel == High)
            // FALSE if: all other conditions

            if (dualRes.IsExactMatch)
            {
                isSafe = true;
                note = "Exact match \u2013 safe to switch";
            }
            else if (dualRes.DifferencePercent < _opts.SimulationSafeDiffPercent && dualRes.TrustLevel == TrustLevel.High)
            {
                isSafe = true;
                note = "Minor diff but high trust";
            }
            else if (dualRes.DifferencePercent >= _opts.SimulationSafeDiffPercent)
            {
                isSafe = false;
                note = "Major difference \u2013 NOT SAFE";
            }
            else
            {
                isSafe = false;
                note = "Low trust \u2013 DO NOT SWITCH";
            }

            simulationResults.Add(new SwitchSimulationResult
            {
                TargetDate = dualRes.TargetDate,
                KasaType = dualRes.KasaType,
                LegacyValue = dualRes.LegacyResult,
                DataFirstValue = dualRes.DataFirstResult,
                DifferenceAmount = dualRes.DifferenceAmount,
                DifferencePercent = dualRes.DifferencePercent,
                TrustScore = dualRes.TrustScore,
                TrustLevel = dualRes.TrustLevel,
                IsSimulationSafe = isSafe,
                SimulationNote = note
            });
        }

        return simulationResults;
    }
}
