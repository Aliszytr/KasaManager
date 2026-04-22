using System.Text;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Orchestration;
using KasaManager.Application.Services;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Calculation;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Export;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Domain.Validation;
using KasaManager.Web.Helpers;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    private readonly KasaManager.Application.Services.ReadAdapter.IKasaReadModelService _readModelService;
    private readonly ICalculatedKasaSnapshotService _calcSnapshots;
    private readonly IKasaRaporSnapshotService _raporSnapshots;

    public KasaPreviewController(
        IKasaOrchestrator orchestrator,
        IWebHostEnvironment env,
        IConfiguration cfg,
        IImportOrchestrator importOrchestrator,
        IKasaReportDateRulesService dateRules,

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
        ILogger<KasaPreviewController> log,
        // FAZ 4: Adapter Injection
        KasaManager.Application.Services.ReadAdapter.IKasaReadModelService readModelService,
        ICalculatedKasaSnapshotService calcSnapshots,
        IKasaRaporSnapshotService raporSnapshots)
    {
        _orchestrator = orchestrator;
        _env = env;
        _cfg = cfg;
        _importOrchestrator = importOrchestrator;
        _dateRules = dateRules;

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
        _readModelService = readModelService;
        _calcSnapshots = calcSnapshots;
        _raporSnapshots = raporSnapshots;
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
            // 1. Tarih default: Yüklü Excel dosyalarından tespit edilen tarih (yoksa bugün)
            var defaultDate = DateOnly.FromDateTime(DateTime.Today);
            model.SelectedDate = defaultDate;

            // 1b. Dosya tarihi tespiti: Excel dosyaları yüklüyse, dosyanın tarihini kullan
            try
            {
                var uploadFolder = ResolveUploadFolderAbsolute();
                var dateEval = await _dateRules.EvaluateAsync(uploadFolder, ct);
                if (dateEval.ProposedDate.HasValue)
                {
                    model.SelectedDate = dateEval.ProposedDate.Value;
                    _log.LogInformation(
                        "KasaPreview: Dosya tarihinden otomatik tarih ayarlandı: {ProposedDate} (bugün: {Today})",
                        dateEval.ProposedDate.Value, defaultDate);
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Dosya tarih tespiti başarısız, bugünkü tarih kullanılacak");
            }

            // 2. Intent-First: Dashboard'dan kasaType geliyorsa pipeline
            if (!string.IsNullOrEmpty(kasaType))
            {
                var normalizedType = NormalizeKasaType(kasaType);
                model.KasaType = normalizedType;

                if (await TryRestoreDraftAsync(model, normalizedType, null, ct))
                    return View(model);

                // 2b. FormulaSet yükleme
                await SafeLoadFormulaSetAsync(model, normalizedType, ct);

                // 2c. Genel Kasa tarih aralığı seed
                if (normalizedType.Equals("Genel", StringComparison.OrdinalIgnoreCase))
                {
                    model.GenelKasaStartDate ??= model.DefaultGenelKasaBaslangicTarihiSeed;
                    model.GenelKasaEndDate ??= model.SelectedDate;
                }

                // Vergide Biriken: Tüm kasa tipleri için hesaplama ÖNCESİ çağrılır
                // VergiKasaBakiyeToplam değeri formüle input olarak gerekli
                await HydrateVergideBirikenSeedAsync(model, ct);

                // 2d-pre. Otomatik Genel Snapshot oluşturma: Dosya varsa ama snapshot yoksa otomatik oluştur
                // Bu sayede kullanıcı KasaÜstRapor sayfasına gitmek zorunda kalmaz.
                await TryAutoProvisionGenelSnapshotAsync(model.SelectedDate ?? defaultDate, ct);

                // 2d. Auto-Load: Stateless - her zaman güncel veriyi yükle (veya cache'ten oku)
                await SafeAutoLoadPreviewAsync(model, normalizedType, ct);
            }

            // 3. Ortak hydration (UstRapor panel, upload dosyaları, IBAN vb.)
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
            // P4.3: LastSnapshotDate removed
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
            
            // FAZ 4: Adapter üzerinden geçirme (Sadece 1 kritik read entry point)
            var readReq = new KasaManager.Application.Services.ReadAdapter.KasaReadRequest {
                TargetDate = model.SelectedDate ?? DateOnly.FromDateTime(DateTime.Today),
                KasaScope = normalizedType,
                BaseUploadFolder = uploadPath,
                ContextDto = model.ToDto() // Düzeltme: UI state kaybolmasın diye ContextDto eklendi
            };
            
            var readRes = await _readModelService.GetReadModelAsync(readReq, ct);
            if (readRes.Ok && readRes.Value != null)
            {
                // Primary Legacy olarak döner (Mutlak kural)
                model.UpdateFromDto(readRes.Value.Primary);

                if (model.IsAdminMode)
                {
                    model.CandidateEligibility = readRes.Value.EligibilityReason.ToString();
                    model.HasCandidate = readRes.Value.Candidate != null;
                    model.HasDrift = readRes.Value.EligibilityReason == KasaManager.Application.Services.ReadAdapter.EligibilityReason.NotMatchedParity;
                    model.ParityStatus = model.HasDrift ? "Drift Detected" : 
                                         (readRes.Value.EligibilityReason == KasaManager.Application.Services.ReadAdapter.EligibilityReason.Eligible ? "Exact Match" : "Not Eligible");
                }

                // DB FormulaSet'lerini de alıp DTO'ya yüklemek isteyebiliriz:
                var tempDto = model.ToDto();
                await _orchestrator.HydrateDbFormulaSetsAsync(tempDto, ct);
                model.UpdateFromDto(tempDto);
                
                // UI'ye candidate/read-mode bilgilerini aktar
                ViewData["CandidateEligibility"] = readRes.Value.EligibilityReason.ToString();
                ViewData["ExecutedReadMode"] = readRes.Value.ExecutedMode.ToString();
            }
            else
            {
                // Fail-closed tam fallback
                await ApplyAutoVergiKasaFromDefaultsAsync(model, ct);
                var autoDto = model.ToDto();
                await _orchestrator.LoadPreviewAsync(autoDto, uploadPath, ct);
                await _orchestrator.HydrateDbFormulaSetsAsync(autoDto, ct);
                model.UpdateFromDto(autoDto);
            }

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

    /// <summary>Ortak panel hydration — UstRapor, upload, IBAN.</summary>
    private async Task HydrateCommonAsync(KasaPreviewViewModel model, CancellationToken ct)
    {
        model.UstRaporPanel = await HydrateUstRaporPanelAsync(ct);
        if (model.UstRaporPanel?.Table != null)
        {
            var vezCol = model.UstRaporPanel.VeznedarColumn ?? "VEZNEDAR";
            model.VeznedarOptions = model.UstRaporPanel.Table.Rows
                .Where(r => r.ContainsKey(vezCol))
                .Select(r => r[vezCol]?.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .OrderBy(x => x)
                .ToList();
        }
        else
        {
            model.VeznedarOptions ??= new List<string>();
        }

        model.HasUploadedFiles = ListUploadedFiles().Count > 0;
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
        var model = new KasaPreviewViewModel
        {
            SelectedDate = DateOnly.FromDateTime(DateTime.Today),
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

        // Vergide Biriken: Tüm kasa tipleri için hesaplama ÖNCESİ
        await HydrateVergideBirikenSeedAsync(model, ct);
        
        // Auto-provision: Genel snapshot yoksa otomatik oluştur
        var loadDate = model.SelectedDate ?? DateOnly.FromDateTime(DateTime.Today);
        await TryAutoProvisionGenelSnapshotAsync(loadDate, ct);

        await ApplyAutoVergiKasaFromDefaultsAsync(model, ct);
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

        // Panel persistence & Common Hydration
        await HydrateCommonAsync(model, ct);

        // ─── B6: HesapKontrol Auto-Fill (Sabah + Akşam Kasa) ───
        if (model.KasaType?.Equals("Sabah", StringComparison.OrdinalIgnoreCase) == true
            || model.KasaType?.Equals("Aksam", StringComparison.OrdinalIgnoreCase) == true)
        {
            await TryAutoFillEksikFazlaAsync(model, ct);
        }

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

        // Vergide Biriken: Tüm kasa tipleri için hesaplama ÖNCESİ
        await HydrateVergideBirikenSeedAsync(model, ct);

        // Auto-provision: Genel snapshot yoksa otomatik oluştur
        var calcDate = model.SelectedDate ?? DateOnly.FromDateTime(DateTime.Today);
        await TryAutoProvisionGenelSnapshotAsync(calcDate, ct);

        await ApplyAutoVergiKasaFromDefaultsAsync(model, ct);
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

        // ── 4. Panel + IBAN + Vergide Biriken + Veznedarlar ──
        await HydrateCommonAsync(model, ct);

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
        _log.LogDebug("Calculate entered: KasaType={KasaType}, HasResults={HasResults}", model.KasaType, model.HasResults);
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

        // Vergide Biriken: Tüm kasa tipleri için hesaplama ÖNCESİ
        await HydrateVergideBirikenSeedAsync(model, ct);
        await ApplyAutoVergiKasaFromDefaultsAsync(model, ct);
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

        // Panel persistence & Common Hydration
        await HydrateCommonAsync(model, ct);

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
        // Vergide Biriken: Tüm kasa tipleri için hesaplama ÖNCESİ
        await HydrateVergideBirikenSeedAsync(model, ct);
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveReport(KasaPreviewViewModel model, CancellationToken ct)
    {
        try
        {
            var raporAdi = Request.Form["SaveRaporAdi"].ToString().Trim();
            var raporNot = Request.Form["RptGunlukNot"].ToString().Trim();
            var inputsJson = Request.Form["SaveInputsJson"].ToString();
            var outputsJson = Request.Form["SaveOutputsJson"].ToString();
            var confirmOverwrite = Request.Form["ConfirmOverwrite"].ToString()
                .Equals("true", StringComparison.OrdinalIgnoreCase);

            // ── Banka doğrulama key'lerini OutputsJson'a enjekte et ──
            // Bu key'ler Pool girdisi olduğu için FormulaEngine çıktısına dahil değildir.
            // LoadSnapshot'ta ResultValRaw() Outputs'tan okuyabilsin diye buraya ekliyoruz.
            // NOT: Tüm değerler string formatında tutulur — LoadSnapshot'taki fallback parser
            // (Dictionary<string,string> → decimal.TryParse) bu formatı doğru handle eder.
            // Karma tip (numeric+string) JSON her iki parser'ı da bozar.
            try
            {
                var allOutputs = new Dictionary<string, string>();

                // Mevcut OutputsJson'daki değerleri oku (numeric veya string fark etmez → hepsini string yap)
                if (!string.IsNullOrWhiteSpace(outputsJson) && outputsJson != "{}")
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(outputsJson);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        allOutputs[prop.Name] = prop.Value.ValueKind == System.Text.Json.JsonValueKind.Number
                            ? prop.Value.GetRawText()
                            : prop.Value.GetString() ?? prop.Value.GetRawText();
                    }
                }

                // Form'dan gelen banka doğrulama değerlerini ekle
                var bankaPoolKeys = new[]
                {
                    ("banka_mevduat_tahsilat", "RptBankaMevduatTahsilat"),
                    ("banka_virman_tahsilat",  "RptBankaVirmanTahsilat"),
                    ("banka_mevduat_harc",     "RptBankaMevduatHarc")
                };

                foreach (var (poolKey, formKey) in bankaPoolKeys)
                {
                    var raw = Request.Form[formKey].ToString();
                    if (!string.IsNullOrWhiteSpace(raw) &&
                        decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var val) &&
                        val != 0m)
                    {
                        allOutputs[poolKey] = val.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                    }
                }

                outputsJson = JsonSerializer.Serialize(allOutputs);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Banka doğrulama key enjeksiyonu başarısız — OutputsJson değiştirilmedi");
            }

            var effectiveKasaType = !string.IsNullOrEmpty(model.KasaType) ? model.KasaType : "Aksam";
            var tarih = model.SelectedDate ?? DateOnly.FromDateTime(DateTime.Today);

            // KasaRaporData oluştur ve serialize et
            var kasaRaporData = await BuildKasaRaporDataAsync(model, includeUstRapor: true, ct);
            var kasaRaporDataJson = JsonSerializer.Serialize(kasaRaporData, new JsonSerializerOptions { WriteIndented = false });

            // Auto-generate name if empty
            if (string.IsNullOrWhiteSpace(raporAdi))
                raporAdi = $"{effectiveKasaType} Kasa — {tarih:dd.MM.yyyy}";

            // KasaTuru enum mapping
            var kasaTuruEnum = effectiveKasaType.ToLowerInvariant() switch
            {
                "sabah" => KasaRaporTuru.Sabah,
                "aksam" or "akşam" => KasaRaporTuru.Aksam,
                "genel" => KasaRaporTuru.Genel,
                _ => KasaRaporTuru.Ortak
            };

            // ── Akıllı Kaydetme: Mevcut rapor kontrolü ──
            var existingActive = await _calcSnapshots.GetActiveAsync(tarih, kasaTuruEnum, ct);
            if (existingActive != null && !confirmOverwrite)
            {
                return Json(new
                {
                    ok = false, needsConfirmation = true,
                    message = $"Bu tarihli {effectiveKasaType} Kasa raporu zaten kayıtlı.",
                    existingVersion = existingActive.Version,
                    existingName = existingActive.Name ?? raporAdi,
                    tarih = tarih.ToString("dd.MM.yyyy")
                });
            }

            var snapshot = new CalculatedKasaSnapshot
            {
                RaporTarihi = tarih, KasaTuru = kasaTuruEnum,
                Name = raporAdi, Notes = raporNot,
                CalculatedBy = model.KasayiYapan ?? "Sistem",
                InputsJson = !string.IsNullOrWhiteSpace(inputsJson) ? inputsJson : "{}",
                OutputsJson = !string.IsNullOrWhiteSpace(outputsJson) ? outputsJson : "{}",
                KasaRaporDataJson = kasaRaporDataJson,
                FormulaSetName = model.FormulaRun?.FormulaSetId
            };

            if (!string.IsNullOrEmpty(model.DbFormulaSetId) && Guid.TryParse(model.DbFormulaSetId, out var fsGuid))
                snapshot.FormulaSetId = fsGuid;

            // Faz 3: Snapshot'a Financial Exceptions özet verisi enjekte et
            try
            {
                var istisnalar = await _finansalIstisna.ListByDateAsync(tarih, ct);
                if (istisnalar.Count > 0)
                {
                    var feSummary = FinancialExceptionsSummary.Build(istisnalar);
                    snapshot.FinancialExceptionsSummaryJson = feSummary.ToJson();
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Snapshot'a Financial Exceptions summary eklenemedi");
            }

            await _calcSnapshots.SaveAsync(snapshot, ct);

            // Draft cache temizle — veriler artık DB'de
            try
            {
                var saveUserName = User.Identity?.Name ?? "anonymous";
                await KasaDraftCacheHelper.ClearDraftAsync(saveUserName, effectiveKasaType);
            }
            catch (Exception ex) { _log.LogDebug(ex, "Draft cache temizleme başarısız (rapor kaydı etkilenmedi)"); }

            var isUpdate = existingActive != null;
            var actionWord = isUpdate ? "güncellendi" : "kaydedildi";

            _log.LogInformation("Rapor {Action}: {Name}, Tarih={Tarih}, Tip={Tip}, v{Version}, Id={Id}",
                actionWord, snapshot.Name, snapshot.RaporTarihi, snapshot.KasaTuru, snapshot.Version, snapshot.Id);

            return Json(new
            {
                ok = true,
                message = isUpdate
                    ? $"✅ {tarih:dd.MM.yyyy} tarihli rapor güncellendi → v{snapshot.Version}"
                    : $"✅ Rapor başarıyla kaydedildi: {snapshot.Name} (v{snapshot.Version})",
                redirectUrl = Url.Action("LoadSnapshot", new { id = snapshot.Id }),
                version = snapshot.Version, isUpdate
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Rapor kaydetme hatasi");
            return Json(new { ok = false, needsConfirmation = false, message = $"❌ Rapor kaydedilemedi: {ex.Message}" });
        }
    }

    private Dictionary<string, string> ExtractOutputsForSnapshot(CalculationRun run)
    {
        var dict = new Dictionary<string, string>();
        
        if (run.Outputs != null)
        {
            foreach (var kv in run.Outputs)
            {
                dict[kv.Key] = kv.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        // Ensure CarryoverResolver can find "SonrayaDevredecek"
        if (!dict.ContainsKey("SonrayaDevredecek") && run.Outputs != null)
        {
            // If the formula set uses "genel_kasa", map it to "SonrayaDevredecek" to be safe.
            if (run.Outputs.TryGetValue("genel_kasa", out var gk))
            {
                dict["SonrayaDevredecek"] = gk.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        // Ensure CarryoverResolver can find upper keys too if required
        if (!dict.ContainsKey("GenelKasa") && dict.ContainsKey("genel_kasa"))
        {
            dict["GenelKasa"] = dict["genel_kasa"];
        }

        // ADIM 2A: YENİ STANDART KEY EKLENMESİ (DB YAZMA)
        if (!dict.ContainsKey("sonraki_kasaya_devredecek") && run.Outputs != null)
        {
            if (run.Outputs.TryGetValue("genel_kasa_devir", out var gkd))
                dict["sonraki_kasaya_devredecek"] = gkd.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            else if (run.Outputs.TryGetValue("genel_kasa_toplam", out var gkt))
                dict["sonraki_kasaya_devredecek"] = gkt.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            else if (run.Outputs.TryGetValue("genel_kasa", out var gk2))
                dict["sonraki_kasaya_devredecek"] = gk2.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            else if (run.Outputs.TryGetValue("kasa_toplam", out var kt))
                dict["sonraki_kasaya_devredecek"] = kt.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            else if (run.Outputs.TryGetValue("sabah_kasa_devir", out var skd))
                dict["sonraki_kasaya_devredecek"] = skd.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        }

        return dict;
    }

    // =========================================================================
    // CRUD: Kayıtlı Raporlar — Ara / Yükle / Sil
    // =========================================================================

    /// <summary>
    /// AJAX GET: Kayıtlı raporları JSON olarak döner.
    /// KasaPreview/Index.cshtml "Kayıtlı Raporlar" paneli bu endpoint'i çağırır.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> SearchReports(string? kasaType, string? searchDate, string? search, CancellationToken ct)
    {
        try
        {
            // kasaType → KasaRaporTuru mapping
            KasaRaporTuru? turu = kasaType?.ToLowerInvariant() switch
            {
                "sabah" => KasaRaporTuru.Sabah,
                "aksam" => KasaRaporTuru.Aksam,
                "genel" => KasaRaporTuru.Genel,
                "ortak" => KasaRaporTuru.Ortak,
                _ => null // Tümü
            };

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
                KasaTuru = turu,
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
                id = s.Id,
                name = s.Name ?? $"{s.KasaTuru} — {s.RaporTarihi:dd.MM.yyyy}",
                notes = s.Notes,
                raporTarihi = s.RaporTarihi.ToString("dd.MM.yyyy"),
                kasaTuru = s.KasaTuru.ToString(),
                calculatedBy = s.CalculatedBy,
                calculatedAt = s.CalculatedAtUtc.ToLocalTime().ToString("dd.MM HH:mm"),
                version = s.Version,
                isActive = s.IsActive
            });

            return Json(new { items, totalCount = results.TotalCount });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "KasaPreview SearchReports hatası — kasaType={KasaType}", kasaType);
            return Json(new { items = Array.Empty<object>(), totalCount = 0 });
        }
    }

    /// <summary>
    /// GET: Kayıtlı raporu yükler ve KasaPreview ekranına bind eder.
    /// Kullanıcı listeden "Yükle" tıkladığında çağrılır.
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

        // Snapshot → KasaPreviewViewModel mapping
        var model = BuildBaseModel();
        model.SelectedDate = snapshot.RaporTarihi;
        model.KasaType = snapshot.KasaTuru switch
        {
            KasaRaporTuru.Sabah => "Sabah",
            KasaRaporTuru.Aksam => "Aksam",
            KasaRaporTuru.Genel => "Genel",
            KasaRaporTuru.Ortak => "Ortak",
            _ => "Aksam"
        };

        // Inputs/Outputs deserialize
        Dictionary<string, decimal> inputs = new();
        Dictionary<string, decimal> outputs = new();

        if (!string.IsNullOrWhiteSpace(snapshot.InputsJson))
        {
            try { inputs = JsonSerializer.Deserialize<Dictionary<string, decimal>>(snapshot.InputsJson) ?? new(); }
            catch { /* bad json — devam */ }
        }
        if (!string.IsNullOrWhiteSpace(snapshot.OutputsJson))
        {
            // OutputsJson iki formatta olabilir:
            // 1. Dictionary<string,decimal> → {"key":1234.56}
            // 2. Dictionary<string,string>  → {"key":"1234.56"} (ExtractOutputsForSnapshot)
            // Her ikisini de destekliyoruz:
            try { outputs = JsonSerializer.Deserialize<Dictionary<string, decimal>>(snapshot.OutputsJson) ?? new(); }
            catch
            {
                try
                {
                    var stringDict = JsonSerializer.Deserialize<Dictionary<string, string>>(snapshot.OutputsJson);
                    if (stringDict != null)
                    {
                        foreach (var kv in stringDict)
                        {
                            if (decimal.TryParse(kv.Value, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out var d))
                                outputs[kv.Key] = d;
                        }
                    }
                }
                catch { /* tamamen okunamayan json — boş outputs ile devam */ }
            }
        }

        // CalculationRun oluştur (sonuçlar görünsün)
        model.FormulaRun = new Domain.Calculation.CalculationRun
        {
            FormulaSetId = snapshot.FormulaSetName ?? "Snapshot",
            ReportDate = snapshot.RaporTarihi,
            Inputs = inputs,
            Outputs = outputs
        };

        model.HasResults = true;
        model.IsDataLoaded = true;

        // ══════════════════════════════════════════════════════════════
        // KasaRaporDataJson → ViewModel: Tüm UI alanlarını restore et.
        // Kaydetme anında BuildKasaRaporDataAsync ile toplanan TÜM
        // hesaplanmış değerler buradan geri yüklenir. Yeniden hesaplama
        // yapılmaz — o günkü kaydedilmiş halleri korunur.
        // ══════════════════════════════════════════════════════════════
        if (!string.IsNullOrWhiteSpace(snapshot.KasaRaporDataJson))
        {
            try
            {
                var raporData = JsonSerializer.Deserialize<KasaRaporData>(
                    snapshot.KasaRaporDataJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (raporData != null)
                {
                    // ── Vergi Bilgileri (Kritik: Bu alanlar daha önce restore edilmiyordu) ──
                    model.VergiKasaBakiyeToplam = raporData.VergiKasa;
                    model.VergideBirikenKasa = raporData.VergideBirikenKasa;
                    model.VergidenGelen = raporData.VergidenGelen;
                    model.VergiKasaVeznedarlar = raporData.VergiCalisanlari ?? new();

                    // ── Eksik/Fazla (Sabah Kasa) ──
                    model.GuneAitEksikFazlaTahsilat = raporData.GuneAitEksikFazlaTahsilat;
                    model.GuneAitEksikFazlaHarc = raporData.GuneAitEksikFazlaHarc;
                    model.DundenEksikFazlaTahsilat = raporData.DundenEksikFazlaTahsilat;
                    model.DundenEksikFazlaHarc = raporData.DundenEksikFazlaHarc;
                    model.DundenEksikFazlaGelenTahsilat = raporData.DundenEksikFazlaGelenTahsilat;
                    model.DundenEksikFazlaGelenHarc = raporData.DundenEksikFazlaGelenHarc;

                    // ── Kullanıcı Girişleri ──
                    model.BankadanCekilen = raporData.BankadanCekilen;
                    model.KasadaKalacakHedef = raporData.KasadaKalacakHedef;
                    model.KaydenTahsilat = raporData.KaydenTahsilat;
                    model.KaydenHarc = raporData.KaydenHarc;
                    model.CesitliNedenlerleBankadanCikamayanTahsilat = raporData.CesitliNedenlerleBankadanCikamayanTahsilat;
                    model.BankayaGonderilmisDeger = raporData.BankayaGonderilmisDeger;
                    model.BankayaYatirilacakTahsilatiDegistir = raporData.BankayaYatirilacakTahsilatiDegistir;
                    model.BankayaYatirilacakHarciDegistir = raporData.BankayaYatirilacakHarciDegistir;
                    model.BozukPara = raporData.BozukPara;
                    model.NakitPara = raporData.NakitPara;
                    model.GelmeyenD = raporData.GelmeyenD;

                    // ── Günlük Not ──
                    if (!string.IsNullOrEmpty(raporData.GunlukNot))
                        model.GunlukKasaNotu = raporData.GunlukNot;

                    _log.LogInformation(
                        "LoadSnapshot KasaRaporData restore: VergiKasa={VK:N2}, VergideBiriken={VB:N2}, " +
                        "VergiCalisanlar={VC}, Tarih={T}",
                        raporData.VergiKasa, raporData.VergideBirikenKasa,
                        string.Join(",", raporData.VergiCalisanlari ?? new()),
                        raporData.Tarih);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "LoadSnapshot: KasaRaporDataJson deserialize başarısız — vergi alanları boş kalacak");
            }
        }

        // Loaded Snapshot metadata (Güncelle/Sil butonları için)
        model.LoadedSnapshotId = snapshot.Id;
        model.LoadedSnapshotName = snapshot.Name;
        model.LoadedSnapshotVersion = snapshot.Version;
        model.KasayiYapan = snapshot.CalculatedBy;

        // Ortak hydration (UstRapor panel, IBAN vb.)
        // NOT: VergiKasa alanları artık yukarıda KasaRaporDataJson'dan restore edildi.
        // HydrateCommonAsync bu alanları ezmez — yalnızca UstRaporPanel, IBAN ve
        // FinansalIstisnalar'ı yükler.
        try { await HydrateCommonAsync(model, ct); }
        catch (Exception ex) { _log.LogWarning(ex, "LoadSnapshot: HydrateCommon başarısız"); }

        TempData["SuccessMessage"] = $"✅ Rapor yüklendi: {snapshot.Name} (v{snapshot.Version})";
        return View("Index", model);
    }

    /// <summary>
    /// AJAX POST: Snapshot'ı soft-delete eder.
    /// Hem "Sil" butonu hem paneldeki satır silme bu endpoint'i çağırır.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSnapshot([FromForm] Guid snapshotId, CancellationToken ct)
    {
        try
        {
            var snapshot = await _calcSnapshots.GetByIdAsync(snapshotId, ct);
            if (snapshot is null)
                return Json(new { ok = false, message = "Rapor bulunamadı." });

            var deletedBy = User.Identity?.Name ?? "Sistem";
            await _calcSnapshots.DeleteAsync(snapshotId, deletedBy, ct);

            _log.LogInformation("KasaPreview snapshot silindi: {Name}, ID={Id}", snapshot.Name, snapshotId);
            return Json(new { ok = true, message = $"🗑️ Rapor silindi: {snapshot.Name}" });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "KasaPreview DeleteSnapshot hatası - Id={Id}", snapshotId);
            return Json(new { ok = false, message = $"❌ Silme hatası: {ex.Message}" });
        }
    }
}
