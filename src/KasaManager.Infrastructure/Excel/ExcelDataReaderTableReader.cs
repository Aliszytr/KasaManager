using System.Text;
using ExcelDataReader;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Services;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;

namespace KasaManager.Infrastructure.Excel;

/// <summary>
/// Streaming'e yakın okuma: ExcelDataReader.
/// Büyük dosyalarda ClosedXML'a göre daha az RAM tüketir.
/// </summary>
public sealed class ExcelDataReaderTableReader : IExcelTableReader
{
    public ExcelDataReaderTableReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public Result<ImportedTable> ReadTable(string filePath, ExcelReadOptions options)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return Result<ImportedTable>.Fail("Dosya yolu boş.");

            if (!File.Exists(filePath))
                return Result<ImportedTable>.Fail($"Dosya bulunamadı: {filePath}");

            var headerRowIndex = options.HeaderRowIndex1Based;

            List<string> rawHeaders = new();
            int bestRow = -1;
            int bestStringCount = -1;

            // =========================
            // 1️⃣ PASS: HEADER BUL
            // =========================
            using (var stream1 = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream1))
            {
                int rowIndex = 0;

                while (reader.Read())
                {
                    rowIndex++;

                    var fields = new object?[reader.FieldCount];
                    for (int i = 0; i < reader.FieldCount; i++)
                        fields[i] = reader.GetValue(i);

                    if (headerRowIndex.HasValue)
                    {
                        if (rowIndex == headerRowIndex.Value)
                        {
                            rawHeaders = fields.Select(v => v?.ToString()?.Trim() ?? string.Empty).ToList();
                            bestRow = rowIndex;
                            break;
                        }
                    }
                    else
                    {
                        if (rowIndex <= 50)
                        {
                            var raw = fields.Select(v => v?.ToString()?.Trim() ?? string.Empty).ToList();

                            // Header satırı heuristiği:
                            // - Boş olmayan hücre sayısı
                            // - Metin hücre sayısı (harf içeren)
                            // - Kasa raporlarında sık geçen header kelimeleri için bonus
                            var nonEmpty = raw.Count(s => !string.IsNullOrWhiteSpace(s));
                            var texty = raw.Count(s => !string.IsNullOrWhiteSpace(s) && s.Any(ch => char.IsLetter(ch)));

                            int bonus = 0;
                            foreach (var cell in raw)
                            {
                                if (string.IsNullOrWhiteSpace(cell)) continue;
                                var n = ExcelHeaderNormalizer.Normalize(cell);

                                // KasaUstRapor tipik başlıkları
                                if (n.Contains("veznedar")) bonus += 4;
                                if (n.Contains("bakiye")) bonus += 4;
                                if (n.Contains("tahsilat")) bonus += 3;
                                if (n.Contains("harc")) bonus += 3;
                                if (n.Contains("reddiyat")) bonus += 2;
                                if (n.Contains("stopaj")) bonus += 2;
                                if (n.Contains("online")) bonus += 1;
                                if (n.Contains("pos")) bonus += 1;
                            }

                            // Ağırlıklı skor (metin ağırlıklı) – header satırını data satırından ayırmak için.
                            var score = (texty * 3) + nonEmpty + bonus;

                            if (score > bestStringCount)
                            {
                                bestStringCount = score;
                                bestRow = rowIndex;
                                rawHeaders = raw;
                            }
                        }

                        if (rowIndex >= 50)
                            break;
                    }
                }
            }

            if (bestRow <= 0)
                return Result<ImportedTable>.Fail("Header satırı bulunamadı.");

            var canonicalMap = ExcelHeaderNormalizer.BuildCanonicalMap(rawHeaders, options.ColumnAliases);
            var columnMetas = BuildImportedColumnMetas(rawHeaders, canonicalMap);

            var columns = columnMetas
                .Select(m => m.CanonicalName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var req in options.RequiredColumns ?? new())
            {
                var nreq = ExcelHeaderNormalizer.Normalize(req);
                if (!string.IsNullOrWhiteSpace(nreq) &&
                    !columns.Contains(nreq, StringComparer.OrdinalIgnoreCase))
                {
                    return Result<ImportedTable>.Fail($"Zorunlu kolon bulunamadı: {req}");
                }
            }

            // =========================
            // 2️⃣ PASS: DATA OKU
            // =========================
            var rows = new List<Dictionary<string, string?>>();

            using (var stream2 = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader2 = ExcelReaderFactory.CreateReader(stream2))
            {
                int currentRow = 0;

                while (reader2.Read())
                {
                    currentRow++;
                    if (currentRow <= bestRow) continue;

                    if (options.MaxRows.HasValue && rows.Count >= options.MaxRows.Value)
                        break;

                    var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    bool anyValue = false;

                    for (int i = 0; i < reader2.FieldCount; i++)
                    {
                        if (!canonicalMap.TryGetValue(i, out var colName))
                            continue;

                        var val = reader2.GetValue(i);
                        var sVal = val?.ToString()?.Trim();

                        if (!string.IsNullOrWhiteSpace(sVal))
                            anyValue = true;

                        row[colName] = sVal;
                    }

                    if (options.SkipEmptyRows && !anyValue)
                        continue;

                    rows.Add(row);
                }
            }

            return Result<ImportedTable>.Success(new ImportedTable
            {
                SourceFileName = Path.GetFileName(filePath),
                Kind = ImportFileKind.Unknown,
                Columns = columns,         // R1 uyumluluk
                ColumnMetas = columnMetas, // R2 ana veri
                Rows = rows
            });
        }
        catch (Exception ex)
        {
            return Result<ImportedTable>.Fail($"Excel okuma hatası: {ex.Message}");
        }
    }

    private static List<ImportedColumnMeta> BuildImportedColumnMetas(
        List<string> rawHeaders,
        Dictionary<int, string> canonicalMap)
    {
        var metas = new List<ImportedColumnMeta>();

        foreach (var kvp in canonicalMap.OrderBy(x => x.Key))
        {
            var idx = kvp.Key;
            var canonical = kvp.Value?.Trim() ?? string.Empty;

            var original = (idx >= 0 && idx < rawHeaders.Count)
                ? (rawHeaders[idx]?.Trim() ?? string.Empty)
                : string.Empty;

            if (string.IsNullOrWhiteSpace(original))
                original = $"Kolon {idx + 1}";

            if (string.IsNullOrWhiteSpace(canonical))
                canonical = ExcelHeaderNormalizer.Normalize(original);

            metas.Add(new ImportedColumnMeta
            {
                Index = idx,
                CanonicalName = canonical,
                OriginalHeader = original
            });
        }

        return metas;
    }
}