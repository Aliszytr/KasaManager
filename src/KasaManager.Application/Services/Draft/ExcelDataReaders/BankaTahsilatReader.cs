#nullable enable
using System.Globalization;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Services.Draft.Helpers;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Services.Draft.ExcelDataReaders;

/// <summary>
/// Banka Tahsilat Excel dosyasını okuma ve günlük özet hesaplama.
/// KasaDraftService'ten çıkarıldı - R1 Refactoring.
/// </summary>
public class BankaTahsilatReader
{
    private readonly IImportOrchestrator _import;

    public BankaTahsilatReader(IImportOrchestrator import)
    {
        _import = import;
    }

    /// <summary>
    /// Banka günlük özet verisi.
    /// </summary>
    public sealed record BankaGunAgg(
        decimal Devreden,
        decimal Yarina,
        decimal Giren,
        decimal Cikan,
        string RawJson);

    /// <summary>
    /// BankaTahsilat.xlsx'ten günlük özet okur.
    /// </summary>
    public BankaGunAgg ReadBankaTahsilatGun(
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
            var full = Path.Combine(uploadFolderAbsolute, "BankaTahsilat.xlsx");
            if (!File.Exists(full))
            {
                issues.Add("BankaTahsilat.xlsx bulunamadı. Banka günlük özet 0 kabul edildi.");
                return new BankaGunAgg(0m, 0m, 0m, 0m, "{}");
            }

            var imported = _import.Import(full, ImportFileKind.BankaTahsilat);
            if (!imported.Ok || imported.Value is null)
            {
                issues.Add($"BankaTahsilat okunamadı: {imported.Error}. Banka günlük özet 0 kabul edildi.");
                return new BankaGunAgg(0m, 0m, 0m, 0m, "{}");
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
                        if (!DateParsingHelper.RowMatchesDateRange(row, dateCol, rangeStart.Value, rangeEnd.Value)) continue;
                    }
                    else
                    {
                        if (!DateParsingHelper.RowMatchesDate(row, dateCol, raporTarihi)) continue;
                    }
                }

                // GEÇİCİ TEŞHİS LOGU — sonra silinecek
                if (row.TryGetValue(tutarCol, out var tRaw_dbg))
                    Console.WriteLine($"[TESHIS] tutarCol={tutarCol}, tRaw={tRaw_dbg}");
                if (row.TryGetValue(bakiyeCol, out var bRaw_dbg))
                    Console.WriteLine($"[TESHIS] bakiyeCol={bakiyeCol}, bRaw={bRaw_dbg}");

                if (!row.TryGetValue(tutarCol, out var tRaw) || !DecimalParsingHelper.TryParseFromJson(tRaw, out var tAbs)) continue;
                if (!row.TryGetValue(bakiyeCol, out var bRaw) || !DecimalParsingHelper.TryParseFromJson(bRaw, out var b)) continue;

                var dir = (directionCol is not null && row.TryGetValue(directionCol, out var dRaw)) ? dRaw : null;
                var signed = DecimalParsingHelper.ApplyDebitCreditSign(tAbs, dir);

                dayRows.Add((signed, b, dir));
            }

            if (dayRows.Count == 0)
            {
                return new BankaGunAgg(0m, 0m, 0m, 0m, JsonSerializer.Serialize(new { date = raporTarihi.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), note = "no rows" }));
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
            issues.Add($"BankaTahsilat günlük özet hatası: {ex.Message}. Banka günlük özet 0 kabul edildi.");
            return new BankaGunAgg(0m, 0m, 0m, 0m, "{}");
        }
    }
}
