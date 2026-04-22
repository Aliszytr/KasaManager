using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Services.DataFirst;
using KasaManager.Domain.Calculation;
using KasaManager.Domain.Calculation.Data;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.FormulaEngine.Authoring;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KasaManager.Infrastructure.Services;

public sealed class ParityCheckService : IParityCheckService
{
    private readonly KasaManagerDbContext _dbContext;
    private readonly IFormulaEngineService _formulaEngine;
    private readonly ILogger<ParityCheckService> _logger;

    public ParityCheckService(
        KasaManagerDbContext dbContext,
        IFormulaEngineService formulaEngine,
        ILogger<ParityCheckService> logger)
    {
        _dbContext = dbContext;
        _formulaEngine = formulaEngine;
        _logger = logger;
    }

    public async Task RunShadowCheckAsync(
        DateOnly targetDate,
        string kasaScope,
        List<UnifiedPoolEntry> legacyInputs,
        CalculationRun legacyFormulaRun,
        FormulaSet formulaSet,
        Dictionary<string, decimal> currentOverrides,
        CancellationToken ct = default)
    {
        try
        {
            // 1. Dataları Çek (Fact & Overrides)
            var facts = await _dbContext.DailyFacts
                .Where(x => x.ForDate == targetDate)
                .ToListAsync(ct);
                
            var dailyOverrides = await _dbContext.DailyOverrides
                .Where(x => x.ForDate == targetDate)
                .ToListAsync(ct);

            // 1.5 Previous Day Result (Carry Over Data)
            var prevDate = targetDate.AddDays(-1);
            var prevResult = await _dbContext.DailyCalculationResults
                .Where(x => x.ForDate == prevDate && x.KasaTuru == kasaScope)
                .OrderByDescending(x => x.CalculatedVersion)
                .FirstOrDefaultAsync(ct);

            // 2. DataFirst Inputs oluştur
            var newInputs = BuildDataFirstInputs(facts, dailyOverrides, prevResult);

            // 3. Calculation Engine'i aynı format ve overridelar ile yepyeni baştan çalıştır
            var newRunResult = _formulaEngine.Run(targetDate, formulaSet, newInputs, currentOverrides);
            
            if (!newRunResult.Ok || newRunResult.Value == null)
            {
                _logger.LogWarning("Parity Check failed to run formula engine for {Date}. Error: {Error}", targetDate, newRunResult.Error);
                return;
            }

            var drifts = new List<CalculationParityDrift>();

            // 3.5. Yeni Hesabı Kaydet (Zinciri oluşturabilmek için)
            var currentResult = await _dbContext.DailyCalculationResults
                .Where(x => x.ForDate == targetDate && x.KasaTuru == kasaScope)
                .FirstOrDefaultAsync(ct);
                
            var outputDict = new Dictionary<string, decimal>(newRunResult.Value.Outputs);
            
            // Seçenek A: Mevcut JSON'daki kritik keyleri koru
            if (currentResult != null && !string.IsNullOrWhiteSpace(currentResult.ResultsJson))
            {
                try
                {
                    var existingDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(currentResult.ResultsJson);
                    if (existingDict != null)
                    {
                        var keysToPreserve = new[] { "sonraki_kasaya_devredecek", "SonrayaDevredecek", "GenelKasa", "genel_kasa", "sabah_kasa_devir", "kasa_toplam" };
                        foreach(var preserveKey in keysToPreserve)
                        {
                            if (existingDict.TryGetValue(preserveKey, out var el))
                            {
                                if (el.ValueKind == System.Text.Json.JsonValueKind.Number && el.TryGetDecimal(out var d))
                                    outputDict[preserveKey] = d;
                                else if (el.ValueKind == System.Text.Json.JsonValueKind.String)
                                {
                                    // Invariant culture with fallback
                                    var rawStr = el.GetString()?.Replace("₺", "")?.Trim();
                                    if (!string.IsNullOrWhiteSpace(rawStr))
                                    {
                                        if (decimal.TryParse(rawStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ds))
                                            outputDict[preserveKey] = ds;
                                        else if (decimal.TryParse(rawStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), out ds))
                                            outputDict[preserveKey] = ds;
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            var finalJson = System.Text.Json.JsonSerializer.Serialize(outputDict);

            if (currentResult == null)
            {
                currentResult = new DailyCalculationResult
                {
                    Id = Guid.NewGuid(),
                    ForDate = targetDate,
                    KasaTuru = kasaScope,
                    FormulaSetId = Guid.TryParse(formulaSet.Id, out var fsId) ? fsId : Guid.Empty,
                    PreviousResultId = prevResult?.Id,
                    IsLocked = false,
                    IsStale = prevResult != null && prevResult.IsStale, // Cascade stale logic
                    CalculatedVersion = 1,
                    ResultsJson = finalJson,
                    CalculatedAt = DateTime.UtcNow
                };
                _dbContext.DailyCalculationResults.Add(currentResult);
            }
            else
            {
                if (!currentResult.IsLocked) // Sadece kilitli değilse güncellenir
                {
                    currentResult.CalculatedVersion++;
                    currentResult.PreviousResultId = prevResult?.Id;
                    currentResult.IsStale = prevResult != null && prevResult.IsStale;
                    currentResult.ResultsJson = finalJson;
                    currentResult.CalculatedAt = DateTime.UtcNow;
                    _dbContext.DailyCalculationResults.Update(currentResult);
                }
            }
            
            var isStale = currentResult.IsStale;

            // 4. INPUT KARŞILAŞTIRMASI (Seed 0 vs Null / Missing Fact Analizi)
            var legacyInputDict = legacyInputs
                .GroupBy(x => x.CanonicalKey)
                .ToDictionary(g => g.Key, g => ParseDecimal(g.First().Value));

            var newInputDict = newInputs
                .GroupBy(x => x.CanonicalKey)
                .ToDictionary(g => g.Key, g => ParseDecimal(g.First().Value));

            var allInputKeys = legacyInputDict.Keys.Union(newInputDict.Keys).Distinct().ToList();

            foreach (var key in allInputKeys)
            {
                var hasLegacy = legacyInputDict.TryGetValue(key, out var legVal);
                var hasNew = newInputDict.TryGetValue(key, out var newVal);

                var (severity, rootCause, reasonHint) = AnalyzeInputDrift(hasLegacy, legVal, hasNew, newVal, isStale);

                var absoluteDiff = Math.Abs((legVal ?? 0m) - (newVal ?? 0m));

                drifts.Add(new CalculationParityDrift
                {
                    Id = Guid.NewGuid(),
                    TargetDate = targetDate,
                    KasaScope = kasaScope,
                    FieldKey = $"[INPUT] {key}",
                    LegacyValue = legVal,
                    DataFirstValue = newVal,
                    AbsoluteDifference = absoluteDiff,
                    Severity = severity,
                    RootCauseCategory = rootCause,
                    ReasonHint = reasonHint,
                    DetectedAt = DateTime.UtcNow
                });
            }

            // 5. OUTPUT KARŞILAŞTIRMASI (Calculation Results Analizi)
            var legacyOutputDict = legacyFormulaRun.Outputs;
            var newOutputDict = newRunResult.Value.Outputs;

            var allOutputKeys = legacyOutputDict.Keys.Union(newOutputDict.Keys).Distinct().ToList();

            foreach (var key in allOutputKeys)
            {
                var hasLegacy = legacyOutputDict.TryGetValue(key, out var legVal);
                var hasNew = newOutputDict.TryGetValue(key, out var newVal);

                decimal legValSafe = hasLegacy ? legVal : 0m;
                decimal newValSafe = hasNew ? newVal : 0m;
                var absoluteDiff = Math.Abs(legValSafe - newValSafe);

                var isStale2 = false;
                string? reasonHint = isStale2 ? "PreviousDayStale" : null;
                var rootCause = "Calculation Output Divergence";
                if (!hasLegacy && hasNew) rootCause = "Missing in Legacy Output";
                if (hasLegacy && !hasNew) rootCause = "Missing in DataFirst Output";
                if (absoluteDiff == 0m) rootCause = "Parity Achieved";
                
                // Specific Output reasons
                if (key.Contains("devred", StringComparison.OrdinalIgnoreCase) && absoluteDiff != 0m)
                {
                    reasonHint = "CarryOverMismatch";
                }

                var severity = AbsoluteDiffToSeverity(absoluteDiff);

                drifts.Add(new CalculationParityDrift
                {
                    Id = Guid.NewGuid(),
                    TargetDate = targetDate,
                    KasaScope = kasaScope,
                    FieldKey = $"[OUTPUT] {key}",
                    LegacyValue = hasLegacy ? legVal : null,
                    DataFirstValue = hasNew ? newVal : null,
                    AbsoluteDifference = absoluteDiff,
                    Severity = severity,
                    RootCauseCategory = rootCause,
                    ReasonHint = reasonHint,
                    DetectedAt = DateTime.UtcNow
                });
            }

            if (drifts.Any())
            {
                _dbContext.CalculationParityDrifts.AddRange(drifts);
            }
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Parity Shadow Check failed for {Date} / Scope: {Scope}", targetDate, kasaScope);
        }
    }

    private List<UnifiedPoolEntry> BuildDataFirstInputs(List<DailyFact> facts, List<DailyOverride> overrides, DailyCalculationResult? prevResult)
    {
        var entries = new List<UnifiedPoolEntry>();
        
        // 1. Facts
        foreach(var f in facts)
        {
            if (f.NumericValue.HasValue || !string.IsNullOrWhiteSpace(f.TextValue))
            {
                entries.Add(new UnifiedPoolEntry
                {
                    CanonicalKey = f.CanonicalKey,
                    Value = f.NumericValue?.ToString() ?? f.TextValue ?? "",
                    Type = UnifiedPoolValueType.Raw,
                    IncludeInCalculations = true,
                    SourceName = "DailyFact",
                    SourceFile = f.SourceFileName
                });
            }
        }
        
        // 2. Overrides
        foreach(var o in overrides)
        {
            if (o.NumericValue.HasValue || !string.IsNullOrWhiteSpace(o.TextValue))
            {
                entries.Add(new UnifiedPoolEntry
                {
                    CanonicalKey = o.CanonicalKey,
                    Value = o.NumericValue?.ToString() ?? o.TextValue ?? "",
                    Type = UnifiedPoolValueType.Override,
                    IncludeInCalculations = true,
                    SourceName = "DailyOverride",
                    Notes = o.Reason
                });
            }
        }
        
        // 3. Shadow Chain (Carry-Over) Pumping
        // Legacy system has magical "devreden" fields. In the new engine,
        // we can take the entire Output dictionary of yesterday, and supply them
        // with both raw keys and 'dunden_' or '_devreden' variants, or directly map known fields.
        if (prevResult != null && !string.IsNullOrWhiteSpace(prevResult.ResultsJson))
        {
            try
            {
                var prevOutputs = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, decimal>>(prevResult.ResultsJson);
                if (prevOutputs != null)
                {
                    // For example, legacy devreden_kasa corresponds to yesterday's kasa_toplam etc. 
                    // To handle exactly, we map ALL known outputs to "prev_" 
                    // AND special legacy conventions automatically:
                    foreach(var kv in prevOutputs)
                    {
                        entries.Add(new UnifiedPoolEntry
                        {
                            CanonicalKey = $"prev_{kv.Key}", // e.g. prev_kasa_toplam
                            Value = kv.Value.ToString(),
                            Type = UnifiedPoolValueType.Devreden,
                            IncludeInCalculations = true,
                            SourceName = "CarryOverChain"
                        });
                        
                        // Strict specific bindings (Phase 3 policy)
                        if (kv.Key.Equals("devreden_kasa", StringComparison.OrdinalIgnoreCase))
                        {
                            entries.Add(new UnifiedPoolEntry
                            {
                                CanonicalKey = "devreden_kasa",
                                Value = kv.Value.ToString(),
                                Type = UnifiedPoolValueType.Devreden,
                                IncludeInCalculations = true,
                                SourceName = "CarryOverChain"
                            });
                        }
                    }
                }
            }
            catch { /* json parse error ignore */ }
        }
        
        return entries;
    }

    private decimal? ParseDecimal(string val)
    {
        if (decimal.TryParse(val, out var res)) return res;
        return null;
    }

    private (DriftSeverity, string, string?) AnalyzeInputDrift(bool hasLegacy, decimal? legVal, bool hasNew, decimal? newVal, bool isStale)
    {
        string? reasonHint = isStale ? "PreviousDayStale" : null;

        if (hasLegacy && hasNew)
        {
            var diff = Math.Abs((legVal ?? 0m) - (newVal ?? 0m));
            if (diff == 0) return (DriftSeverity.ExactMatch, "Parity Achieved", reasonHint);
            return (AbsoluteDiffToSeverity(diff), "Value Divergence (Rounding or Source Drift)", "OverrideRipple");
        }
        
        if (hasLegacy && !hasNew)
        {
            if (legVal == 0m) return (DriftSeverity.MinorDrift, "Legacy Zero Seed vs DataFirst Missing", reasonHint);
            if (legVal == null) return (DriftSeverity.ExactMatch, "Legacy Null Seed vs DataFirst Missing", reasonHint);
            return (AbsoluteDiffToSeverity(Math.Abs(legVal.Value)), "Missing Fact in DataFirst", "MissingPreviousResult");
        }
        
        if (!hasLegacy && hasNew)
        {
            if (newVal == 0m) return (DriftSeverity.MinorDrift, "DataFirst Zero Seed vs Legacy Missing", reasonHint);
            return (AbsoluteDiffToSeverity(Math.Abs(newVal ?? 0m)), "Missing in Legacy (Extra Fact)", reasonHint);
        }

        return (DriftSeverity.ExactMatch, "Unknown", null);
    }

    private DriftSeverity AbsoluteDiffToSeverity(decimal diff)
    {
        if (diff == 0m) return DriftSeverity.ExactMatch;
        if (diff <= 1.0m) return DriftSeverity.MinorDrift;
        return DriftSeverity.MajorDrift;
    }
}
