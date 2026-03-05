#nullable enable
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Constants;
using KasaManager.Domain.Calculation;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Export;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;

namespace KasaManager.Web.Controllers;

/// <summary>
/// FAZ-2 / Adım-1 (KİLİTLİ):
/// - Controller içinde Excel okuma / toplam / devreden / mutabakat vb. hesap YOK.
/// - Tek boru hattı: UI -> IKasaDraftService -> UnifiedPool(+Overrides) -> IFormulaEngineService.Run -> ViewModel
/// </summary>
[Authorize(Roles = "Admin")]
public sealed class GenelKasaRaporController : Controller
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _cfg;
    private readonly IGenelKasaRaporService _raporService;
    private readonly IKasaGlobalDefaultsService _globalDefaults;
    private readonly IReportDataBuilder _reportBuilder;
    private readonly IExportService _exportService;
    private readonly ICalculatedKasaSnapshotService _calcSnapshots;
    private readonly ILogger<GenelKasaRaporController> _log;

    public GenelKasaRaporController(
        IWebHostEnvironment env,
        IConfiguration cfg,
        IGenelKasaRaporService raporService,
        IKasaGlobalDefaultsService globalDefaults,
        IReportDataBuilder reportBuilder,
        IExportService exportService,
        ICalculatedKasaSnapshotService calcSnapshots,
        ILogger<GenelKasaRaporController> log)
    {
        _env = env;
        _cfg = cfg;
        _raporService = raporService;
        _globalDefaults = globalDefaults;
        _reportBuilder = reportBuilder;
        _exportService = exportService;
        _calcSnapshots = calcSnapshots;
        _log = log;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var folder = ResolveUploadFolder();
        var data = await _raporService.BuildReportDataAsync(selectedEndDate: null, gelmeyenD: null, folder, ct);
        var model = MapToViewModel(data);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Hazirla(
        [FromForm] DateOnly? selectedBitisTarihi,
        [FromForm] string? gelmeyenD,
        CancellationToken ct)
    {
        decimal? parsedGelmeyen = null;
        if (!string.IsNullOrWhiteSpace(gelmeyenD) && decimal.TryParse(gelmeyenD, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
            parsedGelmeyen = v;

        var folder = ResolveUploadFolder();
        var data = await _raporService.BuildReportDataAsync(selectedEndDate: selectedBitisTarihi, gelmeyenD: parsedGelmeyen, folder, ct);
        var model = MapToViewModel(data);
        model.SelectedBitisTarihi = selectedBitisTarihi;
        return View("Index", model);
    }

    // ═══════════════════════════════════════════════════════
    // EXPORT ENDPOINT — Genel Kasa Çıktı Modülü
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Genel Kasa verilerini seçilen formatta dosya olarak indirir.
    /// Doğrudan GenelKasaRaporData → PDF/Excel üretir (Sabah/Akşam pipeline'ından bağımsız).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Export(
        [FromForm] string exportType,
        [FromForm] DateOnly? selectedBitisTarihi,
        [FromForm] string? gelmeyenD,
        CancellationToken ct)
    {
        // 1) CalculationRun üret
        decimal? parsedGelmeyen = null;
        if (!string.IsNullOrWhiteSpace(gelmeyenD) && decimal.TryParse(gelmeyenD, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var gv))
            parsedGelmeyen = gv;

        var folder = ResolveUploadFolder();
        var (run, error) = await _raporService.BuildCalculationRunAsync(selectedBitisTarihi, parsedGelmeyen, folder, ct);
        if (run is null)
        {
            TempData["ExportError"] = error ?? "Hesaplama başarısız.";
            return RedirectToAction("Index");
        }

        // 2) CalculationRun → GenelKasaRaporData
        var data = await _raporService.BuildReportDataAsync(selectedEndDate: selectedBitisTarihi, gelmeyenD: parsedGelmeyen, folder, ct);

        // 3) Format'a göre çıktı üret
        byte[] fileBytes;
        string contentType;
        string fileName;
        var ds = data.BaslangicTarihi.ToString("yyyy-MM-dd");
        var de = data.BitisTarihi.ToString("yyyy-MM-dd");

        switch (exportType?.ToLowerInvariant())
        {
            case "pdf_a5":
                var ozetDoc = new Infrastructure.Pdf.GenelKasaOzetPdf(data);
                fileBytes = ozetDoc.GeneratePdf();
                contentType = "application/pdf";
                fileName = $"genel_kasa_ozet_{ds}_{de}.pdf";
                break;

            case "excel":
                var excelResult = Infrastructure.Export.GenelKasaExcelExporter.Export(data);
                fileBytes = excelResult.FileBytes;
                contentType = excelResult.ContentType;
                fileName = excelResult.FileName;
                break;

            case "pdf_a4":
            default:
                var genelDoc = new Infrastructure.Pdf.GenelKasaDokumaniPdf(data);
                fileBytes = genelDoc.GeneratePdf();
                contentType = "application/pdf";
                fileName = $"genel_kasa_rapor_{ds}_{de}.pdf";
                break;
        }

        return File(fileBytes, contentType, fileName);
    }

    // ═══════════════════════════════════════════════════════
    // VERGİDE BİRİKEN SEED TRANSFER
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// AJAX POST: Genel Kasa değerini Vergide Biriken seed olarak kaydeder.
    /// Sabah/Akşam kasalarda başlangıç değeri olarak kullanılır.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TransferVergideBiriken(
        [FromForm] decimal genelKasaValue,
        CancellationToken ct)
    {
        try
        {
            await _globalDefaults.SaveVergideBirikenSeedAsync(genelKasaValue, "GenelKasaRapor", ct);

            _log.LogInformation("Vergide Biriken seed aktarıldı: {Value}", genelKasaValue);

            return Json(new
            {
                ok = true,
                message = $"✅ Vergide Biriken seed olarak {genelKasaValue:N2} ₺ aktarıldı. Sabah/Akşam kasalarda otomatik yüklenecek."
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Vergide Biriken seed transfer hatası");
            return Json(new { ok = false, message = $"❌ Aktarma hatası: {ex.Message}" });
        }
    }

    // ═══════════════════════════════════════════════════════
    // CRUD: Kaydet / Yükle / Güncelle / Sil / Ara
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Genel Kasa raporunu veritabanına kaydeder.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveReport(
        [FromForm] DateOnly? selectedBitisTarihi,
        [FromForm] string? gelmeyenD,
        [FromForm] string? SaveRaporAdi,
        [FromForm] string? SaveVeznedar,
        CancellationToken ct)
    {
        try
        {
            decimal? parsedGelmeyen = null;
            if (!string.IsNullOrWhiteSpace(gelmeyenD) && decimal.TryParse(gelmeyenD, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var gv))
                parsedGelmeyen = gv;

            var folder = ResolveUploadFolder();
            var (run, error) = await _raporService.BuildCalculationRunAsync(selectedBitisTarihi, parsedGelmeyen, folder, ct);
            if (run is null)
            {
                TempData["ErrorMessage"] = error ?? "Hesaplama başarısız.";
                return RedirectToAction("Index");
            }

            var tarih = run.ReportDate;
            var raporAdi = SaveRaporAdi?.Trim();
            if (string.IsNullOrWhiteSpace(raporAdi))
                raporAdi = $"Genel Kasa — {tarih:dd.MM.yyyy}";

            // Aynı tarih için aktif kayıt var mı?
            var existing = await _calcSnapshots.GetActiveAsync(tarih, KasaRaporTuru.Genel, ct);
            if (existing != null)
            {
                TempData["ErrorMessage"] = $"⚠️ Bu tarihli Genel Kasa raporu zaten kayıtlı (v{existing.Version}). Lütfen mevcut raporu «Güncelle» ile güncelleyin.";
                return RedirectToAction("LoadSnapshot", new { id = existing.Id });
            }

            // KasaRaporData oluştur
            var raporData = await _reportBuilder.BuildAsync(run, "Genel", ustRaporTable: null, ct);
            var raporDataJson = JsonSerializer.Serialize(raporData, new JsonSerializerOptions { WriteIndented = false });

            var inputsJson = JsonSerializer.Serialize(run.Inputs);
            var outputsJson = JsonSerializer.Serialize(run.Outputs);

            var snapshot = new CalculatedKasaSnapshot
            {
                RaporTarihi = tarih,
                KasaTuru = KasaRaporTuru.Genel,
                Name = raporAdi,
                CalculatedBy = SaveVeznedar?.Trim() ?? "Sistem",
                InputsJson = inputsJson,
                OutputsJson = outputsJson,
                KasaRaporDataJson = raporDataJson,
                FormulaSetName = run.FormulaSetId
            };

            await _calcSnapshots.SaveAsync(snapshot, ct);

            _log.LogInformation("GenelKasa rapor kaydedildi: {Name}, Tarih={Tarih}, Id={Id}",
                snapshot.Name, snapshot.RaporTarihi, snapshot.Id);

            TempData["SuccessMessage"] = $"✅ Rapor başarıyla kaydedildi: {snapshot.Name} (v{snapshot.Version})";
            return RedirectToAction("LoadSnapshot", new { id = snapshot.Id });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GenelKasa rapor kaydetme hatası");
            TempData["ErrorMessage"] = $"❌ Rapor kaydedilemedi: {ex.Message}";
            return RedirectToAction("Index");
        }
    }

    /// <summary>
    /// Kaydedilmiş Genel Kasa snapshot'ını yükler.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> LoadSnapshot(Guid id, CancellationToken ct)
    {
        var snapshot = await _calcSnapshots.GetByIdAsync(id, ct);
        if (snapshot is null)
        {
            TempData["ErrorMessage"] = "❌ Rapor bulunamadı.";
            return RedirectToAction("Index");
        }

        // Hesaplama verileriyle yeni model oluştur
        var folder = ResolveUploadFolder();
        var data = await _raporService.BuildReportDataAsync(selectedEndDate: snapshot.RaporTarihi, gelmeyenD: null, folder, ct);
        var model = MapToViewModel(data);

        // Snapshot tracking
        model.LoadedSnapshotId = snapshot.Id;
        model.LoadedSnapshotName = snapshot.Name;
        model.LoadedSnapshotVersion = snapshot.Version;

        TempData["SuccessMessage"] = $"✅ Rapor yüklendi: {snapshot.Name} (v{snapshot.Version})";
        return View("Index", model);
    }

    /// <summary>
    /// AJAX POST: Yüklü snapshot'ı günceller.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSnapshot(
        [FromForm] Guid snapshotId,
        [FromForm] string? name,
        [FromForm] string? notes,
        CancellationToken ct)
    {
        try
        {
            var snapshot = await _calcSnapshots.GetByIdAsync(snapshotId, ct);
            if (snapshot is null)
                return Json(new { ok = false, message = "Rapor bulunamadı." });

            await _calcSnapshots.UpdateAsync(snapshotId, name?.Trim(), null, notes?.Trim(), ct);

            return Json(new { ok = true, message = $"✅ Rapor güncellendi: {name?.Trim() ?? snapshot.Name}" });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GenelKasa snapshot güncelleme hatası - Id={Id}", snapshotId);
            return Json(new { ok = false, message = $"❌ Hata: {ex.Message}" });
        }
    }

    /// <summary>
    /// AJAX POST: Snapshot'ı soft-delete eder.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSnapshot(
        [FromForm] Guid snapshotId,
        CancellationToken ct)
    {
        try
        {
            var snapshot = await _calcSnapshots.GetByIdAsync(snapshotId, ct);
            if (snapshot is null)
                return Json(new { ok = false, message = "Rapor bulunamadı." });

            await _calcSnapshots.DeleteAsync(snapshotId, null, ct);

            return Json(new { ok = true, message = $"🗑️ Rapor silindi: {snapshot.Name}" });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GenelKasa snapshot silme hatası - Id={Id}", snapshotId);
            return Json(new { ok = false, message = $"❌ Hata: {ex.Message}" });
        }
    }

    /// <summary>
    /// AJAX: Genel Kasa raporlarını JSON olarak döner.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> SearchReports(string? searchDate, string? search, CancellationToken ct)
    {
        DateOnly? filterDate = null;
        if (!string.IsNullOrWhiteSpace(searchDate))
        {
            if (DateOnly.TryParseExact(searchDate, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d1))
                filterDate = d1;
            else if (DateOnly.TryParseExact(searchDate, "dd.MM.yyyy", null, System.Globalization.DateTimeStyles.None, out var d2))
                filterDate = d2;
        }

        var query = new KasaReportSearchQuery
        {
            KasaTuru = KasaRaporTuru.Genel,
            SearchText = search,
            StartDate = filterDate,
            EndDate = filterDate,
            IncludeDeleted = false,
            SortBy = "RaporTarihi",
            SortDescending = true,
            Page = 1,
            PageSize = 50
        };

        var results = await _calcSnapshots.SearchAsync(query, ct);

        var items = results.Items.Select(s => new
        {
            s.Id,
            s.Name,
            s.Notes,
            RaporTarihi = s.RaporTarihi.ToString("dd.MM.yyyy"),
            KasaTuru = s.KasaTuru.ToString(),
            s.CalculatedBy,
            CalculatedAt = s.CalculatedAtUtc.ToLocalTime().ToString("dd.MM HH:mm"),
            s.Version,
            s.IsActive
        });

        return Json(new { items, results.TotalCount });
    }

    // ═══════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════

    private string ResolveUploadFolder()
    {
        var sub = _cfg.GetValue<string>("Upload:SubFolder") ?? "Data\\Raporlar";
        sub = sub.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_env.WebRootPath, sub);
    }

    private GenelKasaRaporViewModel MapToViewModel(GenelKasaRaporData data)
    {
        return new GenelKasaRaporViewModel
        {
            BaslangicTarihi = data.BaslangicTarihi,
            BitisTarihi = data.BitisTarihi,
            DevredenSonTarihi = data.DevredenSonTarihi,
            RaporSonTarihi = data.BitisTarihi,
            TahsilatTarihi = data.BitisTarihi,

            ToplamTahsilat = data.ToplamTahsilat,
            ToplamReddiyat = data.ToplamReddiyat,
            KaydenTahsilat = data.KaydenTahsilat,
            BankaBakiye = data.BankaBakiye,
            Devreden = data.Devreden,
            Gelmeyen = data.Gelmeyen,
            EksikYadaFazla = data.EksikYadaFazla,
            KasaNakit = data.KasaNakit,
            TahsilatReddiyatFark = data.TahsilatReddiyatFark,
            SonrayaDevredecek = data.SonrayaDevredecek,
            MutabakatFarki = data.MutabakatFarki,
            GenelKasa = data.GenelKasa,

            Issues = data.Issues,
        };
    }
}
