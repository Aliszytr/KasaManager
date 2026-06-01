#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Application.Services.DataFirst;
using KasaManager.Domain.Projection;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Application.Observability;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace KasaManager.Application.Services;

/// <summary>
/// P2: EksikFazla Projection Engine — shadow implementation.
/// 
/// Legacy ComputeEksikFazlaChainAsync ile birebir aynı formülleri kullanır:
///   guneTahsilat = BankaGiren - ((OnlineTahsilat - OnlineReddiyat) + (Tahsilat - BankayaYatirilacakNakit))
///   guneHarc     = BankaHarcGiren - (OnlineHarc + NormalHarc)
///
/// Farklar:
/// - Iterative (recursive değil)
/// - Snapshot yerine hybrid resolver (CalculatedSnapshot → Excel fallback)
/// - Her gün için ProjectionDayNode audit trail
/// - HesapKontrol düzeltmeleri engine içinde inject edilebilir
/// </summary>
public sealed class EksikFazlaProjectionEngine : IEksikFazlaProjectionEngine
{
    private readonly IBankaHesapKontrolService _hesapKontrol;
    private readonly ILogger<EksikFazlaProjectionEngine> _log;
    private readonly IAlertService _alertService;
    private readonly IProjectionDbReadService? _projectionDbReadService;

    // Excel reader delegates — legacy partial class methods ile aynı
    // Bunlar KasaDraftService'den inject edilemez (private).
    // P2.0: Snapshot-based approach — Excel okumadan snapshot'taki veriyi kullan.

    public EksikFazlaProjectionEngine(
        IBankaHesapKontrolService hesapKontrol,
        ILogger<EksikFazlaProjectionEngine> log,
        IAlertService alertService,
        IProjectionDbReadService? projectionDbReadService = null)
    {
        _hesapKontrol = hesapKontrol;
        _log = log;
        _alertService = alertService;
        _projectionDbReadService = projectionDbReadService;
    }

    public async Task<ProjectionResult> ProjectAsync(
        ProjectionRequest request,
        CancellationToken ct = default)
    {
        try
        {
            // Phase 1: Iterative chain build (bottom-up)
            var chain = await BuildChainIterativeAsync(request, ct);

            // Phase 2: Compute carry-forward (top-down)
            var (oncekiT, dundenT, guneT, oncekiH, dundenH, guneH) = ComputeCarryForward(chain);

            // Phase 3: HesapKontrol düzeltmeleri
            bool hkApplied = false;
            (oncekiT, dundenT, oncekiH, dundenH, hkApplied) = await ApplyHesapKontrolAsync(
                request.TargetDate, request.MaxLookbackDays,
                oncekiT, dundenT, oncekiH, dundenH, ct);

            return new ProjectionResult(
                TargetDate: request.TargetDate,
                Ok: true,
                OncekiTahsilat: oncekiT,
                DundenTahsilat: dundenT,
                GuneTahsilat: guneT,
                OncekiHarc: oncekiH,
                DundenHarc: dundenH,
                GuneHarc: guneH,
                HesapKontrolApplied: hkApplied,
                Chain: chain);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "EksikFazla Projection Engine hatası — shadow sonuç üretilemedi");
            return new ProjectionResult(
                TargetDate: request.TargetDate,
                Ok: false,
                OncekiTahsilat: 0m, DundenTahsilat: 0m, GuneTahsilat: 0m,
                OncekiHarc: 0m, DundenHarc: 0m, GuneHarc: 0m,
                HesapKontrolApplied: false,
                Chain: new List<ProjectionDayNode>(),
                ErrorMessage: ex.Message);
        }
    }

    /// <summary>
    /// Iterative chain build: TargetDate, TargetDate-1, ..., TargetDate-MaxLookbackDays
    /// P2.1: InputProvider varsa onu kullanır (Excel + snapshot tam veri).
    /// Yoksa kendi snapshot-only resolver'ını kullanır (P2.0 fallback).
    /// </summary>
    private async Task<List<ProjectionDayNode>> BuildChainIterativeAsync(
        ProjectionRequest request, CancellationToken ct)
    {
        var chain = new List<ProjectionDayNode>();

        for (int depth = 0; depth <= request.MaxLookbackDays; depth++)
        {
            var d = request.TargetDate.AddDays(-depth);

            // P2.1: Prefer external InputProvider (full data) → fallback to snapshot-only
            var input = request.InputProvider is not null
                ? await request.InputProvider(d, ct)
                : await ResolveDayInputAsync(d, request.UploadFolderAbsolute, ct);

            if (input is null)
            {
                ShadowMetrics.IncrementMissingInput();
                _log.LogWarning(new EventId(5001, "ShadowInputMissing"), "SHADOW_INPUT_MISSING | Date: {Date} | Fallback/Snapshot YASAK olduğu için hesaplama iptal ediliyor.", d);
                await _alertService.TriggerAsync("SHADOW_INPUT_MISSING", $"Shadow engine {d} için girdi verisi (InputProvider) bulamadı.");

                chain.Add(new ProjectionDayNode(
                    Date: d, Depth: depth,
                    Source: ProjectionDaySource.NoData,
                    RawGuneTahsilat: 0m, RawGuneHarc: 0m,
                    OncekiTahsilat: 0m, DundenTahsilat: 0m,
                    OncekiHarc: 0m, DundenHarc: 0m,
                    HasData: false));
                // Chain kırılıyor — legacy ile aynı davranış
                break;
            }

            // GÖREV 4: Metrik - SuccessCount++
            ShadowMetrics.IncrementSuccess();

            // Legacy formül parity:
            // guneTahsilat = BankaGiren - ((OnlineTahsilat - OnlineReddiyat) + (Tahsilat - BankayaYatirilacakNakit))
            var guneTahsilat = input.BankaGirenTahsilat
                - ((input.OnlineTahsilat - input.OnlineReddiyat)
                   + (input.ToplamTahsilat - input.BankayaYatirilacakNakit));

            // guneHarc = BankaHarcGiren - (OnlineHarc + NormalHarc)
            var guneHarc = input.BankaGirenHarc
                - (input.OnlineHarc + input.NormalHarc);

            chain.Add(new ProjectionDayNode(
                Date: d, Depth: depth,
                Source: input.Source,
                RawGuneTahsilat: guneTahsilat,
                RawGuneHarc: guneHarc,
                // Carry-forward henüz hesaplanmadı — Phase 2'de doldurulacak
                OncekiTahsilat: 0m, DundenTahsilat: 0m,
                OncekiHarc: 0m, DundenHarc: 0m,
                HasData: true));
        }

        return chain;
    }

    /// <summary>
    /// Legacy carry-forward mantığı:
    /// - oncekiT[i] = prev.OncekiT + prev.DundenT
    /// - dundenT[i] = prev.GuneT
    ///
    /// Chain: [today, yesterday, day-before, ...]
    /// Legacy recurse: today calls yesterday which calls day-before ...
    /// Base case (most distant day): onceki=0, dunden=0
    ///
    /// Iterative eşdeğeri: en eski günden başla, yukarı doğru propagate et.
    /// </summary>
    private static (decimal OncekiT, decimal DundenT, decimal GuneT,
                     decimal OncekiH, decimal DundenH, decimal GuneH)
        ComputeCarryForward(List<ProjectionDayNode> chain)
    {
        if (chain.Count == 0)
            return (0m, 0m, 0m, 0m, 0m, 0m);

        // En eski gün (chain sonunda) → base case
        decimal prevOncekiT = 0m, prevDundenT = 0m, prevGuneT = 0m;
        decimal prevOncekiH = 0m, prevDundenH = 0m, prevGuneH = 0m;

        // Reverse (en eski → en yeni) = bottom-up propagation
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            var node = chain[i];

            // Legacy carry-forward:
            var oncekiT = prevOncekiT + prevDundenT;
            var dundenT = prevGuneT;
            var oncekiH = prevOncekiH + prevDundenH;
            var dundenH = prevGuneH;

            // Update node with carry-forward values (immutable record → reconstruct)
            chain[i] = node with
            {
                OncekiTahsilat = oncekiT,
                DundenTahsilat = dundenT,
                OncekiHarc = oncekiH,
                DundenHarc = dundenH
            };

            // Prepare for next iteration (one day newer)
            prevOncekiT = oncekiT;
            prevDundenT = dundenT;
            prevGuneT = node.RawGuneTahsilat;
            prevOncekiH = oncekiH;
            prevDundenH = dundenH;
            prevGuneH = node.RawGuneHarc;
        }

        // chain[0] = today → final result
        var today = chain[0];
        return (today.OncekiTahsilat, today.DundenTahsilat, today.RawGuneTahsilat,
                today.OncekiHarc, today.DundenHarc, today.RawGuneHarc);
    }

    /// <summary>
    /// GÖREV 2: Deterministik Fallback Guard + Observability
    /// Snapshot veya DB fallback YASAK. Eğer input yoksa, anında log atar ve NULL döner.
    /// Shadow fail ederek sessiz davranışı (silent failure) engeller.
    /// </summary>
    private async Task<ProjectionDayInput?> ResolveDayInputAsync(
        DateOnly date, string uploadFolderAbsolute, CancellationToken ct)
    {
        const string preferredScope = "Sabah";
        try
        {
            if (_projectionDbReadService is null)
            {
                _log.LogWarning("[PROJECTION-DB-RESOLVER] Date={Date} Scope={Scope} Result=NullBecauseNoDbData Reason=NoReadService uploadHint={UploadHint}",
                    date, preferredScope, uploadFolderAbsolute);
                return null;
            }

            var snapshot = await _projectionDbReadService.GetSnapshotAsync(date, preferredScope, ct);
            var resultMap = ParseResultMap(snapshot.DailyResult?.ResultsJson);
            var factMap = BuildFactMap(snapshot.DailyFacts);
            var overrideMap = BuildOverrideMap(snapshot.DailyOverrides);

            var missing = new List<string>();
            var traces = new List<ProjectionFieldTrace>(9);
            var bankaGirenTahsilat = ResolveRequiredDecimalWithTrace("BankaGirenTahsilat", "bankaya_giren_tahsilat", resultMap, factMap, overrideMap, missing, traces);
            var bankaGirenHarc = ResolveRequiredDecimalWithTrace("BankaGirenHarc", "bankaya_giren_harc", resultMap, factMap, overrideMap, missing, traces);
            var toplamTahsilat = ResolveRequiredDecimalWithTrace("ToplamTahsilat", "toplam_tahsilat", resultMap, factMap, overrideMap, missing, traces);
            var onlineTahsilat = ResolveRequiredDecimalWithTrace("OnlineTahsilat", "online_tahsilat", resultMap, factMap, overrideMap, missing, traces);
            var toplamHarc = ResolveRequiredDecimalWithTrace("ToplamHarc", "toplam_harc", resultMap, factMap, overrideMap, missing, traces);
            var onlineReddiyat = ResolveRequiredDecimalWithTrace("OnlineReddiyat", "online_reddiyat", resultMap, factMap, overrideMap, missing, traces);
            var onlineHarc = ResolveRequiredDecimalWithTrace("OnlineHarc", "online_harcama", resultMap, factMap, overrideMap, missing, traces);
            var bankayaYatirilacakNakit = ResolveRequiredDecimalWithTrace("BankayaYatirilacakNakit", "bankaya_yatirilacak_nakit", resultMap, factMap, overrideMap, missing, traces);
            var normalHarc = ResolveRequiredDecimalWithTrace("NormalHarc", "normal_harc", resultMap, factMap, overrideMap, missing, traces);

            var traceSummary = BuildTraceSummary(traces);

            if (snapshot.DailyResult is null && snapshot.DailyFacts.Count == 0 && snapshot.DailyOverrides.Count == 0)
            {
                _log.LogWarning("[PROJECTION-DB-RESOLVER] Date={Date} Scope={Scope} DailyCalculationResultFound={ResultFound} DailyFactsCount={FactsCount} DailyOverridesCount={OverridesCount} MissingFields={MissingFields} FieldTrace={FieldTrace} Result=NullBecauseNoDbData",
                    date, snapshot.ScopeUsed, false, snapshot.DailyFacts.Count, snapshot.DailyOverrides.Count, string.Join(",", missing), traceSummary);
                return null;
            }

            if (missing.Count > 0)
            {
                _log.LogWarning("[PROJECTION-DB-RESOLVER] Date={Date} Scope={Scope} DailyCalculationResultFound={ResultFound} DailyFactsCount={FactsCount} DailyOverridesCount={OverridesCount} MissingFields={MissingFields} FieldTrace={FieldTrace} Result=NullBecauseMissingFields",
                    date, snapshot.ScopeUsed, snapshot.DailyResult is not null, snapshot.DailyFacts.Count, snapshot.DailyOverrides.Count, string.Join(",", missing), traceSummary);
                return null;
            }

            _log.LogInformation("[PROJECTION-DB-RESOLVER] Date={Date} Scope={Scope} DailyCalculationResultFound={ResultFound} DailyFactsCount={FactsCount} DailyOverridesCount={OverridesCount} MissingFields={MissingFields} FieldTrace={FieldTrace} Result=Success",
                date, snapshot.ScopeUsed, snapshot.DailyResult is not null, snapshot.DailyFacts.Count, snapshot.DailyOverrides.Count, string.Empty, traceSummary);

            return new ProjectionDayInput(
                Date: date,
                BankaGirenTahsilat: bankaGirenTahsilat,
                BankaGirenHarc: bankaGirenHarc,
                ToplamTahsilat: toplamTahsilat,
                OnlineTahsilat: onlineTahsilat,
                ToplamHarc: toplamHarc,
                OnlineReddiyat: onlineReddiyat,
                OnlineHarc: onlineHarc,
                BankayaYatirilacakNakit: bankayaYatirilacakNakit,
                NormalHarc: normalHarc,
                Source: ProjectionDaySource.CalculatedSnapshot);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[PROJECTION-DB-RESOLVER] Date={Date} Scope={Scope} Result=Error", date, preferredScope);
            return null;
        }
    }

    /// <summary>
    /// HesapKontrol düzeltmeleri — legacy ile birebir aynı mantık.
    /// Çözülen/Onaylanan kayıtları önce Dünden'den, kalan varsa Önceki'den düş.
    /// </summary>
    private async Task<(decimal OncekiT, decimal DundenT, decimal OncekiH, decimal DundenH, bool Applied)>
        ApplyHesapKontrolAsync(
            DateOnly targetDate, int maxLookback,
            decimal oncekiT, decimal dundenT,
            decimal oncekiH, decimal dundenH,
            CancellationToken ct)
    {
        try
        {
            var lookbackStart = targetDate.AddDays(-maxLookback);
            var yesterday = targetDate.AddDays(-1);

            var resolved = await _hesapKontrol.GetHistoryAsync(
                lookbackStart, yesterday,
                hesapTuru: null, durum: KayitDurumu.Cozuldu, ct);

            var approved = await _hesapKontrol.GetHistoryAsync(
                lookbackStart, yesterday,
                hesapTuru: null, durum: KayitDurumu.Onaylandi, ct);

            var allResolved = resolved.Concat(approved).ToList();
            if (allResolved.Count == 0)
                return (oncekiT, dundenT, oncekiH, dundenH, false);

            var totalResolvedTahsilat = allResolved
                .Where(k => k.HesapTuru == BankaHesapTuru.Tahsilat && k.Yon == KayitYonu.Eksik)
                .Sum(k => k.Tutar);

            var totalResolvedHarc = allResolved
                .Where(k => k.HesapTuru == BankaHesapTuru.Harc && k.Yon == KayitYonu.Eksik)
                .Sum(k => k.Tutar);

            // Önce Dünden'den düş, kalan varsa Önceki'den düş
            var remainT = totalResolvedTahsilat;
            var adjDundenT = Math.Max(0m, dundenT - remainT);
            remainT = Math.Max(0m, remainT - dundenT);
            var adjOncekiT = Math.Max(0m, oncekiT - remainT);

            var remainH = totalResolvedHarc;
            var adjDundenH = Math.Max(0m, dundenH - remainH);
            remainH = Math.Max(0m, remainH - dundenH);
            var adjOncekiH = Math.Max(0m, oncekiH - remainH);

            return (adjOncekiT, adjDundenT, adjOncekiH, adjDundenH, true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "EksikFazla Projection: HesapKontrol çözülen kayıtları alınamadı — ham zincir kullanılacak");
            return (oncekiT, dundenT, oncekiH, dundenH, false);
        }
    }

    // ─── Helpers ───

    private static Dictionary<string, string> ParseColumnsJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            return System.Text.Json.JsonSerializer
                .Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static decimal GetDecimal(Dictionary<string, string> dict, string key)
    {
        if (!dict.TryGetValue(key, out var val) || string.IsNullOrWhiteSpace(val))
            return 0m;

        // InvariantCulture: "1,234.56" → 1234.56
        if (decimal.TryParse(val, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d))
            return d;

        // TurkishCulture fallback: "1.234,56" → 1234.56
        if (decimal.TryParse(val, System.Globalization.NumberStyles.Any,
            new System.Globalization.CultureInfo("tr-TR"), out var d2))
            return d2;

        return 0m;
    }

    private static Dictionary<string, decimal> ParseResultMap(string? resultsJson)
    {
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(resultsJson)) return map;

        try
        {
            using var doc = JsonDocument.Parse(resultsJson);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDecimal(out var num))
                {
                    map[prop.Name] = num;
                }
                else if (prop.Value.ValueKind == JsonValueKind.String
                         && decimal.TryParse(prop.Value.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    map[prop.Name] = parsed;
                }
            }
        }
        catch
        {
            // parse error intentionally ignored; caller handles missing fields.
        }

        return map;
    }

    private static Dictionary<string, decimal> BuildFactMap(IReadOnlyList<Domain.Calculation.Data.DailyFact> facts)
    {
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var fact in facts)
        {
            if (!fact.NumericValue.HasValue || string.IsNullOrWhiteSpace(fact.CanonicalKey))
                continue;

            var key = fact.CanonicalKey.Trim();
            map[key] = fact.NumericValue.Value;
            var idx = key.IndexOf('_');
            if (idx >= 0 && idx < key.Length - 1)
            {
                var suffix = key[(idx + 1)..];
                map[suffix] = fact.NumericValue.Value;
            }
        }

        return map;
    }

    private static Dictionary<string, decimal> BuildOverrideMap(IReadOnlyList<Domain.Calculation.Data.DailyOverride> overrides)
    {
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in overrides)
        {
            if (string.IsNullOrWhiteSpace(o.CanonicalKey)) continue;
            if (o.NumericValue.HasValue)
            {
                map[o.CanonicalKey] = o.NumericValue.Value;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(o.TextValue)
                && decimal.TryParse(o.TextValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                map[o.CanonicalKey] = parsed;
            }
        }

        return map;
    }

    private static decimal ResolveRequiredDecimal(
        string key,
        IReadOnlyDictionary<string, decimal> resultMap,
        IReadOnlyDictionary<string, decimal> factMap,
        IReadOnlyDictionary<string, decimal> overrideMap,
        ICollection<string> missing)
    {
        // Priority: DailyOverrides > DailyCalculationResults > DailyFacts
        if (overrideMap.TryGetValue(key, out var ov)) return ov;
        if (resultMap.TryGetValue(key, out var fromResult)) return fromResult;
        if (factMap.TryGetValue(key, out var fromFact)) return fromFact;

        missing.Add(key);
        return 0m;
    }

    private static decimal ResolveRequiredDecimalWithTrace(
        string fieldName,
        string key,
        IReadOnlyDictionary<string, decimal> resultMap,
        IReadOnlyDictionary<string, decimal> factMap,
        IReadOnlyDictionary<string, decimal> overrideMap,
        ICollection<string> missing,
        ICollection<ProjectionFieldTrace> traces)
    {
        var candidates = new[] { key };
        if (overrideMap.TryGetValue(key, out var ov))
        {
            traces.Add(new ProjectionFieldTrace(fieldName, candidates, key, ov, false));
            return ov;
        }

        if (resultMap.TryGetValue(key, out var fromResult))
        {
            traces.Add(new ProjectionFieldTrace(fieldName, candidates, key, fromResult, false));
            return fromResult;
        }

        if (factMap.TryGetValue(key, out var fromFact))
        {
            traces.Add(new ProjectionFieldTrace(fieldName, candidates, key, fromFact, false));
            return fromFact;
        }

        missing.Add(key);
        traces.Add(new ProjectionFieldTrace(fieldName, candidates, null, null, true));
        return 0m;
    }

    private static string BuildTraceSummary(IEnumerable<ProjectionFieldTrace> traces)
    {
        return string.Join(";", traces.Select(t =>
            $"{t.Field}|c=[{string.Join(",", t.Candidates)}]|f={(t.FoundKey ?? "Missing")}|v={(t.FoundValue.HasValue ? t.FoundValue.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "Missing")}"));
    }

    private sealed record ProjectionFieldTrace(
        string Field,
        IReadOnlyList<string> Candidates,
        string? FoundKey,
        decimal? FoundValue,
        bool Missing);
}
