#nullable enable
using System.Reflection;
using KasaManager.Application.Processing.Models;
using KasaManager.Domain.Constants;
using KasaManager.Domain.Models;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Adapters;

/// <summary>
/// REFACTOR R1: Legacy model ↔ UnifiedKasaRecord adapter.
/// 
/// Mevcut SabahKasaNesnesi, AksamKasaNesnesi, GenelKasaRaporNesnesi modellerini
/// UnifiedKasaRecord'a dönüştürür ve tersi.
/// 
/// Bu adapter sayesinde:
/// - Mevcut kod çalışmaya devam eder
/// - Yeni kod unified record kullanır
/// - Aşamalı migration mümkün olur
/// </summary>
public static class KasaDataAdapter
{
    #region To UnifiedKasaRecord

    /// <summary>
    /// SabahKasaNesnesi → UnifiedKasaRecord
    /// </summary>
    public static UnifiedKasaRecord ToUnified(SabahKasaNesnesi source, DateOnly? raporTarihi = null)
    {
        var record = new UnifiedKasaRecord
        {
            RaporTarihi = raporTarihi ?? DateOnly.FromDateTime(source.IslemTarihiTahsilatSabahK),
            RaporTuru = KasaRaporTuru.Sabah,
            CreatedAtUtc = DateTime.UtcNow
        };

        MapDecimalProperties(source, record);
        MapStringProperties(source, record);
        MapDateProperties(source, record);

        return record;
    }

    /// <summary>
    /// AksamKasaNesnesi → UnifiedKasaRecord
    /// </summary>
    public static UnifiedKasaRecord ToUnified(AksamKasaNesnesi source, DateOnly? raporTarihi = null)
    {
        var record = new UnifiedKasaRecord
        {
            RaporTarihi = raporTarihi ?? DateOnly.FromDateTime(source.IslemTarihiTahsilat),
            RaporTuru = KasaRaporTuru.Aksam,
            CreatedAtUtc = DateTime.UtcNow
        };

        MapDecimalProperties(source, record);
        MapStringProperties(source, record);
        MapDateProperties(source, record);

        return record;
    }

    /// <summary>
    /// GenelKasaRaporNesnesi → UnifiedKasaRecord
    /// </summary>
    public static UnifiedKasaRecord ToUnified(GenelKasaRaporNesnesi source, DateOnly? raporTarihi = null)
    {
        var record = new UnifiedKasaRecord
        {
            RaporTarihi = raporTarihi ?? DateOnly.FromDateTime(source.RaporTarihi),
            RaporTuru = KasaRaporTuru.Genel,
            CreatedAtUtc = DateTime.UtcNow
        };

        MapDecimalProperties(source, record);
        MapStringProperties(source, record);
        MapDateProperties(source, record);
        MapIntProperties(source, record);

        return record;
    }

    #endregion

    #region From UnifiedKasaRecord

    /// <summary>
    /// UnifiedKasaRecord → SabahKasaNesnesi
    /// </summary>
    public static SabahKasaNesnesi ToSabahKasa(UnifiedKasaRecord source)
    {
        var target = new SabahKasaNesnesi();
        PopulateFromUnified(source, target);
        return target;
    }

    /// <summary>
    /// UnifiedKasaRecord → AksamKasaNesnesi
    /// </summary>
    public static AksamKasaNesnesi ToAksamKasa(UnifiedKasaRecord source)
    {
        var target = new AksamKasaNesnesi();
        PopulateFromUnified(source, target);
        return target;
    }

    /// <summary>
    /// UnifiedKasaRecord → GenelKasaRaporNesnesi
    /// </summary>
    public static GenelKasaRaporNesnesi ToGenelKasa(UnifiedKasaRecord source)
    {
        var target = new GenelKasaRaporNesnesi();
        PopulateFromUnified(source, target);
        return target;
    }

    #endregion

    #region Private Mapping Helpers

    private static void MapDecimalProperties<T>(T source, UnifiedKasaRecord target) where T : class
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(decimal) && p.CanRead);

        foreach (var prop in props)
        {
            var legacyName = prop.Name;
            var canonicalKey = KasaFieldMapper.ToCanonical(legacyName);
            var value = (decimal)prop.GetValue(source)!;
            
            // Skip alias properties (they map to same canonical key)
            if (!target.DecimalFields.ContainsKey(canonicalKey))
            {
                target.DecimalFields[canonicalKey] = value;
            }
        }
    }

    private static void MapStringProperties<T>(T source, UnifiedKasaRecord target) where T : class
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) && p.CanRead);

        foreach (var prop in props)
        {
            var legacyName = prop.Name;
            var canonicalKey = KasaFieldMapper.ToCanonical(legacyName);
            var value = (string?)prop.GetValue(source);
            
            if (!target.TextFields.ContainsKey(canonicalKey))
            {
                target.TextFields[canonicalKey] = value;
            }
        }
    }

    private static void MapDateProperties<T>(T source, UnifiedKasaRecord target) where T : class
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(DateTime) && p.CanRead);

        foreach (var prop in props)
        {
            var legacyName = prop.Name;
            var canonicalKey = KasaFieldMapper.ToCanonical(legacyName);
            var value = (DateTime)prop.GetValue(source)!;
            
            if (!target.DateFields.ContainsKey(canonicalKey))
            {
                target.DateFields[canonicalKey] = value;
            }
        }
    }

    private static void MapIntProperties<T>(T source, UnifiedKasaRecord target) where T : class
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => (p.PropertyType == typeof(int) || p.PropertyType == typeof(int?)) && p.CanRead);

        foreach (var prop in props)
        {
            var legacyName = prop.Name;
            var canonicalKey = KasaFieldMapper.ToCanonical(legacyName);
            var rawValue = prop.GetValue(source);
            
            if (rawValue is int intValue && !target.IntFields.ContainsKey(canonicalKey))
            {
                target.IntFields[canonicalKey] = intValue;
            }
        }
    }

    private static void PopulateFromUnified<T>(UnifiedKasaRecord source, T target) where T : class
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite);

        foreach (var prop in props)
        {
            var legacyName = prop.Name;
            var canonicalKey = KasaFieldMapper.ToCanonical(legacyName);

            if (prop.PropertyType == typeof(decimal))
            {
                if (source.DecimalFields.TryGetValue(canonicalKey, out var decValue))
                {
                    prop.SetValue(target, decValue);
                }
            }
            else if (prop.PropertyType == typeof(string))
            {
                if (source.TextFields.TryGetValue(canonicalKey, out var strValue))
                {
                    prop.SetValue(target, strValue);
                }
            }
            else if (prop.PropertyType == typeof(DateTime))
            {
                if (source.DateFields.TryGetValue(canonicalKey, out var dateValue))
                {
                    prop.SetValue(target, dateValue);
                }
            }
            else if (prop.PropertyType == typeof(int))
            {
                if (source.IntFields.TryGetValue(canonicalKey, out var intValue))
                {
                    prop.SetValue(target, intValue);
                }
            }
            else if (prop.PropertyType == typeof(int?))
            {
                if (source.IntFields.TryGetValue(canonicalKey, out var intValue))
                {
                    prop.SetValue(target, (int?)intValue);
                }
            }
        }
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Herhangi bir legacy model'i UnifiedKasaRecord'a dönüştür.
    /// </summary>
    public static UnifiedKasaRecord ToUnified(object source, KasaRaporTuru? overrideTuru = null, DateOnly? raporTarihi = null)
    {
        return source switch
        {
            SabahKasaNesnesi sabah => ToUnified(sabah, raporTarihi),
            AksamKasaNesnesi aksam => ToUnified(aksam, raporTarihi),
            GenelKasaRaporNesnesi genel => ToUnified(genel, raporTarihi),
            _ => throw new ArgumentException($"Unsupported legacy type: {source.GetType().Name}")
        };
    }

    /// <summary>
    /// UnifiedKasaRecord'dan legacy model oluştur.
    /// </summary>
    public static object FromUnified(UnifiedKasaRecord source)
    {
        return source.RaporTuru switch
        {
            KasaRaporTuru.Sabah => ToSabahKasa(source),
            KasaRaporTuru.Aksam => ToAksamKasa(source),
            KasaRaporTuru.Genel => ToGenelKasa(source),
            _ => throw new ArgumentException($"Unsupported RaporTuru: {source.RaporTuru}")
        };
    }

    #endregion
}
