using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Mvc;

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
    private readonly IKasaRaporSnapshotService _snapshots;
    private readonly IDocumentTemplateService _templateService;

    public KasaSettingsController(
        IKasaGlobalDefaultsService globalDefaults,
        IKasaRaporSnapshotService snapshots,
        IDocumentTemplateService templateService)
    {
        _globalDefaults = globalDefaults;
        _snapshots = snapshots;
        _templateService = templateService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var defaults = await _globalDefaults.GetAsync(ct);

        var vm = new KasaSettingsViewModel
        {
            SelectedVergiKasaVeznedarlar = KasaSettingsViewModel.TryParseSelected(defaults.SelectedVeznedarlarJson),
            DefaultNakitPara = defaults.DefaultNakitPara,
            DefaultBozukPara = defaults.DefaultBozukPara,
            DefaultKasaEksikFazla = defaults.DefaultKasaEksikFazla,
            DefaultGenelKasaDevredenSeed = defaults.DefaultGenelKasaDevredenSeed,
            DefaultGenelKasaBaslangicTarihiSeed = defaults.DefaultGenelKasaBaslangicTarihiSeed is null ? null : DateOnly.FromDateTime(defaults.DefaultGenelKasaBaslangicTarihiSeed.Value),
            DefaultKaydenTahsilat = defaults.DefaultKaydenTahsilat,
            DefaultDundenDevredenKasaNakit = defaults.DefaultDundenDevredenKasaNakit,
            // IBAN bilgileri
            HesapAdiStopaj = defaults.HesapAdiStopaj,
            IbanStopaj = defaults.IbanStopaj,
            HesapAdiMasraf = defaults.HesapAdiMasraf,
            IbanMasraf = defaults.IbanMasraf,
            HesapAdiHarc = defaults.HesapAdiHarc,
            IbanHarc = defaults.IbanHarc,
            IbanPostaPulu = defaults.IbanPostaPulu
        };

        // Banka Yazıları Şablonları
        var templates = await _templateService.GetAllAsync(ct);
        vm.DocumentTemplates = templates.ToList();

        // Checkbox seçenekleri için en son Genel snapshot'tan veznedarları topla
        var last = await _snapshots.GetLastSnapshotDateAsync(KasaRaporTuru.Genel, ct);
        if (last is null)
        {
            vm.Warnings.Add("Henüz Genel snapshot yok. Veznedar listesi boş görünebilir.");
            return View(vm);
        }

        vm.OptionsSourceDate = last;
        var snap = await _snapshots.GetAsync(last.Value, KasaRaporTuru.Genel, ct);
        if (snap?.Rows is null || snap.Rows.Count == 0)
        {
            vm.Warnings.Add("En son Genel snapshot okunamadı. Veznedar listesi boş görünebilir.");
            return View(vm);
        }

        vm.VeznedarOptions = snap.Rows
            .Where(r => !r.IsSummaryRow && !string.IsNullOrWhiteSpace(r.Veznedar))
            .Select(r => r.Veznedar!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

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
