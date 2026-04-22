using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Export;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace KasaManager.Web.Controllers;

/// <summary>
/// R7: KasaÜstRapor hesap ekranı + Tarih Kural Motoru entegrasyonu.
/// - Excel'e kolon ekleme/doldurma yok.
/// - Tarih, diğer raporların mevcut tarih sütunlarından tespit edilir.
/// - Snapshot, yalnız kullanıcı onayıyla kaydedilir.
/// </summary>
[Authorize]
public sealed class KasaUstRaporController : Controller
{
    private readonly IFileStorage _storage;
    private readonly IImportOrchestrator _orchestrator;
    private readonly IKasaReportDateRulesService _dateRules;
    private readonly IKasaRaporSnapshotService _snapshots;

    private readonly IKasaGlobalDefaultsService _globalDefaults;
    private readonly IExportService _exportService;
    private readonly IConfiguration _cfg;

    public KasaUstRaporController(
        IFileStorage storage,
        IImportOrchestrator orchestrator,
        IKasaReportDateRulesService dateRules,
        IKasaRaporSnapshotService snapshots,
        IKasaGlobalDefaultsService globalDefaults,
        IExportService exportService,
        IConfiguration cfg)
    {
        _storage = storage;
        _orchestrator = orchestrator;
        _dateRules = dateRules;
        _snapshots = snapshots;
        _globalDefaults = globalDefaults;
        _exportService = exportService;
        _cfg = cfg;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var files = ListUploadedFiles();
        var sub = _cfg.GetValue<string>("Upload:SubFolder") ?? "Data\\Raporlar";
        var folder = GetUploadFolderAbsolute(sub);

        var kasaUst = PickKasaUstRaporFile(files);
        ImportedTable? table = null;

        if (!string.IsNullOrWhiteSpace(kasaUst))
        {
            var fullPath = Path.Combine(folder, kasaUst);
            var imported = _orchestrator.Import(fullPath, ImportFileKind.KasaUstRapor);
            if (imported.Ok)
                table = imported.Value;
        }

        var eval = await _dateRules.EvaluateAsync(folder, ct);
        var proposed = eval.ProposedDate;

        // KasaÜstRapor için kolon tahmini
        var (veznedarCol, bakiyeCol) = GuessColumns(table);

        // Ayarlardan varsayılan VergiKasa veznedarları (grid checkbox'ları için)
        var defaults = await _globalDefaults.GetAsync(ct);
        var defaultVergiList = new List<string>();
        try
        {
            defaultVergiList = JsonSerializer.Deserialize<List<string>>(defaults.SelectedVeznedarlarJson ?? "[]") ?? new List<string>();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[KasaUstRaporController] VergiKasa veznedar JSON parse hatası: {ex.Message}"); defaultVergiList = new List<string>(); }

        var vm = new KasaUstRaporIndexViewModel
        {
            UploadedFiles = files,
            KasaUstRaporFileName = kasaUst,
            Table = table,
            DateEval = eval,
            ProposedDate = proposed,
            FinalDate = proposed,
            VeznedarColumn = veznedarCol,
            BakiyeColumn = bakiyeCol,
            RequireExplicitApprove = eval.RequiresUserDecision,
            DefaultVergiKasaVeznedarlar = defaultVergiList
        };

        return View(vm);
    }

    /// <summary>
    /// R7: KasaÜstRapor snapshot kaydı (Genel).
    /// UI'den gelen seçimlerle KasaÜstRapor satırları DB'ye snapshot olarak yazılır.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        [FromForm] string kasaUstRaporFileName,
        [FromForm] DateOnly? finalDate,
        [FromForm] string? veznedarColumn,
        [FromForm] string? bakiyeColumn,
        [FromForm] int[] selectedRows,
        [FromForm] bool approve,
        CancellationToken ct)
    {
        if (!approve)
        {
            TempData["Error"] = "Snapshot kaydı için tarih/onay gereklidir.";
            return RedirectToAction(nameof(Index));
        }

        // finalDate null/boş geldiyse: tarih kural motorundan veya bugünden al
        if (!finalDate.HasValue)
        {
            var sub0 = _cfg.GetValue<string>("Upload:SubFolder") ?? "Data\\Raporlar";
            var folder0 = GetUploadFolderAbsolute(sub0);
            var eval0 = await _dateRules.EvaluateAsync(folder0, ct);
            finalDate = eval0.ProposedDate
                     ?? DateOnly.FromDateTime(DateTime.Today);
        }

        if (string.IsNullOrWhiteSpace(kasaUstRaporFileName))
        {
            TempData["Error"] = "KasaÜstRapor dosyası seçilemedi.";
            return RedirectToAction(nameof(Index));
        }

        var sub = _cfg.GetValue<string>("Upload:SubFolder") ?? "Data\\Raporlar";
        var folder = GetUploadFolderAbsolute(sub);
        var fullPath = Path.Combine(folder, kasaUstRaporFileName);

        if (!System.IO.File.Exists(fullPath))
        {
            TempData["Error"] = "Dosya bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        var import = _orchestrator.Import(fullPath, ImportFileKind.KasaUstRapor);
        if (!import.Ok || import.Value is null)
        {
            TempData["Error"] = import.Error ?? "Import başarısız.";
            return RedirectToAction(nameof(Index));
        }

        var table = import.Value;
        var headersJson = JsonSerializer.Serialize(table.ColumnMetas);

        // Varsayılan kolonlar (UI boş geldiyse)
        var (guessV, guessB) = GuessColumns(table);
        veznedarColumn = string.IsNullOrWhiteSpace(veznedarColumn) ? guessV : veznedarColumn;
        bakiyeColumn = string.IsNullOrWhiteSpace(bakiyeColumn) ? guessB : bakiyeColumn;

        var sel = new HashSet<int>(selectedRows ?? Array.Empty<int>());
        var rows = new List<KasaRaporSnapshotRow>();

        decimal selectionTotal = 0m;

        for (int i = 0; i < table.Rows.Count; i++)
        {
            var r = table.Rows[i];
            var isSelected = sel.Contains(i);

            var veznedar = TryGet(r, veznedarColumn) ?? string.Empty;
            var bakiye = Application.Services.Draft.Helpers.DecimalParsingHelper.TryParseFromTurkish(TryGet(r, bakiyeColumn), out var b) ? b : 0m;

            if (isSelected)
                selectionTotal += bakiye;

            var columnsJson = JsonSerializer.Serialize(r);

            rows.Add(new KasaRaporSnapshotRow
            {
                Veznedar = veznedar,
                IsSelected = isSelected,
                Bakiye = bakiye,
                IsSummaryRow = IsSummaryRow(r),
                ColumnsJson = columnsJson,
                HeadersJson = i == 0 ? headersJson : null
            });
        }

        // Tarih kural motoru: bu ekranda da güncel uyarıları saklayalım
        var eval = await _dateRules.EvaluateAsync(folder, ct);
        var warningsJson = eval.Warnings.Count > 0 ? JsonSerializer.Serialize(eval.Warnings) : null;

        var snapshot = new KasaRaporSnapshot
        {
            RaporTarihi = finalDate.Value,
            RaporTuru = KasaRaporTuru.Genel,
            Version = 1,
            SelectionTotal = selectionTotal,
            CreatedBy = User?.Identity?.Name,
            WarningsJson = warningsJson,
            Rows = rows
        };

        // P4.1 iptalinden sonra yeniden etkinleştirildi:
        // Sabah/Aksam kasa pipeline'ı o güne ait Genel snapshot'a bağımlıdır.
        // Snapshot kaydedilmezse HydrateFromSnapshotAndDefaultsInternalAsync hata verir.
        var saved = await _snapshots.SaveAsync(snapshot, ct);

        TempData["Info"] = $"✅ KasaÜstRapor doğrulandı — {finalDate.Value:dd.MM.yyyy} ({saved.Rows?.Count ?? 0} satır, v{saved.Version})";
        return RedirectToAction("Index", "Import");
    }

    // ═══════════════════════════════════════════════════════
    // EXPORT ENDPOINT — Çıktı Modülü (Faz C)
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// KasaÜstRapor verilerini seçilen formatta indirir (Landscape PDF / Excel / CSV).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Export(
        [FromForm] int format,
        [FromForm] int content,
        CancellationToken ct)
    {
        // 1) KasaÜstRapor tablosunu import et
        var files = ListUploadedFiles();
        var kasaUstFile = PickKasaUstRaporFile(files);
        if (string.IsNullOrWhiteSpace(kasaUstFile))
        {
            TempData["Error"] = "KasaÜstRapor dosyası bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        var sub = _cfg.GetValue<string>("Upload:SubFolder") ?? "Data\\Raporlar";
        var folder = GetUploadFolderAbsolute(sub);
        var fullPath = Path.Combine(folder, kasaUstFile);

        var import = _orchestrator.Import(fullPath, ImportFileKind.KasaUstRapor);
        if (!import.Ok || import.Value is null)
        {
            TempData["Error"] = import.Error ?? "Import başarısız.";
            return RedirectToAction(nameof(Index));
        }

        var table = import.Value;

        // 2) KasaRaporData oluştur (UstRapor verileriyle)
        var eval = await _dateRules.EvaluateAsync(folder, ct);
        var (vezCol, _) = GuessColumns(table);

        var raporData = new KasaRaporData
        {
            Tarih = eval.ProposedDate ?? DateOnly.FromDateTime(DateTime.Today),
            KasaTuru = "Genel",
            UstRaporKolonlar = table.ColumnMetas.Select(m => m.CanonicalName ?? m.OriginalHeader).ToList(),
        };

        // Satır verilerini doldur
        foreach (var row in table.Rows)
        {
            var veznedar = TryGet(row, vezCol) ?? "";
            var degerler = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in raporData.UstRaporKolonlar)
            {
                degerler[col] = TryGet(row, col);
            }
            raporData.UstRaporSatirlar.Add(new UstRaporSatir
            {
                VeznedarAdi = veznedar,
                Degerler = degerler
            });
        }

        // 3) Export
        var request = new ExportRequest
        {
            Data = raporData,
            Format = (ExportFormat)format,
            Content = (ExportContent)content
        };

        var result = await _exportService.ExportAsync(request, ct);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }

    private static bool IsSummaryRow(Dictionary<string, string?> row)
    {
        // Bazı raporlarda TOPLAM / GENEL TOPLAM gibi satırlar olur.
        foreach (var kv in row)
        {
            var v = kv.Value;
            if (string.IsNullOrWhiteSpace(v)) continue;
            var t = v.Trim().ToLowerInvariant();
            if (t == "toplam" || t.Contains("genel toplam") || t.Contains("toplamlar"))
                return true;
        }
        return false;
    }

    private static (string? Veznedar, string? Bakiye) GuessColumns(ImportedTable? table)
    {
        if (table == null) return (null, null);

        string? veznedar = null;
        string? bakiye = null;

        var metas = table.ColumnMetas;
        if (metas == null || metas.Count == 0) return (null, null);

        // Veznedar adayları
        var vCandidates = new[] { "veznedar", "vezne", "kasiyer", "personel", "ad" };
        foreach (var c in vCandidates)
        {
            var hit = metas.FirstOrDefault(m => string.Equals(m.CanonicalName, c, StringComparison.OrdinalIgnoreCase));
            if (hit != null) { veznedar = hit.CanonicalName; break; }
        }
        // Bakiye adayları
        var bCandidates = new[] { "bakiye", "kasa", "kasada_kalan", "toplam", "tutar", "nakit" };
        foreach (var c in bCandidates)
        {
            var hit = metas.FirstOrDefault(m => string.Equals(m.CanonicalName, c, StringComparison.OrdinalIgnoreCase));
            if (hit != null) { bakiye = hit.CanonicalName; break; }
        }

        // Son çare
        veznedar ??= metas.FirstOrDefault()?.CanonicalName;
        bakiye ??= metas.LastOrDefault()?.CanonicalName;

        return (veznedar, bakiye);
    }

    private static string? TryGet(Dictionary<string, string?> row, string? canonical)
    {
        if (row == null || string.IsNullOrWhiteSpace(canonical)) return null;
        return row.TryGetValue(canonical, out var v) ? v : null;
    }



    private List<string> ListUploadedFiles()
    {
        var sub = _cfg.GetValue<string>("Upload:SubFolder") ?? "Data\\Raporlar";
        return _storage.ListFiles(sub)
            .OrderByDescending(x => x)
            .ToList();
    }

    private string GetUploadFolderAbsolute(string subFolder)
    {
        // FileStorage webroot bazlı
        var envPath = (HttpContext.RequestServices.GetService(typeof(Microsoft.AspNetCore.Hosting.IWebHostEnvironment)) as Microsoft.AspNetCore.Hosting.IWebHostEnvironment)?.WebRootPath;
        envPath ??= Directory.GetCurrentDirectory();
        return Path.Combine(envPath, subFolder);
    }

    private static string? PickKasaUstRaporFile(List<string> files)
    {
        // En basit: fileName içinde kasaUst geçen ilk (list zaten desc)
        return files.FirstOrDefault(f => f.Contains("kasa", StringComparison.OrdinalIgnoreCase) && f.Contains("ust", StringComparison.OrdinalIgnoreCase));
    }
}
