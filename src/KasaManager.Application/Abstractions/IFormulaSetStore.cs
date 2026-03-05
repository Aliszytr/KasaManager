using KasaManager.Domain.FormulaEngine.Authoring;

namespace KasaManager.Application.Abstractions;

public interface IFormulaSetStore
{
    Task<List<PersistedFormulaSet>> ListAsync(CancellationToken ct = default);
    Task<PersistedFormulaSet?> GetAsync(Guid id, CancellationToken ct = default);

    Task<PersistedFormulaSet> CreateAsync(PersistedFormulaSet set, CancellationToken ct = default);
    Task<PersistedFormulaSet> UpdateAsync(PersistedFormulaSet set, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Scope içinde bu seti aktif yapar, diğerlerini pasifler.
    /// </summary>
    Task SetActiveAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Belirli bir seti pasif yapar (diğer setleri etkilemez).
    /// </summary>
    Task DeactivateAsync(Guid id, CancellationToken ct = default);

    Task AddRunAsync(Guid setId, string inputsJson, string outputsJson, string issuesJson, CancellationToken ct = default);
}
