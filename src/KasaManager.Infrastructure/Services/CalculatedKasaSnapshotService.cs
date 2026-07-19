#nullable enable
using System.Text.Json;
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
    private static readonly JsonSerializerOptions FinancialPayloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly KasaManagerDbContext _db;
    private readonly ILogger<CalculatedKasaSnapshotService> _logger;

    public CalculatedKasaSnapshotService(KasaManagerDbContext db, ILogger<CalculatedKasaSnapshotService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<CalculatedKasaSnapshot> SaveAsync(
        CalculatedKasaSnapshot snapshot,
        int actorUserId,
        string? actorUsername,
        CancellationToken ct = default)
    {
        ValidateActorUserId(actorUserId);

        // Aynı tarih+kasa için aktif kayıt var mı kontrol et
        var existing = await _db.CalculatedKasaSnapshots
            .Where(x => x.RaporTarihi == snapshot.RaporTarihi 
                     && x.KasaTuru == snapshot.KasaTuru 
                     && x.IsActive 
                     && !x.IsDeleted)
            .ToListAsync(ct);

        var unchanged = existing.FirstOrDefault(current =>
            HasSameFinancialFingerprint(current, snapshot));
        if (unchanged is not null)
            return unchanged;

        // Versiyon numarasını aktif/pasif ve silinmiş/silinmemiş tüm zincirden belirle.
        var maxVersion = await _db.CalculatedKasaSnapshots
            .IgnoreQueryFilters()
            .Where(x => x.RaporTarihi == snapshot.RaporTarihi
                     && x.KasaTuru == snapshot.KasaTuru)
            .MaxAsync(x => (int?)x.Version, ct) ?? 0;

        // Varsa, hepsini pasif yap
        foreach (var e in existing)
        {
            e.IsActive = false;
        }

        snapshot.Version = maxVersion + 1;
        snapshot.IsActive = true;
        snapshot.IsDeleted = false;
        snapshot.CalculatedAtUtc = DateTime.UtcNow;
        snapshot.CalculatedByUserId = actorUserId;
        snapshot.CalculatedBy = actorUsername;

        _db.CalculatedKasaSnapshots.Add(snapshot);

        // --- DATA FIRST SSOT MIGRATION ---
        // Ensure that explicit saves via UI ("SaveReport") also correctly update the Data-First Carryover lookup source natively
        var scopeStr = snapshot.KasaTuru switch {
            KasaRaporTuru.Sabah => "Sabah",
            KasaRaporTuru.Aksam => "Aksam",
            KasaRaporTuru.Genel => "Genel",
            _ => "Ortak"
        };
        
        var dailyRes = await _db.DailyCalculationResults
            .Where(x => x.ForDate == snapshot.RaporTarihi && x.KasaTuru == scopeStr)
            .FirstOrDefaultAsync(ct);
            
        if (dailyRes == null)
        {
            dailyRes = new KasaManager.Domain.Calculation.Data.DailyCalculationResult
            {
                Id = Guid.NewGuid(),
                ForDate = snapshot.RaporTarihi,
                KasaTuru = scopeStr,
                CalculatedVersion = snapshot.Version,
                ResultsJson = snapshot.OutputsJson ?? "{}",
                CalculatedAt = DateTime.UtcNow
            };
            _db.DailyCalculationResults.Add(dailyRes);
        }
        else
        {
            dailyRes.CalculatedVersion = snapshot.Version;
            dailyRes.ResultsJson = snapshot.OutputsJson ?? "{}";
            dailyRes.CalculatedAt = DateTime.UtcNow;
            _db.DailyCalculationResults.Update(dailyRes);
        }
        // ---------------------------------

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

    public async Task<SnapshotMutationResult> DeleteAsync(
        Guid id,
        int actorUserId,
        bool isAdmin,
        string? deletedBy,
        CancellationToken ct = default)
    {
        ValidateActorUserId(actorUserId);
        if (!isAdmin)
            return SnapshotMutationResult.Forbidden;

        var snapshot = await _db.CalculatedKasaSnapshots.FindAsync(new object[] { id }, ct);
        if (snapshot is null)
        {
            _logger.LogWarning("R17: Silinecek kasa bulunamadı - Id={Id}", id);
            return SnapshotMutationResult.NotFound;
        }

        if (snapshot.IsDeleted)
            return SnapshotMutationResult.NoChange;

        snapshot.IsDeleted = true;
        snapshot.IsActive = false;
        snapshot.DeletedAtUtc = DateTime.UtcNow;
        snapshot.DeletedBy = deletedBy;
        snapshot.DeletedByUserId = actorUserId;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("R17: Kasa silindi (soft) - Tarih={Tarih}, Tip={Tip}, Versiyon={Version}, SilenId={Id}",
            snapshot.RaporTarihi, snapshot.KasaTuru, snapshot.Version, id);
        return SnapshotMutationResult.Success;
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

    public async Task<SnapshotMutationResult> ActivateVersionAsync(
        Guid id,
        int actorUserId,
        bool isAdmin,
        CancellationToken ct = default)
    {
        ValidateActorUserId(actorUserId);
        if (!isAdmin)
            return SnapshotMutationResult.Forbidden;

        var target = await _db.CalculatedKasaSnapshots.FindAsync(new object[] { id }, ct);
        if (target is null || target.IsDeleted)
        {
            _logger.LogWarning("R17: Aktifleştirilecek versiyon bulunamadı veya silinmiş - Id={Id}", id);
            return SnapshotMutationResult.NotFound;
        }

        if (target.IsActive)
            return SnapshotMutationResult.NoChange;

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

        var scopeStr = target.KasaTuru switch
        {
            KasaRaporTuru.Sabah => "Sabah",
            KasaRaporTuru.Aksam => "Aksam",
            KasaRaporTuru.Genel => "Genel",
            _ => "Ortak"
        };

        var dailyRes = await _db.DailyCalculationResults
            .FirstOrDefaultAsync(
                x => x.ForDate == target.RaporTarihi && x.KasaTuru == scopeStr,
                ct);

        if (dailyRes is null)
        {
            dailyRes = new KasaManager.Domain.Calculation.Data.DailyCalculationResult
            {
                Id = Guid.NewGuid(),
                ForDate = target.RaporTarihi,
                KasaTuru = scopeStr,
                CalculatedVersion = target.Version,
                ResultsJson = target.OutputsJson ?? "{}",
                CalculatedAt = DateTime.UtcNow
            };
            _db.DailyCalculationResults.Add(dailyRes);
        }
        else
        {
            dailyRes.CalculatedVersion = target.Version;
            dailyRes.ResultsJson = target.OutputsJson ?? "{}";
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("R17: Versiyon aktifleştirildi - Tarih={Tarih}, Tip={Tip}, Versiyon={Version}",
            target.RaporTarihi, target.KasaTuru, target.Version);
        return SnapshotMutationResult.Success;
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

    public async Task<SnapshotMutationResult> RestoreAsync(
        Guid id,
        int actorUserId,
        bool isAdmin,
        CancellationToken ct = default)
    {
        ValidateActorUserId(actorUserId);
        if (!isAdmin)
            return SnapshotMutationResult.Forbidden;

        var snapshot = await _db.CalculatedKasaSnapshots.FindAsync(new object[] { id }, ct);
        if (snapshot is null)
        {
            _logger.LogWarning("R17: Geri yüklenecek kasa bulunamadı veya zaten aktif - Id={Id}", id);
            return SnapshotMutationResult.NotFound;
        }

        if (!snapshot.IsDeleted)
            return SnapshotMutationResult.NoChange;

        snapshot.IsDeleted = false;
        snapshot.DeletedAtUtc = null;
        snapshot.DeletedBy = null;
        snapshot.DeletedByUserId = null;

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
        return SnapshotMutationResult.Success;
    }

    public async Task<SnapshotMutationResult> UpdateAsync(
        Guid id,
        string? name,
        string? description,
        string? notes,
        int actorUserId,
        bool isAdmin,
        CancellationToken ct = default)
    {
        ValidateActorUserId(actorUserId);
        var snapshot = await _db.CalculatedKasaSnapshots.FindAsync(new object[] { id }, ct);
        if (snapshot is null)
        {
            _logger.LogWarning("R17: Güncellenecek kasa bulunamadı - Id={Id}", id);
            return SnapshotMutationResult.NotFound;
        }

        if (!isAdmin && snapshot.CalculatedByUserId != actorUserId)
            return SnapshotMutationResult.Forbidden;

        snapshot.Name = name;
        snapshot.Description = description;
        snapshot.Notes = notes;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("R17: Kasa güncellendi - Name={Name}, Id={Id}", name, id);
        return SnapshotMutationResult.Success;
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

    private static void ValidateActorUserId(int actorUserId)
    {
        if (actorUserId <= 0)
            throw new ArgumentOutOfRangeException(nameof(actorUserId));
    }

    private static bool HasSameFinancialFingerprint(
        CalculatedKasaSnapshot current,
        CalculatedKasaSnapshot candidate)
    {
        // Actor and editable metadata are deliberately excluded. V2 compares the
        // complete scalar+detail contract. V1 compatibility candidates compare
        // only their persisted scalar contract and never invent record details.
        if (!JsonSemanticallyEquals(current.InputsJson, candidate.InputsJson)
            || !JsonSemanticallyEquals(current.OutputsJson, candidate.OutputsJson)
            || !TryReadCompatiblePayload(current.KasaRaporDataJson, out var currentPayload, out var currentDetails)
            || !TryReadCompatiblePayload(candidate.KasaRaporDataJson, out var candidatePayload, out var candidateDetails)
            || currentPayload.PayloadVersion != candidatePayload.PayloadVersion)
        {
            return false;
        }

        return HasSameImmutableAudit(currentPayload.ImmutableAudit!, candidatePayload.ImmutableAudit!)
            && (currentPayload.PayloadVersion == 1
                || HasSameImmutableAuditDetails(currentDetails!, candidateDetails!))
            && HasSameAuthoritativeAuditRoots(currentPayload, candidatePayload);
    }

    private static bool TryReadCompatiblePayload(
        string? json,
        out KasaRaporData payload,
        out HesapKontrolImmutableAuditDetails? details)
    {
        payload = null!;
        details = null!;

        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<KasaRaporData>(json, FinancialPayloadJsonOptions);
            if (parsed?.PayloadVersion is not (1 or 2)
                || parsed.ImmutableAudit is null
                || parsed.ImmutableAudit.TakipteSayisi < 0)
            {
                return false;
            }

            if (parsed.PayloadVersion == 1)
            {
                if (parsed.ImmutableAuditDetails.HasValue)
                    return false;

                payload = parsed;
                details = null;
                return true;
            }

            if (parsed.ImmutableAuditDetails is not { } detailsElement)
                return false;

            var deserializedDetails = detailsElement.Deserialize<HesapKontrolImmutableAuditDetails>(
                FinancialPayloadJsonOptions);
            if (deserializedDetails is null
                || deserializedDetails.Records is null
                || deserializedDetails.Groups is null
                || deserializedDetails.Groups.AktifKayitlar is null
                || deserializedDetails.Groups.OncekiAciklar is null
                || deserializedDetails.Groups.BugunCozulenler is null
                || deserializedDetails.Groups.ReconciliationKayitlar is null
                || deserializedDetails.Groups.TakipteKayitlar is null
                || deserializedDetails.Groups.BugunTakipCozulenler is null)
            {
                return false;
            }

            var normalizedDetails = NormalizeImmutableAuditDetails(deserializedDetails);
            if (!HesapKontrolImmutableAuditDetailsValidator.TryValidate(normalizedDetails, out _))
                return false;

            payload = parsed;
            details = normalizedDetails;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static HesapKontrolImmutableAuditDetails NormalizeImmutableAuditDetails(
        HesapKontrolImmutableAuditDetails details) => new(
        HesapKontrolImmutableAuditDetailsValidator.OrderRecords(details.Records),
        new HesapKontrolImmutableAuditGroups(
            details.Groups.AktifKayitlar.OrderBy(id => id).ToArray(),
            details.Groups.OncekiAciklar.OrderBy(id => id).ToArray(),
            details.Groups.BugunCozulenler.OrderBy(id => id).ToArray(),
            details.Groups.ReconciliationKayitlar.OrderBy(id => id).ToArray(),
            details.Groups.TakipteKayitlar.OrderBy(id => id).ToArray(),
            details.Groups.BugunTakipCozulenler.OrderBy(id => id).ToArray()));

    private static bool HasSameImmutableAudit(
        KasaImmutableAuditData left,
        KasaImmutableAuditData right) =>
        left.GuneAitEksikFazlaTahsilat == right.GuneAitEksikFazlaTahsilat
        && left.GuneAitEksikFazlaHarc == right.GuneAitEksikFazlaHarc
        && left.OncekiGunAcikTahsilat == right.OncekiGunAcikTahsilat
        && left.OncekiGunAcikHarc == right.OncekiGunAcikHarc
        && left.BugunCozulenTahsilat == right.BugunCozulenTahsilat
        && left.BugunCozulenHarc == right.BugunCozulenHarc
        && left.TakipteEksikTahsilat == right.TakipteEksikTahsilat
        && left.TakipteEksikHarc == right.TakipteEksikHarc
        && left.TakipteFazlaTahsilat == right.TakipteFazlaTahsilat
        && left.TakipteFazlaHarc == right.TakipteFazlaHarc
        && left.TakipteSayisi == right.TakipteSayisi
        && left.ToplamFarkTahsilat == right.ToplamFarkTahsilat
        && left.ToplamFarkHarc == right.ToplamFarkHarc
        && left.BeklenenTahsilat == right.BeklenenTahsilat
        && left.BeklenenHarc == right.BeklenenHarc
        && left.OlaganDisiTahsilat == right.OlaganDisiTahsilat
        && left.OlaganDisiHarc == right.OlaganDisiHarc
        && left.TakipKasaEtkisiTahsilat == right.TakipKasaEtkisiTahsilat
        && left.TakipKasaEtkisiHarc == right.TakipKasaEtkisiHarc
        && left.TakipKasaEtkisiNet == right.TakipKasaEtkisiNet
        && string.Equals(left.BreakdownMesajTahsilat, right.BreakdownMesajTahsilat, StringComparison.Ordinal)
        && string.Equals(left.BreakdownMesajHarc, right.BreakdownMesajHarc, StringComparison.Ordinal);

    private static bool HasSameImmutableAuditDetails(
        HesapKontrolImmutableAuditDetails left,
        HesapKontrolImmutableAuditDetails right) =>
        left.Records.SequenceEqual(right.Records)
        && left.Groups.AktifKayitlar.SequenceEqual(right.Groups.AktifKayitlar)
        && left.Groups.OncekiAciklar.SequenceEqual(right.Groups.OncekiAciklar)
        && left.Groups.BugunCozulenler.SequenceEqual(right.Groups.BugunCozulenler)
        && left.Groups.ReconciliationKayitlar.SequenceEqual(right.Groups.ReconciliationKayitlar)
        && left.Groups.TakipteKayitlar.SequenceEqual(right.Groups.TakipteKayitlar)
        && left.Groups.BugunTakipCozulenler.SequenceEqual(right.Groups.BugunTakipCozulenler);

    private static bool HasSameAuthoritativeAuditRoots(KasaRaporData left, KasaRaporData right) =>
        left.GuneAitEksikFazlaTahsilat == right.GuneAitEksikFazlaTahsilat
        && left.GuneAitEksikFazlaHarc == right.GuneAitEksikFazlaHarc
        && left.DundenEksikFazlaTahsilat == right.DundenEksikFazlaTahsilat
        && left.DundenEksikFazlaHarc == right.DundenEksikFazlaHarc
        && left.DundenEksikFazlaGelenTahsilat == right.DundenEksikFazlaGelenTahsilat
        && left.DundenEksikFazlaGelenHarc == right.DundenEksikFazlaGelenHarc;

    private static bool JsonSemanticallyEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            using var leftDocument = JsonDocument.Parse(left);
            using var rightDocument = JsonDocument.Parse(right);
            return JsonElementsEqual(leftDocument.RootElement, rightDocument.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool JsonElementsEqual(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
            return false;

        return left.ValueKind switch
        {
            JsonValueKind.Object => JsonObjectsEqual(left, right),
            JsonValueKind.Array => left.GetArrayLength() == right.GetArrayLength()
                && left.EnumerateArray().Zip(right.EnumerateArray(), JsonElementsEqual).All(equal => equal),
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => JsonNumbersEqual(left, right),
            JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.Null => true,
            _ => false
        };
    }

    private static bool JsonObjectsEqual(JsonElement left, JsonElement right)
    {
        var leftProperties = left.EnumerateObject().ToArray();
        var rightProperties = right.EnumerateObject().ToArray();
        if (leftProperties.Length != rightProperties.Length)
            return false;

        var rightByName = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in rightProperties)
        {
            if (!rightByName.TryAdd(property.Name, property.Value))
                return false;
        }

        var seenLeftNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in leftProperties)
        {
            if (!seenLeftNames.Add(property.Name)
                || !rightByName.TryGetValue(property.Name, out var rightValue)
                || !JsonElementsEqual(property.Value, rightValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool JsonNumbersEqual(JsonElement left, JsonElement right)
    {
        if (left.TryGetDecimal(out var leftDecimal) && right.TryGetDecimal(out var rightDecimal))
            return leftDecimal == rightDecimal;

        return left.TryGetDouble(out var leftDouble)
            && right.TryGetDouble(out var rightDouble)
            && leftDouble.Equals(rightDouble);
    }
}
