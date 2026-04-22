using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Constants;
using KasaManager.Domain.Reports;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using KasaManager.Application.Services.Draft.Helpers;

namespace KasaManager.Infrastructure.Services;

public sealed class CarryoverResolver : ICarryoverResolver
{
    private readonly IKasaGlobalDefaultsService _defaults;
    private readonly KasaManagerDbContext _dbContext;
    private readonly ILogger<CarryoverResolver> _log;

    public CarryoverResolver(
        IKasaGlobalDefaultsService defaults,
        KasaManagerDbContext dbContext,
        ILogger<CarryoverResolver> log)
    {
        _defaults = defaults;
        _dbContext = dbContext;
        _log = log;
    }

    public async Task<CarryoverResolutionResult> ResolveAsync(DateOnly targetDate, CarryoverScope scope, CancellationToken ct = default)
    {
        return scope switch
        {
            CarryoverScope.GenelKasa => await ResolveGenelKasaAsync(targetDate, ct),
            CarryoverScope.AksamKasaNakit => await ResolveAksamKasaNakitAsync(targetDate, ct),
            CarryoverScope.SabahKasaNakit => await ResolveSabahKasaNakitAsync(targetDate, ct),
            CarryoverScope.VergiKasaSelectionTotal => await ResolveVergiKasaSelectionTotalAsync(targetDate, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
        };
    }

    private async Task<CarryoverResolutionResult> ResolveSabahKasaNakitAsync(DateOnly targetDate, CancellationToken ct)
    {
        var settings = await _defaults.GetOrCreateAsync(ct);
        var overrideVal = settings.DefaultDundenDevredenKasaNakit;
        var prev = targetDate.AddDays(-1);

        _log.LogDebug("[CarryoverDebug] ResolveSabahKasaNakitAsync aranan_tarih: {Date}, Tur: Sabah. Gelen DefaultDundenDevreden (Override): {Override}", prev, overrideVal);

        if (overrideVal.HasValue && overrideVal.Value != 0m)
        {
            _log.LogDebug("[CarryoverDebug] Override ayarlari gecerli oldugu icin SeedOverride branchine girildi.");
            _log.LogInformation("[CarryoverResolver] Scope: {Scope}, Source: {Source}, Value: {Value}, SourceDate: {SourceDate}", 
                "SabahKasaNakit", "SeedOverride", overrideVal.Value, prev);
            return new CarryoverResolutionResult(
                Value: overrideVal.Value,
                TargetKey: "dunden_devreden_kasa_nakit",
                RangeStart: targetDate,
                SourceDate: prev,
                SourceCode: "SeedOverride",
                Reason: "Ayarlardaki DÃ¼nden Devreden Kasa Nakit deÄŸeri > 0 olduÄŸu iÃ§in override edildi.",
                UsedFallback: false
            );
        }

        _log.LogDebug("[CarryoverDebug] DB DailyCalculationResults kontrolune gecildi.");
        var dbRecord = await _dbContext.DailyCalculationResults
            .Where(r => r.ForDate == prev && r.KasaTuru == "Aksam")
            .FirstOrDefaultAsync(ct);

        _log.LogDebug("[CarryoverDebug] DB sorgu sonucu - Bulundu: {Found}. KayÄ±t detayi (ilk 200 karakter): {Record}", 
            dbRecord != null, 
            dbRecord?.ResultsJson?.Substring(0, Math.Min(200, dbRecord.ResultsJson?.Length ?? 0)));

        decimal foundDevreden = 0m;
        string sourceCode = "DefaultZero";
        bool usedFallback = true;
        string reason = "Ã–nceki gÃ¼n Sabah result kaydÄ± bulunamadÄ±. 0 kullanÄ±lÄ±yor.";

        if (dbRecord != null && !string.IsNullOrWhiteSpace(dbRecord.ResultsJson))
        {
            foundDevreden = ExtractDevredenKasa(dbRecord.ResultsJson, prev.ToString(), "Aksam");
            sourceCode = "DailyCalculationResult";
            reason = $"{prev:dd.MM.yyyy} tarihli AkÅŸam hesaplama sonucundan devreden okundu.";
            usedFallback = false;
        }
        else
        {
            // FALLBACK: DailyCalculationResults'ta kayÄ±t yoksa CalculatedKasaSnapshots'a bak
            // (Eski versiyon projeden kaydedilmiÅŸ kasalar iÃ§in)
            var cksResult = await TryExtractFromCalculatedKasaSnapshotAsync(prev, KasaRaporTuru.Aksam, ct);
            if (cksResult.HasValue)
            {
                foundDevreden = cksResult.Value;
                sourceCode = "CalculatedKasaSnapshot";
                reason = $"{prev:dd.MM.yyyy} tarihli AkÅŸam CalculatedKasaSnapshot'tan devreden okundu.";
                usedFallback = false;
            }
        }

        _log.LogDebug("[CarryoverDebug] Sabah final donen devreden deger: {Value}", foundDevreden);
        _log.LogInformation("[CarryoverResolver] Scope: {Scope}, Source: {Source}, Value: {Value}, SourceDate: {SourceDate}", 
            "SabahKasaNakit", sourceCode, foundDevreden, prev);

        return new CarryoverResolutionResult(
            Value: foundDevreden,
            TargetKey: "dunden_devreden_kasa_nakit",
            RangeStart: targetDate,
            SourceDate: prev,
            SourceCode: sourceCode,
            Reason: reason,
            UsedFallback: usedFallback
        );
    }

    private async Task<CarryoverResolutionResult> ResolveAksamKasaNakitAsync(DateOnly targetDate, CancellationToken ct)
    {
        var settings = await _defaults.GetOrCreateAsync(ct);
        var overrideVal = settings.DefaultDundenDevredenKasaNakit;
        var prev = targetDate.AddDays(-1);
        
        _log.LogDebug("[CarryoverDebug] ResolveAksamKasaNakitAsync aranan_tarih: {Date}, Tur: Aksam. Gelen DefaultDundenDevreden (Override): {Override}", prev, overrideVal);

        if (overrideVal.HasValue && overrideVal.Value != 0m)
        {
            _log.LogDebug("[CarryoverDebug] Override ayarlari gecerli oldugu icin SeedOverride branchine girildi.");
            _log.LogInformation("[CarryoverResolver] Scope: {Scope}, Source: {Source}, Value: {Value}, SourceDate: {SourceDate}", 
                "AksamKasaNakit", "SeedOverride", overrideVal.Value, prev);
            return new CarryoverResolutionResult(
                Value: overrideVal.Value,
                TargetKey: "dunden_devreden_kasa_nakit",
                RangeStart: targetDate,
                SourceDate: prev,
                SourceCode: "SeedOverride",
                Reason: "Ayarlardaki DÃ¼nden Devreden Kasa Nakit deÄŸeri > 0 olduÄŸu iÃ§in override edildi.",
                UsedFallback: false
            );
        }

        _log.LogDebug("[CarryoverDebug] DB DailyCalculationResults kontrolune gecildi.");
        var dbRecord = await _dbContext.DailyCalculationResults
            .Where(r => r.ForDate == prev && r.KasaTuru == "Aksam")
            .FirstOrDefaultAsync(ct);

        _log.LogDebug("[CarryoverDebug] DB sorgu sonucu - Bulundu: {Found}. KayÄ±t detayi (ilk 200 karakter): {Record}", 
            dbRecord != null, 
            dbRecord?.ResultsJson?.Substring(0, Math.Min(200, dbRecord.ResultsJson?.Length ?? 0)));

        decimal foundDevreden = 0m;
        string sourceCode = "DefaultZero";
        bool usedFallback = true;
        string reason = "Ã–nceki gÃ¼n AkÅŸam result kaydÄ± bulunamadÄ±. 0 kullanÄ±lÄ±yor.";

        if (dbRecord != null && !string.IsNullOrWhiteSpace(dbRecord.ResultsJson))
        {
            foundDevreden = ExtractDevredenKasa(dbRecord.ResultsJson, prev.ToString(), "Aksam");
            sourceCode = "DailyCalculationResult";
            reason = $"{prev:dd.MM.yyyy} tarihli AkÅŸam hesaplama sonucundan devreden okundu.";
            usedFallback = false;
        }
        else
        {
            // FALLBACK: DailyCalculationResults'ta kayÄ±t yoksa CalculatedKasaSnapshots'a bak
            var cksResult = await TryExtractFromCalculatedKasaSnapshotAsync(prev, KasaRaporTuru.Aksam, ct);
            if (cksResult.HasValue)
            {
                foundDevreden = cksResult.Value;
                sourceCode = "CalculatedKasaSnapshot";
                reason = $"{prev:dd.MM.yyyy} tarihli AkÅŸam CalculatedKasaSnapshot'tan devreden okundu.";
                usedFallback = false;
            }
        }

        _log.LogDebug("[CarryoverDebug] Aksam final donen devreden deger: {Value}", foundDevreden);
        _log.LogInformation("[CarryoverResolver] Scope: {Scope}, Source: {Source}, Value: {Value}, SourceDate: {SourceDate}", 
            "AksamKasaNakit", sourceCode, foundDevreden, prev);

        return new CarryoverResolutionResult(
            Value: foundDevreden,
            TargetKey: "dunden_devreden_kasa_nakit",
            RangeStart: targetDate,
            SourceDate: prev,
            SourceCode: sourceCode,
            Reason: reason,
            UsedFallback: usedFallback
        );
    }

    private async Task<CarryoverResolutionResult> ResolveGenelKasaAsync(DateOnly targetDate, CancellationToken ct)
    {
        var settings = await _defaults.GetOrCreateAsync(ct);
        var seedValue = settings.DefaultGenelKasaDevredenSeed;
        var seedStart = settings.DefaultGenelKasaBaslangicTarihiSeed is DateTime dt ? DateOnly.FromDateTime(dt) : targetDate;
        
        _log.LogDebug("[CarryoverDebug] ResolveGenelKasaAsync. SeedStart: {Date}. Gelen DefaultGenelKasaDevredenSeed (Override): {Override}", seedStart, seedValue);

        if (seedValue.HasValue && seedValue.Value != 0m)
        {
            _log.LogDebug("[CarryoverDebug] Override ayarlari gecerli oldugu icin SeedOverride branchine girildi.");
            _log.LogInformation("[CarryoverResolver] Scope: {Scope}, Source: {Source}, Value: {Value}, SourceDate: {SourceDate}", 
                "GenelKasa", "SeedOverride", seedValue.Value, seedStart.AddDays(-1));
            return new CarryoverResolutionResult(
                Value: seedValue.Value,
                TargetKey: "genel_kasa_devreden_seed",
                RangeStart: seedStart,
                SourceDate: seedStart.AddDays(-1),
                SourceCode: "SeedOverride",
                Reason: "Ayarlardaki Genel Kasa Devreden (Seed) deÄŸeri > 0 olduÄŸu iÃ§in override kabul edildi.",
                UsedFallback: false
            );
        }

        var searchBeforeDate = seedStart;

        _log.LogDebug("[CarryoverDebug] DB DailyCalculationResults genel kasa geriye donuk arama basladi (Tarih oncesi: {Date}).", searchBeforeDate);
        var dbRecord = await _dbContext.DailyCalculationResults
            .Where(r => r.KasaTuru == "Genel" && r.ForDate < searchBeforeDate)
            .OrderByDescending(r => r.ForDate)
            .FirstOrDefaultAsync(ct);

        _log.LogDebug("[CarryoverDebug] DB sorgu sonucu - Bulundu: {Found}. KayÄ±t detayi (ilk 200 karakter): {Record}", 
            dbRecord != null, 
            dbRecord?.ResultsJson?.Substring(0, Math.Min(200, dbRecord.ResultsJson?.Length ?? 0)));

        decimal foundDevreden = 0m;
        string sourceCode = "DefaultZero";
        DateOnly sourceDate = searchBeforeDate.AddDays(-1);
        string reason = "DÃ¶nem Ã¶ncesine ait Genel Kasa result kaydÄ± bulunamadÄ±. 0 kullanÄ±lÄ±yor.";

        if (dbRecord != null && !string.IsNullOrWhiteSpace(dbRecord.ResultsJson))
        {
            foundDevreden = ExtractDevredenKasa(dbRecord.ResultsJson, searchBeforeDate.ToString(), "Genel");
            sourceCode = "DailyCalculationResult";
            sourceDate = dbRecord.ForDate;
            reason = $"DÃ¶nem baÅŸlangÄ±cÄ±ndan Ã¶nceki en son Genel Kasa kaydÄ±ndan ({sourceDate:dd.MM.yyyy}) devreden okundu.";
        }
        else
        {
            // FALLBACK: DailyCalculationResults'ta kayÄ±t yoksa CalculatedKasaSnapshots'a bak
            // Genel Kasa iÃ§in baÅŸlangÄ±Ã§ tarihinden Ã¶nceki en son kaydedilmiÅŸ Genel Kasa raporu
            var cksRecord = await _dbContext.CalculatedKasaSnapshots
                .Where(x => x.KasaTuru == KasaRaporTuru.Genel
                         && x.RaporTarihi < searchBeforeDate
                         && x.IsActive && !x.IsDeleted)
                .OrderByDescending(x => x.RaporTarihi)
                .FirstOrDefaultAsync(ct);

            if (cksRecord != null && !string.IsNullOrWhiteSpace(cksRecord.OutputsJson))
            {
                foundDevreden = ExtractDevredenKasa(cksRecord.OutputsJson, cksRecord.RaporTarihi.ToString(), "Genel");
                sourceCode = "CalculatedKasaSnapshot";
                sourceDate = cksRecord.RaporTarihi;
                reason = $"DÃ¶nem baÅŸlangÄ±cÄ±ndan Ã¶nceki en son Genel Kasa CalculatedKasaSnapshot'tan ({sourceDate:dd.MM.yyyy}) devreden okundu.";
                _log.LogDebug("[CarryoverDebug] Genel Kasa CKS fallback basarili. CKS tarih: {Date}, Deger: {Value}", sourceDate, foundDevreden);
            }
        }

        _log.LogDebug("[CarryoverDebug] Genel final donen devreden deger: {Value}", foundDevreden);
        _log.LogInformation("[CarryoverResolver] Scope: {Scope}, Source: {Source}, Value: {Value}, SourceDate: {SourceDate}", 
            "GenelKasa", sourceCode, foundDevreden, sourceDate);

        return new CarryoverResolutionResult(
            Value: foundDevreden,
            TargetKey: "genel_kasa_devreden_seed",
            RangeStart: sourceDate.AddDays(1),
            SourceDate: sourceDate,
            SourceCode: sourceCode,
            Reason: reason,
            UsedFallback: sourceCode == "DefaultZero"
        );
    }

    private decimal ExtractDevredenKasa(string json, string searchDate, string kasaTuru)
    {
        try 
        {
            var outputs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (outputs == null) return 0m;

            var ciOutputs = new Dictionary<string, JsonElement>(outputs, StringComparer.OrdinalIgnoreCase);
            
            // KRÄ°TÄ°K: Genel Kasa ve Sabah/AkÅŸam kasalar iÃ§in FARKLI aday listesi!
            // Genel Kasa: "sonraya_devredecek" â‰  "genel_kasa"
            //   sonraya_devredecek = Devreden + TahRedFark - GelmeyenD (bir sonraki dÃ¶neme devredecek miktar)
            //   genel_kasa = Devreden + EksikFazla + TahRedFark - BankaBakiye - KasaNakit - GelmeyenD (kasa hesaplama sonucu)
            // Sabah/AkÅŸam: "genel_kasa" = kasadaki nakit = ertesi gÃ¼ne devredecek miktar (aynÄ± deÄŸer)
            string[] candidates;
            if (string.Equals(kasaTuru, "Genel", StringComparison.OrdinalIgnoreCase))
            {
                // Genel Kasa: SADECE sonraya_devredecek aramalÄ±, genel_kasa YANLIÅ deÄŸer!
                candidates = new[] { "sonraki_kasaya_devredecek", "SonrayaDevredecek", "sonraya_devredecek", "devreden_kasa" };
            }
            else
            {
                // Sabah/AkÅŸam: kasadaki nakit = genel_kasa = sonraya devredecek
                candidates = new[] { "sonraki_kasaya_devredecek", "SonrayaDevredecek", "GenelKasa", "genel_kasa", "devreden_kasa", "sonraya_devredecek", "KasaSonDurum.GenelKasa" };
            }

            foreach (var key in candidates)
            {
                if (ciOutputs.TryGetValue(key, out var el))
                {
                    decimal d = 0m;
                    if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out d)) 
                    {
                        _log.LogDebug("[CarryoverDebug] JSON'dan '{Key}' basariyla Number olarak devreden cekildi: {Value} (KasaTuru={KasaTuru})", key, d, kasaTuru);
                        return d;
                    }
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        var str = el.GetString();
                        if (DecimalParsingHelper.TryParseFromJson(str, out d))
                        {
                            _log.LogDebug("[CarryoverDebug] JSON'dan '{Key}' basariyla String olarak devreden cekildi: {Value} (KasaTuru={KasaTuru})", key, d, kasaTuru);
                            return d;
                        }
                    }
                }
            }
            _log.LogDebug("[CarryoverDebug] JSON icinde gecerli bir devreden_kasa keyi bulunamadi (KasaTuru={KasaTuru}). Mevcut keyler: {Keys}", kasaTuru, string.Join(", ", ciOutputs.Keys));
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "[CarryoverDebug] ResultsJson parse hatasÄ±.");
        }
        return 0m;
    }

    /// <summary>
    /// FALLBACK: DailyCalculationResults'ta kayÄ±t yoksa CalculatedKasaSnapshots'tan oku.
    /// Eski versiyondan kaydedilmiÅŸ kasalar bu tabloda bulunur.
    /// Kasadaki nakit = genel_kasa = sonraki gÃ¼ne devredecek miktar.
    /// </summary>
    private async Task<decimal?> TryExtractFromCalculatedKasaSnapshotAsync(DateOnly date, KasaRaporTuru kasaTuru, CancellationToken ct)
    {
        var cksRecord = await _dbContext.CalculatedKasaSnapshots
            .Where(x => x.RaporTarihi == date
                     && x.KasaTuru == kasaTuru
                     && x.IsActive && !x.IsDeleted)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);

        if (cksRecord == null || string.IsNullOrWhiteSpace(cksRecord.OutputsJson))
        {
            _log.LogDebug("[CarryoverDebug] CKS fallback: {Date} / {KasaTuru} icin aktif kayit bulunamadi.", date, kasaTuru);
            return null;
        }

        var devreden = ExtractDevredenKasa(cksRecord.OutputsJson, date.ToString(), kasaTuru.ToString());
        _log.LogDebug("[CarryoverDebug] CKS fallback basarili: {Date} / {KasaTuru} = {Value}", date, kasaTuru, devreden);
        return devreden;
    }

    private static bool TryParseDecimal(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        raw = raw.Trim().Replace("â‚º", "").Replace(" ", "");
        if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return true;
        if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out value)) return true;
        return false;
    }

    private async Task<CarryoverResolutionResult> ResolveVergiKasaSelectionTotalAsync(DateOnly targetDate, CancellationToken ct)
    {
        // Snapshot sistemi kaldÄ±rÄ±ldÄ±ÄŸÄ± iÃ§in bu scope her zaman 0 dÃ¶nmeli veya tamamen devre dÄ±ÅŸÄ± bÄ±rakÄ±lmalÄ±dÄ±r.
        // DataFirst mimarisinde manual seleksiyonlar farklÄ± tabloya yansÄ±r.
        _log.LogInformation("[CarryoverResolver] Scope: {Scope}, Source: {Source}, Value: {Value}, SourceDate: {SourceDate}", 
            "VergiKasaSelectionTotal", "DefaultZero", 0m, targetDate);

        return await Task.FromResult(new CarryoverResolutionResult(
            Value: 0m,
            TargetKey: "vergi_kasa_bakiye_toplam",
            RangeStart: targetDate,
            SourceDate: null,
            SourceCode: "DefaultZero",
            Reason: "Snapshot sistemi kaldÄ±rÄ±ldÄ±ÄŸÄ±ndan manual seÃ§im toplamÄ± varsayÄ±lan 0 olarak alÄ±nÄ±yor.",
            UsedFallback: true
        ));
    }
}
