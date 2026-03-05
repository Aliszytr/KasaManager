using System;
using System.Linq;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Settings;
using KasaManager.Infrastructure.Caching;
using Microsoft.EntityFrameworkCore;

namespace KasaManager.Infrastructure.Persistence;

/// <summary>
/// Global varsayılan ayarlar (tek satır / Id=1).
/// SQL Server ENFORCED çalışmada EF Core migration ile tablo oluşur.
/// R19: Cache desteği eklendi - GetAsync 5 dakika cache'lenir.
/// </summary>
public sealed class KasaGlobalDefaultsService : IKasaGlobalDefaultsService
{
    private readonly KasaManagerDbContext _db;
    private readonly ICachingService _cache;

    public KasaGlobalDefaultsService(KasaManagerDbContext db, ICachingService cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<KasaGlobalDefaultsSettings> GetAsync(CancellationToken ct)
    {
        // R19: Cache'den al, yoksa DB'den yükle
        return await _cache.GetOrCreateAsync(
            CacheKeys.GlobalDefaults,
            async () =>
            {
                // Tek satır (Id=1) mantığı. Yoksa oluştur.
                var entity = await _db.Set<KasaGlobalDefaultsSettings>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == 1, ct);

                if (entity is not null)
                    return entity;

                var created = new KasaGlobalDefaultsSettings
                {
                    Id = 1,
                    SelectedVeznedarlarJson = "[]",
                    LastUpdatedAt = DateTime.UtcNow
                };

                _db.Set<KasaGlobalDefaultsSettings>().Add(created);
                await _db.SaveChangesAsync(ct);
                return created;
            },
            TimeSpan.FromMinutes(5),
            ct);
    }

    /// <summary>
    /// Alias: eski çağrıları kırmamak için.
    /// GetAsync zaten "yoksa oluştur" mantığıyla çalışır.
    /// </summary>
    public Task<KasaGlobalDefaultsSettings> GetOrCreateAsync(CancellationToken ct)
        => GetAsync(ct);

    public async Task SaveVergiKasaSelectedAsync(IReadOnlyCollection<string> selectedVeznedarlar, string? updatedBy, CancellationToken ct)
    {
        var clean = selectedVeznedarlar
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var json = JsonSerializer.Serialize(clean);

        var entity = await _db.Set<KasaGlobalDefaultsSettings>()
            .FirstOrDefaultAsync(x => x.Id == 1, ct);

        if (entity is null)
        {
            entity = new KasaGlobalDefaultsSettings { Id = 1 };
            _db.Set<KasaGlobalDefaultsSettings>().Add(entity);
        }

        entity.SelectedVeznedarlarJson = json;
        entity.LastUpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = updatedBy;

        await _db.SaveChangesAsync(ct);
        
        // R19: Cache'i invalidate et
        _cache.Invalidate(CacheKeys.GlobalDefaults);
    }

    public async Task SaveDefaultCashAsync(decimal? defaultNakitPara, decimal? defaultBozukPara, decimal? defaultKasaEksikFazla, decimal? defaultGenelKasaDevredenSeed, DateTime? defaultGenelKasaBaslangicTarihiSeed, decimal? defaultKaydenTahsilat, decimal? defaultDundenDevredenKasaNakit, string? updatedBy, CancellationToken ct)
    {
        var entity = await _db.Set<KasaGlobalDefaultsSettings>()
            .FirstOrDefaultAsync(x => x.Id == 1, ct);

        if (entity is null)
        {
            entity = new KasaGlobalDefaultsSettings { Id = 1 };
            _db.Set<KasaGlobalDefaultsSettings>().Add(entity);
        }

        entity.DefaultNakitPara = defaultNakitPara;
        entity.DefaultBozukPara = defaultBozukPara;
        entity.DefaultKasaEksikFazla = defaultKasaEksikFazla;
        entity.DefaultGenelKasaDevredenSeed = defaultGenelKasaDevredenSeed;
        entity.DefaultGenelKasaBaslangicTarihiSeed = defaultGenelKasaBaslangicTarihiSeed;
        entity.DefaultKaydenTahsilat = defaultKaydenTahsilat;
        entity.DefaultDundenDevredenKasaNakit = defaultDundenDevredenKasaNakit;
        entity.LastUpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = updatedBy;

        await _db.SaveChangesAsync(ct);
        
        // R19: Cache'i invalidate et
        _cache.Invalidate(CacheKeys.GlobalDefaults);
    }

    public async Task SaveIbanSettingsAsync(
        string? hesapAdiStopaj, string? ibanStopaj,
        string? hesapAdiMasraf, string? ibanMasraf,
        string? hesapAdiHarc, string? ibanHarc,
        string? ibanPostaPulu,
        string? updatedBy,
        CancellationToken ct)
    {
        var entity = await _db.Set<KasaGlobalDefaultsSettings>()
            .FirstOrDefaultAsync(x => x.Id == 1, ct);

        if (entity is null)
        {
            entity = new KasaGlobalDefaultsSettings { Id = 1 };
            _db.Set<KasaGlobalDefaultsSettings>().Add(entity);
        }

        entity.HesapAdiStopaj = hesapAdiStopaj?.Trim();
        entity.IbanStopaj = NormalizeIban(ibanStopaj);
        entity.HesapAdiMasraf = hesapAdiMasraf?.Trim();
        entity.IbanMasraf = NormalizeIban(ibanMasraf);
        entity.HesapAdiHarc = hesapAdiHarc?.Trim();
        entity.IbanHarc = NormalizeIban(ibanHarc);
        entity.IbanPostaPulu = NormalizeIban(ibanPostaPulu);
        entity.LastUpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = updatedBy;

        await _db.SaveChangesAsync(ct);
        _cache.Invalidate(CacheKeys.GlobalDefaults);
    }

    public async Task SaveVergideBirikenSeedAsync(decimal seed, string? updatedBy, CancellationToken ct)
    {
        var entity = await _db.Set<KasaGlobalDefaultsSettings>()
            .FirstOrDefaultAsync(x => x.Id == 1, ct);

        if (entity is null)
        {
            entity = new KasaGlobalDefaultsSettings { Id = 1 };
            _db.Set<KasaGlobalDefaultsSettings>().Add(entity);
        }

        entity.VergideBirikenSeed = seed;
        entity.VergideBirikenSeedUpdatedAt = DateTime.UtcNow;
        entity.LastUpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = updatedBy;

        await _db.SaveChangesAsync(ct);
        _cache.Invalidate(CacheKeys.GlobalDefaults);
    }

    /// <summary>
    /// IBAN'ı normalize eder: boşluk/tire silinir, UPPER yapılır.
    /// Güvenilir copy-paste için standart format.
    /// </summary>
    private static string? NormalizeIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban)) return null;
        return iban.Replace(" ", "").Replace("-", "").ToUpperInvariant();
    }
}

