using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Orchestration.Dtos;
using KasaManager.Application.Services.ReadAdapter; // get IKasaReadModelService etc
using KasaManager.Domain.Abstractions;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using KasaManager.Domain.Calculation.Data;

namespace KasaManager.Infrastructure.Services.ReadAdapter;

public class ReadEligibilityService : IReadEligibilityService
{
    private readonly KasaManagerDbContext _dbContext;

    public ReadEligibilityService(KasaManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<EligibilityReason> CheckEligibilityAsync(KasaReadRequest request, CancellationToken ct = default)
    {
        var targetDate = request.TargetDate;
        var scope = request.KasaScope;

        var dailyResult = await _dbContext.DailyCalculationResults
            .Where(x => x.ForDate == targetDate && x.KasaTuru == scope)
            .OrderByDescending(x => x.CalculatedVersion)
            .FirstOrDefaultAsync(ct);

        if (dailyResult == null) return EligibilityReason.NoCandidateFound;
        if (dailyResult.IsLocked) return EligibilityReason.LockedPeriod;
        if (dailyResult.IsStale) return EligibilityReason.StaleResult;

        var candidateDrifts = await _dbContext.CalculationParityDrifts
            .Where(x => x.TargetDate == targetDate && x.KasaScope == scope && 
                        (x.Severity == DriftSeverity.MajorDrift || x.Severity == DriftSeverity.MinorDrift))
            .ToListAsync(ct);

        if (candidateDrifts != null && candidateDrifts.Any())
        {
            var openDrift = candidateDrifts.FirstOrDefault(d => 
                d.ResolutionStatus == DriftResolutionStatus.Open || 
                d.ResolutionStatus == DriftResolutionStatus.Investigating);

            if (openDrift != null)
            {
                return EligibilityReason.NotMatchedParity;
            }
        }

        return EligibilityReason.Eligible;
    }
}

public class ReadModeResolver : IReadModeResolver
{
    public Task<KasaReadMode> ResolveModeAsync(KasaReadRequest request, CancellationToken ct = default)
    {
        return Task.FromResult(KasaReadMode.ParallelShadow);
    }
}

public class LegacyKasaReadService : ILegacyKasaReadService
{
    private readonly KasaManager.Application.Orchestration.IKasaOrchestrator _orchestrator;
    
    public LegacyKasaReadService(KasaManager.Application.Orchestration.IKasaOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public async Task<Result<KasaPreviewDto>> ReadLegacyAsync(KasaReadRequest request, CancellationToken ct = default)
    {
        var dto = request.ContextDto ?? new KasaPreviewDto { SelectedDate = request.TargetDate, KasaType = request.KasaScope };

        await _orchestrator.LoadPreviewAsync(dto, request.BaseUploadFolder, ct);
        await _orchestrator.HydrateDbFormulaSetsAsync(dto, ct);

        return Result<KasaPreviewDto>.Success(dto);
    }
}

public class DataFirstKasaReadService : IDataFirstKasaReadService
{
    private readonly KasaManagerDbContext _dbContext;

    public DataFirstKasaReadService(KasaManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<KasaPreviewDto>> ReadDataFirstCandidateAsync(KasaReadRequest request, CancellationToken ct = default)
    {
        var dailyResult = await _dbContext.DailyCalculationResults
            .Where(x => x.ForDate == request.TargetDate && x.KasaTuru == request.KasaScope)
            .OrderByDescending(x => x.CalculatedVersion)
            .FirstOrDefaultAsync(ct);

        if (dailyResult == null) return Result<KasaPreviewDto>.Fail("No DataFirst result.");

        Dictionary<string, decimal> ds = new();
        try
        {
            ds = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, decimal>>(dailyResult.ResultsJson) ?? new();
        }
        catch { }

        var kasaResult = new KasaDraftResult { Fields = ds.ToDictionary(k => k.Key, v => v.Value.ToString("N2")) };

        var bundle = new KasaDraftBundle
        {
            Aksam = request.KasaScope.Equals("Aksam", StringComparison.OrdinalIgnoreCase) ? kasaResult : new(),
            Sabah = request.KasaScope.Equals("Sabah", StringComparison.OrdinalIgnoreCase) ? kasaResult : new(),
            Genel = request.KasaScope.Equals("Genel", StringComparison.OrdinalIgnoreCase) ? kasaResult : new()
        };

        var dto = new KasaPreviewDto
        {
            KasaType = request.KasaScope,
            Drafts = bundle
        };

        return Result<KasaPreviewDto>.Success(dto);
    }
}

public class KasaReadModelService : IKasaReadModelService
{
    private readonly ILegacyKasaReadService _legacy;
    private readonly IDataFirstKasaReadService _dataFirst;
    private readonly IReadModeResolver _modeResolver;
    private readonly IReadEligibilityService _eligibility;

    public KasaReadModelService(
        ILegacyKasaReadService legacy, 
        IDataFirstKasaReadService dataFirst, 
        IReadModeResolver modeResolver, 
        IReadEligibilityService eligibility)
    {
        _legacy = legacy;
        _dataFirst = dataFirst;
        _modeResolver = modeResolver;
        _eligibility = eligibility;
    }

    public async Task<Result<KasaReadModelResult>> GetReadModelAsync(KasaReadRequest request, CancellationToken ct = default)
    {
        var mode = await _modeResolver.ResolveModeAsync(request, ct);

        // HARD RULE: KasaReadModelResult.Primary = her durumda Legacy
        var legacyRes = await _legacy.ReadLegacyAsync(request, ct);
        if (!legacyRes.Ok) return Result<KasaReadModelResult>.Fail(legacyRes.Error!);

        var model = new KasaReadModelResult 
        {
            Primary = legacyRes.Value!,
            ExecutedMode = mode
        };

        if (mode == KasaReadMode.ParallelShadow)
        {
            model.EligibilityReason = await _eligibility.CheckEligibilityAsync(request, ct);

            var dfRes = await _dataFirst.ReadDataFirstCandidateAsync(request, ct);
            if (dfRes.Ok)
            {
                model.Candidate = dfRes.Value!;
            }
        }
        else
        {
            model.EligibilityReason = EligibilityReason.NotEvaluated;
        }

        return Result<KasaReadModelResult>.Success(model);
    }
}
