using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Services.DataFirst;
using KasaManager.Application.Services.ReadAdapter;
using KasaManager.Domain.Calculation.Data;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KasaManager.Infrastructure.Services.ReadAdapter;

public class DualKasaReadService : IDualKasaReadService
{
    private readonly KasaManagerDbContext _dbContext;
    private readonly IDataFirstTrustService _trustService;

    public DualKasaReadService(KasaManagerDbContext dbContext, IDataFirstTrustService trustService)
    {
        _dbContext = dbContext;
        _trustService = trustService;
    }

    public async Task<DualKasaResult> GetDualResultAsync(DateOnly date, string kasaType, CancellationToken ct = default)
    {
        var results = await GetDualResultsBulkAsync(new[] { (date, kasaType) }, ct);
        return results.FirstOrDefault() ?? new DualKasaResult
        {
            TargetDate = date,
            KasaType = kasaType
        };
    }

    public async Task<List<DualKasaResult>> GetDualResultsBulkAsync(IEnumerable<(DateOnly Date, string KasaScope)> scopes, CancellationToken ct = default)
    {
        var scopeList = scopes.ToList();
        if (!scopeList.Any()) return new List<DualKasaResult>();

        var minDate = scopeList.Min(x => x.Date);
        var maxDate = scopeList.Max(x => x.Date);

        // Optimization: bulk queries
        var allDrifts = await _dbContext.CalculationParityDrifts
            .Where(d => d.TargetDate >= minDate && d.TargetDate <= maxDate 
                        && d.FieldKey == "[OUTPUT] devreden_kasa")
            .ToListAsync(ct);

        var dfSnaps = await _dbContext.DailyCalculationResults
            .Where(r => r.ForDate >= minDate && r.ForDate <= maxDate)
            .ToListAsync(ct);

        var legacySnaps = await _dbContext.CalculatedKasaSnapshots
            .Where(r => r.IsActive && !r.IsDeleted && r.RaporTarihi >= minDate && r.RaporTarihi <= maxDate)
            .ToListAsync(ct);

        // FAZ 12 FIX: N+1 eliminasyonu — tüm trust snapshot'ları tek seferde hesapla
        var trustBulk = await _trustService.CalculateBulkAsync(scopeList, ct);

        var list = new List<DualKasaResult>();

        foreach (var req in scopeList)
        {
            var dfRecord = dfSnaps.FirstOrDefault(d => d.ForDate == req.Date && string.Equals(d.KasaTuru, req.KasaScope, StringComparison.OrdinalIgnoreCase));
            var mappedScope = MapScope(req.KasaScope);
            var legRecord = legacySnaps.FirstOrDefault(d => d.RaporTarihi == req.Date && d.KasaTuru == mappedScope);

            decimal legVal = 0m;
            if (legRecord != null)
            {
                var outputs = legRecord.GetOutputs();
                if (outputs.TryGetValue("devreden_kasa", out var val)) legVal = val;
            }

            decimal newVal = 0m;
            if (dfRecord != null && !string.IsNullOrWhiteSpace(dfRecord.ResultsJson))
            {
               try 
               {
                  var dfOutputs = JsonSerializer.Deserialize<Dictionary<string, decimal>>(dfRecord.ResultsJson);
                  if (dfOutputs != null && dfOutputs.TryGetValue("devreden_kasa", out var val)) newVal = val;
               } 
               catch { /* ignore bad json */ }
            }

            var diffAmount = newVal - legVal;
            var diffAbs = Math.Abs(diffAmount);
            
            decimal diffPercent = 0m;
            if (legVal != 0m)
            {
                diffPercent = (diffAbs / legVal) * 100m;
            }

            var isExact = diffAbs == 0m;
            
            var driftRecord = allDrifts.FirstOrDefault(d => d.TargetDate == req.Date && d.KasaScope == req.KasaScope);
            var driftCat = driftRecord?.Severity ?? DriftSeverity.ExactMatch;
            
            // Fallback categorization if not in parity table
            if (driftRecord == null && !isExact) 
            {
                driftCat = diffAbs <= 1.0m ? DriftSeverity.MinorDrift : DriftSeverity.MajorDrift;
            }

            var result = new DualKasaResult
            {
                TargetDate = req.Date,
                KasaType = req.KasaScope,
                LegacyResult = legVal,
                DataFirstResult = newVal,
                DifferenceAmount = diffAmount,
                DifferenceAbs = diffAbs,
                DifferencePercent = diffPercent,
                IsExactMatch = isExact,
                DriftCategory = driftCat
            };

            // FAZ 12 FIX: Preloaded trust — DB'ye tekrar gitmez
            if (trustBulk.TryGetValue((req.Date, req.KasaScope), out var trustSnap))
            {
                result.TrustScore = trustSnap.ConfidenceScore;
                result.TrustLevel = trustSnap.TrustLevel;
            }

            list.Add(result);
        }

        return list;
    }

    private KasaRaporTuru MapScope(string scope)
    {
        return scope.ToLowerInvariant() switch
        {
            "sabah" => KasaRaporTuru.Sabah,
            "aksam" => KasaRaporTuru.Aksam,
            "genel" => KasaRaporTuru.Genel,
            "ortak" => KasaRaporTuru.Ortak,
            _ => KasaRaporTuru.Genel
        };
    }
}
