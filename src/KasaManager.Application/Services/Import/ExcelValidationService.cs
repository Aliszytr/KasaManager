using ExcelDataReader;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;
using Microsoft.Extensions.Logging;

namespace KasaManager.Application.Services.Import;

/// <summary>
/// MS7: Excel dosya validasyonu — import öncesinde erken hata tespiti.
/// Boyut, tür, worksheet varlığı ve zorunlu kolon kontrolü yapar.
/// </summary>
public sealed class ExcelValidationService : IExcelValidationService
{
    private readonly ILogger<ExcelValidationService> _log;

    /// <summary>Maksimum dosya boyutu (100 MB)</summary>
    private const long MaxFileSizeBytes = 100 * 1024 * 1024;

    private static readonly string[] AllowedExtensions = { ".xlsx", ".xls" };

    /// <summary>
    /// Her ImportFileKind için zorunlu kolonlar.
    /// Kolon eşleşmesi case-insensitive + contains ile yapılır
    /// (Excel başlıkları "İşlem Tarihi", "ISLEM_TARIHI" vb. olabilir).
    /// </summary>
    private static readonly Dictionary<ImportFileKind, string[][]> RequiredColumns = new()
    {
        [ImportFileKind.KasaUstRapor] = new[]
        {
            new[] { "tarih", "işlem tarihi", "islem_tarihi" },       // Tarih kolonu
            new[] { "açıklama", "aciklama", "veznedar", "ad" },       // Kimlik/açıklama kolonu
            new[] { "tutar", "bakiye", "kasa", "toplam", "nakit" }    // Tutar kolonu
        },
        [ImportFileKind.BankaTahsilat] = new[]
        {
            new[] { "tarih", "işlem tarihi", "islem_tarihi" },
            new[] { "açıklama", "aciklama", "islem_aciklama" },
            new[] { "tutar", "işlem tutarı", "islem_tutari", "miktar" }
        },
        [ImportFileKind.BankaHarcama] = new[]
        {
            new[] { "tarih", "işlem tarihi", "islem_tarihi" },
            new[] { "açıklama", "aciklama", "islem_aciklama" },
            new[] { "tutar", "işlem tutarı", "islem_tutari", "miktar" }
        },
        [ImportFileKind.MasrafVeReddiyat] = new[]
        {
            new[] { "tarih", "işlem tarihi", "islem_tarihi" },
            new[] { "tutar", "miktar", "toplam" }
        },
        [ImportFileKind.OnlineMasraf] = new[]
        {
            new[] { "tarih", "işlem tarihi" },
            new[] { "tutar", "miktar" }
        },
        [ImportFileKind.OnlineHarcama] = new[]
        {
            new[] { "tarih", "işlem tarihi" },
            new[] { "tutar", "miktar" }
        },
        [ImportFileKind.OnlineReddiyat] = new[]
        {
            new[] { "tarih", "işlem tarihi" },
            new[] { "tutar", "miktar" }
        }
    };

    public ExcelValidationService(ILogger<ExcelValidationService> log)
    {
        _log = log;
    }

    public Result<ExcelValidationResult> Validate(string filePath, ImportFileKind kind)
    {
        // ── 1. Dosya varlığı ──
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return Result<ExcelValidationResult>.Fail("Dosya bulunamadı.");

        // ── 2. Uzantı kontrolü ──
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext) ||
            !AllowedExtensions.Any(a => a.Equals(ext, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<ExcelValidationResult>.Fail(
                $"Geçersiz dosya türü: '{ext}'. Sadece .xlsx ve .xls desteklenir.");
        }

        // ── 3. Boyut kontrolü ──
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > MaxFileSizeBytes)
        {
            var sizeMb = fileInfo.Length / (1024.0 * 1024.0);
            return Result<ExcelValidationResult>.Fail(
                $"Dosya çok büyük: {sizeMb:N1} MB. Maksimum: {MaxFileSizeBytes / (1024 * 1024)} MB.");
        }

        if (fileInfo.Length == 0)
            return Result<ExcelValidationResult>.Fail("Dosya boş (0 byte).");

        // ── 4. Excel açılabilirlik + worksheet kontrolü ──
        List<string> detectedColumns;
        int sheetCount;
        int rowCount;

        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = ExcelReaderFactory.CreateReader(stream);

            sheetCount = reader.ResultsCount;
            if (sheetCount == 0)
                return Result<ExcelValidationResult>.Fail("Excel dosyasında hiç worksheet bulunamadı.");

            // İlk satırı header olarak oku
            detectedColumns = new List<string>();
            if (reader.Read())
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var val = reader.GetValue(i)?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(val))
                        detectedColumns.Add(val);
                }
            }

            // Satır sayısı tahmini (header dahil)
            rowCount = 1;
            while (reader.Read()) rowCount++;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Excel validasyon: dosya okunamadı — {File}", Path.GetFileName(filePath));
            return Result<ExcelValidationResult>.Fail(
                $"Excel dosyası okunamadı: {ex.Message}");
        }

        // ── 5. Zorunlu kolon kontrolü ──
        var warnings = new List<string>();

        if (RequiredColumns.TryGetValue(kind, out var requiredGroups))
        {
            var missingGroups = new List<string>();

            foreach (var group in requiredGroups)
            {
                // Grup içindeki herhangi bir kolon adı header'da bulunmalı
                var found = detectedColumns.Any(col =>
                    group.Any(candidate =>
                        col.Contains(candidate, StringComparison.OrdinalIgnoreCase)));

                if (!found)
                {
                    missingGroups.Add(string.Join(" / ", group));
                }
            }

            if (missingGroups.Count > 0)
            {
                var msg = $"Eksik kolonlar: {string.Join(", ", missingGroups.Select(g => $"[{g}]"))}. " +
                          $"Tespit edilen kolonlar: {string.Join(", ", detectedColumns.Take(10))}";
                warnings.Add(msg);

                // Eksik kolon uyarı olarak ekle ama başarısız sayma — 
                // reader kendi mapping'i ile devam edebilir
                _log.LogWarning("Excel validasyon uyarı: {Kind} - {Msg}", kind, msg);
            }
        }

        if (rowCount <= 1)
        {
            warnings.Add("Dosyada sadece başlık satırı var, veri satırı bulunamadı.");
        }

        _log.LogInformation(
            "Excel validasyon OK: {File}, Kind={Kind}, Sheets={Sheets}, Rows={Rows}, Cols={Cols}",
            Path.GetFileName(filePath), kind, sheetCount, rowCount, detectedColumns.Count);

        return Result<ExcelValidationResult>.Success(new ExcelValidationResult
        {
            IsValid = true,
            Warnings = warnings,
            SheetCount = sheetCount,
            RowCount = rowCount,
            DetectedColumns = detectedColumns
        });
    }
}
