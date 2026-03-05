using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Constants;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Services;

// Helpers: FindCanonical, TryParseDateOnly, TryParseDateTime, BuildRawJson, TryParseDecimal, ApplyDebitCreditSign, TryGet
public sealed partial class KasaDraftService
{

    private static string? FindCanonical(ImportedTable table, string canonical)
        => Draft.Helpers.CanonicalKeyHelper.FindCanonical(table, canonical);

    private static string? FindCanonicalByHeaderContains(ImportedTable table, string contains)
        => Draft.Helpers.CanonicalKeyHelper.FindCanonicalByHeaderContains(table, contains);

    private static string? FindDateCanonical(ImportedTable table)
        => Draft.Helpers.CanonicalKeyHelper.FindDateCanonical(table);

    private static bool RowMatchesDate(Dictionary<string, string?> row, string? dateCol, DateOnly raporTarihi)
        => Draft.Helpers.DateParsingHelper.RowMatchesDate(row, dateCol, raporTarihi);

    private static bool RowMatchesDateRange(Dictionary<string, string?> row, string? dateCol, DateOnly start, DateOnly end)
        => Draft.Helpers.DateParsingHelper.RowMatchesDateRange(row, dateCol, start, end);

                                                private static bool TryParseDateOnly(string? raw, out DateOnly date)
        => Draft.Helpers.DateParsingHelper.TryParseDateOnly(raw, out date);

    private static bool TryParseDateTime(string? input, out DateTime dt)
        => Draft.Helpers.DateParsingHelper.TryParseDateTime(input, out dt);


    private static string BuildRawJson(
        KasaManager.Domain.Reports.Snapshots.KasaRaporSnapshot genSnap,
        string? bankaLastRowRaw,
        string? onlineAggRaw = null,
        string? bankaGunAggRaw = null,
        string? bankaExtraAggRaw = null,
        string? bankaHarcGunAggRaw = null,
        string? onlineHarcRaw = null,
        string? onlineMasrafRaw = null,
        string? masrafReddiyatRaw = null)
    {
        try
        {
            object? TryDeserializeDict(string? raw)
            {
                if (string.IsNullOrWhiteSpace(raw)) return null;
                try
                {
                    // online/banka gun debug objeleri Dictionary<string, object?> olabilir
                    return JsonSerializer.Deserialize<Dictionary<string, object?>>(raw);
                }
                catch (JsonException)
                {
                    try
                    {
                        return JsonSerializer.Deserialize<Dictionary<string, string?>>(raw);
                    }
                    catch (JsonException)
                    {
                        return raw; // fallback: raw string
                    }
                }
            }

            var obj = new
            {
                snapshot = new
                {
                    genSnap.RaporTarihi,
                    genSnap.RaporTuru,
                    genSnap.SelectionTotal,
                    SelectedCount = genSnap.Rows.Count(r => r.IsSelected)
                },
                bankaTahsilatLastRow = TryDeserializeDict(bankaLastRowRaw),
                onlineReddiyatAgg = TryDeserializeDict(onlineAggRaw),
                bankaTahsilatGunAgg = TryDeserializeDict(bankaGunAggRaw),
                bankaTahsilatExtraInflowAgg = TryDeserializeDict(bankaExtraAggRaw),
                bankaHarcGunAgg = TryDeserializeDict(bankaHarcGunAggRaw),
                onlineHarcAgg = TryDeserializeDict(onlineHarcRaw),
                onlineMasrafAgg = TryDeserializeDict(onlineMasrafRaw),
                masrafReddiyatAgg = TryDeserializeDict(masrafReddiyatRaw)
            };

            return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"[BuildRawJson] JSON oluşturma hatası: {ex.Message}");
            return "{}";
        }
    }

    private static bool TryParseDecimal(string? input, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var s = input.Trim();
        // TR/EN parse denemesi
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out value)) return true;
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return true;

        // Para birimi vb. temizle
        s = s.Replace("TL", "", StringComparison.OrdinalIgnoreCase).Trim();
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out value)
               || decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static decimal ApplyDebitCreditSign(decimal absAmount, string? direction)
    {
        // Default: export'larda Alacak (+) varsayıyoruz.
        if (absAmount < 0) absAmount = Math.Abs(absAmount);
        if (string.IsNullOrWhiteSpace(direction)) return absAmount;

        var d = direction.Trim();

        // TR bankalar: "Borç" / "Alacak" veya kısaltmalar.
        // Borç => çıkış (negatif), Alacak => giriş (pozitif)
        if (d.StartsWith("B", StringComparison.OrdinalIgnoreCase) || d.Contains("borç", StringComparison.OrdinalIgnoreCase) || d.Contains("borc", StringComparison.OrdinalIgnoreCase))
            return -absAmount;

        if (d.StartsWith("A", StringComparison.OrdinalIgnoreCase) || d.Contains("alacak", StringComparison.OrdinalIgnoreCase))
            return absAmount;

        // Bilinmeyen değer: güvenli varsayım (+)
        return absAmount;
    }

    // ------------------------------------------------------------
    // Small safe helpers (R8)
    // ------------------------------------------------------------
    // IMPORTANT (R8): Tek overload bırakıyoruz.
    // string -> string? implicit olduğu için iki overload ambiguous'e düşüyordu.
    private static string? TryGet(Dictionary<string, string?> row, string? canonical)
    {
        if (row is null) return null;
        if (string.IsNullOrWhiteSpace(canonical)) return null;
        return row.TryGetValue(canonical, out var v) ? v : null;
    }
}
