#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// R17: Hesaplanmış Kasa snapshot'ları yönetimi implementation.
/// Versioning ve soft delete ile audit trail sağlar.
/// </summary>
public sealed class CalculatedKasaSnapshotService : ICalculatedKasaSnapshotService
{
    private readonly KasaManagerDbContext _db;
    private readonly ILogger<CalculatedKasaSnapshotService> _logger;

    public CalculatedKasaSnapshotService(KasaManagerDbContext db, ILogger<CalculatedKasaSnapshotService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<CalculatedKasaSnapshot> SaveAsync(CalculatedKasaSnapshot snapshot, CancellationToken ct = default)
    {
        // Aynı tarih+kasa için aktif kayıt var mı kontrol et
        var existing = await _db.CalculatedKasaSnapshots
            .Where(x => x.RaporTarihi == snapshot.RaporTarihi 
                     && x.KasaTuru == snapshot.KasaTuru 
                     && x.IsActive 
                     && !x.IsDeleted)
            .ToListAsync(ct);

        // Varsa, hepsini pasif yap
        foreach (var e in existing)
        {
            e.IsActive = false;
        }

        // Versiyon numarasını belirle
        var maxVersion = existing.Count > 0 
            ? existing.Max(x => x.Version) 
            : 0;
        
        snapshot.Version = maxVersion + 1;
        snapshot.IsActive = true;
        snapshot.IsDeleted = false;
        snapshot.CalculatedAtUtc = DateTime.UtcNow;

        _db.CalculatedKasaSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("R17: Kasa kaydedildi - Tarih={Tarih}, Tip={Tip}, Versiyon={Version}, Id={Id}",
            snapshot.RaporTarihi, snapshot.KasaTuru, snapshot.Version, snapshot.Id);

        return snapshot;
    }

    public async Task<CalculatedKasaSnapshot?> GetActiveAsync(DateOnly raporTarihi, KasaRaporTuru kasaTuru, CancellationToken ct = default)
    {
        return await _db.CalculatedKasaSnapshots
            .AsNoTracking()
            .Where(x => x.RaporTarihi == raporTarihi 
                     && x.KasaTuru == kasaTuru 
                     && x.IsActive 
                     && !x.IsDeleted)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<CalculatedKasaSnapshot?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.CalculatedKasaSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<List<CalculatedKasaSnapshot>> GetAllVersionsAsync(DateOnly raporTarihi, KasaRaporTuru kasaTuru, CancellationToken ct = default)
    {
        return await _db.CalculatedKasaSnapshots
            .AsNoTracking()
            .Where(x => x.RaporTarihi == raporTarihi 
                     && x.KasaTuru == kasaTuru 
                     && !x.IsDeleted)
            .OrderByDescending(x => x.Version)
            .ToListAsync(ct);
    }

    public async Task DeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    {
        var snapshot = await _db.CalculatedKasaSnapshots.FindAsync(new object[] { id }, ct);
        if (snapshot is null)
        {
            _logger.LogWarning("R17: Silinecek kasa bulunamadı - Id={Id}", id);
            return;
        }

        snapshot.IsDeleted = true;
        snapshot.IsActive = false;
        snapshot.DeletedAtUtc = DateTime.UtcNow;
        snapshot.DeletedBy = deletedBy;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("R17: Kasa silindi (soft) - Tarih={Tarih}, Tip={Tip}, Versiyon={Version}, SilenId={Id}",
            snapshot.RaporTarihi, snapshot.KasaTuru, snapshot.Version, id);
    }

    public async Task<List<CalculatedKasaSnapshot>> ListRecentAsync(KasaRaporTuru? kasaTuru = null, int days = 30, CancellationToken ct = default)
    {
        var startDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-days));
        
        var query = _db.CalculatedKasaSnapshots
            .AsNoTracking()
            .Where(x => x.RaporTarihi >= startDate 
                     && x.IsActive 
                     && !x.IsDeleted);

        if (kasaTuru.HasValue)
        {
            query = query.Where(x => x.KasaTuru == kasaTuru.Value);
        }

        return await query
            .OrderByDescending(x => x.RaporTarihi)
            .ThenBy(x => x.KasaTuru)
            .ToListAsync(ct);
    }

    public async Task<List<CalculatedKasaSnapshot>> ListByDateRangeAsync(
        DateOnly startDate, 
        DateOnly endDate, 
        KasaRaporTuru? kasaTuru = null, 
        CancellationToken ct = default)
    {
        var query = _db.CalculatedKasaSnapshots
            .AsNoTracking()
            .Where(x => x.RaporTarihi >= startDate 
                     && x.RaporTarihi <= endDate 
                     && x.IsActive 
                     && !x.IsDeleted);

        if (kasaTuru.HasValue)
        {
            query = query.Where(x => x.KasaTuru == kasaTuru.Value);
        }

        return await query
            .OrderByDescending(x => x.RaporTarihi)
            .ThenBy(x => x.KasaTuru)
            .ToListAsync(ct);
    }

    public async Task ActivateVersionAsync(Guid id, CancellationToken ct = default)
    {
        var target = await _db.CalculatedKasaSnapshots.FindAsync(new object[] { id }, ct);
        if (target is null || target.IsDeleted)
        {
            _logger.LogWarning("R17: Aktifleştirilecek versiyon bulunamadı veya silinmiş - Id={Id}", id);
            return;
        }

        // Aynı tarih+kasa için diğer aktif kayıtları pasif yap
        var others = await _db.CalculatedKasaSnapshots
            .Where(x => x.RaporTarihi == target.RaporTarihi 
                     && x.KasaTuru == target.KasaTuru 
                     && x.Id != id 
                     && x.IsActive 
                     && !x.IsDeleted)
            .ToListAsync(ct);

        foreach (var o in others)
        {
            o.IsActive = false;
        }

        target.IsActive = true;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("R17: Versiyon aktifleştirildi - Tarih={Tarih}, Tip={Tip}, Versiyon={Version}",
            target.RaporTarihi, target.KasaTuru, target.Version);
    }

    public async Task<PagedResult<CalculatedKasaSnapshot>> SearchAsync(KasaReportSearchQuery query, CancellationToken ct = default)
    {
        var q = _db.CalculatedKasaSnapshots.AsNoTracking().AsQueryable();

        // Silinmiş kayıtları dahil etme filtresi
        if (!query.IncludeDeleted)
            q = q.Where(x => !x.IsDeleted);

        // Kasa tipi filtresi
        if (query.KasaTuru.HasValue)
            q = q.Where(x => x.KasaTuru == query.KasaTuru.Value);

        // Tarih aralığı filtresi
        if (query.StartDate.HasValue)
            q = q.Where(x => x.RaporTarihi >= query.StartDate.Value);
        if (query.EndDate.HasValue)
            q = q.Where(x => x.RaporTarihi <= query.EndDate.Value);

        // Full-text arama (Name, Description, Notes)
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var search = query.SearchText.Trim().ToLower();
            q = q.Where(x =>
                (x.Name != null && x.Name.ToLower().Contains(search)) ||
                (x.Description != null && x.Description.ToLower().Contains(search)) ||
                (x.Notes != null && x.Notes.ToLower().Contains(search)) ||
                (x.CalculatedBy != null && x.CalculatedBy.ToLower().Contains(search))
            );
        }

        // Toplam sayım
        var totalCount = await q.CountAsync(ct);

        // Sıralama
        q = query.SortBy?.ToLower() switch
        {
            "name" => query.SortDescending ? q.OrderByDescending(x => x.Name).ThenBy(x => x.Id) : q.OrderBy(x => x.Name).ThenBy(x => x.Id),
            "kasaturu" => query.SortDescending ? q.OrderByDescending(x => x.KasaTuru).ThenBy(x => x.Id) : q.OrderBy(x => x.KasaTuru).ThenBy(x => x.Id),
            "calculatedatutc" => query.SortDescending ? q.OrderByDescending(x => x.CalculatedAtUtc).ThenBy(x => x.Id) : q.OrderBy(x => x.CalculatedAtUtc).ThenBy(x => x.Id),
            _ => query.SortDescending
                ? q.OrderByDescending(x => x.RaporTarihi).ThenByDescending(x => x.CalculatedAtUtc).ThenBy(x => x.Id)
                : q.OrderBy(x => x.RaporTarihi).ThenBy(x => x.CalculatedAtUtc).ThenBy(x => x.Id)
        };

        // Sayfalama
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new PagedResult<CalculatedKasaSnapshot>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task RestoreAsync(Guid id, CancellationToken ct = default)
    {
        var snapshot = await _db.CalculatedKasaSnapshots.FindAsync(new object[] { id }, ct);
        if (snapshot is null || !snapshot.IsDeleted)
        {
            _logger.LogWarning("R17: Geri yüklenecek kasa bulunamadı veya zaten aktif - Id={Id}", id);
            return;
        }

        snapshot.IsDeleted = false;
        snapshot.DeletedAtUtc = null;
        snapshot.DeletedBy = null;

        // Aynı tarih+kasa için başka aktif kayıt yoksa, bunu aktif yap
        var hasActive = await _db.CalculatedKasaSnapshots
            .AnyAsync(x => x.RaporTarihi == snapshot.RaporTarihi
                        && x.KasaTuru == snapshot.KasaTuru
                        && x.Id != id
                        && x.IsActive
                        && !x.IsDeleted, ct);

        if (!hasActive)
            snapshot.IsActive = true;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("R17: Kasa geri yüklendi - Tarih={Tarih}, Tip={Tip}, Id={Id}",
            snapshot.RaporTarihi, snapshot.KasaTuru, id);
    }

    public async Task UpdateAsync(Guid id, string? name, string? description, string? notes, CancellationToken ct = default)
    {
        var snapshot = await _db.CalculatedKasaSnapshots.FindAsync(new object[] { id }, ct);
        if (snapshot is null)
        {
            _logger.LogWarning("R17: Güncellenecek kasa bulunamadı - Id={Id}", id);
            return;
        }

        snapshot.Name = name;
        snapshot.Description = description;
        snapshot.Notes = notes;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("R17: Kasa güncellendi - Name={Name}, Id={Id}", name, id);
    }

    public async Task<int> CountAsync(KasaRaporTuru? kasaTuru = null, CancellationToken ct = default)
    {
        var q = _db.CalculatedKasaSnapshots
            .AsNoTracking()
            .Where(x => x.IsActive && !x.IsDeleted);

        if (kasaTuru.HasValue)
            q = q.Where(x => x.KasaTuru == kasaTuru.Value);

        return await q.CountAsync(ct);
    }
}
