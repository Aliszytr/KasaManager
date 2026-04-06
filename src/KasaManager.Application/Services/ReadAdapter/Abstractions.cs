using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Orchestration.Dtos;
using KasaManager.Domain.Abstractions;

namespace KasaManager.Application.Services.ReadAdapter;

public enum KasaReadMode
{
    LegacyOnly = 0,
    ParallelShadow = 1
    // İleride: DataFirstPrimaryReady = 2 vb. gelebilir (Phase 5)
}

public class KasaReadRequest
{
    public DateOnly TargetDate { get; set; }
    public string KasaScope { get; set; } = string.Empty;
    public string BaseUploadFolder { get; set; } = string.Empty;
    
    // UI state'lerini kaybetmemek için (AksamMesaiSonuModu, IsAdminMode vb.)
    public KasaPreviewDto? ContextDto { get; set; }
}

public class KasaReadModelResult
{
    // User requested: "KasaReadModelResult.Primary = her durumda Legacy"
    public KasaPreviewDto Primary { get; set; } = new();
    
    // Candidate varsa ayrıca doldurulsun
    public KasaPreviewDto? Candidate { get; set; }
    
    public EligibilityReason EligibilityReason { get; set; }
    
    public KasaReadMode ExecutedMode { get; set; }
}

public enum EligibilityReason
{
    NotEvaluated = 0,
    Eligible = 1,
    StaleResult = 2,
    NotMatchedParity = 3,
    NoCandidateFound = 4,
    LockedPeriod = 5,
    ManualResolutionRequired = 6
}

public interface IKasaReadModelService
{
    Task<Result<KasaReadModelResult>> GetReadModelAsync(KasaReadRequest request, CancellationToken ct = default);
}

public interface ILegacyKasaReadService
{
    Task<Result<KasaPreviewDto>> ReadLegacyAsync(KasaReadRequest request, CancellationToken ct = default);
}

public interface IDataFirstKasaReadService
{
    Task<Result<KasaPreviewDto>> ReadDataFirstCandidateAsync(KasaReadRequest request, CancellationToken ct = default);
}

public interface IReadModeResolver
{
    Task<KasaReadMode> ResolveModeAsync(KasaReadRequest request, CancellationToken ct = default);
}

public interface IReadEligibilityService
{
    Task<EligibilityReason> CheckEligibilityAsync(KasaReadRequest request, CancellationToken ct = default);
}
