#nullable enable
using System.Globalization;

namespace KasaManager.Application.Services.Draft.Helpers;

/// <summary>
/// Tarih parse işlemleri için yardımcı sınıf.
/// KasaDraftService'ten çıkarıldı - R1 Refactoring.
/// </summary>
public static class DateParsingHelper
{
    /// <summary>
    /// String değeri DateOnly'e dönüştürmeyi dener.
    /// Excel serial sayıları ve çeşitli Türkçe tarih formatlarını destekler.
    /// </summary>
    public static bool TryParseDateOnly(string? raw, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = raw.Trim();

        // Excel bazen DateTime (serial) gibi gelebilir
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var dbl))
        {
            try
            {
                var dt = DateTime.FromOADate(dbl);
                date = DateOnly.FromDateTime(dt);
                return true;
            }
            catch (ArgumentException) { /* OADate aralık dışı — normal durum, parse devam eder */ }
        }

        var formats = new[] { "dd.MM.yyyy", "d.MM.yyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" };
        foreach (var f in formats)
        {
            if (DateOnly.TryParseExact(s, f, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out date))
                return true;
            if (DateOnly.TryParseExact(s, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return true;
        }

        // Son çare
        if (DateTime.TryParse(s, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out var dt2))
        {
            date = DateOnly.FromDateTime(dt2);
            return true;
        }

        return false;
    }

    /// <summary>
    /// String değeri DateTime'a dönüştürmeyi dener.
    /// TR kültürü + invariant fallback ile güvenli parse.
    /// </summary>
    public static bool TryParseDateTime(string? input, out DateTime dt)
    {
        dt = default;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var s = input.Trim();
        var tr = CultureInfo.GetCultureInfo("tr-TR");

        if (DateTime.TryParse(s, tr, DateTimeStyles.AssumeLocal, out dt))
            return true;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt))
            return true;

        return false;
    }

    /// <summary>
    /// Satırın tarih alanı belirtilen tarihe eşit mi kontrol eder.
    /// </summary>
    public static bool RowMatchesDate(Dictionary<string, string?> row, string? dateCol, DateOnly raporTarihi)
    {
        if (string.IsNullOrWhiteSpace(dateCol)) return true;
        if (!row.TryGetValue(dateCol, out var raw) || string.IsNullOrWhiteSpace(raw)) return false;
        return TryParseDateOnly(raw, out var d) && d == raporTarihi;
    }

    /// <summary>
    /// Satırın tarih alanı belirtilen aralıkta mı kontrol eder.
    /// </summary>
    public static bool RowMatchesDateRange(Dictionary<string, string?> row, string? dateCol, DateOnly start, DateOnly end)
    {
        if (string.IsNullOrWhiteSpace(dateCol)) return true;
        if (!row.TryGetValue(dateCol, out var raw) || string.IsNullOrWhiteSpace(raw)) return false;
        if (!TryParseDateOnly(raw, out var d)) return false;
        return d >= start && d <= end;
    }
}
