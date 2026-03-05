#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Services.Draft.Helpers;
using KasaManager.Domain.Reports;

namespace KasaManager.Application.Services.Draft.ExcelDataReaders;

/// <summary>
/// R19: Online Tahsilat/Reddiyat Excel dosyalarından veri okuma.
/// KasaDraftService'ten bağımsız, yeniden kullanılabilir reader sınıfı.
/// </summary>
public class OnlineReader
{
    private readonly IImportOrchestrator _import;

    public OnlineReader(IImportOrchestrator import)
    {
        _import = import;
    }

    /// <summary>
    /// Online Tahsilat veya Harç dosyasının toplamını hesaplar.
    /// </summary>
    public decimal ReadTotal(
        DateOnly raporTarihi,
        string uploadFolderAbsolute,
        string fileName,
        ImportFileKind kind,
        List<string> issues,
        out string? rawJson,
        DateOnly? rangeStart = null,
        DateOnly? rangeEnd = null,
        bool fullExcelTotals = false)
    {
        rawJson = null;
        try
        {
            var full = FileResolverHelper.ResolveExistingFile(uploadFolderAbsolute, fileName);
            if (full is null)
            {
                issues.Add($"{fileName} bulunamadı. {kind} toplamı 0 kabul edildi.");
                return 0m;
            }

            var imported = _import.Import(full, kind);
            if (!imported.Ok || imported.Value is null)
            {
                issues.Add($"{fileName} okunamadı: {imported.Error}. Toplam 0 kabul edildi.");
                return 0m;
            }

            var table = imported.Value;
            var dateCol = CanonicalKeyHelper.FindDateCanonical(table);
            var miktarCol = CanonicalKeyHelper.FindCanonical(table, "miktar") 
                         ?? CanonicalKeyHelper.FindCanonical(table, "islem_tutari") 
                         ?? "miktar";

            decimal total = 0m;
            int matched = 0;

            foreach (var row in table.Rows)
            {
                if (row is null) continue;

                if (dateCol is not null && !fullExcelTotals)
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

                if (!row.TryGetValue(miktarCol, out var raw)) continue;
                if (!DecimalParsingHelper.TryParseDecimal(raw, out var v)) continue;

                total += v;
                matched++;
            }

            rawJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["date"] = raporTarihi.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                ["file"] = Path.GetFileName(full),
                ["kind"] = kind.ToString(),
                ["dateCol"] = dateCol,
                ["miktarCol"] = miktarCol,
                ["rowCount"] = table.Rows.Count,
                ["matchedRows"] = matched,
                ["total"] = total
            });

            return total;
        }
        catch (Exception ex)
        {
            issues.Add($"{fileName} toplam hesaplama hatası: {ex.Message}. Toplam 0 kabul edildi.");
            return 0m;
        }
    }

    /// <summary>
    /// Online reddiyat toplamlarını okur.
    /// </summary>
    public record OnlineReddiyatAgg(decimal OnlineTahsilat, decimal OnlineReddiyat, decimal OnlineHarc, string RawJson);

    public OnlineReddiyatAgg ReadReddiyatTotals(
        DateOnly raporTarihi,
        string uploadFolderAbsolute,
        List<string> issues,
        out string? rawJson,
        DateOnly? rangeStart = null,
        DateOnly? rangeEnd = null,
        bool fullExcelTotals = false)
    {
        rawJson = null;

        // Not: OnlineTahsilat ayrı bir dosya değil, BankaTahsilat içinde yer alabilir.
        // Bu reader sadece OnlineReddiyat ve OnlineMasraf'ı destekler.
        
        var reddiyat = ReadTotal(raporTarihi, uploadFolderAbsolute, "OnlineReddiyat.xlsx", 
            ImportFileKind.OnlineReddiyat, issues, out var reddiyatJson, rangeStart, rangeEnd, fullExcelTotals);
        
        var masraf = ReadTotal(raporTarihi, uploadFolderAbsolute, "OnlineMasraf.xlsx", 
            ImportFileKind.OnlineMasraf, issues, out var masrafJson, rangeStart, rangeEnd, fullExcelTotals);
        
        var harcama = ReadTotal(raporTarihi, uploadFolderAbsolute, "OnlineHarcama.xlsx", 
            ImportFileKind.OnlineHarcama, issues, out var harcamaJson, rangeStart, rangeEnd, fullExcelTotals);

        rawJson = JsonSerializer.Serialize(new
        {
            reddiyat = reddiyatJson,
            masraf = masrafJson,
            harcama = harcamaJson
        });

        return new OnlineReddiyatAgg(masraf, reddiyat, harcama, rawJson);
    }
}

/// <summary>
/// Dosya çözümleme yardımcı sınıfı.
/// </summary>
public static class FileResolverHelper
{
    private static readonly string[] XlsxExtensions = new[] { ".xlsx", ".xls" };

    public static string? ResolveExistingFile(string folder, string fileName)
    {
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(fileName))
            return null;

        // Önce tam dosya adıyla dene
        var full = Path.Combine(folder, fileName);
        if (File.Exists(full))
            return full;

        // Uzantısız dene
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        foreach (var ext in XlsxExtensions)
        {
            var candidate = Path.Combine(folder, nameWithoutExt + ext);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
