using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Calculation;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// R16: Formula & Calculation Engine.
/// - UnifiedPool hamını değiştirmez
/// - Override sadece hesap katmanında etki eder
/// - Guard + Explain üretir
/// </summary>
public interface IFormulaEngineService
{
    /// <summary>
    /// Preview için: UnifiedPoolEntry listesinden çalıştır.
    /// </summary>
    Result<CalculationRun> Run(
        DateOnly reportDate,
        FormulaSet formulaSet,
        IReadOnlyList<UnifiedPoolEntry> poolEntries,
        IReadOnlyDictionary<string, decimal>? overrides = null);

    /// <summary>
    /// Bu fazda in-memory kayıtlı hazır formül setleri.
    /// CRUD fazında DB'ye taşınacaktır.
    /// </summary>
    IReadOnlyList<FormulaSet> GetBuiltInFormulaSets();
}
