#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Services.Draft.Helpers;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Services.Draft.ExcelDataReaders;

/// <summary>
/// R19: Banka Harç Excel dosyasından günlük özet okuma.
/// KasaDraftService'ten bağımsız, yeniden kullanılabilir reader sınıfı.
/// </summary>
public class BankaHarcReader
{
    private readonly IImportOrchestrator _import;

    public BankaHarcReader(IImportOrchestrator import)
    {
        _import = import;
    }

    /// <summary>
    /// Banka harç günlük özet verisi.
    /// </summary>
    public record BankaGunAgg(
        decimal Devreden,
        decimal Yarina,
        decimal Giren,
        decimal Cikan,
        string RawJson);

    /// <summary>
    /// BankaHarc.xlsx dosyasından günlük özet okur.
    /// </summary>
    public BankaGunAgg ReadGunAgg(
        DateOnly raporTarihi,
        string uploadFolderAbsolute,
        List<string> issues,
        out string? rawJson,
        DateOnly? rangeStart = null,
        DateOnly? rangeEnd = null,
        bool fullExcelTotals = false)
    {
        rawJson = null;
        try
        {
            var full = FileResolverHelper.ResolveExistingFile(uploadFolderAbsolute, "BankaHarc.xlsx");
            if (full is null)
            {
                issues.Add("BankaHarc.xlsx bulunamadı. Banka harc günlük özet 0 kabul edildi.");
                return CreateEmptyAgg();
            }

            var imported = _import.Import(full, ImportFileKind.BankaHarcama);
            if (!imported.Ok || imported.Value is null)
            {
                issues.Add($"BankaHarc okunamadı: {imported.Error}. Banka harc günlük özet 0 kabul edildi.");
                return CreateEmptyAgg();
            }

            var table = imported.Value;
            var dateCol = CanonicalKeyHelper.FindDateCanonical(table) ?? "islem_tarihi";
            var tutarCol = CanonicalKeyHelper.FindCanonical(table, "islem_tutari") ?? "islem_tutari";
            var bakiyeCol = CanonicalKeyHelper.FindCanonical(table, "islem_sonrasi_bakiye") ?? "islem_sonrasi_bakiye";

            var directionCol =
                CanonicalKeyHelper.FindCanonical(table, "borc_alacak")
                ?? CanonicalKeyHelper.FindCanonicalByHeaderContains(table, "borç")
                ?? CanonicalKeyHelper.FindCanonicalByHeaderContains(table, "borc")
                ?? CanonicalKeyHelper.FindCanonicalByHeaderContains(table, "alacak");

            var dayRows = new List<(decimal SignedTutar, decimal Bakiye, string? Direction)>();
            
            foreach (var row in table.Rows)
            {
                if (row is null) continue;
                
                if (!fullExcelTotals)
                {
                    if (rangeStart.HasValue && rangeEnd.HasValue)
                    {
                        if (!CanonicalKeyHelper.RowMatchesDateRange(row, dateCol, rangeStart.Value, rangeEnd.Value)) 
                            continue;
                    }
                    else
                    {
                        if (!CanonicalKeyHelper.RowMatchesDate(row, dateCol, raporTarihi)) 
                            continue;
                    }
                }
                
                if (!row.TryGetValue(tutarCol, out var tRaw) || !DecimalParsingHelper.TryParseDecimal(tRaw, out var tAbs)) 
                    continue;
                if (!row.TryGetValue(bakiyeCol, out var bRaw) || !DecimalParsingHelper.TryParseDecimal(bRaw, out var b)) 
                    continue;

                var dir = (directionCol is not null && row.TryGetValue(directionCol, out var dRaw)) ? dRaw : null;
                var signed = ApplyDebitCreditSign(tAbs, dir);

                dayRows.Add((signed, b, dir));
            }

            if (dayRows.Count == 0)
            {
                rawJson = JsonSerializer.Serialize(new
                {
                    date = raporTarihi.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                    note = "no rows"
                });
                return new BankaGunAgg(0m, 0m, 0m, 0m, rawJson);
            }

            decimal giren = 0m;
            decimal cikan = 0m;
            foreach (var (signedTutar, _, _) in dayRows)
            {
                if (signedTutar >= 0) giren += signedTutar;
                else cikan += Math.Abs(signedTutar);
            }

            var first = dayRows[0];
            var devreden = first.Bakiye - first.SignedTutar;
            var yarina = dayRows[^1].Bakiye;

            var dbg = new Dictionary<string, object?>
            {
                ["date"] = raporTarihi.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                ["devreden"] = devreden,
                ["yarina"] = yarina,
                ["giren"] = giren,
                ["cikan"] = cikan,
                ["dateCol"] = dateCol,
                ["tutarCol"] = tutarCol,
                ["bakiyeCol"] = bakiyeCol,
                ["directionCol"] = directionCol,
                ["firstDirection"] = dayRows[0].Direction,
                ["rowCount"] = dayRows.Count
            };
            rawJson = JsonSerializer.Serialize(dbg);

            return new BankaGunAgg(devreden, yarina, giren, cikan, rawJson);
        }
        catch (Exception ex)
        {
            issues.Add($"BankaHarc günlük özet hatası: {ex.Message}. Banka harc günlük özet 0 kabul edildi.");
            return CreateEmptyAgg();
        }
    }

    private static BankaGunAgg CreateEmptyAgg() => new(0m, 0m, 0m, 0m, "{}");

    /// <summary>
    /// Borç/Alacak yönüne göre tutar işareti belirleme.
    /// Alacak = pozitif (giren), Borç = negatif (çıkan).
    /// </summary>
    private static decimal ApplyDebitCreditSign(decimal absValue, string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction))
            return absValue;

        var d = direction.Trim().ToLowerInvariant();
        
        // Borç = negatif (çıkan para)
        if (d.Contains("borç") || d.Contains("borc") || d == "b")
            return -absValue;

        // Alacak = pozitif (giren para)
        if (d.Contains("alacak") || d == "a")
            return absValue;

        // Varsayılan: pozitif
        return absValue;
    }
}
