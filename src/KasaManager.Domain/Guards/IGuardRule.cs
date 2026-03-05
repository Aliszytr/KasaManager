using KasaManager.Domain.Calculation;

namespace KasaManager.Domain.Guards;

/// <summary>
/// Guard rule: tutarsızlık tespiti ve gerekirse çıktı düzeltmesi (clamp gibi).
/// </summary>
public interface IGuardRule
{
    string Id { get; }
    GuardSeverity Severity { get; }
    GuardResult? Evaluate(CalculationRun run);
}
