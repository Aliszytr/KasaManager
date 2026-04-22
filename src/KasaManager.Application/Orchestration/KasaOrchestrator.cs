using System.Globalization;
using System.Text.Json;
using System.Text.Encodings.Web;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Orchestration.Dtos;
using KasaManager.Application.Orchestration.Helpers;
using KasaManager.Application.Pipeline;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Calculation;
using KasaManager.Domain.Constants;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.FormulaEngine.Authoring;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;
using Microsoft.Extensions.Logging;

namespace KasaManager.Application.Orchestration;


public partial class KasaOrchestrator : IKasaOrchestrator
{
    private readonly IKasaDraftService _drafts;
    private readonly IFormulaEngineService _formulaEngine;
    private readonly IKasaRaporSnapshotService _snapshots;
    private readonly IKasaGlobalDefaultsService _globalDefaults;
    private readonly IFormulaSetStore _formulaSetStore;
    private readonly IDataPipeline _dataPipeline;
    private readonly ILogger<KasaOrchestrator> _logger;
    private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory _scopeFactory;

    public KasaOrchestrator(
        IKasaDraftService drafts,
        IFormulaEngineService formulaEngine,
        IKasaRaporSnapshotService snapshots,
        IKasaGlobalDefaultsService globalDefaults,
        IFormulaSetStore formulaSetStore,
        IDataPipeline dataPipeline,
        ILogger<KasaOrchestrator> logger,
        Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory)
    {
        _drafts = drafts;
        _formulaEngine = formulaEngine;
        _snapshots = snapshots;
        _globalDefaults = globalDefaults;
        _formulaSetStore = formulaSetStore;
        _dataPipeline = dataPipeline;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    // =========================================================================
    // Public API
    // =========================================================================

    public async Task LoadPreviewAsync(KasaPreviewDto dto, string uploadBasePath, CancellationToken ct)
    {
        var date = dto.SelectedDate ?? DateOnly.FromDateTime(DateTime.Today);
        dto.SelectedDate = date;

        if (!await HydrateFromSnapshotAndDefaultsInternalAsync(dto, date, ct))
            return;

        dto.IsDataLoaded = true;

        await HydrateGenelKasaDateRangeAsync(dto, date, ct);
        await HydrateDbFormulaSetsAsync(dto, ct);

        // Build Inputs for Draft
        var finalize = BuildFinalizeInputs(dto);

        // Scope Logic (aynı mantık RunFormulaEnginePreviewAsync ile)
        var scopeHint = (dto.DbScopeType ?? "Custom").Trim();
        var kasaTypeHint = (dto.KasaType ?? "").Trim();
        var useRange = scopeHint.Equals("GenelKasa", StringComparison.OrdinalIgnoreCase)
            || scopeHint.Equals("Genel", StringComparison.OrdinalIgnoreCase)
            || scopeHint.IndexOf("genel", StringComparison.OrdinalIgnoreCase) >= 0
            || kasaTypeHint.Equals("Genel", StringComparison.OrdinalIgnoreCase);

        // Akşam Kasa Mesai Sonu modu: sadece Akşam tipinde ve kullanıcı seçtiyse aktif
        var effectiveMesaiSonu = kasaTypeHint.Equals("Aksam", StringComparison.OrdinalIgnoreCase) && dto.AksamMesaiSonuModu;

        // 1. Build UnifiedPool (date range aware)
        // FIX: kasaScope must be null for LoadPreview — SlimPool filtering (R25) must NOT run here.
        // When kasaScope="Aksam"/"Sabah", FieldCatalog.GetRequiredKeysFor() stripped essential pool entries
        // (e.g. devreden_kasa, vergi_kasa, yt_tahsilat_degistir_*) causing FormulaEngine wrong results.
        // Genel Kasa is unaffected (uses separate SSOT path inside BuildUnifiedPoolAsync).
        var dailyPoolRes = useRange
            ? await _drafts.BuildUnifiedPoolAsync(date, uploadBasePath, finalize, dto.GenelKasaStartDate, dto.GenelKasaEndDate, false, scopeHint, effectiveMesaiSonu, true, ct)
            : await _drafts.BuildUnifiedPoolAsync(date, uploadBasePath, finalize, rangeStart: null, rangeEnd: null, fullExcelTotals: false, kasaScope: scopeHint, mesaiSonuModu: effectiveMesaiSonu, skipSlimPoolFilter: true, ct: ct);
        if (!dailyPoolRes.Ok || dailyPoolRes.Value is null)
        {
            dto.Errors.Add(dailyPoolRes.Error ?? "UnifiedPool (günlük) üretilemedi.");
            return;
        }

        dto.PoolEntries = dailyPoolRes.Value.ToList();

        // Commit 5: İptal edilen işlem toplamını ComparisonService'ten çek (stopaj fark düzeltmesi)
        await HydrateIptalEdilenCikisTutarAsync(dto, uploadBasePath, date, ct);

         // Append UI overrides (manual fields) to Pool for visibility
        AppendUiOnlyOverridePoolEntries(dto);

        dto.InputCatalog = BuildInputCatalog(dto.PoolEntries);

        var bundleRes = await _drafts.BuildAsync(date, uploadBasePath, finalize, ct);
        if (bundleRes.Ok)
        {
            dto.Drafts = bundleRes.Value;
        }
        else
        {
            dto.Errors.Add(bundleRes.Error ?? "Draft Bundle üretilemedi.");
        }
    }

    // REMOVED: CalculatePreviewAsync — eski legacy hesaplama akışı (prefix'li key'ler kullanıyordu)
    // Tüm hesaplamalar artık RunFormulaEnginePreviewAsync üzerinden yapılır.


    public async Task RunFormulaEnginePreviewAsync(KasaPreviewDto dto, string uploadBasePath, CancellationToken ct)
    {
        var date = dto.SelectedDate ?? DateOnly.FromDateTime(DateTime.Today);
        dto.SelectedDate = date;

        await HydrateFromSnapshotAndDefaultsInternalAsync(dto, date, ct);
        await HydrateGenelKasaDateRangeAsync(dto, date, ct);

        var finalize = BuildFinalizeInputs(dto);

        // Scope Logic
        var scopeHint = KasaScopeTypes.Normalize(dto.DbScopeType);
        var kasaTypeHint = KasaScopeTypes.Normalize(dto.KasaType);
        var useRange = scopeHint == KasaScopeTypes.Genel
            || kasaTypeHint == KasaScopeTypes.Genel;

        var effectiveMesaiSonu = kasaTypeHint == KasaScopeTypes.Aksam && dto.AksamMesaiSonuModu;

        // FIX: kasaScope=null — same as LoadPreviewAsync fix. SlimPool filtering must not run
        // for Aksam/Sabah, otherwise essential pool entries get stripped before FormulaEngine.
        // WE NOW PASS skipSlimPoolFilter = true to bypass filtering while still allowing scope detection for Genel Kasa SSOT!
        var poolRes = useRange
           ? await _drafts.BuildUnifiedPoolAsync(date, uploadBasePath, finalize, dto.GenelKasaStartDate, dto.GenelKasaEndDate, false, scopeHint, effectiveMesaiSonu, true, ct)
           : await _drafts.BuildUnifiedPoolAsync(date, uploadBasePath, finalize, rangeStart: null, rangeEnd: null, fullExcelTotals: false, kasaScope: scopeHint, mesaiSonuModu: effectiveMesaiSonu, skipSlimPoolFilter: true, ct: ct);

        if (!poolRes.Ok || poolRes.Value is null)
        {
            dto.Errors.Add(poolRes.Error ?? "UnifiedPool üretilemedi.");
            return;
        }

        dto.IsDataLoaded = true;
        dto.PoolEntries = poolRes.Value.ToList();

        // Commit 5: İptal edilen işlem toplamını ComparisonService'ten çek (stopaj fark düzeltmesi)
        await HydrateIptalEdilenCikisTutarAsync(dto, uploadBasePath, date, ct);

        AppendUiOnlyOverridePoolEntries(dto);
        EnsureSelectedKeysInPool(dto);
        dto.InputCatalog = BuildInputCatalog(dto.PoolEntries);

        // 2) Load/Build UI Set
        var uiSet = BuildUiFormulaSetFromMappings(dto);

        // 3) Validate Selection
        var selCheck = ValidateSelection(dto, poolRes.Value, uiSet);
        if (!selCheck.ok)
        {
            dto.Errors.Add(selCheck.error ?? "Selection invalid");
            return;
        }

        // 4) Overrides
        var overrides = BuildOverridesForFormulaEngine(dto); // Returns Dictionary
        // FIX: Do NOT auto-promote all pool entries to overrides. 
        // The Engine already reads pool entries as Inputs. Overrides are only for manual user intervention (via BuildOverridesForFormulaEngine).
        /* 
        foreach(var p in poolRes.Value) ... removed 
        */

        // 5) Run
        // FIX: Must use dto.PoolEntries because we appended 'AppendedUiOnlyOverridePoolEntries' to it, possibly including 'genel_kasa_devreden_seed'.
        // 'poolRes.Value' contains only the raw daily pool from draft service.
        var runRes = _formulaEngine.Run(date, uiSet, dto.PoolEntries, overrides: overrides);
        if (!runRes.Ok || runRes.Value is null)
        {
            dto.Errors.Add(runRes.Error ?? "FormulaEngine run failed");
            return;
        }

        dto.FormulaRun = runRes.Value;
        
         if (dto.Drafts == null)
        {
             var dBundle = await _drafts.BuildAsync(date, uploadBasePath, finalize, ct);
             if(dBundle.Ok) dto.Drafts = dBundle.Value;
        }
        if (dto.Drafts != null)
        {
            dto.ParityDiffs = BuildParityDiffs(dto.Drafts, dto.FormulaRun);
        }

        // FAZ 2: Shadow Parity Check (Fire and Forget)
        // Kullanıcı arayüzünü geciktirmeden arka planda legacy vs data-first sonuçlarını DB'ye kaydet.
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var parity = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<KasaManager.Application.Services.DataFirst.IParityCheckService>(scope.ServiceProvider);

                // FAZ 12 FIX: HTTP request CT kullanma — request bittiğinde token iptal edilir
                // ve shadow check yarıda kalır. CancellationToken.None ile tamamlanması garanti.
                await parity.RunShadowCheckAsync(date, scopeHint, dto.PoolEntries, dto.FormulaRun!, uiSet, overrides, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Shadow Parity Check Trigger failed for {Date}", date);
            }
        });
    }

    /// <summary>
    /// Legacy FormulaSet V1 — Intent-First yaklaşımı ile bu akış devre dışı kaldı.
    /// Geriye dönük uyumluluk için korunuyor, boş şablon yükler.
    /// </summary>
    public Task LoadFormulaSetV1Async(KasaPreviewDto dto, CancellationToken ct)
    {
        dto.Mappings = new List<KasaPreviewMappingRow>();
        dto.SelectedInputKeys = new List<string>();
        dto.Warnings.Add("R16: FormulaSet v1 loaded (empty — legacy mode).");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Legacy Akşam Contract — Intent-First yaklaşımı ile bu akış devre dışı kaldı.
    /// Geriye dönük uyumluluk için korunuyor, boş şablon yükler.
    /// </summary>
    public Task LoadAksamContractAsync(KasaPreviewDto dto, CancellationToken ct)
    {
        dto.Mappings = new List<KasaPreviewMappingRow>();
        dto.SelectedInputKeys = new List<string>();
        dto.Warnings.Add("R5.1: Aksam Contract loaded (empty — legacy mode).");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Commit 5: ComparisonService'ten iptal edilen işlem toplamını çeker.
    /// Başarısız olursa (dosya eksik, format hatası vb.) IptalEdilenCikisTutar = 0 kalır.
    /// Kasa hesaplaması kırılmaz — sadece warning loglanır.
    /// </summary>
    private async Task HydrateIptalEdilenCikisTutarAsync(
        KasaPreviewDto dto, string uploadBasePath, DateOnly date, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var comparisonService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<IComparisonService>(scope.ServiceProvider);

            var reddiyatResult = await comparisonService.CompareReddiyatCikisAsync(uploadBasePath, date, ct);
            if (reddiyatResult.Ok && reddiyatResult.Value != null)
            {
                dto.IptalEdilenCikisTutar = reddiyatResult.Value.CancelledRecordsTotal;

                // Commit 5.1: Sadece Virman türündeki iptaller — banka_virman_tahsilat düzeltmesi
                var report = reddiyatResult.Value;
                if (report.CancelledPairs?.Count > 0)
                {
                    dto.IptalEdilenVirmanTutar = report.CancelledPairs
                        .Where(p => string.Equals(p.Tur, "Virman", StringComparison.OrdinalIgnoreCase))
                        .Sum(p => p.Tutar);
                }

                if (dto.IptalEdilenCikisTutar > 0)
                {
                    _logger.LogInformation(
                        "Commit 5: {Date} için {Tutar:N2} ₺ iptal edilen işlem tespit edildi (Virman: {VirmanTutar:N2} ₺). Stopaj fark hesabından düşülecek.",
                        date, dto.IptalEdilenCikisTutar, dto.IptalEdilenVirmanTutar);
                }
            }
            else
            {
                _logger.LogWarning(
                    "Commit 5: {Date} için Reddiyat karşılaştırma başarısız: {Error}. IptalEdilenCikisTutar = 0 kalacak.",
                    date, reddiyatResult.Error ?? "Bilinmeyen hata");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Commit 5: {Date} için iptal edilen işlem toplamı çekilemedi. IptalEdilenCikisTutar = 0 kalacak.",
                date);
        }
    }

    /// <summary>
    /// Intent-First: Dashboard'dan gelen kasaType'a göre aktif FormulaSet'i DB'den yükler.
    /// DB'de bulunamazsa embedded fallback şablonunu kullanır.
    /// </summary>
    public async Task LoadActiveFormulaSetByScopeAsync(KasaPreviewDto dto, string scopeType, CancellationToken ct)
    {
        dto.KasaType = scopeType;

        try
        {
            var sets = await _formulaSetStore.ListAsync(ct);
            var persistedSets = sets.ToList();

            // STRICT: Sadece tam eşleşen ScopeType aranır.
            // Eski broad Contains araması kaldırıldı — "Aksam" araması "AksamKasa" scope'unu
            // yanlışlıkla yüklüyordu ve prefix'li key'ler (AksamKasa.BankaTahsilat) ile
            // kanonik key'ler (banka_tahsilat) eşleşmiyordu.
            var activeSet = persistedSets.FirstOrDefault(s =>
                s.IsActive && s.ScopeType.Equals(scopeType, StringComparison.OrdinalIgnoreCase));

            if (activeSet != null)
            {
                // ListAsync Lines include etmez (performans). GetAsync ile tekrar yükle.
                var fullSet = await _formulaSetStore.GetAsync(activeSet.Id, ct);
                if (fullSet == null)
                {
                    dto.Warnings.Add($"DB'den '{activeSet.Name}' şablonu bulunamadı, embedded fallback kullanılıyor.");
                    LoadEmbeddedFallbackTemplate(dto, scopeType);
                    return;
                }

                // DB'den yükle
                dto.DbFormulaSetId = fullSet.Id.ToString();
                dto.DbFormulaSetName = fullSet.Name;
                dto.DbScopeType = fullSet.ScopeType ?? "Custom";

                try
                {
                    dto.SelectedInputKeys = System.Text.Json.JsonSerializer.Deserialize<List<string>>(fullSet.SelectedInputsJson ?? "[]") ?? new();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SelectedInputsJson parse edilemedi (SetId={SetId}), boş liste kullanılıyor", fullSet.Id);
                    dto.SelectedInputKeys = new();
                }

                dto.Mappings = (fullSet.Lines ?? new List<PersistedFormulaLine>())
                    .OrderBy(x => x.SortOrder)
                    .Select(x => new KasaPreviewMappingRow
                    {
                        RowId = x.Id.ToString(),
                        TargetKey = x.TargetKey,
                        Mode = x.Mode,
                        SourceKey = x.SourceKey,
                        Expression = x.Expression,
                        IsHidden = x.IsHidden
                    }).ToList();

                dto.Warnings.Add($"Intent-First: '{fullSet.Name}' ({fullSet.ScopeType}) şablonu DB'den yüklendi ({dto.Mappings.Count} satır).");

                // Genel scope: temel mutabakat formülleri eksikse otomatik ekle
                if (scopeType.Equals(KasaScopeTypes.Genel, StringComparison.OrdinalIgnoreCase))
                {
                    InjectEssentialGenelFormulas(dto);
                }
            }
            else
            {
                // Fallback: Embedded default şablonlardan yükle
                // Kullanıcıyı uyar: scope için aktif şablon yok
                var inactiveCount = persistedSets.Count(s =>
                    s.ScopeType.Equals(scopeType, StringComparison.OrdinalIgnoreCase));
                if (inactiveCount > 0)
                    dto.Warnings.Add($"⚠️ '{scopeType}' scope için DB'de {inactiveCount} şablon var ama hiçbiri aktif değil. Embedded fallback kullanılıyor — sonuçlar eksik olabilir. FormulaDesigner'dan şablonu aktif yapın.");
                else
                    dto.Warnings.Add($"⚠️ '{scopeType}' scope için DB'de kayıtlı şablon bulunamadı. Embedded fallback kullanılıyor.");

                LoadEmbeddedFallbackTemplate(dto, scopeType);
            }
        }
        catch (Exception ex)
        {
            // DB'ye erişilemedi — embedded fallback kullan
            dto.Warnings.Add($"DB'ye erişilemedi ({ex.Message}), embedded fallback kullanılıyor.");
            LoadEmbeddedFallbackTemplate(dto, scopeType);
        }

        // Varsayılan girdi değerlerini hydrate et (DRY: tek kaynak)
        var defaults = await _globalDefaults.GetAsync(ct);
        KasaDefaultsHydrator.Apply(dto, defaults);
    }

    /// <summary>
    /// Public wrapper: Snapshot ve Global Defaults'tan varsayılan girdi değerlerini hydrate eder.
    /// </summary>
    public async Task HydrateFromSnapshotAndDefaultsAsync(KasaPreviewDto dto, CancellationToken ct)
    {
        var date = dto.SelectedDate ?? DateOnly.FromDateTime(DateTime.Today);
        dto.SelectedDate = date;
        await HydrateFromSnapshotAndDefaultsInternalAsync(dto, date, ct);
    }
}
