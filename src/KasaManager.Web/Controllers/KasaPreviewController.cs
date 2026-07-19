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
    private readonly ICurrentUser _currentUser;
    private readonly IHesapKontrolSourceResolver _hesapKontrolSourceResolver;
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
    private static readonly string[] DiagnosticTargetKeys =
    [
        "genel_kasa",
        "bankaya_yatirilacak_tahsilat",
        "bankaya_yatirilacak_harc",
        "toplam_tahsilat",
        "toplam_harc",
        "kasa_eksik_fazla",
        "gune_ait_eksik_fazla_tahsilat",
        "gune_ait_eksik_fazla_harc",
        "dunden_eksik_fazla_tahsilat",
        "dunden_eksik_fazla_harc"
    ];

    public KasaPreviewController(
        IKasaOrchestrator orchestrator,
        IWebHostEnvironment env,
        IConfiguration cfg,
        IImportOrchestrator importOrchestrator,
        IKasaReportDateRulesService dateRules,

        IKasaGlobalDefaultsService globalDefaults,
        IBankaHesapKontrolService hesapKontrol,
        ICurrentUser currentUser,
        IHesapKontrolSourceResolver hesapKontrolSourceResolver,
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
        _currentUser = currentUser;
        _hesapKontrolSourceResolver = hesapKontrolSourceResolver;
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

    private bool TryResolveSnapshotActor(
        out int actorUserId,
        out string? actorUsername,
        out bool isAdmin)
    {
        try
        {
            actorUserId = _currentUser.RequireAuthenticatedUserId();
            actorUsername = _currentUser.Username;
            isAdmin = _currentUser.IsInRole("Admin");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            actorUserId = default;
            actorUsername = null;
            isAdmin = false;
            return false;
        }
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

        // ─── R2B-FIX: LoadData ASLA HasResults=true üretmez ───
        // LoadPreviewAsync sadece PoolEntries/Drafts/IsDataLoaded doldurur.
        // HasResults yalnızca Calculate/RunFormulaEngine tarafından set edilir.
        // Bu yüzden LoadData'dan View'e dönmeden önce HasResults kesin false olmalı
        // ve result-render state (PoolEntries, FormulaRun, Drafts) temizlenmelidir.
        // Aksi halde PoolEntries>0 view'de sonuç bloklarını açtırır → NRE → siyah ekran.
        //
        // NOT: Bu temizlik yalnızca render ViewModel'ini etkiler.
        // Fiziksel cache/draft/snapshot silinmez. Hesaplama mantığı değişmez.
        model.HasResults = false; // defensive — LoadData formunda hidden input yok ama garanti

        // Panel persistence & Common Hydration (PoolEntries temizlenmeden ÖNCE çalışmalı)
        await HydrateCommonAsync(model, ct);

        // ─── B6: HesapKontrol Auto-Fill (Sabah + Akşam Kasa) ───
        if (model.KasaType?.Equals("Sabah", StringComparison.OrdinalIgnoreCase) == true
            || model.KasaType?.Equals("Aksam", StringComparison.OrdinalIgnoreCase) == true)
        {
            await TryAutoFillEksikFazlaAsync(model, ct);
        }

        // ─── R2B: Render-safe ViewModel cleanup ───
        // PoolEntries ham veri olarak pool debug panelinde görünür kalabilir ama
        // sonuç hesaplama kartları (resultFields, stopaj, formül) bundan beslenemez.
        // Güvenli çözüm: PoolEntries'i boş liste yap, FormulaRun/Drafts null yap.
        var poolCountBeforeCleanup = model.PoolEntries?.Count ?? 0;
        model.PoolEntries = new List<Application.Abstractions.UnifiedPoolEntry>();
        model.FormulaRun = null;
        model.Drafts = null;

        // Kullanıcı uyarısı: HasData=true ama sonuç üretilemedi
        if (model.IsDataLoaded && poolCountBeforeCleanup > 0)
        {
            TempData["ErrorMessage"] = BuildLoadDataResultWarning(model.KasaType);
        }
        else if (!model.IsDataLoaded)
        {
            var errorSummary = string.Join(" | ", (model.Errors ?? new List<string>()).Take(3));
            if (!string.IsNullOrEmpty(errorSummary))
            {
                TempData["ErrorMessage"] = $"⚠️ Veri yükleme başarısız: {errorSummary}";
            }
        }

        _log.LogInformation(
            "[LOADDATA-RENDER-GUARD] State temizlendi. PoolBeforeCleanup={PoolBefore} " +
            "KasaType={KasaType} ReportDate={ReportDate} AksamMesaiSonuModu={AksamMesaiSonuModu} " +
            "HasData={HasData} HasResults={HasResults} PoolEntries={PoolEntriesCount} " +
            "UstRaporPanelNull={UstRaporPanelNull} ValidationCount={ValidationCount} " +
            "DismissedCodesNull={DismissedCodesNull} ModelStateValid={ModelStateValid}",
            poolCountBeforeCleanup,
            model.KasaType,
            model.SelectedDate,
            model.AksamMesaiSonuModu,
            model.IsDataLoaded,
            model.HasResults,
            model.PoolEntries?.Count ?? 0,
            model.UstRaporPanel == null,
            model.ValidationResults?.Count ?? 0,
            model.DismissedRuleCodes == null,
            ModelState.IsValid);

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
        if (!TryResolveHesapKontrolActor(out var actorUserId))
            return Unauthorized();

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
        var sourceContextBefore = await CaptureKasaDraftSourceContextAsync(
            uploadPath, model.SelectedDate, model.KasaType, ct);

        // ── 2. Veri Yükle (LoadData logic) ──
        var effectiveKasaType = !string.IsNullOrEmpty(model.KasaType) ? model.KasaType : "Aksam";
        await _orchestrator.LoadActiveFormulaSetByScopeAsync(dto, effectiveKasaType, ct);
        await _orchestrator.LoadPreviewAsync(dto, uploadPath, ct);
        await _orchestrator.HydrateDbFormulaSetsAsync(dto, ct);
        model.UpdateFromDto(dto);

        var isAksamTamGunLC = model.KasaType?.Equals("Aksam", StringComparison.OrdinalIgnoreCase) == true
                              && !model.AksamMesaiSonuModu;
        if (isAksamTamGunLC)
        {
            var analizTarihi = model.SelectedDate ?? DateOnly.FromDateTime(DateTime.Now);
            await TryRunHesapKontrolAnalysisAsync(
                model, analizTarihi, uploadPath, nameof(LoadAndCalculate), actorUserId, ct);
        }

        _log.LogDebug(
            "[LOADANDCALC-GATEWAY] Before formula: TakipKasaEtkisiTahsilat={TakipKasaEtkisiTahsilat} TakipKasaEtkisiHarc={TakipKasaEtkisiHarc} TakipKasaEtkisiNet={TakipKasaEtkisiNet}",
            model.TakipKasaEtkisiTahsilat,
            model.TakipKasaEtkisiHarc,
            model.TakipKasaEtkisiNet);

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

        LogValueSourceDiagnostics(model, actionName: "LoadAndCalculate");

        await HydrateValidationAsync(model, ct);

        // ─── Draft Auto-Save ───
        try
        {
            var userNameLC = User.Identity?.Name ?? "anonymous";
            _log.LogInformation("KasaDraft SAVE (LoadAndCalc): User={User}, KasaType={KT}, HasResults={HR}",
                userNameLC, effectiveKasaType, model.HasResults);
            var sourceContext = model.HasResults && model.Errors.Count == 0
                ? await VerifyKasaDraftSourceContextAsync(
                    sourceContextBefore, uploadPath, model.SelectedDate, effectiveKasaType, ct)
                : null;
            await KasaDraftCacheHelper.SaveDraftAsync(
                userNameLC, effectiveKasaType, model, _log, sourceContext);
        }
        catch (Exception ex) { _log.LogError(ex, "KasaDraft SAVE (LoadAndCalc) HATA"); }

        // ─── Render Guard: Razor patlama noktalarını teşhis ───
        _log.LogInformation(
            "[CALCULATE-RENDER-GUARD] KasaType={KasaType} HasResults={HasResults} UstRaporPanelNull={UstRaporPanelNull} " +
            "ValidationCount={ValidationCount} DismissedCodesNull={DismissedCodesNull} DismissedCodesCount={DismissedCodesCount} " +
            "PoolEntries={PoolEntries} SelectedFormulaSet={SelectedFormulaSet}",
            model.KasaType, model.HasResults,
            model.UstRaporPanel == null,
            model.ValidationResults?.Count ?? 0,
            model.DismissedRuleCodes == null, model.DismissedRuleCodes?.Count ?? -1,
            model.PoolEntries?.Count ?? 0,
            model.SelectedFormulaSetId ?? "(yok)");

        return View("Index", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Calculate(KasaPreviewViewModel model, CancellationToken ct)
    {
        if (!TryResolveHesapKontrolActor(out var actorUserId))
            return Unauthorized();

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
        var uploadPath = ResolveUploadFolderAbsolute();
        var sourceContextBefore = await CaptureKasaDraftSourceContextAsync(
            uploadPath, model.SelectedDate, model.KasaType, ct);

        var isAksamTamGun = model.KasaType?.Equals("Aksam", StringComparison.OrdinalIgnoreCase) == true
                            && !model.AksamMesaiSonuModu;
        if (isAksamTamGun)
        {
            var analizTarihi = model.SelectedDate ?? DateOnly.FromDateTime(DateTime.Now);
            await TryRunHesapKontrolAnalysisAsync(
                model, analizTarihi, uploadPath, nameof(Calculate), actorUserId, ct);
        }

        _log.LogDebug(
            "[CALCULATE-GATEWAY] Before formula: TakipKasaEtkisiTahsilat={TakipKasaEtkisiTahsilat} TakipKasaEtkisiHarc={TakipKasaEtkisiHarc} TakipKasaEtkisiNet={TakipKasaEtkisiNet}",
            model.TakipKasaEtkisiTahsilat,
            model.TakipKasaEtkisiHarc,
            model.TakipKasaEtkisiNet);

        var dto = model.ToDto();
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

        LogValueSourceDiagnostics(model, actionName: "Calculate");

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
            var sourceContext = model.HasResults && model.Errors.Count == 0
                ? await VerifyKasaDraftSourceContextAsync(
                    sourceContextBefore, uploadPath, model.SelectedDate, effectiveKasaType, ct)
                : null;
            await KasaDraftCacheHelper.SaveDraftAsync(
                userName, effectiveKasaType, model, _log, sourceContext);
            _log.LogInformation("KasaDraft SAVE tamamlandı: {KasaType}", effectiveKasaType);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "KasaDraft SAVE HATA: {KasaType}", effectiveKasaType);
        }

        // ─── Render Guard: Razor patlama noktalarını teşhis ───
        _log.LogInformation(
            "[CALCULATE-RENDER-GUARD] KasaType={KasaType} HasResults={HasResults} UstRaporPanelNull={UstRaporPanelNull} " +
            "ValidationCount={ValidationCount} DismissedCodesNull={DismissedCodesNull} DismissedCodesCount={DismissedCodesCount} " +
            "PoolEntries={PoolEntries} SelectedFormulaSet={SelectedFormulaSet}",
            model.KasaType, model.HasResults,
            model.UstRaporPanel == null,
            model.ValidationResults?.Count ?? 0,
            model.DismissedRuleCodes == null, model.DismissedRuleCodes?.Count ?? -1,
            model.PoolEntries?.Count ?? 0,
            model.SelectedFormulaSetId ?? "(yok)");

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
        _log.LogDebug(
            "SAVEREPORT-DIAG: Phase=ENTRY ModelLoadedSnapshotId={ModelLoadedSnapshotId} FormLoadedSnapshotId={FormLoadedSnapshotId} RptEfGuneT={RptEfGuneT} RptEfGuneH={RptEfGuneH} Path={Path}",
            model.LoadedSnapshotId,
            Request.Form["LoadedSnapshotId"].ToString(),
            Request.Form["RptEfGuneT"].ToString(),
            Request.Form["RptEfGuneH"].ToString(),
            model.LoadedSnapshotId.HasValue ? "historical" : "live");

        if (!TryResolveSnapshotActor(out var actorUserId, out var actorUsername, out _))
            return Unauthorized();

        try
        {
            var raporAdi = Request.Form["SaveRaporAdi"].ToString().Trim();
            var raporNot = Request.Form["RptGunlukNot"].ToString().Trim();
            var kasayiYapan = !string.IsNullOrWhiteSpace(raporAdi)
                ? raporAdi
                : model.KasayiYapan?.Trim();
            var inputsJson = Request.Form["SaveInputsJson"].ToString();
            var outputsJson = Request.Form["SaveOutputsJson"].ToString();
            var confirmOverwrite = Request.Form["ConfirmOverwrite"].ToString()
                .Equals("true", StringComparison.OrdinalIgnoreCase);

            LogHiddenConsistency(outputsJson);

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
            kasaRaporData.KasayiYapan = kasayiYapan;
            kasaRaporData.Aciklama = model.Aciklama;
            kasaRaporData.GunlukNot = raporNot;

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

            (KasaImmutableAuditData Summary, HesapKontrolImmutableAuditDetails Details)
                immutableAudit;
            var immutableAuditPayloadVersion = 2;
            if (model.LoadedSnapshotId.HasValue)
            {
                var sourceSnapshot = await _calcSnapshots.GetByIdAsync(
                    model.LoadedSnapshotId.Value, ct);
                if (sourceSnapshot is null || sourceSnapshot.IsDeleted)
                {
                    return Json(new
                    {
                        ok = false,
                        needsConfirmation = false,
                        message = "Tarihsel kaynak snapshot bulunamadı veya silinmiş; kayıt güvenlik için yapılmadı."
                    });
                }

                if (sourceSnapshot.RaporTarihi != tarih
                    || sourceSnapshot.KasaTuru != kasaTuruEnum)
                {
                    return Json(new
                    {
                        ok = false,
                        needsConfirmation = false,
                        message = "Tarihsel kaynak snapshot tarih veya kasa zinciriyle eşleşmiyor; kayıt güvenlik için yapılmadı."
                    });
                }

                if (!TryReadHistoricalImmutableAudit(
                        sourceSnapshot.KasaRaporDataJson,
                        out immutableAudit,
                        out immutableAuditPayloadVersion,
                        out var auditError))
                {
                    _log.LogWarning(
                        "[KASA-SNAPSHOT-SAVE] Tarihsel kaynak audit doğrulanamadı. Snapshot={SnapshotId} Date={Date} KasaType={KasaType} Error={Error}",
                        sourceSnapshot.Id, tarih, kasaTuruEnum, auditError);
                    return Json(new
                    {
                        ok = false,
                        needsConfirmation = false,
                        message = "Tarihsel kaynak snapshot V2 audit verisi güvenle doğrulanamadı; kayıt yapılmadı."
                    });
                }
            }
            else
            {
                // Yeni/live save: scalar audit ve kayıt ayrıntıları aynı
                // server-side canonical HK source setlerinden üretilir.
                immutableAudit = await BuildImmutableAuditAsync(tarih, ct);
            }

            kasaRaporData.PayloadVersion = immutableAuditPayloadVersion;
            kasaRaporData.ImmutableAudit = immutableAudit.Summary;
            kasaRaporData.ImmutableAuditDetails = immutableAuditPayloadVersion == 2
                ? JsonSerializer.SerializeToElement(immutableAudit.Details)
                : null;
            kasaRaporData.GuneAitEksikFazlaTahsilat = immutableAudit.Summary.GuneAitEksikFazlaTahsilat;
            kasaRaporData.GuneAitEksikFazlaHarc = immutableAudit.Summary.GuneAitEksikFazlaHarc;
            kasaRaporData.DundenEksikFazlaTahsilat = immutableAudit.Summary.OncekiGunAcikTahsilat;
            kasaRaporData.DundenEksikFazlaHarc = immutableAudit.Summary.OncekiGunAcikHarc;
            kasaRaporData.DundenEksikFazlaGelenTahsilat = immutableAudit.Summary.BugunCozulenTahsilat;
            kasaRaporData.DundenEksikFazlaGelenHarc = immutableAudit.Summary.BugunCozulenHarc;

            var consistentOutputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var outputsDocument = JsonDocument.Parse(outputsJson))
            {
                foreach (var property in outputsDocument.RootElement.EnumerateObject())
                {
                    consistentOutputs[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText();
                }
            }
            consistentOutputs["gune_ait_eksik_fazla_tahsilat"] = immutableAudit.Summary.GuneAitEksikFazlaTahsilat.ToString(System.Globalization.CultureInfo.InvariantCulture);
            consistentOutputs["gune_ait_eksik_fazla_harc"] = immutableAudit.Summary.GuneAitEksikFazlaHarc.ToString(System.Globalization.CultureInfo.InvariantCulture);
            consistentOutputs["dunden_eksik_fazla_tahsilat"] = immutableAudit.Summary.OncekiGunAcikTahsilat.ToString(System.Globalization.CultureInfo.InvariantCulture);
            consistentOutputs["dunden_eksik_fazla_harc"] = immutableAudit.Summary.OncekiGunAcikHarc.ToString(System.Globalization.CultureInfo.InvariantCulture);
            consistentOutputs["dunden_eksik_fazla_gelen_tahsilat"] = immutableAudit.Summary.BugunCozulenTahsilat.ToString(System.Globalization.CultureInfo.InvariantCulture);
            consistentOutputs["dunden_eksik_fazla_gelen_harc"] = immutableAudit.Summary.BugunCozulenHarc.ToString(System.Globalization.CultureInfo.InvariantCulture);
            outputsJson = JsonSerializer.Serialize(consistentOutputs);

            var kasaRaporDataJson = JsonSerializer.Serialize(
                kasaRaporData,
                new JsonSerializerOptions { WriteIndented = false });

            var snapshot = new CalculatedKasaSnapshot
            {
                RaporTarihi = tarih, KasaTuru = kasaTuruEnum,
                Name = raporAdi, Notes = raporNot,
                CalculatedBy = actorUsername ?? "Sistem",
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

            var transientCandidateId = snapshot.Id;
            var persistedSnapshot = await _calcSnapshots.SaveAsync(
                snapshot, actorUserId, actorUsername ?? "Sistem", ct);
            var createdNewVersion = persistedSnapshot.Id == transientCandidateId;
            var isNoOp = !createdNewVersion;
            _log.LogDebug(
                "SAVEREPORT-DIAG: Phase=SAVEASYNC ModelLoadedSnapshotId={ModelLoadedSnapshotId} CandidateId={CandidateId} PersistedId={PersistedId} PersistedVersion={PersistedVersion} Result={Result}",
                model.LoadedSnapshotId,
                transientCandidateId,
                persistedSnapshot.Id,
                persistedSnapshot.Version,
                isNoOp ? "no-op" : "new-version");

            // Draft cache temizle — veriler artık DB'de
            try
            {
                var saveUserName = User.Identity?.Name ?? "anonymous";
                await KasaDraftCacheHelper.ClearDraftAsync(saveUserName, effectiveKasaType);
            }
            catch (Exception ex) { _log.LogDebug(ex, "Draft cache temizleme başarısız (rapor kaydı etkilenmedi)"); }

            var isUpdate = existingActive != null && createdNewVersion;
            var saveOutcome = isNoOp
                ? "mevcut sürüm yeniden kullanıldı"
                : isUpdate ? "yeni sürüm oluşturuldu" : "ilk sürüm oluşturuldu";

            _log.LogInformation(
                "Rapor kaydetme sonucu: {Outcome}, {Name}, Tarih={Tarih}, Tip={Tip}, v{Version}, Id={Id}",
                saveOutcome, persistedSnapshot.Name, persistedSnapshot.RaporTarihi,
                persistedSnapshot.KasaTuru, persistedSnapshot.Version, persistedSnapshot.Id);

            return Json(new
            {
                ok = true,
                message = isNoOp
                    ? $"✅ Finansal değişiklik yok; mevcut v{persistedSnapshot.Version} kullanıldı."
                    : isUpdate
                        ? $"✅ {persistedSnapshot.RaporTarihi:dd.MM.yyyy} tarihli rapor için v{persistedSnapshot.Version} oluşturuldu."
                        : $"✅ Rapor başarıyla kaydedildi: {persistedSnapshot.Name} (v{persistedSnapshot.Version})",
                redirectUrl = Url.Action("LoadSnapshot", new { id = persistedSnapshot.Id }),
                version = persistedSnapshot.Version,
                isUpdate,
                isNoOp,
                createdNewVersion
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
            _log.LogWarning("[SNAPSHOT-RESTORE] Phase=NOT-FOUND SnapshotId={SnapshotId}", id);
            TempData["ErrorMessage"] = "❌ Rapor bulunamadı.";
            return RedirectToAction("Index");
        }
        var restoreParseErrors = 0;
        var restoredRaporFields = 0;
        var restoreMismatches = 0;

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
            catch (Exception ex)
            {
                restoreParseErrors++;
                _log.LogWarning(
                    ex,
                    "[SNAPSHOT-RESTORE] Phase=INPUTS-PARSE-FAILED SnapshotId={SnapshotId} Length={Length}",
                    snapshot.Id,
                    snapshot.InputsJson.Length);
            }
        }
        if (!string.IsNullOrWhiteSpace(snapshot.OutputsJson))
        {
            try
            {
                using var outputsDocument = JsonDocument.Parse(snapshot.OutputsJson);
                foreach (var property in outputsDocument.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Number
                        && property.Value.TryGetDecimal(out var numericValue))
                    {
                        outputs[property.Name] = numericValue;
                    }
                    else if (property.Value.ValueKind == JsonValueKind.String
                             && decimal.TryParse(
                                 property.Value.GetString(),
                                 System.Globalization.NumberStyles.Any,
                                 System.Globalization.CultureInfo.InvariantCulture,
                                 out var stringValue))
                    {
                        outputs[property.Name] = stringValue;
                    }
                }
            }
            catch (Exception ex)
            {
                restoreParseErrors++;
                _log.LogWarning(
                    ex,
                    "[SNAPSHOT-RESTORE] Phase=OUTPUTS-PARSE-FAILED SnapshotId={SnapshotId} Length={Length}",
                    snapshot.Id,
                    snapshot.OutputsJson.Length);
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
        model.HasImmutableAuditData = false;
        model.LoadedAuditPayloadVersion = 0;
        model.ImmutableAuditNotice =
            "Bu eski raporda takip ve fark audit ayrıntıları saklanmamıştır.";
        model.HasImmutableAuditRecordDetails = false;
        model.ImmutableAuditRecordDetailsNotice = null;
        model.ImmutableAuditRecords = Array.Empty<ImmutableAuditRecordViewModel>();
        model.ImmutableAuditRecordGroups = ImmutableAuditRecordGroupsViewModel.Empty;

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
                    (restoredRaporFields, restoreMismatches) =
                        GetSnapshotRestoreSummary(raporData, outputs);
                    // ── Vergi Bilgileri (Kritik: Bu alanlar daha önce restore edilmiyordu) ──
                    model.VergiKasaBakiyeToplam = raporData.VergiKasa;
                    model.VergideBirikenKasa = raporData.VergideBirikenKasa;
                    model.VergidenGelen = raporData.VergidenGelen;
                    model.VergiKasaVeznedarlar = raporData.VergiCalisanlari ?? new();

                    // ── Legacy top-level Eksik/Fazla alanları ──
                    // C1 öncesi snapshot'larda bu değerler KasaRaporData'nın
                    // doğrudan alanlarıydı. Version 1'de aşağıdaki immutable
                    // audit mapping'i bunları kaydetme anındaki değerlerle ezer.
                    model.GuneAitEksikFazlaTahsilat = raporData.GuneAitEksikFazlaTahsilat;
                    model.GuneAitEksikFazlaHarc = raporData.GuneAitEksikFazlaHarc;
                    model.DundenEksikFazlaTahsilat = raporData.DundenEksikFazlaTahsilat;
                    model.DundenEksikFazlaHarc = raporData.DundenEksikFazlaHarc;
                    model.DundenEksikFazlaGelenTahsilat = raporData.DundenEksikFazlaGelenTahsilat;
                    model.DundenEksikFazlaGelenHarc = raporData.DundenEksikFazlaGelenHarc;

                    model.LoadedAuditPayloadVersion = raporData.PayloadVersion;
                    if (raporData.PayloadVersion == 1)
                    {
                        if (IsValidImmutableAuditSummary(raporData.ImmutableAudit))
                        {
                            ApplyImmutableAuditSummary(model, raporData.ImmutableAudit!);
                            model.HasImmutableAuditData = true;
                            model.ImmutableAuditNotice = null;
                            model.ImmutableAuditRecordDetailsNotice =
                                "Bu snapshot scalar audit içeriyor; kayıt ayrıntıları bu sürümde bulunmuyor.";
                        }
                        else
                        {
                            model.ImmutableAuditNotice =
                                "Kaydedilmiş audit payload'ı eksik veya okunamadı.";
                        }
                    }
                    else if (raporData.PayloadVersion == 2)
                    {
                        if (IsValidImmutableAuditSummary(raporData.ImmutableAudit))
                        {
                            ApplyImmutableAuditSummary(model, raporData.ImmutableAudit!);
                            model.HasImmutableAuditData = true;
                            model.ImmutableAuditNotice = null;

                            if (!TryApplyImmutableAuditDetails(
                                    model,
                                    raporData.ImmutableAuditDetails,
                                    raporData.ImmutableAudit!))
                            {
                                model.HasImmutableAuditRecordDetails = false;
                                model.ImmutableAuditRecordDetailsNotice =
                                    "Kayıt ayrıntıları bozuk veya doğrulanamadı.";
                            }
                        }
                        else
                        {
                            model.HasImmutableAuditData = false;
                            model.HasImmutableAuditRecordDetails = false;
                            model.ImmutableAuditNotice =
                                "Kaydedilmiş audit payload'ı eksik veya okunamadı.";
                            model.ImmutableAuditRecordDetailsNotice = null;
                        }
                    }
                    else if (raporData.PayloadVersion > 2)
                    {
                        model.ImmutableAuditNotice =
                            "Bu rapor daha yeni bir audit payload sürümü kullanıyor.";
                    }

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
                    model.KasayiYapan = raporData.KasayiYapan;
                    model.Aciklama = raporData.Aciklama;
                    model.MuhabereNo = raporData.MuhabereNo;

                    // ── Günlük Not ──
                    if (!string.IsNullOrEmpty(raporData.GunlukNot))
                        model.GunlukKasaNotu = raporData.GunlukNot;

                }
            }
            catch (Exception ex)
            {
                restoreParseErrors++;
                _log.LogWarning(ex, "LoadSnapshot: KasaRaporDataJson deserialize başarısız — vergi alanları boş kalacak");
                model.HasImmutableAuditData = false;
                model.LoadedAuditPayloadVersion = 0;
                model.ImmutableAuditNotice =
                    "Kaydedilmiş audit payload'ı eksik veya okunamadı.";
                model.HasImmutableAuditRecordDetails = false;
                model.ImmutableAuditRecordDetailsNotice = null;
                model.ImmutableAuditRecords = Array.Empty<ImmutableAuditRecordViewModel>();
                model.ImmutableAuditRecordGroups = ImmutableAuditRecordGroupsViewModel.Empty;
            }
        }

        // Loaded Snapshot metadata (Güncelle/Sil butonları için)
        model.LoadedSnapshotId = snapshot.Id;
        model.LoadedSnapshotName = snapshot.Name;
        model.LoadedSnapshotVersion = snapshot.Version;
        if (string.IsNullOrWhiteSpace(model.KasayiYapan))
            model.KasayiYapan = snapshot.CalculatedBy;

        // Ortak hydration (UstRapor panel, IBAN vb.)
        // NOT: VergiKasa alanları artık yukarıda KasaRaporDataJson'dan restore edildi.
        // HydrateCommonAsync bu alanları ezmez — yalnızca UstRaporPanel, IBAN ve
        // FinansalIstisnalar'ı yükler.
        try { await HydrateCommonAsync(model, ct); }
        catch (Exception ex) { _log.LogWarning(ex, "LoadSnapshot: HydrateCommon başarısız"); }

        _log.LogDebug(
            "[SNAPSHOT-RESTORE] SnapshotId={SnapshotId} Version={Version} Inputs={Inputs} Outputs={Outputs} RaporFields={RaporFields} Mismatches={Mismatches} ParseErrors={ParseErrors}",
            snapshot.Id,
            snapshot.Version,
            inputs.Count,
            outputs.Count,
            restoredRaporFields,
            restoreMismatches,
            restoreParseErrors);

        TempData["SuccessMessage"] = $"✅ Rapor yüklendi: {snapshot.Name} (v{snapshot.Version})";
        return View("Index", model);
    }

    /// <summary>
    /// AJAX POST: Snapshot'ı soft-delete eder.
    /// Hem "Sil" butonu hem paneldeki satır silme bu endpoint'i çağırır.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteSnapshot([FromForm] Guid snapshotId, CancellationToken ct)
    {
        if (!TryResolveSnapshotActor(out var actorUserId, out var actorUsername, out var isAdmin))
            return Unauthorized();
        if (!isAdmin)
            return Forbid();

        try
        {
            var snapshot = await _calcSnapshots.GetByIdAsync(snapshotId, ct);
            if (snapshot is null)
                return Json(new { ok = false, message = "Rapor bulunamadı." });

            var result = await _calcSnapshots.DeleteAsync(
                snapshotId, actorUserId, isAdmin, actorUsername ?? "Sistem", ct);
            if (result == SnapshotMutationResult.Forbidden)
                return Forbid();
            if (result == SnapshotMutationResult.NotFound)
                return Json(new { ok = false, message = "Rapor bulunamadı." });

            _log.LogInformation("KasaPreview snapshot silindi: {Name}, ID={Id}", snapshot.Name, snapshotId);
            return Json(new { ok = true, message = $"🗑️ Rapor silindi: {snapshot.Name}" });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "KasaPreview DeleteSnapshot hatası - Id={Id}", snapshotId);
            return Json(new { ok = false, message = $"❌ Silme hatası: {ex.Message}" });
        }
    }

    private void LogValueSourceDiagnostics(KasaPreviewViewModel model, string actionName)
    {
        var snapshotSource = model.LoadedSnapshotId.HasValue;
        var formulaCount = 0;
        var poolCount = 0;
        var modelCount = 0;
        var draftCount = 0;
        var autoFillDiffCount = 0;
        var winnerCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in DiagnosticTargetKeys)
        {
            var formulaExists = TryGetFormulaValue(model, key, out var formulaValue);
            var poolExists = TryGetPoolValue(model, key, out var poolValue);
            var modelExists = TryGetModelValue(model, key, out var modelValue);
            var draftExists = TryGetDraftValue(model, key, out var draftValue);

            if (formulaExists) formulaCount++;
            if (poolExists) poolCount++;
            if (modelExists) modelCount++;
            if (draftExists) draftCount++;

            var hasAutoFillSignal = key is "gune_ait_eksik_fazla_tahsilat"
                or "gune_ait_eksik_fazla_harc"
                or "dunden_eksik_fazla_tahsilat"
                or "dunden_eksik_fazla_harc";

            var autoFillLikelyChanged = hasAutoFillSignal && modelExists && poolExists && modelValue != poolValue;
            var finalDisplayed = ResolveFinalDisplayedCandidate(model, key, formulaExists, formulaValue, poolExists, poolValue, modelExists, modelValue, draftExists, draftValue);
            var winner = EstimateWinnerSource(model, key, formulaExists, poolExists, modelExists, draftExists);
            if (autoFillLikelyChanged) autoFillDiffCount++;
            winnerCounts[winner] = winnerCounts.GetValueOrDefault(winner) + 1;

            if (actionName == "Calculate")
            {
                if (key == "gune_ait_eksik_fazla_tahsilat")
                {
                    model.GuneAitEksikFazlaTahsilat = finalDisplayed;
                    ModelState.Remove(nameof(KasaPreviewViewModel.GuneAitEksikFazlaTahsilat));
                }
                else if (key == "gune_ait_eksik_fazla_harc")
                {
                    model.GuneAitEksikFazlaHarc = finalDisplayed;
                    ModelState.Remove(nameof(KasaPreviewViewModel.GuneAitEksikFazlaHarc));
                }
                else if (key == "dunden_eksik_fazla_tahsilat")
                {
                    model.DundenEksikFazlaTahsilat = finalDisplayed;
                    ModelState.Remove(nameof(KasaPreviewViewModel.DundenEksikFazlaTahsilat));
                }
                else if (key == "dunden_eksik_fazla_harc")
                {
                    model.DundenEksikFazlaHarc = finalDisplayed;
                    ModelState.Remove(nameof(KasaPreviewViewModel.DundenEksikFazlaHarc));
                }
            }

        }

        _log.LogDebug(
            "[VALUE-SOURCE] Action={Action} Fields={Fields} Formula={Formula} Pool={Pool} Model={Model} Draft={Draft} AutoFillDiffs={AutoFillDiffs} SnapshotSource={SnapshotSource} Winners={Winners}",
            actionName,
            DiagnosticTargetKeys.Length,
            formulaCount,
            poolCount,
            modelCount,
            draftCount,
            autoFillDiffCount,
            snapshotSource,
            string.Join(",", winnerCounts.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}")));
    }

    private static decimal ResolveFinalDisplayedCandidate(
        KasaPreviewViewModel model,
        string key,
        bool formulaExists,
        decimal formulaValue,
        bool poolExists,
        decimal poolValue,
        bool modelExists,
        decimal modelValue,
        bool draftExists,
        decimal draftValue)
    {
        if (formulaExists) return formulaValue;
        if (poolExists) return poolValue;
        if (modelExists) return modelValue;
        if (draftExists) return draftValue;
        return 0m;
    }

    private static string EstimateWinnerSource(
        KasaPreviewViewModel model,
        string key,
        bool formulaExists,
        bool poolExists,
        bool modelExists,
        bool draftExists)
    {
        if (formulaExists) return "FormulaOutput";
        if (poolExists) return "Pool";
        if (modelExists) return "Model";
        if (draftExists) return "Draft";
        return "Unknown/Default";
    }

    private static bool TryGetFormulaValue(KasaPreviewViewModel model, string key, out decimal value)
    {
        value = 0m;
        if (model.FormulaRun?.Outputs == null) return false;
        return model.FormulaRun.Outputs.TryGetValue(key, out value);
    }

    private static bool TryGetPoolValue(KasaPreviewViewModel model, string key, out decimal value)
    {
        value = 0m;
        var item = model.PoolEntries?.FirstOrDefault(x =>
            string.Equals(x.CanonicalKey, key, StringComparison.OrdinalIgnoreCase));
        if (item == null) return false;
        return decimal.TryParse(item.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetDraftValue(KasaPreviewViewModel model, string key, out decimal value)
    {
        value = 0m;
        if (model.Drafts == null) return false;
        var scope = (model.KasaType ?? "Aksam").Trim().ToLowerInvariant();
        var draft = scope switch
        {
            "sabah" => model.Drafts.Sabah,
            "genel" => model.Drafts.Genel,
            _ => model.Drafts.Aksam
        };

        if (!draft.Fields.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetModelValue(KasaPreviewViewModel model, string key, out decimal value)
    {
        value = 0m;
        decimal? nullable = key switch
        {
            "gune_ait_eksik_fazla_tahsilat" => model.GuneAitEksikFazlaTahsilat,
            "gune_ait_eksik_fazla_harc" => model.GuneAitEksikFazlaHarc,
            "dunden_eksik_fazla_tahsilat" => model.DundenEksikFazlaTahsilat,
            "dunden_eksik_fazla_harc" => model.DundenEksikFazlaHarc,
            _ => null
        };

        if (!nullable.HasValue) return false;
        value = nullable.Value;
        return true;
    }

    private static (int RestoredFields, int MismatchCount) GetSnapshotRestoreSummary(
        KasaRaporData raporData,
        Dictionary<string, decimal> outputs)
    {
        var raporValues = new Dictionary<string, decimal>
        {
            ["genel_kasa"] = raporData.GenelKasa,
            ["bankaya_yatirilacak_tahsilat"] = raporData.BankayaTahsilat,
            ["bankaya_yatirilacak_harc"] = raporData.BankayaHarc,
            ["gune_ait_eksik_fazla_tahsilat"] = raporData.GuneAitEksikFazlaTahsilat,
            ["gune_ait_eksik_fazla_harc"] = raporData.GuneAitEksikFazlaHarc,
            ["dunden_eksik_fazla_tahsilat"] = raporData.DundenEksikFazlaTahsilat,
            ["dunden_eksik_fazla_harc"] = raporData.DundenEksikFazlaHarc
        };

        var mismatchCount = raporValues.Count(pair =>
            outputs.TryGetValue(pair.Key, out var outputValue) && outputValue != pair.Value);
        return (raporValues.Count, mismatchCount);
    }

    private void LogHiddenConsistency(string outputsJson)
    {
        var outputMap = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var parseErrors = 0;
        if (!string.IsNullOrWhiteSpace(outputsJson) && outputsJson != "{}")
        {
            try
            {
                using var doc = JsonDocument.Parse(outputsJson);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                    {
                        if (prop.Value.TryGetDecimal(out var num))
                            outputMap[prop.Name] = num;
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.String
                             && decimal.TryParse(prop.Value.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    {
                        outputMap[prop.Name] = parsed;
                    }
                }
            }
            catch (Exception ex)
            {
                parseErrors++;
                _log.LogWarning(ex, "[HIDDEN-CONSISTENCY] SaveOutputsJson parse başarısız");
            }
        }

        var hiddenMap = new Dictionary<string, string>
        {
            ["genel_kasa"] = "RptGenelKasa",
            ["bankaya_yatirilacak_tahsilat"] = "RptBankayaTahsilat",
            ["bankaya_yatirilacak_harc"] = "RptBankayaHarc",
            ["gune_ait_eksik_fazla_tahsilat"] = "RptEfGuneT",
            ["gune_ait_eksik_fazla_harc"] = "RptEfGuneH",
            ["dunden_eksik_fazla_tahsilat"] = "RptEfDundenT",
            ["dunden_eksik_fazla_harc"] = "RptEfDundenH"
        };

        var comparedCount = 0;
        var mismatchCount = 0;
        foreach (var key in DiagnosticTargetKeys)
        {
            outputMap.TryGetValue(key, out var outputValue);
            decimal hiddenValue = 0m;
            var hasHidden = hiddenMap.TryGetValue(key, out var hiddenName)
                            && decimal.TryParse(Request.Form[hiddenName], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out hiddenValue);

            if (outputMap.ContainsKey(key) && hasHidden && outputValue != hiddenValue)
                mismatchCount++;
            if (outputMap.ContainsKey(key) && hasHidden)
                comparedCount++;
        }

        _log.LogDebug(
            "[HIDDEN-CONSISTENCY] Fields={Fields} Compared={Compared} Mismatches={Mismatches} ParseErrors={ParseErrors}",
            DiagnosticTargetKeys.Length,
            comparedCount,
            mismatchCount,
            parseErrors);
    }
}
