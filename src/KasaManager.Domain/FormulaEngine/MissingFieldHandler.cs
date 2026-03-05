#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace KasaManager.Domain.FormulaEngine;

/// <summary>
/// R19: Eksik alanların varsayılan değerlerle doldurulmasını sağlayan yardımcı sınıf.
/// Eski snapshot'ların yeni alan yapısıyla uyumlu çalışmasını garantiler.
/// </summary>
public static class MissingFieldHandler
{
    /// <summary>
    /// FieldCatalog'daki tüm decimal alanları kontrol eder ve eksik olanları
    /// varsayılan değerle (0) doldurur.
    /// </summary>
    /// <param name="source">Mevcut alan-değer dictionary'si</param>
    /// <param name="defaultValue">Eksik alanlar için varsayılan değer (default: 0)</param>
    /// <returns>Tüm alanları içeren normalize edilmiş dictionary</returns>
    public static Dictionary<string, decimal> EnsureAllDecimalFields(
        Dictionary<string, decimal>? source,
        decimal defaultValue = 0m)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        
        // Kaynak değerleri kopyala
        if (source != null)
        {
            foreach (var kvp in source)
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        
        // FieldCatalog'daki tüm decimal alanları kontrol et
        foreach (var field in FieldCatalog.All)
        {
            // Sadece decimal/numeric alanları işle
            if (string.IsNullOrEmpty(field.DataType) || 
                field.DataType.Equals("decimal", StringComparison.OrdinalIgnoreCase) ||
                field.DataType.Equals("number", StringComparison.OrdinalIgnoreCase))
            {
                if (!result.ContainsKey(field.Key))
                {
                    result[field.Key] = defaultValue;
                }
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Belirtilen anahtar listesindeki eksik alanları varsayılan değerle doldurur.
    /// </summary>
    /// <param name="source">Mevcut alan-değer dictionary'si</param>
    /// <param name="requiredKeys">Zorunlu alan anahtarları</param>
    /// <param name="defaultValue">Eksik alanlar için varsayılan değer</param>
    /// <returns>Tüm zorunlu alanları içeren normalize edilmiş dictionary</returns>
    public static Dictionary<string, decimal> EnsureFields(
        Dictionary<string, decimal>? source,
        IEnumerable<string> requiredKeys,
        decimal defaultValue = 0m)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        
        // Kaynak değerleri kopyala
        if (source != null)
        {
            foreach (var kvp in source)
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        
        // Zorunlu alanları kontrol et
        foreach (var key in requiredKeys)
        {
            if (!result.ContainsKey(key))
            {
                result[key] = defaultValue;
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// String dictionary için eksik alanları boş string ile doldurur.
    /// </summary>
    public static Dictionary<string, string?> EnsureAllStringFields(
        Dictionary<string, string?>? source)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        
        // Kaynak değerleri kopyala
        if (source != null)
        {
            foreach (var kvp in source)
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        
        // FieldCatalog'daki tüm string alanları kontrol et
        foreach (var field in FieldCatalog.All)
        {
            if (field.DataType?.Equals("string", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (!result.ContainsKey(field.Key))
                {
                    result[field.Key] = null;
                }
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Verilen dictionary'de eksik olan FieldCatalog alanlarını listeler.
    /// Debug ve logging amaçlı kullanılır.
    /// </summary>
    public static IReadOnlyList<string> GetMissingFields(
        IReadOnlyDictionary<string, decimal>? source)
    {
        if (source == null)
            return FieldCatalog.All
                .Where(f => string.IsNullOrEmpty(f.DataType) || f.DataType == "decimal")
                .Select(f => f.Key)
                .ToList();
        
        var missing = new List<string>();
        
        foreach (var field in FieldCatalog.All)
        {
            if (string.IsNullOrEmpty(field.DataType) || 
                field.DataType.Equals("decimal", StringComparison.OrdinalIgnoreCase))
            {
                if (!source.ContainsKey(field.Key))
                {
                    missing.Add(field.Key);
                }
            }
        }
        
        return missing;
    }
    
    /// <summary>
    /// Dictionary'deki alanların kaçının FieldCatalog'da tanımlı olduğunu raporlar.
    /// </summary>
    public static (int matched, int total, int catalogTotal) GetFieldCoverage(
        IReadOnlyDictionary<string, decimal>? source)
    {
        if (source == null)
            return (0, 0, FieldCatalog.All.Count);
        
        var catalogKeys = new HashSet<string>(
            FieldCatalog.All.Select(f => f.Key), 
            StringComparer.OrdinalIgnoreCase);
        
        var matched = source.Keys.Count(k => catalogKeys.Contains(k));
        
        return (matched, source.Count, FieldCatalog.All.Count);
    }
}
