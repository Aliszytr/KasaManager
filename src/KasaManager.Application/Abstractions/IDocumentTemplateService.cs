#nullable enable
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// Banka resmi yazıları ve şablon yönetimi CRUD servisi.
/// Şablonlar DB'ye kaydedilir, yeniden kullanılabilir.
/// </summary>
public interface IDocumentTemplateService
{
    Task<IReadOnlyList<DocumentTemplate>> GetAllAsync(CancellationToken ct);
    Task<IReadOnlyList<DocumentTemplate>> GetByCategoryAsync(string category, CancellationToken ct);
    Task<DocumentTemplate?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<DocumentTemplate> SaveAsync(DocumentTemplate template, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
