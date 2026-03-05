#nullable enable
using KasaManager.Domain.Legacy;

namespace KasaManager.Application.Abstractions;

/// <summary>
/// Eski KasaRaporuDB veritabanından okuma servisi.
/// Sabah Kasa, Akşam Kasa ve Genel Kasa Rapor verilerine erişim sağlar.
/// </summary>
public interface ILegacyKasaService
{
    // ── Liste (sayfalama + tarih filtresi) ─────────────────
    Task<LegacyPagedResult<LegacySabahKasa>> GetSabahKasaListAsync(
        DateTime? startDate = null, DateTime? endDate = null,
        int page = 1, int pageSize = 25, CancellationToken ct = default);

    Task<LegacyPagedResult<LegacyAksamKasa>> GetAksamKasaListAsync(
        DateTime? startDate = null, DateTime? endDate = null,
        int page = 1, int pageSize = 25, CancellationToken ct = default);

    Task<LegacyPagedResult<LegacyGenelKasaRapor>> GetGenelKasaRaporListAsync(
        DateTime? startDate = null, DateTime? endDate = null,
        int page = 1, int pageSize = 25, CancellationToken ct = default);

    // ── Tekil kayıt ───────────────────────────────────────
    Task<LegacySabahKasa?> GetSabahKasaByIdAsync(Guid id, CancellationToken ct = default);
    Task<LegacyAksamKasa?> GetAksamKasaByIdAsync(Guid id, CancellationToken ct = default);
    Task<LegacyGenelKasaRapor?> GetGenelKasaByIdAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Sayfalanmış sonuç kapsülü.
/// </summary>
public sealed record LegacyPagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}
