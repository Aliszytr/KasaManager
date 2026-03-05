#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Infrastructure.Caching;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KasaManager.Infrastructure.Services;

/// <summary>
/// R17: Field Chooser tercihleri yönetimi implementation.
/// R19: FieldCatalog sorguları için cache desteği eklendi.
/// </summary>
public sealed class FieldPreferenceService : IFieldPreferenceService
{
    private readonly KasaManagerDbContext _db;
    private readonly ILogger<FieldPreferenceService> _logger;
    private readonly ICachingService _cache;

    public FieldPreferenceService(
        KasaManagerDbContext db, 
        ILogger<FieldPreferenceService> logger,
        ICachingService cache)
    {
        _db = db;
        _logger = logger;
        _cache = cache;
    }

    public async Task<List<string>> GetSelectedFieldsAsync(string kasaType, string? userName, CancellationToken ct = default)
    {
        // 1) Önce kullanıcıya özel tercihi ara
        if (!string.IsNullOrWhiteSpace(userName))
        {
            var userPref = await _db.UserFieldPreferences
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.KasaType == kasaType && x.UserName == userName, ct);
            
            if (userPref is not null)
            {
                var fields = userPref.GetSelectedFields();
                if (fields.Count > 0)
                {
                    _logger.LogDebug("R17: Kullanıcı tercihi bulundu - KasaType={KasaType}, User={User}, Fields={Count}",
                        kasaType, userName, fields.Count);
                    return fields;
                }
            }
        }
        
        // 2) Global tercih ara
        var globalPref = await _db.UserFieldPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.KasaType == kasaType && x.UserName == null, ct);
        
        if (globalPref is not null)
        {
            var fields = globalPref.GetSelectedFields();
            if (fields.Count > 0)
            {
                _logger.LogDebug("R17: Global tercih bulundu - KasaType={KasaType}, Fields={Count}",
                    kasaType, fields.Count);
                return fields;
            }
        }
        
        // 3) Varsayılanları döndür
        _logger.LogDebug("R17: Tercih bulunamadı, varsayılanlar dönüyor - KasaType={KasaType}", kasaType);
        return GetDefaultFieldsFor(kasaType);
    }

    public async Task SaveSelectedFieldsAsync(string kasaType, string? userName, List<string> selectedFields, CancellationToken ct = default)
    {
        var existing = await _db.UserFieldPreferences
            .FirstOrDefaultAsync(x => x.KasaType == kasaType && x.UserName == userName, ct);

        if (existing is not null)
        {
            existing.SetSelectedFields(selectedFields);
            _logger.LogInformation("R17: Tercih güncellendi - KasaType={KasaType}, User={User}, Fields={Count}",
                kasaType, userName ?? "(global)", selectedFields.Count);
        }
        else
        {
            var pref = new UserFieldPreference
            {
                KasaType = kasaType,
                UserName = userName
            };
            pref.SetSelectedFields(selectedFields);
            _db.UserFieldPreferences.Add(pref);
            _logger.LogInformation("R17: Yeni tercih oluşturuldu - KasaType={KasaType}, User={User}, Fields={Count}",
                kasaType, userName ?? "(global)", selectedFields.Count);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task ResetToDefaultsAsync(string kasaType, string? userName, CancellationToken ct = default)
    {
        var existing = await _db.UserFieldPreferences
            .FirstOrDefaultAsync(x => x.KasaType == kasaType && x.UserName == userName, ct);

        if (existing is not null)
        {
            _db.UserFieldPreferences.Remove(existing);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("R17: Tercih silindi, varsayılana dönüldü - KasaType={KasaType}, User={User}",
                kasaType, userName ?? "(global)");
        }
    }

    public List<FieldCategoryGroup> GetFieldCatalogGrouped()
    {
        // R19: FieldCatalog değişmez, 1 saat cache'le
        return _cache.GetOrCreate(
            CacheKeys.FieldCatalogByCategory,
            () => FieldCatalog.GetGroupedByCategory()
                .Select(g => new FieldCategoryGroup
                {
                    Category = g.Key,
                    Fields = g.OrderBy(f => f.SortOrder).ToList()
                })
                .OrderBy(g => GetCategorySortOrder(g.Category))
                .ToList(),
            InMemoryCachingService.LongExpiration);
    }

    public List<string> GetDefaultFieldsFor(string kasaType)
    {
        return FieldCatalog.GetDefaultsFor(kasaType)
            .Select(x => x.Key)
            .ToList();
    }
    
    public List<FieldSourceGroup> GetFieldCatalogGroupedBySource()
    {
        // R19: FieldCatalog değişmez, 1 saat cache'le
        return _cache.GetOrCreate(
            CacheKeys.FieldCatalogBySource,
            () => FieldCatalog.GetGroupedBySource()
                .Select(g => new FieldSourceGroup
                {
                    Source = g.Key,
                    Fields = g.OrderBy(f => f.SortOrder).ToList()
                })
                .OrderBy(g => (int)g.Source) // Excel=0, UserInput=1, Calculated=2
                .ToList(),
            InMemoryCachingService.LongExpiration);
    }

    /// <summary>Kategori sıralaması için yardımcı metod</summary>
    private static int GetCategorySortOrder(string category)
    {
        return category.ToLowerInvariant() switch
        {
            "tahsilat" => 1,
            "harç" => 2,
            "reddiyat" => 3,
            "stopaj" => 4,
            "eksik/fazla" => 5,
            "banka" => 6,
            "kasa" => 7,
            "banka yatırım" => 8,
            "vergi" => 9,
            "ortak kasa" => 10,
            "genel kasa hesap" => 11,
            "masraf" => 12,
            "meta" => 99,
            _ => 50
        };
    }
}
