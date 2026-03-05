#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Legacy;
using Microsoft.EntityFrameworkCore;

namespace KasaManager.Infrastructure.Legacy;

/// <summary>
/// Eski KasaRaporuDB veritabanından read-only veri okuma servisi.
/// </summary>
public sealed class LegacyKasaService : ILegacyKasaService
{
    private readonly LegacyKasaDbContext _db;

    public LegacyKasaService(LegacyKasaDbContext db) => _db = db;

    // ═══════════════════════════════════════════════════════════════
    // Sabah Kasa
    // ═══════════════════════════════════════════════════════════════

    public async Task<LegacyPagedResult<LegacySabahKasa>> GetSabahKasaListAsync(
        DateTime? startDate, DateTime? endDate,
        int page, int pageSize, CancellationToken ct)
    {
        var query = _db.SabahKasaNesnesis.AsQueryable();

        if (startDate.HasValue)
            query = query.Where(x => x.IslemTarihiTahsilatSabahK >= startDate.Value);
        if (endDate.HasValue)
            query = query.Where(x => x.IslemTarihiTahsilatSabahK <= endDate.Value);

        query = query.OrderByDescending(x => x.IslemTarihiTahsilatSabahK);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new LegacyPagedResult<LegacySabahKasa>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<LegacySabahKasa?> GetSabahKasaByIdAsync(Guid id, CancellationToken ct)
        => await _db.SabahKasaNesnesis.FirstOrDefaultAsync(x => x.Id == id, ct);

    // ═══════════════════════════════════════════════════════════════
    // Akşam Kasa
    // ═══════════════════════════════════════════════════════════════

    public async Task<LegacyPagedResult<LegacyAksamKasa>> GetAksamKasaListAsync(
        DateTime? startDate, DateTime? endDate,
        int page, int pageSize, CancellationToken ct)
    {
        var query = _db.AksamKasaNesnesis.AsQueryable();

        if (startDate.HasValue)
            query = query.Where(x => x.IslemTarihiTahsilat >= startDate.Value);
        if (endDate.HasValue)
            query = query.Where(x => x.IslemTarihiTahsilat <= endDate.Value);

        query = query.OrderByDescending(x => x.IslemTarihiTahsilat);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new LegacyPagedResult<LegacyAksamKasa>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<LegacyAksamKasa?> GetAksamKasaByIdAsync(Guid id, CancellationToken ct)
        => await _db.AksamKasaNesnesis.FirstOrDefaultAsync(x => x.Id == id, ct);

    // ═══════════════════════════════════════════════════════════════
    // Genel Kasa Rapor
    // ═══════════════════════════════════════════════════════════════

    public async Task<LegacyPagedResult<LegacyGenelKasaRapor>> GetGenelKasaRaporListAsync(
        DateTime? startDate, DateTime? endDate,
        int page, int pageSize, CancellationToken ct)
    {
        var query = _db.GenelKasaRaporNesnesis.AsQueryable();

        if (startDate.HasValue)
            query = query.Where(x => x.raporTarihi >= startDate.Value);
        if (endDate.HasValue)
            query = query.Where(x => x.raporTarihi <= endDate.Value);

        query = query.OrderByDescending(x => x.raporTarihi);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new LegacyPagedResult<LegacyGenelKasaRapor>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<LegacyGenelKasaRapor?> GetGenelKasaByIdAsync(Guid id, CancellationToken ct)
        => await _db.GenelKasaRaporNesnesis.FirstOrDefaultAsync(x => x.Id == id, ct);
}
