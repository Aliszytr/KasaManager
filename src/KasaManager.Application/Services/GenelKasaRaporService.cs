#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Calculation;
using KasaManager.Domain.Constants;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Reports;
using Microsoft.Extensions.Logging;

namespace KasaManager.Application.Services;

/// <summary>
/// GenelKasaRapor ekranı iş mantığı.
/// Controller'dan çıkarılan BuildCalculationRunAsync ve BuildReportDataAsync burada yaşar.
/// </summary>
public sealed class GenelKasaRaporService : IGenelKasaRaporService
{
    private readonly IKasaDraftService _drafts;
    private readonly IFormulaEngineService _engine;
    private readonly ILogger<GenelKasaRaporService> _log;

    public GenelKasaRaporService(
        IKasaDraftService drafts,
        IFormulaEngineService engine,
        ILogger<GenelKasaRaporService> log)
    {
        _drafts = drafts;
        _engine = engine;
        _log = log;
    }

    /// <inheritdoc />
    public async Task<(CalculationRun? Run, string? Error)> BuildCalculationRunAsync(
        DateOnly? selectedEndDate, decimal? gelmeyenD,
        string uploadFolder, CancellationToken ct)
    {
        var inputsRes = await _drafts.BuildGenelKasaR10EngineInputsAsync(
            selectedEndDate, gelmeyenD, uploadFolder, ct);

        if (!inputsRes.Ok || inputsRes.Value is null)
            return (null, inputsRes.Error ?? "GenelKasa engine inputları üretilemedi.");

        var bundle = inputsRes.Value;

        var set = _engine.GetBuiltInFormulaSets()
            .FirstOrDefault(s => string.Equals(
                s.Id, BuiltInFormulaSetIds.GenelKasaR10, StringComparison.OrdinalIgnoreCase));

        if (set is null)
            return (null, $"Built-in FormulaSet bulunamadı: {BuiltInFormulaSetIds.GenelKasaR10}");

        var runRes = _engine.Run(bundle.BitisTarihi, set, bundle.PoolEntries, overrides: null);
        if (!runRes.Ok || runRes.Value is null)
            return (null, runRes.Error ?? "FormulaEngine.Run başarısız.");

        return (runRes.Value, null);
    }

    /// <inheritdoc />
    public async Task<GenelKasaRaporData> BuildReportDataAsync(
        DateOnly? selectedEndDate, decimal? gelmeyenD,
        string uploadFolder, CancellationToken ct)
    {
        var (run, error) = await BuildCalculationRunAsync(selectedEndDate, gelmeyenD, uploadFolder, ct);
        if (run is null)
        {
            return new GenelKasaRaporData
            {
                Issues = new List<string> { error ?? "Hesaplama başarısız." }
            };
        }

        var inputsRes = await _drafts.BuildGenelKasaR10EngineInputsAsync(
            selectedEndDate, gelmeyenD, uploadFolder, ct);
        var bundle = inputsRes.Value!;

        var issues = bundle.Issues.ToList();

        return new GenelKasaRaporData
        {
            BaslangicTarihi = bundle.BaslangicTarihi,
            BitisTarihi = bundle.BitisTarihi,
            DevredenSonTarihi = bundle.DevredenSonTarihi,

            // inputs
            ToplamTahsilat = IGenelKasaRaporService.GetDecimal(run, KasaCanonicalKeys.ToplamTahsilat, issues) ?? 0m,
            ToplamReddiyat = IGenelKasaRaporService.GetDecimal(run, KasaCanonicalKeys.ToplamReddiyat, issues) ?? 0m,
            KaydenTahsilat = IGenelKasaRaporService.GetDecimal(run, KasaCanonicalKeys.KaydenTahsilat, issues) ?? 0m,
            BankaBakiye = IGenelKasaRaporService.GetDecimal(run, KasaCanonicalKeys.BankaBakiye, issues) ?? 0m,

            // overrides
            Devreden = IGenelKasaRaporService.GetDecimal(run, KasaCanonicalKeys.Devreden, issues) ?? 0m,
            Gelmeyen = IGenelKasaRaporService.GetDecimal(run, KasaCanonicalKeys.GelmeyenD, issues) ?? 0m,
            EksikYadaFazla = IGenelKasaRaporService.GetDecimal(run, KasaCanonicalKeys.EksikFazla, issues) ?? 0m,
            KasaNakit = IGenelKasaRaporService.GetDecimal(run, KasaCanonicalKeys.KasaNakit, issues) ?? 0m,

            // outputs
            TahsilatReddiyatFark = IGenelKasaRaporService.GetDecimal(run, KasaCanonicalKeys.TahRedFark, issues) ?? 0m,
            SonrayaDevredecek = IGenelKasaRaporService.GetDecimal(run, KasaCanonicalKeys.SonrayaDevredecek, issues) ?? 0m,
            MutabakatFarki = IGenelKasaRaporService.GetDecimal(run, KasaCanonicalKeys.MutabakatFarki, issues) ?? 0m,
            GenelKasa = IGenelKasaRaporService.GetDecimal(run, KasaCanonicalKeys.GenelKasa, issues) ?? 0m,

            Issues = issues,
        };
    }
}
