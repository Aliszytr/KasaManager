using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Calculation;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.FormulaEngine.Authoring;

namespace KasaManager.Application.Services.DataFirst;

public interface IParityCheckService
{
    /// <summary>
    /// R17 -> Faz 2: Shadow Read / Parity Check.
    /// Eski Snapshot sistemi üzerinden akan legacy input ve outputları,
    /// DataFirst (DailyFacts/DailyOverrides) tabanlı yeni kaynak üzerinden paralel hesaplayarak karşılaştırır.
    /// Farklılıkları veritabanına CalculationParityDrift olarak kalıcı loglar.
    /// </summary>
    Task RunShadowCheckAsync(
        DateOnly targetDate,
        string kasaScope,
        List<UnifiedPoolEntry> legacyInputs,
        CalculationRun legacyFormulaRun,
        FormulaSet formulaSet,
        Dictionary<string, decimal> currentOverrides,
        CancellationToken ct = default);
}
