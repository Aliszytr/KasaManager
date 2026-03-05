#nullable enable
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KasaManager.Web.Controllers;

/// <summary>
/// Banka ve Online dosyalar arasında karşılaştırma yapan controller.
/// </summary>
[Authorize]
public class ComparisonController : Controller
{
    private readonly IComparisonService _comparisonService;
    private readonly IComparisonExportService _exportService;
    private readonly IComparisonDecisionService _decisionService;
    private readonly IComparisonArchiveService _archiveService;
    private readonly IWebHostEnvironment _env;

    public ComparisonController(
        IComparisonService comparisonService,
        IComparisonExportService exportService,
        IComparisonDecisionService decisionService,
        IComparisonArchiveService archiveService,
        IWebHostEnvironment env)
    {
        _comparisonService = comparisonService;
        _exportService = exportService;
        _decisionService = decisionService;
        _archiveService = archiveService;
        _env = env;
    }

    /// <summary>
    /// Karşılaştırma ana sayfası.
    /// </summary>
    [HttpGet]
    public IActionResult Index()
    {
        var uploadFolder = GetUploadFolder();
        var dates = _archiveService.GetAvailableArchiveDates(uploadFolder);
        ViewBag.AvailableDates = dates;
        return View();
    }

    /// <summary>Arşivlenmiş karşılaştırma tarihlerini JSON döndürür (AJAX).</summary>
    [HttpGet]
    public IActionResult GetAvailableDates()
    {
        var uploadFolder = GetUploadFolder();
        var dates = _archiveService.GetAvailableArchiveDates(uploadFolder);
        return Json(dates.Select(d => new { value = d.ToString("yyyy-MM-dd"), label = d.ToString("dd.MM.yyyy") }));
    }

    /// <summary>
    /// BankaTahsilat vs onlineMasraf karşılaştırması.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompareTahsilatMasraf(string? archiveDate, CancellationToken ct)
    {
        var folder = ResolveComparisonFolder(archiveDate, out var filterDate);
        var result = await _comparisonService.CompareTahsilatMasrafAsync(folder, filterDate, ct);

        if (!result.Ok)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Index));
        }

        // Kalıcı kararları uygula
        await ApplyStoredDecisions(result.Value!, ComparisonType.TahsilatMasraf, ct);
        return View("Results", result.Value);
    }

    /// <summary>
    /// BankaHarc vs onlineHarc karşılaştırması.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompareHarcamaHarc(string? archiveDate, CancellationToken ct)
    {
        var folder = ResolveComparisonFolder(archiveDate, out var filterDate);
        var result = await _comparisonService.CompareHarcamaHarcAsync(folder, filterDate, ct);

        if (!result.Ok)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Index));
        }

        await ApplyStoredDecisions(result.Value!, ComparisonType.HarcamaHarc, ct);
        return View("Results", result.Value);
    }

    /// <summary>
    /// BankaTahsilat(-) vs onlineReddiyat karşılaştırması (Çıkan Ödemeler).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompareReddiyatCikis(string? archiveDate, CancellationToken ct)
    {
        var folder = ResolveComparisonFolder(archiveDate, out var filterDate);
        var result = await _comparisonService.CompareReddiyatCikisAsync(folder, filterDate, ct);

        if (!result.Ok)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Index));
        }

        await ApplyStoredDecisions(result.Value!, ComparisonType.ReddiyatCikis, ct);
        return View("Results", result.Value);
    }

    // ═══════════════════════════════════════════════════════════════
    // AJAX — Karar Kaydet / Sil / Listele
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Kısmi eşleşme için karar kaydet (AJAX).</summary>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SaveDecision([FromBody] SaveDecisionRequest req, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.DosyaNo) || string.IsNullOrEmpty(req.Decision))
            return BadRequest(new { error = "DosyaNo ve Decision zorunludur." });

        var decision = await _decisionService.SaveDecisionAsync(
            req.ComparisonType,
            req.DosyaNo,
            req.Miktar,
            req.BirimAdi,
            req.BankaTutar,
            req.BankaAciklama,
            req.Confidence,
            req.MatchReason,
            req.Decision,
            User.Identity?.Name,
            ct);

        return Json(new
        {
            id = decision.Id,
            decision = decision.Decision,
            decidedAt = decision.DecidedAtUtc.ToString("dd.MM.yyyy HH:mm")
        });
    }

    /// <summary>Kararı sil — kayıt tekrar kısmi'ye döner (AJAX).</summary>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> DeleteDecision([FromBody] DeleteDecisionRequest req, CancellationToken ct)
    {
        await _decisionService.DeleteDecisionAsync(req.Id, ct);
        return Json(new { success = true });
    }

    /// <summary>Tüm kullanıcı kararlarını getir (AJAX).</summary>
    [HttpGet]
    public async Task<IActionResult> UserDecisions(ComparisonType type, CancellationToken ct)
    {
        var decisions = await _decisionService.GetDecisionsAsync(type, ct);
        var result = decisions.Select(d => new
        {
            d.Id,
            d.OnlineDosyaNo,
            d.OnlineMiktar,
            d.OnlineBirimAdi,
            d.BankaTutar,
            d.Decision,
            decidedAt = d.DecidedAtUtc.ToString("dd.MM.yyyy HH:mm"),
            d.DecidedBy,
            confidence = (int)(d.OriginalConfidence * 100),
            d.OriginalMatchReason
        });
        return Json(result);
    }

    // ═══════════════════════════════════════════════════════════════
    // PDF Export
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Karşılaştırma sonuçlarını PDF olarak indirir.
    /// Kullanıcı kararlarını (onay/red) dikkate alarak PDF üretir.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportPdf(
        [FromForm] string reportJson,
        [FromForm] string? decisionsJson,
        CancellationToken ct)
    {
        ComparisonReport? report;
        try
        {
            report = JsonSerializer.Deserialize<ComparisonReport>(reportJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            TempData["Error"] = "PDF oluşturma hatası: Rapor verisi geçersiz.";
            return RedirectToAction(nameof(Index));
        }

        if (report is null)
        {
            TempData["Error"] = "PDF oluşturma hatası: Rapor verisi boş.";
            return RedirectToAction(nameof(Index));
        }

        // Kullanıcı kararlarını parse et
        Dictionary<int, string>? decisions = null;
        if (!string.IsNullOrEmpty(decisionsJson))
        {
            try
            {
                decisions = JsonSerializer.Deserialize<Dictionary<int, string>>(decisionsJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException)
            {
                // Kararlar parse edilemezse kararlar olmadan devam et
            }
        }

        var pdfBytes = await _exportService.ExportToPdfAsync(report, decisions, ct);

        var typeLabel = report.Type switch
        {
            ComparisonType.TahsilatMasraf => "tahsilat_masraf",
            ComparisonType.HarcamaHarc => "harcama_harc",
            ComparisonType.ReddiyatCikis => "reddiyat_cikis",
            _ => "karsilastirma"
        };
        var dateStr = report.ReportDate?.ToString("yyyy-MM-dd") ?? "tum";
        var fileName = $"karsilastirma_{typeLabel}_{dateStr}.pdf";

        return File(pdfBytes, "application/pdf", fileName);
    }

    // ═══════════════════════════════════════════════════════════════
    // Private Helpers
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Kalıcı DB kararlarını rapor sonuçlarına uygular ve istatistikleri günceller.</summary>
    private async Task ApplyStoredDecisions(ComparisonReport report, ComparisonType type, CancellationToken ct)
    {
        var decisions = await _decisionService.GetDecisionsAsync(type, ct);
        if (decisions.Count == 0) return;

        _decisionService.ApplyDecisions(report.Results, decisions);

        // İstatistikleri yeniden hesapla — report init-only olduğu için reflection kullan
        RecalculateStats(report);
    }

    /// <summary>Rapor istatistiklerini sonuç listesinden yeniden hesaplar.</summary>
    private static void RecalculateStats(ComparisonReport report)
    {
        var matched = report.Results.Count(r => r.Status == MatchStatus.Matched);
        var partial = report.Results.Count(r => r.Status == MatchStatus.PartialMatch);
        var notFound = report.Results.Count(r => r.Status == MatchStatus.NotFound);
        var matchedAmount = report.Results
            .Where(r => r.Status == MatchStatus.Matched ||
                        r.Status == MatchStatus.PartialMatch ||
                        r.Status == MatchStatus.MultipleMatches)
            .Sum(r => r.OnlineMiktar);
        var unmatchedAmount = report.Results
            .Where(r => r.Status == MatchStatus.NotFound)
            .Sum(r => r.OnlineMiktar);

        // Init-only properties'i güncellemek için unsafe set
        var type = typeof(ComparisonReport);
        type.GetProperty(nameof(ComparisonReport.MatchedCount))!.SetValue(report, matched);
        type.GetProperty(nameof(ComparisonReport.PartialMatchCount))!.SetValue(report, partial);
        type.GetProperty(nameof(ComparisonReport.NotFoundCount))!.SetValue(report, notFound);
        type.GetProperty(nameof(ComparisonReport.TotalMatchedAmount))!.SetValue(report, matchedAmount);
        type.GetProperty(nameof(ComparisonReport.UnmatchedAmount))!.SetValue(report, unmatchedAmount);
    }

    /// <summary>Excel dosyalarının bulunduğu klasörü döndürür.</summary>
    private string GetUploadFolder()
    {
        return Path.Combine(_env.WebRootPath, "Data", "Raporlar");
    }

    /// <summary>
    /// Arşiv tarihine göre doğru klasörü çözümler.
    /// archiveDate boş ise mevcut dosyalar kullanılır.
    /// Arşiv seçilmişse ilgili arşiv klasörü ve null filterDate döner (çünkü dosyalar zaten o tarihe ait).
    /// </summary>
    private string ResolveComparisonFolder(string? archiveDate, out DateOnly? filterDate)
    {
        var baseFolder = GetUploadFolder();
        filterDate = null;

        if (string.IsNullOrWhiteSpace(archiveDate) || archiveDate == "current")
            return baseFolder;

        if (!DateOnly.TryParseExact(archiveDate, "yyyy-MM-dd", out var date))
            return baseFolder;

        var archiveFolder = _archiveService.GetArchiveFolder(baseFolder, date);
        if (archiveFolder != null)
            return archiveFolder;

        // Arşiv klasörü yoksa mevcut dosyalarda tarih filtresi uygula (geriye uyumluluk)
        filterDate = date;
        return baseFolder;
    }
}

// ═══════════════════════════════════════════════════════════════
// Request DTOs
// ═══════════════════════════════════════════════════════════════

public class SaveDecisionRequest
{
    public ComparisonType ComparisonType { get; set; }
    public string DosyaNo { get; set; } = "";
    public decimal Miktar { get; set; }
    public string? BirimAdi { get; set; }
    public decimal? BankaTutar { get; set; }
    public string? BankaAciklama { get; set; }
    public double Confidence { get; set; }
    public string? MatchReason { get; set; }
    public string Decision { get; set; } = "";
}

public class DeleteDecisionRequest
{
    public int Id { get; set; }
}
