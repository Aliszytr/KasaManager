#nullable enable
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace KasaManager.Web.Controllers;

/// <summary>
/// Kasa Raporları CRUD Yönetim Sistemi.
/// Kaydedilmiş hesaplama sonuçlarının tam yönetimi:
/// Liste, Arama, Detay, Güncelleme, Silme, Geri Yükleme, Export.
/// </summary>
[Authorize]
public sealed class KasaRaporlarController : Controller
{
    private readonly ICalculatedKasaSnapshotService _service;
    private readonly ILogger<KasaRaporlarController> _logger;

    public KasaRaporlarController(
        ICalculatedKasaSnapshotService service,
        ILogger<KasaRaporlarController> logger)
    {
        _service = service;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════
    // INDEX — Ana Liste Sayfası
    // ══════════════════════════════════════════════════════
    
    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        KasaRaporTuru? turu,
        string? startDate,
        string? endDate,
        bool includeDeleted = false,
        string sortBy = "RaporTarihi",
        bool sortDesc = true,
        int page = 1,
        CancellationToken ct = default)
    {
        var query = new KasaReportSearchQuery
        {
            SearchText = search,
            KasaTuru = turu,
            StartDate = ParseDate(startDate),
            EndDate = ParseDate(endDate),
            IncludeDeleted = includeDeleted,
            SortBy = sortBy,
            SortDescending = sortDesc,
            Page = page,
            PageSize = 20
        };

        // Varsayılan: Tarih filtresi yoksa son 90 gün
        if (!query.StartDate.HasValue && !query.EndDate.HasValue && string.IsNullOrWhiteSpace(search))
        {
            query.StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-90));
        }

        var results = await _service.SearchAsync(query, ct);

        // İstatistikler
        var sabahCount = await _service.CountAsync(KasaRaporTuru.Sabah, ct);
        var aksamCount = await _service.CountAsync(KasaRaporTuru.Aksam, ct);
        var genelCount = await _service.CountAsync(KasaRaporTuru.Genel, ct);

        var vm = new KasaRaporlarViewModel
        {
            Results = results,
            ToplamRapor = sabahCount + aksamCount + genelCount,
            SabahCount = sabahCount,
            AksamCount = aksamCount,
            GenelCount = genelCount,
            SearchText = search,
            KasaTuru = turu,
            StartDate = query.StartDate,
            EndDate = query.EndDate,
            IncludeDeleted = includeDeleted,
            SortBy = sortBy,
            SortDescending = sortDesc
        };

        return View(vm);
    }

    // ══════════════════════════════════════════════════════
    // SEARCH — AJAX Arama Endpoint
    // ══════════════════════════════════════════════════════
    
    [HttpGet]
    public async Task<IActionResult> Search(
        string? search,
        KasaRaporTuru? turu,
        string? startDate,
        string? endDate,
        bool includeDeleted = false,
        string sortBy = "RaporTarihi",
        bool sortDesc = true,
        int page = 1,
        CancellationToken ct = default)
    {
        var query = new KasaReportSearchQuery
        {
            SearchText = search,
            KasaTuru = turu,
            StartDate = ParseDate(startDate),
            EndDate = ParseDate(endDate),
            IncludeDeleted = includeDeleted,
            SortBy = sortBy,
            SortDescending = sortDesc,
            Page = page,
            PageSize = 20
        };

        var results = await _service.SearchAsync(query, ct);

        var items = results.Items.Select(s => new
        {
            s.Id,
            s.Name,
            s.Description,
            s.Notes,
            RaporTarihi = s.RaporTarihi.ToString("dd.MM.yyyy"),
            KasaTuru = s.KasaTuru.ToString(),
            KasaTuruInt = (int)s.KasaTuru,
            s.FormulaSetName,
            s.CalculatedBy,
            CalculatedAt = s.CalculatedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
            s.Version,
            s.IsActive,
            s.IsDeleted
        });

        return Json(new
        {
            items,
            results.TotalCount,
            results.Page,
            results.PageSize,
            results.TotalPages,
            results.HasPrevious,
            results.HasNext
        });
    }

    // ══════════════════════════════════════════════════════
    // DETAY — Rapor Detay Görüntüleme
    // ══════════════════════════════════════════════════════
    
    [HttpGet]
    public async Task<IActionResult> Detay(Guid id, CancellationToken ct)
    {
        var snapshot = await _service.GetByIdAsync(id, ct);
        if (snapshot is null)
        {
            TempData["Error"] = "Rapor bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        KasaRaporData? raporData = null;
        if (!string.IsNullOrWhiteSpace(snapshot.KasaRaporDataJson))
        {
            try
            {
                raporData = JsonSerializer.Deserialize<KasaRaporData>(snapshot.KasaRaporDataJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "KasaRaporDataJson parse edilemedi - Id={Id}", id);
            }
        }

        // Tüm versiyonları yükle (versiyon geçmişi UI için)
        var versiyonlar = await _service.GetAllVersionsAsync(snapshot.RaporTarihi, snapshot.KasaTuru, ct);

        var vm = new KasaRaporDetayViewModel
        {
            Snapshot = snapshot,
            Inputs = snapshot.GetInputs(),
            Outputs = snapshot.GetOutputs(),
            RaporData = raporData,
            Versiyonlar = versiyonlar
        };

        return View(vm);
    }

    // ══════════════════════════════════════════════════════
    // VERSİYONLAR — AJAX JSON (tarih+tip için tüm versiyonlar)
    // ══════════════════════════════════════════════════════

    [HttpGet]
    public async Task<IActionResult> Versiyonlar(Guid id, CancellationToken ct)
    {
        var snapshot = await _service.GetByIdAsync(id, ct);
        if (snapshot is null) return NotFound();

        var versiyonlar = await _service.GetAllVersionsAsync(snapshot.RaporTarihi, snapshot.KasaTuru, ct);
        var items = versiyonlar.Select(v => new
        {
            v.Id,
            v.Name,
            v.Version,
            v.IsActive,
            v.IsDeleted,
            v.CalculatedBy,
            CalculatedAt = v.CalculatedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
            v.FormulaSetName
        });
        return Json(new { items });
    }

    // ══════════════════════════════════════════════════════
    // VERSİYON AKTİF YAP — AJAX POST
    // ══════════════════════════════════════════════════════

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VersionuAktifYap(Guid id, CancellationToken ct)
    {
        var snapshot = await _service.GetByIdAsync(id, ct);
        if (snapshot is null)
            return Json(new { success = false, message = "Rapor bulunamadı." });

        await _service.ActivateVersionAsync(id, ct);
        _logger.LogInformation("Versiyon aktifleştirildi: Id={Id}, v{Version}", id, snapshot.Version);
        return Json(new { success = true, message = $"v{snapshot.Version} aktif olarak ayarlandı." });
    }

    // ══════════════════════════════════════════════════════
    // UPDATE — İsim/Açıklama/Not Güncelleme (AJAX)
    // ══════════════════════════════════════════════════════
    
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, string? name, string? description, string? notes, CancellationToken ct)
    {
        await _service.UpdateAsync(id, name, description, notes, ct);
        return Json(new { success = true, message = "Rapor güncellendi." });
    }

    // ══════════════════════════════════════════════════════
    // SİL — Soft Delete (AJAX)
    // ══════════════════════════════════════════════════════
    
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sil(Guid id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, User?.Identity?.Name, ct);
        return Json(new { success = true, message = "Rapor silindi." });
    }

    // ══════════════════════════════════════════════════════
    // GERİ YÜKLE — Restore (AJAX)
    // ══════════════════════════════════════════════════════
    
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GeriYukle(Guid id, CancellationToken ct)
    {
        await _service.RestoreAsync(id, ct);
        return Json(new { success = true, message = "Rapor geri yüklendi." });
    }

    // ══════════════════════════════════════════════════════
    // EXPORT — CSV Export
    // ══════════════════════════════════════════════════════
    
    [HttpGet]
    public async Task<IActionResult> ExportCsv(
        string? search,
        KasaRaporTuru? turu,
        string? startDate,
        string? endDate,
        CancellationToken ct)
    {
        var query = new KasaReportSearchQuery
        {
            SearchText = search,
            KasaTuru = turu,
            StartDate = ParseDate(startDate),
            EndDate = ParseDate(endDate),
            PageSize = 1000 // tüm sonuçları al
        };

        var results = await _service.SearchAsync(query, ct);

        var sb = new StringBuilder();
        sb.AppendLine("Tarih;Kasa Türü;Rapor Adı;Formül Seti;Hesaplayan;Kayıt Zamanı;Versiyon;Not");

        foreach (var s in results.Items)
        {
            sb.AppendLine(string.Join(";",
                s.RaporTarihi.ToString("dd.MM.yyyy"),
                s.KasaTuru.ToString(),
                EscapeCsv(s.Name),
                EscapeCsv(s.FormulaSetName),
                EscapeCsv(s.CalculatedBy),
                s.CalculatedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                s.Version,
                EscapeCsv(s.Notes)
            ));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"KasaRaporlari_{DateTime.Now:yyyyMMdd_HHmm}.csv");
    }

    // ══════════════════════════════════════════════════════
    // EXPORT — JSON (Tek rapor detaylı export)
    // ══════════════════════════════════════════════════════
    
    [HttpGet]
    public async Task<IActionResult> ExportJson(Guid id, CancellationToken ct)
    {
        var snapshot = await _service.GetByIdAsync(id, ct);
        if (snapshot is null) return NotFound();

        var export = new
        {
            Meta = new
            {
                snapshot.Id,
                RaporTarihi = snapshot.RaporTarihi.ToString("dd.MM.yyyy"),
                KasaTuru = snapshot.KasaTuru.ToString(),
                snapshot.Name,
                snapshot.Description,
                snapshot.Notes,
                snapshot.FormulaSetName,
                snapshot.FormulaSetId,
                snapshot.CalculatedBy,
                CalculatedAt = snapshot.CalculatedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
                snapshot.Version,
                snapshot.IsActive,
                snapshot.IsDeleted
            },
            Inputs = snapshot.GetInputs(),
            Outputs = snapshot.GetOutputs()
        };

        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(json)).ToArray();
        return File(bytes, "application/json; charset=utf-8",
            $"KasaRapor_{snapshot.RaporTarihi:yyyyMMdd}_{snapshot.KasaTuru}_{snapshot.Version}.json");
    }

    // ══════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════
    
    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateOnly.TryParse(value, out var d) ? d : null;
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
