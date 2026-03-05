#nullable enable
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Services.Draft.Helpers;

/// <summary>
/// ImportedTable üzerinde canonical key arama işlemleri için yardımcı sınıf.
/// KasaDraftService'ten çıkarıldı - R1 Refactoring.
/// R19: RowMatchesDate ve RowMatchesDateRange metodları eklendi.
/// </summary>
public static class CanonicalKeyHelper
{
    /// <summary>
    /// Tabloda belirtilen canonical key'i arar.
    /// Exact match ve token fallback destekler.
    /// </summary>
    public static string? FindCanonical(ImportedTable table, string canonical)
    {
        if (table.ColumnMetas is null || table.ColumnMetas.Count == 0)
            return null;

        // 1) Exact canonical match
        var exact = table.ColumnMetas.FirstOrDefault(m =>
            string.Equals(m.CanonicalName, canonical, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact.CanonicalName;

        // 2) Token fallback: odenecek_miktar => ["odenecek","miktar"]
        var hit = table.ColumnMetas.FirstOrDefault(m =>
        {
            if (string.IsNullOrWhiteSpace(m.CanonicalName)) return false;
            var parts = m.CanonicalName.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Any(p => string.Equals(p, canonical, StringComparison.OrdinalIgnoreCase));
        });

        return hit?.CanonicalName;
    }

    /// <summary>
    /// Header içinde belirtilen string'i içeren kolonun canonical key'ini bulur.
    /// </summary>
    public static string? FindCanonicalByHeaderContains(ImportedTable table, string contains)
        => table.ColumnMetas?.FirstOrDefault(m =>
            !string.IsNullOrWhiteSpace(m.OriginalHeader) &&
            m.OriginalHeader.Contains(contains, StringComparison.OrdinalIgnoreCase))?.CanonicalName;

    /// <summary>
    /// Tabloda tarih kolonunu otomatik tespit eder.
    /// Öncelik sırası: tarih, islem_tarihi, *tarih*, *_tar suffix, header'da tarih.
    /// </summary>
    public static string? FindDateCanonical(ImportedTable table)
    {
        if (table.ColumnMetas is null || table.ColumnMetas.Count == 0)
            return null;

        // 1) Net canonical'lar
        var exact = table.ColumnMetas.FirstOrDefault(m =>
            string.Equals(m.CanonicalName, "tarih", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.CanonicalName, "islem_tarihi", StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.CanonicalName;

        // 2) Canonical içinde "tarih" geçen
        var containsTarih = table.ColumnMetas.FirstOrDefault(m =>
            !string.IsNullOrWhiteSpace(m.CanonicalName) &&
            m.CanonicalName.Contains("tarih", StringComparison.OrdinalIgnoreCase));
        if (containsTarih is not null) return containsTarih.CanonicalName;

        // 3) "_tar" suffix (reddiyat_tar gibi)
        var tarSuffix = table.ColumnMetas.FirstOrDefault(m =>
            !string.IsNullOrWhiteSpace(m.CanonicalName) &&
            m.CanonicalName.EndsWith("_tar", StringComparison.OrdinalIgnoreCase));
        if (tarSuffix is not null) return tarSuffix.CanonicalName;

        // 4) OriginalHeader'da "tarih" geçen
        var headerHit = table.ColumnMetas.FirstOrDefault(m =>
            !string.IsNullOrWhiteSpace(m.OriginalHeader) &&
            m.OriginalHeader.Contains("tarih", StringComparison.OrdinalIgnoreCase));
        if (headerHit is not null) return headerHit.CanonicalName;

        // 5) OriginalHeader'da " tar." / "tar" geçen (son çare)
        return table.ColumnMetas.FirstOrDefault(m =>
            !string.IsNullOrWhiteSpace(m.OriginalHeader) &&
            m.OriginalHeader.Contains(" tar", StringComparison.OrdinalIgnoreCase))?.CanonicalName;
    }

    /// <summary>
    /// Satırın belirtilen tarih kolonundaki değerin hedef tarihe eşit olup olmadığını kontrol eder.
    /// </summary>
    public static bool RowMatchesDate(Dictionary<string, string?> row, string dateCol, DateOnly targetDate)
    {
        if (!row.TryGetValue(dateCol, out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        if (!DateParsingHelper.TryParseDateOnly(raw, out var rowDate))
            return false;

        return rowDate == targetDate;
    }

    /// <summary>
    /// Satırın belirtilen tarih kolonundaki değerin belirtilen tarih aralığında olup olmadığını kontrol eder.
    /// </summary>
    public static bool RowMatchesDateRange(Dictionary<string, string?> row, string dateCol, DateOnly rangeStart, DateOnly rangeEnd)
    {
        if (!row.TryGetValue(dateCol, out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        if (!DateParsingHelper.TryParseDateOnly(raw, out var rowDate))
            return false;

        return rowDate >= rangeStart && rowDate <= rangeEnd;
    }
}

