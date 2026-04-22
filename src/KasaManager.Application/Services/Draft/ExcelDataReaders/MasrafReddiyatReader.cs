#nullable enable
using System.Globalization;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Services.Draft.Helpers;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Services.Draft.ExcelDataReaders;

/// <summary>
/// MasrafveReddiyat Excel dosyasını okuma ve aggregation.
/// KasaDraftService'ten çıkarıldı - R1 Refactoring.
/// </summary>
public class MasrafReddiyatReader
{
    private readonly IImportOrchestrator _import;

    public MasrafReddiyatReader(IImportOrchestrator import)
    {
        _import = import;
    }

    /// <summary>
    /// Masraf ve Reddiyat aggregation sonucu.
    /// </summary>
    public sealed record MasrafReddiyatAgg(decimal Masraf, decimal Reddiyat, decimal Diger, string RawJson);

    /// <summary>
    /// Tek gün veya tarih aralığı için masraf/reddiyat toplamı okur.
    /// </summary>
    public MasrafReddiyatAgg ReadMasrafReddiyatAgg(
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
            var full = ResolveExistingFile(uploadFolderAbsolute, "MasrafveReddiyat.xlsx");
            if (full is null)
            {
                issues.Add("MasrafveReddiyat.xlsx bulunamadı. Masraf/Reddiyat 0 kabul edildi.");
                rawJson = "{}";
                return new MasrafReddiyatAgg(0m, 0m, 0m, rawJson);
            }

            var imported = _import.ImportTrueSource(full, ImportFileKind.MasrafVeReddiyat);
            if (!imported.Ok || imported.Value is null)
            {
                issues.Add($"MasrafveReddiyat okunamadı: {imported.Error}. Masraf/Reddiyat 0 kabul edildi.");
                rawJson = "{}";
                return new MasrafReddiyatAgg(0m, 0m, 0m, rawJson);
            }

            var table = imported.Value;
            var dateCol = CanonicalKeyHelper.FindDateCanonical(table);
            var tipCol = CanonicalKeyHelper.FindCanonical(table, "tip") ?? "tip";
            var miktarCol = CanonicalKeyHelper.FindCanonical(table, "miktar") 
                ?? CanonicalKeyHelper.FindCanonical(table, "islem_tutari") ?? "miktar";

            decimal masraf = 0m;
            decimal reddiyat = 0m;
            decimal diger = 0m;
            int matched = 0;
            int inRangeRows = 0;
            int masrafRows = 0;
            int reddiyatRows = 0;
            int digerRows = 0;

            foreach (var row in table.Rows)
            {
                if (row is null) continue;

                if (!fullExcelTotals && dateCol is not null)
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

                inRangeRows++;
                if (!row.TryGetValue(miktarCol, out var rawM) || !DecimalParsingHelper.TryParseFromTurkish(rawM, out var m)) continue;

                var tip = row.GetValueOrDefault(tipCol) ?? string.Empty;
                var tipU = tip.Trim().ToUpperInvariant();

                if (tipU.Contains("MASRAF"))
                    { masraf += m; masrafRows++; }
                else if (tipU.Contains("REDD"))
                    { reddiyat += m; reddiyatRows++; }
                else
                    { diger += m; digerRows++; }

                matched++;
            }

            rawJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["date"] = raporTarihi.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                ["file"] = Path.GetFileName(full),
                ["dateCol"] = dateCol,
                ["tipCol"] = tipCol,
                ["miktarCol"] = miktarCol,
                ["rowCount"] = table.Rows.Count,
                ["matchedRows"] = matched,
                ["inRangeRows"] = inRangeRows,
                ["masrafRows"] = masrafRows,
                ["reddiyatRows"] = reddiyatRows,
                ["digerRows"] = digerRows,
                ["masraf"] = masraf,
                ["reddiyat"] = reddiyat,
                ["diger"] = diger
            });

            return new MasrafReddiyatAgg(masraf, reddiyat, diger, rawJson);
        }
        catch (Exception ex)
        {
            issues.Add($"MasrafveReddiyat hesaplama hatası: {ex.Message}. Masraf/Reddiyat 0 kabul edildi.");
            rawJson = "{}";
            return new MasrafReddiyatAgg(0m, 0m, 0m, rawJson);
        }
    }

    /// <summary>
    /// Dosya yolunu çözümler (hem tam yol hem dosya adı destekler).
    /// </summary>
    private static string? ResolveExistingFile(string folder, string fileNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath)) return null;

        // 1) Tam yol verilmiş
        if (Path.IsPathRooted(fileNameOrPath) && File.Exists(fileNameOrPath))
            return fileNameOrPath;

        // 2) Klasör + dosya adı
        var combined = Path.Combine(folder, fileNameOrPath);
        if (File.Exists(combined)) return combined;

        // 3) Case-insensitive arama
        if (Directory.Exists(folder))
        {
            var files = Directory.GetFiles(folder);
            var match = files.FirstOrDefault(f =>
                Path.GetFileName(f).Equals(fileNameOrPath, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return null;
    }
}
