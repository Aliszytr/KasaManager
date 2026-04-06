using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

using KasaManager.Application.Abstractions;
using KasaManager.Application.Processing.Narratives;
using KasaManager.Application.Services;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KasaManager.Web.Controllers;

[Authorize]
public sealed class ImportController : Controller
{
    private readonly IFileStorage _storage;
    private readonly IImportOrchestrator _orchestrator;
    private readonly IExcelValidationService _excelValidation;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _cfg;
    private readonly ILogger<ImportController> _log;
    private readonly IKasaReportDateRulesService _dateRules;
    private readonly IKasaGlobalDefaultsService _globalDefaults;
    private readonly IComparisonArchiveService _archiveService;

    public ImportController(
        IFileStorage storage,
        IImportOrchestrator orchestrator,
        IExcelValidationService excelValidation,
        IWebHostEnvironment env,
        IConfiguration cfg,
        ILogger<ImportController> log,
        IKasaReportDateRulesService dateRules,

        IKasaGlobalDefaultsService globalDefaults,
        IComparisonArchiveService archiveService)
    {
        _storage = storage;
        _orchestrator = orchestrator;
        _excelValidation = excelValidation;
        _env = env;
        _cfg = cfg;
        _log = log;
        _dateRules = dateRules;

        _globalDefaults = globalDefaults;
        _archiveService = archiveService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var files = ListUploadedFiles();
        var vm = new ImportIndexViewModel
        {
            UploadedFiles = files,
            SelectedFileName = files.FirstOrDefault() ?? string.Empty,
            UstRaporPanel = await HydrateUstRaporPanelAsync(files, ct)
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(200 * 1024 * 1024)]          // 200 MB — yıllık büyük dosyalar için
    [RequestFormLimits(MultipartBodyLengthLimit = 200 * 1024 * 1024)]
    public IActionResult Upload(List<IFormFile> files)
    {
        _log.LogInformation("Upload başlatıldı. Dosya sayısı: {Count}", files?.Count ?? 0);

        if (files is null || files.Count == 0)
        {
            TempData["Error"] = "Lütfen dosya seçin.";
            return RedirectToAction(nameof(Index));
        }

        var sub = _cfg.GetValue<string>("Upload:SubFolder") ?? "Data\\Raporlar";
        var maxBytes = _cfg.GetValue<long>("Upload:MaxBytes", 50 * 1024 * 1024);
        var overwrite = _cfg.GetValue<bool>("Upload:Overwrite", true);
        var allowed = _cfg.GetSection("Upload:AllowedExtensions").Get<string[]>() ?? new[] { ".xlsx", ".xls" };

        int ok = 0, fail = 0;
        var errors = new List<string>();

        foreach (var f in files)
        {
            if (f is null || f.Length == 0)
            {
                fail++;
                continue;
            }

            try
            {
                using var s = f.OpenReadStream();
                var res = _storage.SaveUploadedFile(s, f.FileName, sub, allowed, maxBytes, overwrite);

                if (res.Ok)
                    ok++;
                else
                {
                    fail++;
                    errors.Add($"{f.FileName}: {res.Error}");
                }
            }
            catch (Exception ex)
            {
                fail++;
                errors.Add($"{f.FileName}: {ex.Message}");
            }
        }

        // ✅ Karşılaştırma dosyalarını otomatik arşivle
        if (ok > 0)
        {
            try
            {
                var folder = GetUploadFolderAbsolute(sub);
                var reportDate = DetectReportDateFromFiles(folder);
                _archiveService.ArchiveComparisonFiles(folder, reportDate);
                // Eski arşivleri temizle (varsayılan 60 gün)
                var retentionDays = _cfg.GetValue<int>("Comparison:ArchiveRetentionDays", 60);
                _archiveService.CleanupOldArchives(folder, retentionDays);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Karşılaştırma arşivleme hatası (yükleme etkilenmedi)");
            }
        }

        if (errors.Count > 0)
            TempData["Error"] = string.Join(" | ", errors);

        _log.LogInformation("Upload tamamlandı. Başarılı: {Ok}, Hatalı: {Fail}", ok, fail);
        TempData["Info"] = $"Yükleme tamamlandı. Başarılı: {ok}, Hatalı: {fail}";

        // QW6: Eski upload klasörlerini temizle (7 günden eski)
        try
        {
            var uploadRoot = Path.Combine(_env.WebRootPath, "Data", "Raporlar");
            if (Directory.Exists(uploadRoot))
            {
                var cutoff = DateTime.UtcNow.AddDays(-7);
                foreach (var dir in Directory.GetDirectories(uploadRoot))
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if (dirInfo.CreationTimeUtc < cutoff)
                    {
                        dirInfo.Delete(recursive: true);
                        _log.LogInformation("Eski upload klasörü silindi: {Dir}", dirInfo.Name);
                    }
                }
            }
        }
        catch (Exception ex) { _log.LogDebug(ex, "Upload temizleme başarısız (kritik değil)"); }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RunImport(string selectedFileName, ImportFileKind kind)
    {
        if (string.IsNullOrWhiteSpace(selectedFileName))
        {
            TempData["Error"] = "Lütfen bir dosya seçin.";
            return RedirectToAction(nameof(Index));
        }

        var sub = _cfg.GetValue<string>("Upload:SubFolder") ?? "Data\\Raporlar";
        var folder = GetUploadFolderAbsolute(sub);
        var fullPath = Path.Combine(folder, selectedFileName);

        if (!System.IO.File.Exists(fullPath))
        {
            TempData["Error"] = "Dosya bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        // MS7: Import öncesi Excel validasyonu
        var validation = _excelValidation.Validate(fullPath, kind);
        if (!validation.Ok)
        {
            TempData["Error"] = validation.Error;
            return RedirectToAction(nameof(Index));
        }
        if (validation.Value!.Warnings.Count > 0)
        {
            TempData["Warning"] = string.Join(" | ", validation.Value.Warnings);
        }

        var result = _orchestrator.Import(fullPath, kind);
        if (!result.Ok || result.Value is null)
        {
            TempData["Error"] = result.Error ?? "Import başarısız.";
            return RedirectToAction(nameof(Index));
        }

        var table = result.Value;

        // ✅ R3D: Hangi dosya olursa olsun, Preview katmanı “ham grid’in devamı” olacak.
        // BankaTahsilat/BankaHarcama + OnlineMasraf/OnlineHarcama hepsi aynı mantık.
        if (IsNarrativePreviewKind(table.Kind))
        {
            var preview = BuildNarrativePreview(table, maxRows: 250);

            // 1) VM preview listesi (ayrı grid/rapor için)
            // 2) Ham tabloyu zenginleştir: tüm orijinal kolonlar + ek kolonlar
            EnrichTableWithPreviewColumns(table, preview);

            var vmPreview = new ImportResultViewModel
            {
                SourceFileName = table.SourceFileName,
                Kind = table.Kind,
                ColumnMetas = table.ColumnMetas,
                Rows = table.Rows,
                NarrativePreviewRows = preview
            };

            return View("Result", vmPreview);
        }

        // Diğer rapor türleri (şimdilik) normal şekilde
        var vm = new ImportResultViewModel
        {
            SourceFileName = table.SourceFileName,
            Kind = table.Kind,
            ColumnMetas = table.ColumnMetas,
            Rows = table.Rows
        };

        return View("Result", vm);
    }

    private static bool IsNarrativePreviewKind(ImportFileKind kind)
        => kind == ImportFileKind.BankaTahsilat
           || kind == ImportFileKind.BankaHarcama
           || kind == ImportFileKind.OnlineMasraf
           || kind == ImportFileKind.OnlineHarcama;

    /// <summary>
    /// R3D: Ham import grid bozulmaz.
    /// Orijinal kolonlar korunur; sadece ek kolonlar tabloya enjekte edilir.
    /// </summary>
    private static void EnrichTableWithPreviewColumns(ImportedTable table, List<NarrativePreviewRow> previewRows)
    {
        // ColumnMetas init-only ama list mutable → assignment yok, Add var.
        var metas = table.ColumnMetas; // init default: new()
        var rows = table.Rows;         // init default: new()

        // Ek kolon tanımları
        var extras = new (string Canonical, string Header)[]
        {
            ("aciklama_ham", "Açıklama (Ham)"),
            ("aciklama_secilen", "Açıklama (Seçilen Segment)"),
            ("aciklama_normalize", "Açıklama (Normalize)"),
            ("normalized_court_unit", "NormalizedCourtUnit"),
            ("file_no", "FileNo"),
        };

        foreach (var ex in extras)
        {
            var exists = false;
            for (int i = 0; i < metas.Count; i++)
            {
                if (string.Equals(metas[i].CanonicalName, ex.Canonical, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                metas.Add(new ImportedColumnMeta
                {
                    Index = metas.Count,
                    CanonicalName = ex.Canonical,
                    OriginalHeader = ex.Header
                });
            }
        }

        // İlk N satıra enjekte et (previewRows zaten maxRows kadar)
        var take = Math.Min(previewRows.Count, rows.Count);
        for (int i = 0; i < take; i++)
        {
            var r = rows[i];
            var p = previewRows[i];

            r["aciklama_ham"] = p.AciklamaHam;
            r["aciklama_secilen"] = p.AciklamaSecilen;
            r["aciklama_normalize"] = p.AciklamaNormalize;
            r["normalized_court_unit"] = p.NormalizedCourtUnit;
            r["file_no"] = p.FileNo;
        }
    }

    private List<NarrativePreviewRow> BuildNarrativePreview(ImportedTable table, int maxRows)
    {
        // ✅ R3D kritik: Banka dosyalarında tarih/tutar canonical'ı farklı:
        // Banka: islem_tarihi / islem_tutari
        // Online: tarih / tutar (çoğu zaman)
        var colAciklama = FindCanonicalByCandidates(table, new[]
        {
            "aciklama", "açıklama", "islem_aciklama", "işlem açıklama", "aciklama_metni"
        });

        var colTarih = FindCanonicalByCandidates(table, new[]
        {
            "islem_tarihi", "işlem tarihi", "tarih"
        });

        var colTutar = FindCanonicalByCandidates(table, new[]
        {
            "islem_tutari", "işlem tutarı", "tutar", "miktar"
        });

        var list = new List<NarrativePreviewRow>();

        var rows = table.Rows;
        var take = Math.Min(maxRows, rows.Count);

        for (int i = 0; i < take; i++)
        {
            var r = rows[i];

            r.TryGetValue(colAciklama ?? string.Empty, out var aciklamaHam);
            r.TryGetValue(colTarih ?? string.Empty, out var tarihRaw);
            r.TryGetValue(colTutar ?? string.Empty, out var tutarRaw);

            var parse = BankNarrativeParser.Parse(aciklamaHam ?? string.Empty);

            list.Add(new NarrativePreviewRow
            {
                RowNo = i + 1,
                Tarih = FormatShortDate(tarihRaw),
                Tutar = NormalizeMoney(tutarRaw),

                AciklamaHam = aciklamaHam,
                AciklamaSecilen = parse.SelectedSegment,
                AciklamaNormalize = parse.Normalized,
                NormalizedCourtUnit = parse.NormalizedCourtUnit,
                FileNo = parse.FileNo,

                IssueCount = parse.Issues.Count,
                Issues = string.Join(" | ", parse.Issues.Select(x => x.Message))
            });
        }

        return list;
    }

    private static string? FindCanonicalByCandidates(ImportedTable table, IEnumerable<string> candidates)
    {
        var metas = table.ColumnMetas;
        if (metas.Count == 0) return null;

        foreach (var cand in candidates)
        {
            var c = cand?.Trim();
            if (string.IsNullOrWhiteSpace(c)) continue;

            // 1) CanonicalName direkt eşleşme
            for (int i = 0; i < metas.Count; i++)
            {
                if (string.Equals(metas[i].CanonicalName, c, StringComparison.OrdinalIgnoreCase))
                    return metas[i].CanonicalName;
            }

            // 2) OriginalHeader içinde geçme
            for (int i = 0; i < metas.Count; i++)
            {
                var h = metas[i].OriginalHeader ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(h) &&
                    h.Contains(c, StringComparison.OrdinalIgnoreCase))
                    return metas[i].CanonicalName;
            }
        }

        return null;
    }

    private static string? FormatShortDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
            return dt.ToString("dd.MM.yyyy");

        return raw;
    }

    private static string? NormalizeMoney(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        var s = raw.Trim();

        // 1.234,56 → 1234,56
        if (s.Contains('.') && s.Contains(','))
            s = s.Replace(".", string.Empty);

        if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
            return d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        return raw;
    }

    private List<string> ListUploadedFiles()
    {
        var sub = _cfg.GetValue<string>("Upload:SubFolder") ?? "Data\\Raporlar";
        var folder = GetUploadFolderAbsolute(sub);

        return Directory
            .GetFiles(folder, "*.xls*")
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderByDescending(name => name)
            .ToList();
    }

    private string GetUploadFolderAbsolute(string subFolder)
    {
        var webRoot = _env.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
            webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");

        // Safety: prevent path traversal (keep uploads under wwwroot)
        subFolder ??= string.Empty;
        subFolder = subFolder
            // Normalize separators
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            // Prevent absolute paths like "\Data\Raporlar" or "/Data/Raporlar"
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var folder = Path.Combine(webRoot, subFolder);

        // Ensure folder exists (prevents DirectoryNotFoundException on fresh machines)
        Directory.CreateDirectory(folder);

        return folder;
    }

    /// &lt;summary&gt;
    /// Yüklenen Excel dosyalarından rapor tarihini hafif yöntemle tespit eder.
    /// Full orchestrator import YAPMAZ — sadece ExcelDataReader ile ilk birkaç satırı tarar.
    /// Bulamazsa null döner; ArchiveService DateTime.Now fallback'i uygular.
    /// &lt;/summary&gt;
    private DateOnly? DetectReportDateFromFiles(string uploadFolder)
    {
        var candidates = new[]
        {
            "BankaHarc.xlsx",
            "BankaTahsilat.xlsx",
            "onlineHarc.xlsx",
            "onlineMasraf.xlsx"
        };

        foreach (var fileName in candidates)
        {
            var path = Path.Combine(uploadFolder, fileName);
            if (!System.IO.File.Exists(path)) continue;

            try
            {
                var date = LightweightDateScan(path);
                if (date.HasValue)
                {
                    _log.LogInformation("Arşiv rapor tarihi tespit edildi: {Date} (kaynak: {File})", date.Value, fileName);
                    return date;
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Rapor tarihi tespiti başarısız: {File}", fileName);
            }
        }

        _log.LogWarning("Rapor tarihi Excel dosyalarından tespit edilemedi, DateTime.Now kullanılacak.");
        return null;
    }

    /// &lt;summary&gt;
    /// ExcelDataReader ile dosyayı açıp sadece ilk 10 veri satırından tarih arar.
    /// Full import pipeline'a göre 100x+ daha hızlıdır.
    /// &lt;/summary&gt;
    private static DateOnly? LightweightDateScan(string filePath)
    {
        using var stream = System.IO.File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream);

        // İlk sheet yeterli
        if (!reader.Read()) return null; // header satırı

        // Header'da tarih sütununu bul
        int dateColIndex = -1;
        var dateKeywords = new[] { "tarih", "işlem tarihi", "islem_tarihi" };
        for (int col = 0; col < reader.FieldCount; col++)
        {
            var header = reader.GetValue(col)?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(header)) continue;

            foreach (var kw in dateKeywords)
            {
                if (header.Contains(kw, StringComparison.OrdinalIgnoreCase))
                {
                    dateColIndex = col;
                    break;
                }
            }
            if (dateColIndex >= 0) break;
        }

        if (dateColIndex < 0) return null;

        // İlk 10 veri satırından tarihleri topla
        var counts = new Dictionary<DateOnly, int>();
        int rowsRead = 0;
        const int maxRows = 10;

        while (reader.Read() && rowsRead < maxRows)
        {
            rowsRead++;
            var cellValue = reader.GetValue(dateColIndex);
            if (cellValue == null) continue;

            DateOnly date;
            // ExcelDataReader DateTime olarak döner (OADate otomatik çevrilir)
            if (cellValue is DateTime dt)
            {
                date = DateOnly.FromDateTime(dt);
            }
            else if (cellValue is double dbl)
            {
                try { date = DateOnly.FromDateTime(DateTime.FromOADate(dbl)); }
                catch { continue; }
            }
            else if (TryParseDateOnlyForArchive(cellValue.ToString(), out date))
            {
                // string parse fallback
            }
            else continue;

            counts.TryGetValue(date, out var c);
            counts[date] = c + 1;
        }

        if (counts.Count == 0) return null;

        // En sık geçen tarih (eşitlikte en yeni)
        return counts.OrderByDescending(kv => kv.Value).ThenByDescending(kv => kv.Key).First().Key;
    }

    private static bool TryParseDateOnlyForArchive(string? raw, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = raw.Trim();

        // Excel OADate
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var dbl))
        {
            try
            {
                var dt = DateTime.FromOADate(dbl);
                date = DateOnly.FromDateTime(dt);
                return true;
            }
            catch (ArgumentException) { }
        }

        // Standard formats
        var formats = new[] { "dd.MM.yyyy", "d.MM.yyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" };
        foreach (var f in formats)
        {
            if (DateOnly.TryParseExact(s, f, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out date))
                return true;
            if (DateOnly.TryParseExact(s, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return true;
        }

        // DateTime parse (handles "2026-03-04 00:18:22" format)
        if (DateTime.TryParse(s, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out var dt2))
        {
            date = DateOnly.FromDateTime(dt2);
            return true;
        }
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt3))
        {
            date = DateOnly.FromDateTime(dt3);
            return true;
        }

        return false;
    }

    // ─── KasaÜstRapor Panel Hydration (Import Sayfası) ───

    private async Task<KasaUstRaporPanelViewModel?> HydrateUstRaporPanelAsync(List<string> files, CancellationToken ct)
    {
        try
        {
            var kasaUst = PickKasaUstRaporFile(files);
            ImportedTable? table = null;

            var sub = _cfg.GetValue<string>("Upload:SubFolder") ?? "Data\\Raporlar";
            var folder = GetUploadFolderAbsolute(sub);

            if (!string.IsNullOrWhiteSpace(kasaUst))
            {
                var fullPath = Path.Combine(folder, kasaUst);
                if (System.IO.File.Exists(fullPath))
                {
                    var imported = _orchestrator.Import(fullPath, ImportFileKind.KasaUstRapor);
                    if (imported.Ok)
                        table = imported.Value;
                }
            }

            var eval = await _dateRules.EvaluateAsync(folder, ct);
            var proposed = eval.ProposedDate;
            var (veznedarCol, bakiyeCol) = GuessColumns(table);

            // Varsayılan VergiKasa veznedarları
            var defaults = await _globalDefaults.GetAsync(ct);
            var defaultVergiList = new List<string>();
            try
            {
                defaultVergiList = JsonSerializer.Deserialize<List<string>>(defaults.SelectedVeznedarlarJson ?? "[]") ?? new();
            }
            catch (JsonException) { defaultVergiList = new(); }

            return new KasaUstRaporPanelViewModel
            {
                Table = table,
                KasaUstRaporFileName = kasaUst,
                DateEval = eval,
                ProposedDate = proposed,
                FinalDate = proposed,
                VeznedarColumn = veznedarCol,
                BakiyeColumn = bakiyeCol,
                DefaultVergiKasaVeznedarlar = defaultVergiList,
                // P4.3: Snapshot references removed
                StartOpen = true,
                ShowSaveButton = true,
                Context = "import"
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "KasaÜstRapor panel hydration failed");
            return null;
        }
    }

    private static string? PickKasaUstRaporFile(List<string> files)
    {
        return files.FirstOrDefault(f =>
            f.Contains("kasa", StringComparison.OrdinalIgnoreCase) &&
            f.Contains("ust", StringComparison.OrdinalIgnoreCase));
    }

    private static (string? Veznedar, string? Bakiye) GuessColumns(ImportedTable? table)
    {
        if (table == null) return (null, null);
        var metas = table.ColumnMetas;
        if (metas == null || metas.Count == 0) return (null, null);

        string? veznedar = null;
        string? bakiye = null;

        var vCandidates = new[] { "veznedar", "vezne", "kasiyer", "personel", "ad" };
        foreach (var c in vCandidates)
        {
            var hit = metas.FirstOrDefault(m => string.Equals(m.CanonicalName, c, StringComparison.OrdinalIgnoreCase));
            if (hit != null) { veznedar = hit.CanonicalName; break; }
        }

        var bCandidates = new[] { "bakiye", "kasa", "kasada_kalan", "toplam", "tutar", "nakit" };
        foreach (var c in bCandidates)
        {
            var hit = metas.FirstOrDefault(m => string.Equals(m.CanonicalName, c, StringComparison.OrdinalIgnoreCase));
            if (hit != null) { bakiye = hit.CanonicalName; break; }
        }

        veznedar ??= metas.FirstOrDefault()?.CanonicalName;
        bakiye ??= metas.LastOrDefault()?.CanonicalName;

        return (veznedar, bakiye);
    }
}
