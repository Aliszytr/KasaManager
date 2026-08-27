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

        if (dbRecord != null
            && !string.IsNullOrWhiteSpace(dbRecord.ResultsJson)
            && TryExtractDevredenKasa(dbRecord.ResultsJson, prev.ToString(), "Aksam", out var dbDevreden))
        {
            foundDevreden = dbDevreden;
            sourceCode = "DailyCalculationResult";
            reason = $"{prev:dd.MM.yyyy} tarihli AkÅŸam hesaplama sonucundan devreden okundu.";
            usedFallback = false;
        }
        else
        {
            // FALLBACK: DailyCalculationResults yoksa VEYA kullanÄ±lamaz durumdaysa
            // (bozuk JSON / eksik zorunlu alan) CalculatedKasaSnapshots'a bak.
            var cksResult = await TryExtractFromCalculatedKasaSnapshotAsync(prev, KasaRaporTuru.Aksam, ct);
            if (cksResult.HasValue)
            {
                foundDevreden = cksResult.Value;
                sourceCode = "CalculatedKasaSnapshot";
                reason = dbRecord != null
                    ? $"{prev:dd.MM.yyyy} tarihli AkÅŸam DailyCalculationResult kaydÄ± kullanÄ±lamadÄ± (bozuk/eksik veri); CalculatedKasaSnapshot'tan devreden okundu."
                    : $"{prev:dd.MM.yyyy} tarihli AkÅŸam CalculatedKasaSnapshot'tan devreden okundu.";
                usedFallback = false;
            }
            else if (dbRecord != null)
            {
                // Case D: kayÄ±t var ama kullanÄ±lamaz (bozuk JSON / eksik alan) ve kullanÄ±labilir
                // bir CalculatedKasaSnapshot da yok. GÃ¼venli varsayÄ±lan 0 â€” ama gerÃ§ek bir
                // authoritative sÄ±fÄ±rdan (Case A) ayÄ±rt edilebilir ÅŸekilde iÅŸaretlenir.
                sourceCode = "InvalidResultFallbackZero";
                reason = $"{prev:dd.MM.yyyy} tarihli AkÅŸam result kaydÄ± bozuk/eksik ve kullanÄ±labilir bir CalculatedKasaSnapshot bulunamadÄ±. GÃ¼venli varsayÄ±lan 0 kullanÄ±lÄ±yor.";
                usedFallback = true;
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

        if (dbRecord != null
            && !string.IsNullOrWhiteSpace(dbRecord.ResultsJson)
            && TryExtractDevredenKasa(dbRecord.ResultsJson, prev.ToString(), "Aksam", out var dbDevreden))
        {
            foundDevreden = dbDevreden;
            sourceCode = "DailyCalculationResult";
            reason = $"{prev:dd.MM.yyyy} tarihli AkÅŸam hesaplama sonucundan devreden okundu.";
            usedFallback = false;
        }
        else
        {
            // FALLBACK: DailyCalculationResults yoksa VEYA kullanÄ±lamaz durumdaysa
            // (bozuk JSON / eksik zorunlu alan) CalculatedKasaSnapshots'a bak.
            var cksResult = await TryExtractFromCalculatedKasaSnapshotAsync(prev, KasaRaporTuru.Aksam, ct);
            if (cksResult.HasValue)
            {
                foundDevreden = cksResult.Value;
                sourceCode = "CalculatedKasaSnapshot";
                reason = dbRecord != null
                    ? $"{prev:dd.MM.yyyy} tarihli AkÅŸam DailyCalculationResult kaydÄ± kullanÄ±lamadÄ± (bozuk/eksik veri); CalculatedKasaSnapshot'tan devreden okundu."
                    : $"{prev:dd.MM.yyyy} tarihli AkÅŸam CalculatedKasaSnapshot'tan devreden okundu.";
                usedFallback = false;
            }
            else if (dbRecord != null)
            {
                // Case D: kayÄ±t var ama kullanÄ±lamaz (bozuk JSON / eksik alan) ve kullanÄ±labilir
                // bir CalculatedKasaSnapshot da yok. GÃ¼venli varsayÄ±lan 0 â€” ama gerÃ§ek bir
                // authoritative sÄ±fÄ±rdan (Case A) ayÄ±rt edilebilir ÅŸekilde iÅŸaretlenir.
                sourceCode = "InvalidResultFallbackZero";
                reason = $"{prev:dd.MM.yyyy} tarihli AkÅŸam result kaydÄ± bozuk/eksik ve kullanÄ±labilir bir CalculatedKasaSnapshot bulunamadÄ±. GÃ¼venli varsayÄ±lan 0 kullanÄ±lÄ±yor.";
                usedFallback = true;
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
        bool usedFallback = true;
        DateOnly sourceDate = searchBeforeDate.AddDays(-1);
        string reason = "DÃ¶nem Ã¶ncesine ait Genel Kasa result kaydÄ± bulunamadÄ±. 0 kullanÄ±lÄ±yor.";

        if (dbRecord != null
            && !string.IsNullOrWhiteSpace(dbRecord.ResultsJson)
            && TryExtractDevredenKasa(dbRecord.ResultsJson, searchBeforeDate.ToString(), "Genel", out var dbDevreden))
        {
            foundDevreden = dbDevreden;
            sourceCode = "DailyCalculationResult";
            sourceDate = dbRecord!.ForDate;
            reason = $"DÃ¶nem baÅŸlangÄ±cÄ±ndan Ã¶nceki en son Genel Kasa kaydÄ±ndan ({sourceDate:dd.MM.yyyy}) devreden okundu.";
            usedFallback = false;
        }
        else
        {
            // FALLBACK: DailyCalculationResults yoksa VEYA kullanÄ±lamaz durumdaysa
            // (bozuk JSON / eksik zorunlu alan) Genel Kasa iÃ§in baÅŸlangÄ±Ã§ tarihinden
            // Ã¶nceki en son kaydedilmiÅŸ CalculatedKasaSnapshot'a bak.
            var cksRecord = await _dbContext.CalculatedKasaSnapshots
                .Where(x => x.KasaTuru == KasaRaporTuru.Genel
                         && x.RaporTarihi < searchBeforeDate
                         && x.IsActive && !x.IsDeleted)
                .OrderByDescending(x => x.RaporTarihi)
                .FirstOrDefaultAsync(ct);

            if (cksRecord != null
                && !string.IsNullOrWhiteSpace(cksRecord.OutputsJson)
                && TryExtractDevredenKasa(cksRecord.OutputsJson, cksRecord.RaporTarihi.ToString(), "Genel", out var cksDevreden))
            {
                foundDevreden = cksDevreden;
                sourceCode = "CalculatedKasaSnapshot";
                sourceDate = cksRecord.RaporTarihi;
                reason = dbRecord != null
                    ? $"DÃ¶nem baÅŸlangÄ±cÄ±ndan Ã¶nceki Genel Kasa result kaydÄ± kullanÄ±lamadÄ± (bozuk/eksik veri); ({sourceDate:dd.MM.yyyy}) tarihli CalculatedKasaSnapshot'tan devreden okundu."
                    : $"DÃ¶nem baÅŸlangÄ±cÄ±ndan Ã¶nceki en son Genel Kasa CalculatedKasaSnapshot'tan ({sourceDate:dd.MM.yyyy}) devreden okundu.";
                usedFallback = false;
                _log.LogDebug("[CarryoverDebug] Genel Kasa CKS fallback basarili. CKS tarih: {Date}, Deger: {Value}", sourceDate, foundDevreden);
            }
            else if (dbRecord != null)
            {
                // Case D: kayÄ±t var ama kullanÄ±lamaz ve kullanÄ±labilir bir CalculatedKasaSnapshot
                // da yok. GÃ¼venli varsayÄ±lan 0 â€” gerÃ§ek bir authoritative sÄ±fÄ±rdan ayÄ±rt edilebilir.
                sourceCode = "InvalidResultFallbackZero";
                sourceDate = dbRecord.ForDate;
                reason = $"({dbRecord.ForDate:dd.MM.yyyy}) tarihli Genel Kasa result kaydÄ± bozuk/eksik ve kullanÄ±labilir bir CalculatedKasaSnapshot bulunamadÄ±. GÃ¼venli varsayÄ±lan 0 kullanÄ±lÄ±yor.";
                usedFallback = true;
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
            UsedFallback: usedFallback
        );
    }

    private bool TryExtractDevredenKasa(string json, string searchDate, string kasaTuru, out decimal value)
    {
        value = 0m;
        try
        {
            var outputs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (outputs == null)
            {
                _log.LogDebug("[CarryoverDebug] ResultsJson bos/gecersiz JSON dondurdu (KasaTuru={KasaTuru}).", kasaTuru);
                return false;
            }

            var ciOutputs = new Dictionary<string, JsonElement>(outputs, StringComparer.OrdinalIgnoreCase);

            // KRITIK: Genel Kasa ve Sabah/Aksam kasalar icin FARKLI aday listesi!
            // Genel Kasa: "sonraya_devredecek" != "genel_kasa"
            //   sonraya_devredecek = Devreden + TahRedFark - GelmeyenD (bir sonraki doneme devredecek miktar)
            //   genel_kasa = Devreden + EksikFazla + TahRedFark - BankaBakiye - KasaNakit - GelmeyenD (kasa hesaplama sonucu)
            // Sabah/Aksam: "genel_kasa" = kasadaki nakit = ertesi gune devredecek miktar (ayni deger)
            string[] candidates;
            if (string.Equals(kasaTuru, "Genel", StringComparison.OrdinalIgnoreCase))
            {
                // Genel Kasa: SADECE sonraya_devredecek aranmali, genel_kasa YANLIS deger!
                candidates = new[] { "sonraki_kasaya_devredecek", "SonrayaDevredecek", "sonraya_devredecek", "devreden_kasa" };
            }
            else
            {
                // Sabah/Aksam: kasadaki nakit = genel_kasa = sonraya devredecek
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
                        value = d;
                        return true;
                    }
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        var str = el.GetString();
                        if (DecimalParsingHelper.TryParseFromJson(str, out d))
                        {
                            _log.LogDebug("[CarryoverDebug] JSON'dan '{Key}' basariyla String olarak devreden cekildi: {Value} (KasaTuru={KasaTuru})", key, d, kasaTuru);
                            value = d;
                            return true;
                        }
                    }
                }
            }
            _log.LogDebug("[CarryoverDebug] JSON icinde gecerli bir devreden_kasa keyi bulunamadi (KasaTuru={KasaTuru}). Mevcut keyler: {Keys}", kasaTuru, string.Join(", ", ciOutputs.Keys));
            return false;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "[CarryoverDebug] ResultsJson parse hatasi (KasaTuru={KasaTuru}).", kasaTuru);
            return false;
        }
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

        if (!TryExtractDevredenKasa(cksRecord.OutputsJson, date.ToString(), kasaTuru.ToString(), out var devreden))
        {
            _log.LogDebug("[CarryoverDebug] CKS fallback: {Date} / {KasaTuru} kaydi bozuk/eksik, kullanilamiyor.", date, kasaTuru);
            return null;
        }
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
