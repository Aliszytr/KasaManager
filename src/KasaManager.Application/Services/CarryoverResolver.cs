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
using KasaManager.Domain.Reports.Snapshots;
using Microsoft.Extensions.Logging;

namespace KasaManager.Application.Services;

public sealed class CarryoverResolver : ICarryoverResolver
{
    private readonly IKasaGlobalDefaultsService _defaults;
    private readonly ILogger<CarryoverResolver> _log;

    public CarryoverResolver(
        IKasaGlobalDefaultsService defaults,
        ILogger<CarryoverResolver> log)
    {
        _defaults = defaults;
        _log = log;
    }

    public async Task<CarryoverResolutionResult> ResolveAsync(DateOnly targetDate, CarryoverScope scope, CancellationToken ct = default)
    {
        return scope switch
        {
            CarryoverScope.GenelKasa => await ResolveGenelKasaAsync(targetDate, ct),
            CarryoverScope.AksamKasaNakit => await ResolveAksamKasaNakitAsync(targetDate, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
        };
    }

    private async Task<CarryoverResolutionResult> ResolveGenelKasaAsync(DateOnly targetDate, CancellationToken ct)
    {
        var settings = await _defaults.GetOrCreateAsync(ct);
        var seedValue = settings.DefaultGenelKasaDevredenSeed;
        var seedStart = settings.DefaultGenelKasaBaslangicTarihiSeed is DateTime dt ? DateOnly.FromDateTime(dt) : targetDate;

        // 1. Ayarlardan Seed Gelmesi (Yalnızca 0'dan büyükse aktif seed kabul edilir)
        if (seedValue.HasValue && seedValue.Value > 0m)
        {
            _log.LogInformation("[CarryoverResolver] date={Date} scope=GenelKasa source=SeedOverride value={Value}", targetDate, seedValue.Value);
            return new CarryoverResolutionResult(
                Value: seedValue.Value,
                RangeStart: seedStart,
                SourceDate: seedStart.AddDays(-1),
                SourceCode: "SeedOverride",
                Reason: "Ayarlardaki Genel Kasa Devreden (Seed) değeri > 0 olduğu için override kabul edildi.",
                UsedFallback: false
            );
        }

        // 2. Default Zero (Snapshot Fallback YASAK - P4.2 Stateless Engine Kuralı)
        // Eğer ayarlarda bir seed yoksa, snapshot'tan okumak yerine deterministik olarak 0 döner.
        _log.LogInformation("[CarryoverResolver] date={Date} scope=GenelKasa source=DefaultZero reason=NoValidSourceFound value=0", targetDate);
        return new CarryoverResolutionResult(
            Value: 0m,
            RangeStart: seedStart,
            SourceDate: seedStart.AddDays(-1),
            SourceCode: "DefaultZero",
            Reason: "Devreden Seed Override yok. Sistem kuralları gereği Snapshot okuma deaktif, 0 yüklendi.",
            UsedFallback: true
        );
    }

    private async Task<CarryoverResolutionResult> ResolveAksamKasaNakitAsync(DateOnly targetDate, CancellationToken ct)
    {
        // KasaDraftService -> DetermineDevredenKasaAsync muadili
        var settings = await _defaults.GetOrCreateAsync(ct);
        var overrideVal = settings.DefaultDundenDevredenKasaNakit;
        
        if (overrideVal.HasValue && overrideVal.Value != 0m)
        {
            _log.LogInformation("[CarryoverResolver] date={Date} scope=AksamKasaNakit source=SeedOverride value={Value}", targetDate, overrideVal.Value);
            return new CarryoverResolutionResult(
                Value: overrideVal.Value,
                RangeStart: targetDate,
                SourceDate: targetDate.AddDays(-1),
                SourceCode: "SeedOverride",
                Reason: "Ayarlardaki Dünden Devreden Kasa Nakit değeri > 0 olduğu için override edildi.",
                UsedFallback: false
            );
        }

        var prev = targetDate.AddDays(-1);

        // Snapshot fallback kuralı (P4.2) gereğince kapatılmıştır. Devreden için yalnızca Seed baz alınır.
        _log.LogInformation("[CarryoverResolver] date={Date} scope=AksamKasaNakit source=DefaultZero reason=NoValidSourceFound value=0", targetDate);
        return new CarryoverResolutionResult(
            Value: 0m,
            RangeStart: targetDate,
            SourceDate: prev,
            SourceCode: "DefaultZero",
            Reason: "Devreden Kasa override değeri yok. Snapshot okuma iptal, 0 kullanılıyor.",
            UsedFallback: true
        );
    }
}
