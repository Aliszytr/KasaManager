using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KasaManager.Infrastructure.Excel;

/// <summary>
/// Header normalize: Türkçe karakter, boşluk, noktalama vs.
/// Örn: 'İşlem Tutarı' -> 'islem_tutari'
/// </summary>
public static class ExcelHeaderNormalizer
{
    public static string Normalize(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return string.Empty;

        var s = header.Trim();

        // Türkçe karakter dönüşümü
        s = s.Replace('İ', 'I').Replace('ı', 'i');
        s = s.Replace('Ş', 'S').Replace('ş', 's');
        s = s.Replace('Ğ', 'G').Replace('ğ', 'g');
        s = s.Replace('Ü', 'U').Replace('ü', 'u');
        s = s.Replace('Ö', 'O').Replace('ö', 'o');
        s = s.Replace('Ç', 'C').Replace('ç', 'c');

        s = s.ToLowerInvariant();
        s = Regex.Replace(s, @"\s+", "_");
        s = Regex.Replace(s, @"[^a-z0-9_]+", "_");
        s = Regex.Replace(s, @"_+", "_").Trim('_');

        return s;
    }

    /// <summary>
    /// R1 uyumluluğu için bırakıldı:
    /// Kolon index -> canonical map döner.
    /// Duplicate canonical olursa suffix (_2) ekler.
    /// </summary>
    public static Dictionary<int, string> BuildCanonicalMap(
        IReadOnlyList<string> rawHeaders,
        Dictionary<string, string[]> aliases)
    {
        var map = new Dictionary<int, string>();

        // Önce normalize edilmiş raw header'ları al
        var normalized = rawHeaders.Select(Normalize).ToList();

        // Alias ters sözlüğü: alternatif normalize -> canonical
        var aliasReverse = BuildAliasReverse(aliases);

        for (int i = 0; i < normalized.Count; i++)
        {
            var n = normalized[i];
            if (string.IsNullOrWhiteSpace(n))
                continue;

            if (aliasReverse.TryGetValue(n, out var canonical))
                map[i] = canonical;
            else
                map[i] = n;
        }

        // Aynı canonical birden fazla kolona eşlenmişse,
        // ilkini tut, diğerlerini suffix ile benzersizleştir.
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in map.Keys.ToList())
        {
            var c = map[k];
            if (used.Add(c)) continue;

            var suffix = 2;
            var tmp = $"{c}_{suffix}";
            while (!used.Add(tmp))
            {
                suffix++;
                tmp = $"{c}_{suffix}";
            }
            map[k] = tmp;
        }

        return map;
    }

    /// <summary>
    /// R2: ColumnMeta üretmek için.
    /// Her kolon için (Index, CanonicalName, OriginalHeader) döner.
    /// Duplicate canonical'a İZİN verir (suffix üretmez).
    /// </summary>
    public static List<(int Index, string CanonicalName, string OriginalHeader)> BuildColumnMetas(
        IReadOnlyList<string> rawHeaders,
        Dictionary<string, string[]> aliases)
    {
        var result = new List<(int, string, string)>();
        var aliasReverse = BuildAliasReverse(aliases);

        for (int i = 0; i < rawHeaders.Count; i++)
        {
            var original = rawHeaders[i]?.Trim() ?? string.Empty;
            var normalized = Normalize(original);

            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            var canonical = aliasReverse.TryGetValue(normalized, out var c)
                ? c
                : normalized;

            result.Add((i, canonical, original));
        }

        return result;
    }

    private static Dictionary<string, string> BuildAliasReverse(Dictionary<string, string[]> aliases)
    {
        var aliasReverse = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in aliases)
        {
            var canonical = Normalize(kv.Key);
            if (string.IsNullOrWhiteSpace(canonical))
                continue;

            foreach (var alt in kv.Value ?? Array.Empty<string>())
            {
                var n = Normalize(alt);
                if (!string.IsNullOrWhiteSpace(n) && !aliasReverse.ContainsKey(n))
                    aliasReverse[n] = canonical;
            }
        }

        return aliasReverse;
    }
}
