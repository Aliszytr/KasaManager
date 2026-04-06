#nullable enable
using KasaManager.Domain.Calculation;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// GenelKasaRapor ekranının iş mantığı.
/// Controller'dan çıkarılmış hesap ve rapor üretme işlemleri.
/// </summary>
public interface IGenelKasaRaporService
{
    /// <summary>
    /// FormulaEngine pipeline: input üret → built-in set bul → Run.
    /// </summary>
    Task<(CalculationRun? Run, string? Error)> BuildCalculationRunAsync(
        DateOnly? selectedEndDate, decimal? gelmeyenD,
        string uploadFolder, bool confirmBankaDiagnosticOverride = false, CancellationToken ct = default);

    /// <summary>
    /// Tam rapor verisi üretir (GenelKasaRaporData).
    /// </summary>
    Task<GenelKasaRaporData> BuildReportDataAsync(
        DateOnly? selectedEndDate, decimal? gelmeyenD,
        string uploadFolder, bool confirmBankaDiagnosticOverride = false, CancellationToken ct = default);

    /// <summary>
    /// CalculationRun içinden Outputs → Overrides → Inputs sırasıyla değer okur.
    /// Bulamazsa issues'a BLOCKING_MISSING_KEY ekler ve null döner.
    /// </summary>
    static decimal? GetDecimal(CalculationRun run, string key, List<string>? issues = null)
    {
        if (run.Outputs.TryGetValue(key, out var o)) return o;
        if (run.Overrides.TryGetValue(key, out var ov)) return ov;
        if (run.Inputs.TryGetValue(key, out var i)) return i;

        issues?.Add($"BLOCKING_MISSING_KEY: '{key}' not found in Outputs/Overrides/Inputs. Değer üretilemedi (0'a düşülmedi).");
        return null;
    }
}
