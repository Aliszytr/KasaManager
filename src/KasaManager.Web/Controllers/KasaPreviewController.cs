using System.Text;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Orchestration;
using KasaManager.Application.Services;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Export;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Domain.Validation;
using KasaManager.Web.Helpers;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using QuestPDF.Fluent;

namespace KasaManager.Web.Controllers;

/// <summary>
/// Kasa Preview controller — partial class.
/// Export, Snapshot ve Helper dosyaları ayrıdır.
/// </summary>
[Authorize]
public sealed partial class KasaPreviewController : Controller
{
    private readonly IKasaOrchestrator _orchestrator;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _cfg;
    private readonly IImportOrchestrator _importOrchestrator;
    private readonly IKasaReportDateRulesService _dateRules;
    private readonly IKasaRaporSnapshotService _snapshots;
    private readonly ICalculatedKasaSnapshotService _calcSnapshots;
    private readonly IKasaGlobalDefaultsService _globalDefaults;
    private readonly IBankaHesapKontrolService _hesapKontrol;
    private readonly IReportDataBuilder _reportBuilder;
    private readonly IExportService _exportService;
    private readonly IKasaValidationService _validation;
    private readonly IVergideBirikenLedgerService _vergiLedger;
    private readonly IDocumentTemplateService _templateService;
    private readonly IFinansalIstisnaService _finansalIstisna;
    private readonly IFinansalIstisnaAnomaliService _anomali;
    private readonly IDistributedCache _cache;
    private readonly ILogger<KasaPreviewController> _log;

    public KasaPreviewController(
        IKasaOrchestrator orchestrator,
        IWebHostEnvironment env,
        IConfiguration cfg,
        IImportOrchestrator importOrchestrator,
        IKasaReportDateRulesService dateRules,
        IKasaRaporSnapshotService snapshots,
        ICalculatedKasaSnapshotService calcSnapshots,
        IKasaGlobalDefaultsService globalDefaults,
        IBankaHesapKontrolService hesapKontrol,
        IReportDataBuilder reportBuilder,
        IExportService exportService,
        IKasaValidationService validation,
        IVergideBirikenLedgerService vergiLedger,
        IDocumentTemplateService templateService,
        IFinansalIstisnaService finansalIstisna,
        IFinansalIstisnaAnomaliService anomali,
        IDistributedCache cache,
        ILogger<KasaPreviewController> log)
    {
        _orchestrator = orchestrator;
        _env = env;
        _cfg = cfg;
        _importOrchestrator = importOrchestrator;
        _dateRules = dateRules;
        _snapshots = snapshots;
        _calcSnapshots = calcSnapshots;
        _globalDefaults = globalDefaults;
        _hesapKontrol = hesapKontrol;
        _reportBuilder = reportBuilder;
        _exportService = exportService;
        _validation = validation;
        _vergiLedger = vergiLedger;
        _templateService = templateService;
        _finansalIstisna = finansalIstisna;
        _anomali = anomali;
        _cache = cache;
        _log = log;
    }

    // =========================================================================
    // Intent-First: Dashboard'dan gelen kasaType parametresiyle otomatik yükleme
    // =========================================================================

    [HttpGet]
    public async Task<IActionResult> Index(string? kasaType, CancellationToken ct)
    {
        var model = BuildBaseModel();

        try
        {
            // 1. Tarih default: KasaÜstRapor snapshot tarihi (yoksa bugün)
            var lastSnapshotDate = await _snapshots.GetLastSnapshotDateAsync(KasaRaporTuru.Genel, ct);
            model.SelectedDate = lastSnapshotDate ?? DateOnly.FromDateTime(DateTime.Today);
            model.LastSnapshotDate = lastSnapshotDate;

            // 2. Intent-First: Dashboard'dan kasaType geliyorsa pipeline
            if (!string.IsNullOrEmpty(kasaType))
            {
                var normalizedType = NormalizeKasaType(kasaType);
                model.KasaType = normalizedType;

                // 2a. Draft restore — cache'te hesaplanmış veri varsa geri yükle
                if (await TryRestoreDraftAsync(model, normalizedType, lastSnapshotDate, ct))
                    return View(model);

                // 2b. FormulaSet yükleme
                await SafeLoadFormulaSetAsync(model, normalizedType, ct);

                // 2c. Genel Kasa tarih aralığı seed
                if (normalizedType.Equals("Genel", StringComparison.OrdinalIgnoreCase))
                {
                    model.GenelKasaStartDate ??= model.DefaultGenelKasaBaslangicTarihiSeed;
                    model.GenelKasaEndDate ??= model.SelectedDate;
                }

                // 2d. Auto-Load: Snapshot varsa verileri otomatik yükle
                if (lastSnapshotDate.HasValue)
                    await SafeAutoLoadPreviewAsync(model, normalizedType, ct);
            }

            // 3. Ortak hydration (UstRapor panel, upload dosyaları, vergide biriken, IBAN)
            await HydrateCommonAsync(model, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "KasaPreview Index pipeline hatası — kasaType={KasaType}", kasaType);
            model.Errors.Add($"❌ Sayfa yüklenirken kritik hata: {ex.Message}");
            await SafeHydrateFallbackAsync(model, ct);
        }

        return View(model);
    }

    // ── MS6 Pipeline Helpers ─────────────────────────────────────────────

    private static KasaPreviewViewModel BuildBaseModel() => new()
    {
        SelectedDate = DateOnly.FromDateTime(DateTime.Today)
    };

    /// <summary>Draft cache'ten geri yükleme dener. Başarılıysa true döner (early return).</summary>
    private async Task<bool> TryRestoreDraftAsync(
        KasaPreviewViewModel model, string normalizedType, DateOnly? lastSnapshotDate, CancellationToken ct)
    {
        try
        {
            var userName = User.Identity?.Name ?? "anonymous";
            var draftRestored = await KasaDraftCacheHelper.TryLoadDraftAsync(userName, normalizedType, model, _log);
            if (!draftRestored || !model.HasResults) return false;

            _log.LogInformation("KasaDraft cache'ten geri yüklendi: {KasaType}, Tarih={Tarih}",
                normalizedType, model.SelectedDate);
            ViewData["DraftRestored"] = true;
            ViewData["DraftInfo"] = KasaDraftCacheHelper.GetDraftInfoMessage(userName, normalizedType)
                ?? $"📋 {normalizedType} Kasa verileri önbellekten yüklendi. Tekrar hesaplamak için 'Tekrar Hesapla' butonunu kullanabilirsiniz.";

            model.UstRaporPanel ??= await HydrateUstRaporPanelAsync(ct);
            model.LastSnapshotDate = lastSnapshotDate;
            model.HasUploadedFiles = ListUploadedFiles().Count > 0;

            if (string.IsNullOrEmpty(model.IbanStopaj))
                await HydrateIbanInfoAsync(model, ct);

            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Draft restore başarısız, normal akışla devam ediliyor");
            return false;
        }
    }

    /// <summary>FormulaSet yükleme — hata soft (uyarı olarak eklenir).</summary>
    private async Task SafeLoadFormulaSetAsync(KasaPreviewViewModel model, string normalizedType, CancellationToken ct)
    {
        try
        {
            var dto = model.ToDto();
            await _orchestrator.LoadActiveFormulaSetByScopeAsync(dto, normalizedType, ct);
            await _orchestrator.HydrateDbFormulaSetsAsync(dto, ct);
            model.UpdateFromDto(dto);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "FormulaSet yükleme başarısız: {KasaType}", normalizedType);
            model.Warnings.Add($"⚠️ FormulaSet yüklenemedi: {ex.Message}");
        }
    }

    /// <summary>Snapshot varsa preview otomatik yükle — hata soft.</summary>
    private async Task SafeAutoLoadPreviewAsync(KasaPreviewViewModel model, string normalizedType, CancellationToken ct)
    {
        try
        {
            var uploadPath = ResolveUploadFolderAbsolute();
            var autoDto = model.ToDto();
            await _orchestrator.LoadPreviewAsync(autoDto, uploadPath, ct);
            await _orchestrator.HydrateDbFormulaSetsAsync(autoDto, ct);
            model.UpdateFromDto(autoDto);

            // Sabah + Akşam Kasa: Eksik/Fazla auto-fill
            if (normalizedType.Equals("Sabah", StringComparison.OrdinalIgnoreCase)
                || normalizedType.Equals("Aksam", StringComparison.OrdinalIgnoreCase))
            {
                await TryAutoFillEksikFazlaAsync(model, ct);
            }

            _log.LogInformation("KasaPreview auto-load başarılı: {KasaType}, Tarih={Tarih}",
                normalizedType, model.SelectedDate);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "KasaPreview auto-load başarısız (kritik değil, kullanıcı 'Veri Yükle' ile deneyebilir)");
        }
    }

    /// <summary>Ortak panel hydration — UstRapor, upload, vergide biriken, IBAN.</summary>
    private async Task HydrateCommonAsync(KasaPreviewViewModel model, CancellationToken ct)
    {
        model.UstRaporPanel = await HydrateUstRaporPanelAsync(ct);
        model.HasUploadedFiles = ListUploadedFiles().Count > 0;
        await HydrateVergideBirikenSeedAsync(model, ct);
        await HydrateIbanInfoAsync(model, ct);
        await HydrateFinansalIstisnalarAsync(model, ct);
    }

    /// <summary>Kritik hata sonrası minimum düzeyde sayfa yüklemesini garanti eder.</summary>
    private async Task SafeHydrateFallbackAsync(KasaPreviewViewModel model, CancellationToken ct)
    {
        try { model.UstRaporPanel ??= await HydrateUstRaporPanelAsync(ct); }
        catch (Exception ex) { _log.LogDebug(ex, "Fallback: UstRaporPanel yüklenemedi"); }

        try { model.HasUploadedFiles = ListUploadedFiles().Count > 0; }
        catch (Exception ex) { _log.LogDebug(ex, "Fallback: HasUploadedFiles kontrol edilemedi"); }
    }

    /// <summary>
    /// Admin Designer modu: R16 FormulaSet Builder görünür.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Designer(string? kasaType, CancellationToken ct)
    {
        var lastSnapshotDate = await _snapshots.GetLastSnapshotDateAsync(KasaRaporTuru.Genel, ct);
        var model = new KasaPreviewViewModel
        {
            SelectedDate = lastSnapshotDate ?? DateOnly.FromDateTime(DateTime.Today),
            IsAdminMode = true,
            KasaType = NormalizeKasaType(kasaType ?? "Custom")
        };

        var dto = model.ToDto();

        if (!string.IsNullOrEmpty(kasaType))
        {
            await _orchestrator.LoadActiveFormulaSetByScopeAsync(dto, model.KasaType, ct);
        }

        await _orchestrator.HydrateDbFormulaSetsAsync(dto, ct);
        model.UpdateFromDto(dto);

        // Panel persistence
        model.UstRaporPanel = await HydrateUstRaporPanelAsync(ct);

        // IBAN hydration
        await HydrateIbanInfoAsync(model, ct);

        return View("Index", model);
    }

    // =========================================================================
    // Data Loading & Calculation Actions
    // =========================================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoadData(KasaPreviewViewModel model, CancellationToken ct)
    {
        // Genel Kasa tarihleri: DateOnly? model binding fallback
        if (!model.GenelKasaStartDate.HasValue)
        {
            var raw = Request.Form["GenelKasaStartDate"].ToString();
            if (DateOnly.TryParse(raw, out var parsed)) model.GenelKasaStartDate = parsed;
        }
        if (!model.GenelKasaEndDate.HasValue)
        {
            var raw = Request.Form["GenelKasaEndDate"].ToString();
            if (DateOnly.TryParse(raw, out var parsed)) model.GenelKasaEndDate = parsed;
        }

        var dto = model.ToDto();
        var uploadPath = ResolveUploadFolderAbsolute();

        // Intent-First: kasaType varsa FormulaSet'i yeniden yükle
        if (!string.IsNullOrEmpty(model.KasaType))
        {
            await _orchestrator.LoadActiveFormulaSetByScopeAsync(dto, model.KasaType, ct);
        }

        await _orchestrator.LoadPreviewAsync(dto, uploadPath, ct);
        await _orchestrator.HydrateDbFormulaSetsAsync(dto, ct);
        model.UpdateFromDto(dto);

        // Panel persistence
        model.UstRaporPanel = await HydrateUstRaporPanelAsync(ct);

        // ─── B6: HesapKontrol Auto-Fill (Sabah + Akşam Kasa) ───
        if (model.KasaType?.Equals("Sabah", StringComparison.OrdinalIgnoreCase) == true
            || model.KasaType?.Equals("Aksam", StringComparison.OrdinalIgnoreCase) == true)
        {
            await TryAutoFillEksikFazlaAsync(model, ct);
        }

        // IBAN hydration
        await HydrateIbanInfoAsync(model, ct);

        // Financial Exceptions + Anomali Önerileri (Akıllı Öneriler)
        await HydrateFinansalIstisnalarAsync(model, ct);

        return View("Index", model);
    }

    /// <summary>
    /// Birleşik akış: Veri Yükle + Hesapla tek POST ile çalışır.
    /// Kullanıcı 2 buton yerine 1 buton ile tüm işlemi tamamlar.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoadAndCalculate(KasaPreviewViewModel model, CancellationToken ct)
    {
        // ── 1. Genel Kasa tarihleri fallback ──
        if (!model.GenelKasaStartDate.HasValue)
        {
            var raw = Request.Form["GenelKasaStartDate"].ToString();
            if (DateOnly.TryParse(raw, out var parsed)) model.GenelKasaStartDate = parsed;
        }
        if (!model.GenelKasaEndDate.HasValue)
        {
            var raw = Request.Form["GenelKasaEndDate"].ToString();
            if (DateOnly.TryParse(raw, out var parsed)) model.GenelKasaEndDate = parsed;
        }

        var dto = model.ToDto();
        var uploadPath = ResolveUploadFolderAbsolute();

        // ── 2. Veri Yükle (LoadData logic) ──
        var effectiveKasaType = !string.IsNullOrEmpty(model.KasaType) ? model.KasaType : "Aksam";
        await _orchestrator.LoadActiveFormulaSetByScopeAsync(dto, effectiveKasaType, ct);
        await _orchestrator.LoadPreviewAsync(dto, uploadPath, ct);
        await _orchestrator.HydrateDbFormulaSetsAsync(dto, ct);
        model.UpdateFromDto(dto);

        // ── 3. Hesapla (Calculate logic) ──
        dto = model.ToDto(); // dto'yu güncellenmiş model'den yenile
        await _orchestrator.RunFormulaEnginePreviewAsync(dto, uploadPath, ct);
        await _orchestrator.HydrateDbFormulaSetsAsync(dto, ct);
        model.UpdateFromDto(dto);

        if (model.Errors.Count == 0 && (model.Drafts != null || model.FormulaRun != null))
        {
            model.HasResults = true;
        }

        // ── 4. Panel + IBAN + Vergide Biriken ──
        model.UstRaporPanel = await HydrateUstRaporPanelAsync(ct);

        // ─── B5: HesapKontrol Otomatik Analiz (Sabah + Akşam Tam Gün) ───
        var isSabahLC = model.KasaType?.Equals("Sabah", StringComparison.OrdinalIgnoreCase) == true;
        var isAksamTamGunLC = model.KasaType?.Equals("Aksam", StringComparison.OrdinalIgnoreCase) == true
                              && !model.AksamMesaiSonuModu;
        if (isSabahLC || isAksamTamGunLC)
        {
            try
            {
                var analizTarihi = model.SelectedDate ?? DateOnly.FromDateTime(DateTime.Now);
                var rapor = await _hesapKontrol.AnalyzeFromComparisonAsync(analizTarihi, uploadPath, ct);
                _log.LogInformation("HesapKontrol analiz tamamlandı: {Ozet}", rapor.OzetMesaj);
                await TryAutoFillEksikFazlaAsync(model, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "HesapKontrol otomatik analiz başarısız, sonuçlar etkilenmedi");
            }
        }

        await HydrateVergideBirikenSeedAsync(model, ct);
        await HydrateIbanInfoAsync(model, ct);
        await HydrateFinansalIstisnalarAsync(model, ct);
        await HydrateValidationAsync(model, ct);

        // ─── Draft Auto-Save ───
        try
        {
            var userNameLC = User.Identity?.Name ?? "anonymous";
            _log.LogInformation("KasaDraft SAVE (LoadAndCalc): User={User}, KasaType={KT}, HasResults={HR}",
                userNameLC, effectiveKasaType, model.HasResults);
            await KasaDraftCacheHelper.SaveDraftAsync(userNameLC, effectiveKasaType, model, _log);
        }
        catch (Exception ex) { _log.LogError(ex, "KasaDraft SAVE (LoadAndCalc) HATA"); }

        return View("Index", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Calculate(KasaPreviewViewModel model, CancellationToken ct)
    {
        _log.LogWarning("▶▶▶ CALCULATE ACTION ENTERED ◀◀◀ KasaType={KT}, HasResults={HR}", model.KasaType, model.HasResults);
        // Genel Kasa tarihleri: hidden field'dan model binding başarısız olabilir
        if (!model.GenelKasaStartDate.HasValue)
        {
            var raw = Request.Form["GenelKasaStartDate"].ToString();
            if (DateOnly.TryParse(raw, out var parsed)) model.GenelKasaStartDate = parsed;
        }
        if (!model.GenelKasaEndDate.HasValue)
        {
            var raw = Request.Form["GenelKasaEndDate"].ToString();
            if (DateOnly.TryParse(raw, out var parsed)) model.GenelKasaEndDate = parsed;
        }

        var dto = model.ToDto();
        var uploadPath = ResolveUploadFolderAbsolute();

        var effectiveKasaType = !string.IsNullOrEmpty(model.KasaType) ? model.KasaType : "Aksam";
        await _orchestrator.LoadActiveFormulaSetByScopeAsync(dto, effectiveKasaType, ct);
        await _orchestrator.RunFormulaEnginePreviewAsync(dto, uploadPath, ct);
        await _orchestrator.HydrateDbFormulaSetsAsync(dto, ct);

        model.UpdateFromDto(dto);

        // Progressive Disclosure: Hesaplama başarılıysa sonuçlar var
        if (model.Errors.Count == 0 && (model.Drafts != null || model.FormulaRun != null))
        {
            model.HasResults = true;
        }

        // Panel persistence
        model.UstRaporPanel = await HydrateUstRaporPanelAsync(ct);

        // ─── B5: HesapKontrol Otomatik Analiz (Sabah + Akşam Tam Gün) ───
        var isSabah = model.KasaType?.Equals("Sabah", StringComparison.OrdinalIgnoreCase) == true;
        var isAksamTamGun = model.KasaType?.Equals("Aksam", StringComparison.OrdinalIgnoreCase) == true
                            && !model.AksamMesaiSonuModu;
        if (isSabah || isAksamTamGun)
        {
            try
            {
                var analizTarihi = model.SelectedDate ?? DateOnly.FromDateTime(DateTime.Now);
                var rapor = await _hesapKontrol.AnalyzeFromComparisonAsync(analizTarihi, uploadPath, ct);
                _log.LogInformation("HesapKontrol analiz tamamlandı: {Ozet}", rapor.OzetMesaj);
                await TryAutoFillEksikFazlaAsync(model, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "HesapKontrol otomatik analiz başarısız, sonuçlar etkilenmedi");
            }
        }

        // Vergide Biriken seed hydration
        await HydrateVergideBirikenSeedAsync(model, ct);

        // IBAN hydration
        await HydrateIbanInfoAsync(model, ct);

        // Financial Exceptions + Anomali Önerileri (Akıllı Öneriler)
        await HydrateFinansalIstisnalarAsync(model, ct);

        // ─── Validation Uyarı Sistemi ───
        await HydrateValidationAsync(model, ct);

        // ─── Draft Auto-Save: Hesaplama sonuçlarını cache'e yaz ───
        // HasResults guard kaldırıldı — her hesaplama sonrası kaydet
        try
        {
            var userName = User.Identity?.Name ?? "anonymous";
            _log.LogInformation("KasaDraft SAVE başlıyor: User={User}, KasaType={KT}, HasResults={HR}, Drafts={D}, FormulaRun={FR}",
                userName, effectiveKasaType, model.HasResults,
                model.Drafts != null, model.FormulaRun != null);
            await KasaDraftCacheHelper.SaveDraftAsync(userName, effectiveKasaType, model, _log);
            _log.LogInformation("KasaDraft SAVE tamamlandı: {KasaType}", effectiveKasaType);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "KasaDraft SAVE HATA: {KasaType}", effectiveKasaType);
        }

        return View("Index", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunFormulaEngine(KasaPreviewViewModel model, CancellationToken ct)
    {
        var dto = model.ToDto();
        var uploadPath = ResolveUploadFolderAbsolute();

        // UI Action Dispatch
        var uiActionRaw = (Request.Form["uiAction"].ToString() ?? string.Empty).Trim();
        var uiAction = uiActionRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;

        // DB Actions
        if (uiAction.StartsWith("db", StringComparison.OrdinalIgnoreCase))
        {
            if (uiAction.Equals("dbLoad", StringComparison.OrdinalIgnoreCase))
            {
                await _orchestrator.LoadDbFormulaSetIntoModelAsync(dto, ct);
                await _orchestrator.LoadPreviewAsync(dto, uploadPath, ct);
                dto.IsDataLoaded = true;
            }
            else if (uiAction.Equals("dbCreate", StringComparison.OrdinalIgnoreCase))
            {
                await _orchestrator.CreateDbFormulaSetAsync(dto, ct);
            }
            else if (uiAction.Equals("dbSaveNew", StringComparison.OrdinalIgnoreCase))
            {
                 dto.DbFormulaSetId = null; 
                 await _orchestrator.CreateDbFormulaSetAsync(dto, ct);
            }
            else if (uiAction.Equals("dbUpdate", StringComparison.OrdinalIgnoreCase))
            {
                await _orchestrator.SaveDbFormulaSetAsync(dto, isUpdate: true, ct);
            }
            else if (uiAction.Equals("dbDelete", StringComparison.OrdinalIgnoreCase))
            {
                await _orchestrator.DeleteDbFormulaSetAsync(dto, ct);
            }
            else if (uiAction.Equals("dbCopy", StringComparison.OrdinalIgnoreCase))
            {
                await _orchestrator.CopyDbFormulaSetAsync(dto, ct);
            }
            else if (uiAction.Equals("dbActivate", StringComparison.OrdinalIgnoreCase))
            {
                await _orchestrator.ToggleActiveDbFormulaSetAsync(dto, ct);
            }
            
            await _orchestrator.HydrateDbFormulaSetsAsync(dto, ct);
            ModelState.Clear();
        }
        else if (uiAction.Equals("loadSetV1", StringComparison.OrdinalIgnoreCase))
        {
            await _orchestrator.LoadFormulaSetV1Async(dto, ct);
            ModelState.Clear();
        }
        else if (uiAction.Equals("loadAksamContract", StringComparison.OrdinalIgnoreCase))
        {
            await _orchestrator.LoadAksamContractAsync(dto, ct);
            ModelState.Clear();
        }

        if (dto.IsDataLoaded || uiAction.Equals("run", StringComparison.OrdinalIgnoreCase)) 
        {
             await _orchestrator.RunFormulaEnginePreviewAsync(dto, uploadPath, ct);
             await _orchestrator.HydrateDbFormulaSetsAsync(dto, ct);
        }
        
        model.UpdateFromDto(dto);

        // Panel persistence
        model.UstRaporPanel = await HydrateUstRaporPanelAsync(ct);

        // IBAN hydration
        await HydrateIbanInfoAsync(model, ct);

        return View("Index", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReloadGenelKasaTrueSourceV2(KasaPreviewViewModel model, CancellationToken ct)
    {
        var dto = model.ToDto();
        await _orchestrator.LoadPreviewAsync(dto, ResolveUploadFolderAbsolute(), ct);
        model.UpdateFromDto(dto);

        // Panel persistence
        model.UstRaporPanel = await HydrateUstRaporPanelAsync(ct);

        return View("Index", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnifiedPool(KasaPreviewViewModel model, CancellationToken ct)
    {
        var dto = model.ToDto();
        await _orchestrator.LoadPreviewAsync(dto, ResolveUploadFolderAbsolute(), ct);
        model.UpdateFromDto(dto);

        // Panel persistence
        model.UstRaporPanel = await HydrateUstRaporPanelAsync(ct);

        return View("Index", model);
    }
}
