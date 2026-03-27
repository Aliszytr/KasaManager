using KasaManager.Application.Processing.Abstractions.Repositories;
using KasaManager.Application.Processing.Abstractions.Services;

namespace KasaManager.Infrastructure.Processing.InMemory;

/// <summary>
/// R4: DB yokken dataset store için thread-safe InMemory workspace.
/// Workspace, her dataset için IReportRepository tabanlı repository'ler expose eder.
/// 
/// REFACTOR R1: Unified repository eklendi. Legacy repository'ler geçiş süresince korunuyor.
/// </summary>
public sealed class InMemoryProcessingWorkspace : IProcessingWorkspace
{
    public InMemoryProcessingWorkspace(
        IUnifiedKasaRepository unified,
        IAksamKasaNesnesiRepository aksamKasa,
        ISabahKasaNesnesiRepository sabahKasa,
        IGenelKasaRaporNesnesiRepository genelKasa,
        IBankaVerilerNesnesiRepository banka,
        IUYAPVerilerNesnesiRepository uyap)
    {
        Unified = unified ?? throw new ArgumentNullException(nameof(unified));
        AksamKasa = aksamKasa ?? throw new ArgumentNullException(nameof(aksamKasa));
        SabahKasa = sabahKasa ?? throw new ArgumentNullException(nameof(sabahKasa));
        GenelKasa = genelKasa ?? throw new ArgumentNullException(nameof(genelKasa));
        Banka = banka ?? throw new ArgumentNullException(nameof(banka));
        Uyap = uyap ?? throw new ArgumentNullException(nameof(uyap));
    }

    // ===== UNIFIED (Yeni yapı) =====
    public IUnifiedKasaRepository Unified { get; }

    // ===== LEGACY =====
    public IAksamKasaNesnesiRepository AksamKasa { get; }
    public ISabahKasaNesnesiRepository SabahKasa { get; }
    public IGenelKasaRaporNesnesiRepository GenelKasa { get; }
    public IBankaVerilerNesnesiRepository Banka { get; }
    public IUYAPVerilerNesnesiRepository Uyap { get; }

    public void ClearAll()
    {
        Unified.Clear();
        AksamKasa.Clear();
        SabahKasa.Clear();
        GenelKasa.Clear();
        Banka.Clear();
        Uyap.Clear();
    }
}

