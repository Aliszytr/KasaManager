using KasaManager.Application.Orchestration.Dtos;

namespace KasaManager.Application.Orchestration;

public interface IKasaOrchestrator
{
    // === Runtime / Preview ===
    Task LoadPreviewAsync(KasaPreviewDto dto, string uploadBasePath, CancellationToken ct);
    Task RunFormulaEnginePreviewAsync(KasaPreviewDto dto, string uploadBasePath, CancellationToken ct);
    Task LoadFormulaSetV1Async(KasaPreviewDto dto, CancellationToken ct);
    Task LoadAksamContractAsync(KasaPreviewDto dto, CancellationToken ct);

    // === Intent-First: Scope-Based Auto-Loading ===
    /// <summary>
    /// Dashboard'dan gelen kasaType'a göre aktif FormulaSet'i DB'den yükler.
    /// DB'de bulunamazsa embedded fallback şablonunu kullanır.
    /// </summary>
    Task LoadActiveFormulaSetByScopeAsync(KasaPreviewDto dto, string scopeType, CancellationToken ct);

    /// <summary>
    /// Snapshot ve Global Defaults'tan varsayılan girdi değerlerini hydrate eder.
    /// </summary>
    Task HydrateFromSnapshotAndDefaultsAsync(KasaPreviewDto dto, CancellationToken ct);

    // === DB FormulaSet Management ===
    Task HydrateDbFormulaSetsAsync(KasaPreviewDto dto, CancellationToken ct);
    Task LoadDbFormulaSetIntoModelAsync(KasaPreviewDto dto, CancellationToken ct);
    Task CreateDbFormulaSetAsync(KasaPreviewDto dto, CancellationToken ct);
    Task SaveDbFormulaSetAsync(KasaPreviewDto dto, bool isUpdate, CancellationToken ct);
    Task DeleteDbFormulaSetAsync(KasaPreviewDto dto, CancellationToken ct);
    Task CopyDbFormulaSetAsync(KasaPreviewDto dto, CancellationToken ct);
    Task ToggleActiveDbFormulaSetAsync(KasaPreviewDto dto, CancellationToken ct);
}
