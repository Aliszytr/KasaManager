using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace KasaManager.Web.Controllers;

/// <summary>
/// R9.1: Global Ayarlar
/// - Vergi Kasa veznedar varsayılan listesi (KasaÜstRapor'da checkbox preselect)
/// - Varsayılan Nakit / Bozuk para
/// </summary>
[Authorize(Roles = "Admin")]
public sealed class KasaSettingsController : Controller
{
    private readonly IKasaGlobalDefaultsService _globalDefaults;

    private readonly IDocumentTemplateService _templateService;
    private readonly ILogger<KasaSettingsController> _log;

    public KasaSettingsController(
        IKasaGlobalDefaultsService globalDefaults,
        IDocumentTemplateService templateService,
        ILogger<KasaSettingsController> log)
    {
        _globalDefaults = globalDefaults;
        _templateService = templateService;
        _log = log;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = new KasaSettingsViewModel();

        // 1. Ayarları oku — patlarsa sayfa yine de açılsın
        try
        {
            var defaults = await _globalDefaults.GetAsync(ct);

            vm.SelectedVergiKasaVeznedarlar = KasaSettingsViewModel.TryParseSelected(defaults.SelectedVeznedarlarJson);
            vm.DefaultNakitPara = defaults.DefaultNakitPara;
            vm.DefaultBozukPara = defaults.DefaultBozukPara;
            vm.DefaultKasaEksikFazla = defaults.DefaultKasaEksikFazla;
            vm.DefaultGenelKasaDevredenSeed = defaults.DefaultGenelKasaDevredenSeed;
            vm.DefaultGenelKasaBaslangicTarihiSeed = defaults.DefaultGenelKasaBaslangicTarihiSeed is null ? null : DateOnly.FromDateTime(defaults.DefaultGenelKasaBaslangicTarihiSeed.Value);
            vm.DefaultKaydenTahsilat = defaults.DefaultKaydenTahsilat;
            vm.DefaultDundenDevredenKasaNakit = defaults.DefaultDundenDevredenKasaNakit;
            // IBAN bilgileri
            vm.HesapAdiStopaj = defaults.HesapAdiStopaj;
            vm.IbanStopaj = defaults.IbanStopaj;
            vm.HesapAdiMasraf = defaults.HesapAdiMasraf;
            vm.IbanMasraf = defaults.IbanMasraf;
            vm.HesapAdiHarc = defaults.HesapAdiHarc;
            vm.IbanHarc = defaults.IbanHarc;
            vm.IbanPostaPulu = defaults.IbanPostaPulu;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[KasaSettings.Index] Ayarlar DB'den okunamadı");
            vm.Errors ??= new List<string>();
            vm.SelectedVergiKasaVeznedarlar ??= new List<string>();
            vm.Errors.Add($"Ayarlar veritabanından okunamadı: {ex.Message}");
        }

        // 2. Banka Yazıları Şablonları — patlarsa sadece şablonlar boş kalır
        try
        {
            var templates = await _templateService.GetAllAsync(ct);
            vm.DocumentTemplates = templates.ToList();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[KasaSettings.Index] Banka yazıları şablonları okunamadı");
            vm.Warnings ??= new List<string>();
            vm.DocumentTemplates ??= new List<KasaManager.Domain.Reports.DocumentTemplate>();
            vm.Warnings.Add($"Banka yazıları şablonları yüklenemedi: {ex.Message}");
        }

        // 3. Veznedar Listesi Snapshot ile toplanıyordu (P4.4: İptal edildi)
        vm.Warnings ??= new List<string>();
        vm.Warnings.Add("Veznedar listesi (otomatik önerme) Snapshot sistemi ile birlikte deaktif edilmiştir.");
        vm.VeznedarOptions = new List<string>();

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(KasaSettingsViewModel model, CancellationToken ct)
    {
        // Normalize
        model.SelectedVergiKasaVeznedarlar ??= new List<string>();
        var selected = model.SelectedVergiKasaVeznedarlar
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        await _globalDefaults.SaveVergiKasaSelectedAsync(selected, User?.Identity?.Name, ct);
        await _globalDefaults.SaveDefaultCashAsync(
            model.DefaultNakitPara,
            model.DefaultBozukPara,
            model.DefaultKasaEksikFazla,
            model.DefaultGenelKasaDevredenSeed,
            model.DefaultGenelKasaBaslangicTarihiSeed is null ? null : model.DefaultGenelKasaBaslangicTarihiSeed.Value.ToDateTime(TimeOnly.MinValue),
            model.DefaultKaydenTahsilat,
            model.DefaultDundenDevredenKasaNakit,
            User?.Identity?.Name,
            ct);

        // IBAN ayarlarını kaydet
        await _globalDefaults.SaveIbanSettingsAsync(
            model.HesapAdiStopaj, model.IbanStopaj,
            model.HesapAdiMasraf, model.IbanMasraf,
            model.HesapAdiHarc, model.IbanHarc,
            model.IbanPostaPulu,
            User?.Identity?.Name,
            ct);

        TempData["Info"] = "Ayarlar kaydedildi.";
        return RedirectToAction(nameof(Index));
    }

    // ═══════════════════════════════════════════════════════
    // AJAX: Banka Yazıları Şablon CRUD
    // ═══════════════════════════════════════════════════════

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTemplate(
        Guid? templateId, string templateName, string templateCategory,
        string bodyTemplate, string? headerText, string? footerText,
        string? sayiKonuText, string? muhatapText, string? imzaBlokuText,
        bool isActive, CancellationToken ct)
    {
        var isUpdate = templateId.HasValue && templateId.Value != Guid.Empty;
        var template = isUpdate
            ? await _templateService.GetByIdAsync(templateId!.Value, ct)
            : null;

        if (template == null)
        {
            template = new DocumentTemplate();
            // Güncelleme isteğiyse orijinal ID'yi koru — aksi halde yeni dosya oluşur
            if (isUpdate)
                template.Id = templateId!.Value;
        }

        template.Name = string.IsNullOrWhiteSpace(templateName) ? "Adsız Şablon" : templateName;
        template.Category = templateCategory ?? "BankaYazisi";
        template.HeaderText = headerText;
        template.SayiKonuText = sayiKonuText;
        template.MuhatapText = muhatapText;
        template.BodyTemplate = bodyTemplate ?? "";
        template.ImzaBlokuText = imzaBlokuText;
        template.FooterText = footerText;
        template.IsActive = isActive;

        await _templateService.SaveAsync(template, ct);

        var msg = isUpdate ? "Şablon güncellendi." : "Yeni şablon oluşturuldu.";
        return Json(new { success = true, id = template.Id, message = msg });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTemplate(Guid templateId, CancellationToken ct)
    {
        await _templateService.DeleteAsync(templateId, ct);
        return Json(new { success = true, message = "Şablon silindi." });
    }
}
