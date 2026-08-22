using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Orchestration;
using KasaManager.Application.Orchestration.Dtos;
using KasaManager.Application.Services;
using KasaManager.Application.Services.ReadAdapter;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Infrastructure.Services;
using KasaManager.Web.Controllers;
using KasaManager.Web.Helpers;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace KasaManager.Tests.Controllers;

/// <summary>
/// Helpy closure task 2 (smallest possible tests): KasaPreview stale-auto-POST kabulü.
/// Seam: HesapKontrolControllerTests.cs'de zaten kurulu olan doğrudan Moq controller-instantiation
/// deseni (WebApplicationFactory/TestServer YOK — bu repoda hiç kullanılmıyor; bu yüzden büyük bir
/// test framework icat edilmedi, mevcut desen genişletildi).
///
/// Kapsam bilinçli olarak dar tutuldu: Index() GET'te CanAutoPost/TempData guard kararı, resolver
/// çağrılarından SONRA ama ağır Intent-First pipeline'ından (SafeAutoLoadPreviewAsync vb.) ÖNCE
/// hesaplanıyor ve o pipeline'ın attığı her exception zaten Index'in kendi try/catch'i içinde
/// yutuluyor — bu yüzden diğer ~15 bağımlılığı gevşek (Mock.Of&lt;T&gt;()) bırakmak güvenli.
/// LoadAndCalculate'in TAM başarı yolu (formül motorunun gerçekten HasResults=true üretmesi) bu
/// oturumda derleyici geri bildirimi olmadan güvenle taklit edilemeyeceği için test edilmiyor;
/// bunun yerine LoadAndCalculate'in HERHANGİ bir başarısızlık modunda (yetkisiz/Unauthorized dahil)
/// guard'ın nasıl davrandığı doğrulanıyor — kontrat açısından yeterli ve daha az kırılgan.
/// </summary>
public sealed class KasaPreviewAutoPostTests : IDisposable
{
    private const string AutoPostGuardKey = "HK_AutoPostAttempted";
    private static readonly DateOnly PersistedDate = new(2026, 8, 15);
    private static readonly DateOnly ExcelDate = new(2026, 8, 18);

    private readonly List<string> _tempFolders = new();

    public void Dispose()
    {
        foreach (var folder in _tempFolders)
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
            catch { /* best-effort temizlik */ }
        }
    }

    // ─── Controller Factory ───

    /// <summary>
    /// <paramref name="realUploadFolder"/>: verildiğinde env.WebRootPath/Upload:SubFolder, bu GERÇEK
    /// geçici klasörü ResolveUploadFolderAbsolute() sonucu olacak şekilde ayarlanır — freshness
    /// testlerinin KasaSourceFingerprintHelper üzerinden gerçek dosya I/O yapabilmesi için (Step 7:
    /// "kanıt karşılaştırmasının kendisi" test edilmeli, mocked tier değil). Verilmezse davranış
    /// öncekiyle birebir aynıdır (var olmayan sahte C:\FakeWebRoot — freshness'ı hiç tetiklemeyen
    /// testler için yeterli ve daha hızlı).
    /// </summary>
    private static (KasaPreviewController controller, Mock<IEffectiveAnalysisDateResolver> resolver,
        Mock<IKasaOrchestrator> orchestrator, Mock<ICurrentUser> currentUser, TempDataDictionary tempData,
        Mock<IKasaRaporSnapshotService> raporSnapshots)
        CreateController(bool authenticated = true, string? realUploadFolder = null)
    {
        var env = new Mock<IWebHostEnvironment>();
        string subFolder;
        if (realUploadFolder != null)
        {
            env.SetupGet(e => e.WebRootPath).Returns(Directory.GetParent(realUploadFolder)!.FullName);
            subFolder = Path.GetFileName(realUploadFolder);
        }
        else
        {
            env.SetupGet(e => e.WebRootPath).Returns(@"C:\FakeWebRoot");
            subFolder = @"Data\Raporlar";
        }
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["Upload:SubFolder"] = subFolder
            })
            .Build();

        var resolver = new Mock<IEffectiveAnalysisDateResolver>();
        var orchestrator = new Mock<IKasaOrchestrator>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.IsAuthenticated).Returns(authenticated);
        currentUser.SetupGet(u => u.UserId).Returns(authenticated ? 17 : (int?)null);
        currentUser.SetupGet(u => u.Username).Returns("test-user");
        var raporSnapshots = new Mock<IKasaRaporSnapshotService>();

        var controller = new KasaPreviewController(
            orchestrator.Object,
            env.Object,
            configuration,
            Mock.Of<IImportOrchestrator>(),
            Mock.Of<IKasaReportDateRulesService>(),
            Mock.Of<IKasaGlobalDefaultsService>(),
            Mock.Of<IBankaHesapKontrolService>(),
            currentUser.Object,
            Mock.Of<IHesapKontrolSourceResolver>(),
            Mock.Of<IReportDataBuilder>(),
            Mock.Of<IExportService>(),
            Mock.Of<IKasaValidationService>(),
            Mock.Of<IVergideBirikenLedgerService>(),
            Mock.Of<IDocumentTemplateService>(),
            Mock.Of<IFinansalIstisnaService>(),
            Mock.Of<IFinansalIstisnaAnomaliService>(),
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<KasaPreviewController>>(),
            Mock.Of<IKasaReadModelService>(),
            Mock.Of<ICalculatedKasaSnapshotService>(),
            raporSnapshots.Object,
            resolver.Object);

        var httpContext = new DefaultHttpContext();
        var tempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = tempData;

        return (controller, resolver, orchestrator, currentUser, tempData, raporSnapshots);
    }

    private static void SetupResolverTier(
        Mock<IEffectiveAnalysisDateResolver> resolver, AnalysisDateSourceTier tier, DateOnly? date)
        => resolver
            .Setup(r => r.ResolveAsync(null, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectiveAnalysisDateResult(date, tier, "test"));

    /// <summary>
    /// Revision 3 PERSISTED SOURCE FRESHNESS CLOSURE: Tier1 + gerçek bir PersistedSnapshot ile resolver
    /// kurulumu — GET/POST'un CheckFreshnessAsync'i tetiklemesi için PersistedSnapshot'ın null OLMAMASI
    /// gerekiyor (bkz. KasaPreviewController.Index/AutoRunStaleAnalysis).
    /// </summary>
    private static void SetupResolverTierWithPersistedSnapshot(
        Mock<IEffectiveAnalysisDateResolver> resolver, DateOnly date, KasaRaporSnapshot persistedSnapshot)
        => resolver
            .Setup(r => r.ResolveAsync(It.IsAny<DateOnly?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectiveAnalysisDateResult(
                date, AnalysisDateSourceTier.SuccessfulPersistedKasa, "test", PersistedSnapshot: persistedSnapshot));

    private string CreateRealUploadFolderWithFile(string fileName, string content)
    {
        var folder = Path.Combine(Path.GetTempPath(), "KasaPreviewFreshnessTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, fileName), content);
        _tempFolders.Add(folder);
        return folder;
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(new[] { root }.Concat(segments).ToArray());
    }

    // ─── 1. GET performs zero writes ───

    [Fact]
    public async Task Index_Get_NeverCallsRaporSnapshotSave()
    {
        var (controller, resolver, _, _, _, raporSnapshots) = CreateController();
        SetupResolverTier(resolver, AnalysisDateSourceTier.AnalyzableExcel, ExcelDate);

        await controller.Index("Aksam", CancellationToken.None);

        // GET zero-write mandate: KasaEtkisi/Snapshot yazan hiçbir servis GET'te çağrılmamalı.
        // (Revision 3 Section 4/7'de TryAutoProvisionGenelSnapshotAsync GET'ten kaldırıldı — bu test
        // o regresyonu bir daha yaşamamak için var.)
        raporSnapshots.Verify(
            s => s.SaveAsync(It.IsAny<KasaRaporSnapshot>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── 2. Current analysis does not auto-post (degenerate: no PersistedSnapshot on the result) ───
    //
    // Revision 3 PERSISTED SOURCE FRESHNESS CLOSURE: a real evidence-comparison mechanism now exists
    // (KasaSourceFingerprintHelper.CheckFreshnessAsync, wired into Index/AutoRunStaleAnalysis — see the
    // dedicated Current/Stale/Unknown tests further below, which use REAL temp upload folders and
    // exercise the comparison itself, per Helpy's explicit "do not fabricate tests around mocked tier
    // values" instruction). This particular test is narrower: it proves the DEFENSIVE fallback when
    // the resolver reports SuccessfulPersistedKasa tier but (for whatever reason) does not hand back a
    // PersistedSnapshot reference — Index must not crash and must fail closed (no auto-post) rather
    // than assume freshness. It is intentionally NOT a freshness test.

    [Fact]
    public async Task Index_Get_SuccessfulPersistedKasaTier_NoPersistedSnapshotOnResult_FailsClosed_DoesNotSetCanAutoPost()
    {
        var (controller, resolver, _, _, tempData, _) = CreateController();
        SetupResolverTier(resolver, AnalysisDateSourceTier.SuccessfulPersistedKasa, PersistedDate);

        var result = await controller.Index("Aksam", CancellationToken.None);

        var model = Assert.IsType<KasaPreviewViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(model.IsStaleAnalysis);
        Assert.False(model.CanAutoPost);
        Assert.False(tempData.ContainsKey(AutoPostGuardKey));
    }

    // ─── 2b/2c/2d. Persisted source freshness (Current/Stale/Unknown) — real evidence comparison ───
    //
    // Revision 3 PERSISTED SOURCE FRESHNESS CLOSURE, Step 7: these use a REAL temp upload folder and
    // REAL file content so KasaSourceFingerprintHelper.CheckFreshnessAsync performs an actual SHA256
    // comparison — only the resolver's tier/date/PersistedSnapshot plumbing is mocked (necessary and
    // legitimate: the resolver itself is already covered in isolation by EffectiveAnalysisDateResolverTests).

    [Fact]
    public async Task Index_Get_Tier1_EvidenceMatchesCurrentSource_Current_NotStale_NoAutoPost()
    {
        var folder = CreateRealUploadFolderWithFile("MasrafveReddiyat.xlsx", "same-content");
        var evidence = await KasaSourceFingerprintHelper.CaptureAsync(
            folder, PersistedDate, "Genel", NullLogger.Instance, CancellationToken.None);
        var snapshot = new KasaRaporSnapshot
        {
            RaporTarihi = PersistedDate,
            RaporTuru = KasaRaporTuru.Genel,
            SourceEvidenceJson = KasaSourceFingerprintHelper.SerializeEvidence(evidence!)
        };

        var (controller, resolver, _, _, tempData, _) = CreateController(realUploadFolder: folder);
        SetupResolverTierWithPersistedSnapshot(resolver, PersistedDate, snapshot);

        var result = await controller.Index("Aksam", CancellationToken.None);

        var model = Assert.IsType<KasaPreviewViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(KasaSourceFreshness.Current, model.SourceFreshness);
        Assert.False(model.IsStaleAnalysis);
        Assert.False(model.CanAutoPost);
        Assert.False(tempData.ContainsKey(AutoPostGuardKey));
    }

    [Fact]
    public async Task Index_Get_Tier1_EvidenceDiffersFromCurrentSource_Stale_SetsCanAutoPostAndArmsGuard()
    {
        var folder = CreateRealUploadFolderWithFile("MasrafveReddiyat.xlsx", "content-at-persist-time");
        var evidence = await KasaSourceFingerprintHelper.CaptureAsync(
            folder, PersistedDate, "Genel", NullLogger.Instance, CancellationToken.None);
        var snapshot = new KasaRaporSnapshot
        {
            RaporTarihi = PersistedDate,
            RaporTuru = KasaRaporTuru.Genel,
            SourceEvidenceJson = KasaSourceFingerprintHelper.SerializeEvidence(evidence!)
        };
        // Kullanıcı persist'ten SONRA yeni bir Excel yükledi.
        File.WriteAllText(Path.Combine(folder, "MasrafveReddiyat.xlsx"), "content-changed-after-persist");

        var (controller, resolver, _, _, tempData, _) = CreateController(realUploadFolder: folder);
        SetupResolverTierWithPersistedSnapshot(resolver, PersistedDate, snapshot);

        var result = await controller.Index("Aksam", CancellationToken.None);

        var model = Assert.IsType<KasaPreviewViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(KasaSourceFreshness.Stale, model.SourceFreshness);
        Assert.True(model.IsStaleAnalysis);
        Assert.True(model.CanAutoPost);
        Assert.True(tempData.ContainsKey(AutoPostGuardKey));
    }

    [Fact]
    public async Task Index_Get_Tier1_LegacyNullEvidence_Unknown_NeverAutoOverwrites_NotTreatedAsStale()
    {
        var folder = CreateRealUploadFolderWithFile("MasrafveReddiyat.xlsx", "irrelevant-content");
        var snapshot = new KasaRaporSnapshot
        {
            RaporTarihi = PersistedDate,
            RaporTuru = KasaRaporTuru.Genel,
            SourceEvidenceJson = null // legacy satır — hiçbir zaman TryAutoProvisionGenelSnapshotAsync
                                       // dışındaki yollarda dolduruldu (Step 1 finding), veya eski kayıt.
        };

        var (controller, resolver, _, _, tempData, _) = CreateController(realUploadFolder: folder);
        SetupResolverTierWithPersistedSnapshot(resolver, PersistedDate, snapshot);

        var result = await controller.Index("Aksam", CancellationToken.None);

        var model = Assert.IsType<KasaPreviewViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(KasaSourceFreshness.Unknown, model.SourceFreshness);
        // Step 3/4 invariant: Unknown != Stale — asla otomatik yazma tetiklenmez, guard set edilmez.
        Assert.False(model.IsStaleAnalysis);
        Assert.False(model.CanAutoPost);
        Assert.False(tempData.ContainsKey(AutoPostGuardKey));
    }

    [Fact]
    public async Task Index_Get_Tier1_InvalidEvidenceJson_Unknown_NeverAutoOverwrites()
    {
        var folder = CreateRealUploadFolderWithFile("MasrafveReddiyat.xlsx", "irrelevant-content");
        var snapshot = new KasaRaporSnapshot
        {
            RaporTarihi = PersistedDate,
            RaporTuru = KasaRaporTuru.Genel,
            SourceEvidenceJson = "{ not valid json at all"
        };

        var (controller, resolver, _, _, tempData, _) = CreateController(realUploadFolder: folder);
        SetupResolverTierWithPersistedSnapshot(resolver, PersistedDate, snapshot);

        var result = await controller.Index("Aksam", CancellationToken.None);

        var model = Assert.IsType<KasaPreviewViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(KasaSourceFreshness.Unknown, model.SourceFreshness);
        Assert.False(model.IsStaleAnalysis);
        Assert.False(model.CanAutoPost);
        Assert.False(tempData.ContainsKey(AutoPostGuardKey));
    }

    // ─── POST rechecks freshness server-side (never trusts a client currentness flag) ───

    [Fact]
    public async Task AutoRunStaleAnalysis_Tier1_EvidenceCurrent_NoOp_DoesNotCallOrchestrator()
    {
        var folder = CreateRealUploadFolderWithFile("MasrafveReddiyat.xlsx", "same-content");
        var evidence = await KasaSourceFingerprintHelper.CaptureAsync(
            folder, PersistedDate, "Aksam", NullLogger.Instance, CancellationToken.None);
        var snapshot = new KasaRaporSnapshot
        {
            RaporTarihi = PersistedDate,
            RaporTuru = KasaRaporTuru.Genel,
            SourceEvidenceJson = KasaSourceFingerprintHelper.SerializeEvidence(evidence!)
        };

        var (controller, resolver, orchestrator, _, tempData, _) = CreateController(realUploadFolder: folder);
        SetupResolverTierWithPersistedSnapshot(resolver, PersistedDate, snapshot);

        var result = await controller.AutoRunStaleAnalysis("Aksam", null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(KasaPreviewController.Index), redirect.ActionName);
        Assert.False(tempData.ContainsKey(AutoPostGuardKey));
        orchestrator.Verify(
            o => o.LoadActiveFormulaSetByScopeAsync(It.IsAny<KasaPreviewDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AutoRunStaleAnalysis_Tier1_EvidenceStale_ProceedsToLoadAndCalculate()
    {
        var folder = CreateRealUploadFolderWithFile("MasrafveReddiyat.xlsx", "content-at-persist-time");
        var evidence = await KasaSourceFingerprintHelper.CaptureAsync(
            folder, PersistedDate, "Aksam", NullLogger.Instance, CancellationToken.None);
        var snapshot = new KasaRaporSnapshot
        {
            RaporTarihi = PersistedDate,
            RaporTuru = KasaRaporTuru.Genel,
            SourceEvidenceJson = KasaSourceFingerprintHelper.SerializeEvidence(evidence!)
        };
        File.WriteAllText(Path.Combine(folder, "MasrafveReddiyat.xlsx"), "content-changed-after-persist");

        // Yetkisiz aktör: LoadAndCalculate'in TryResolveHesapKontrolActor guard'ı ile "gerçekten
        // LoadAndCalculate'e ULAŞILDI mı" sorusunu, formül motorunun tam başarı akışını taklit etmeden,
        // deterministik biçimde doğruluyoruz (Index_Get_StaleAnalysis testlerindeki desenle aynı).
        var (controller, resolver, _, _, tempData, _) = CreateController(authenticated: false, realUploadFolder: folder);
        SetupResolverTierWithPersistedSnapshot(resolver, PersistedDate, snapshot);

        var result = await controller.AutoRunStaleAnalysis("Aksam", null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(KasaPreviewController.Index), redirect.ActionName);
        // Stale → LoadAndCalculate'e ULAŞILDI (Unauthorized ile başarısız oldu) → başarısız deneme guard'ı
        // tekrar arms eder. Bu, "no-op" (Current/Unknown) davranışından AYIRT EDİCİDİR: no-op'ta guard
        // dokunulmaz kalırdı (yukarıdaki Current testine bakınız).
        Assert.True(tempData.ContainsKey(AutoPostGuardKey));
    }

    [Fact]
    public async Task AutoRunStaleAnalysis_Tier1_EvidenceUnknown_NoOp_DoesNotCallOrchestrator()
    {
        var folder = CreateRealUploadFolderWithFile("MasrafveReddiyat.xlsx", "irrelevant-content");
        var snapshot = new KasaRaporSnapshot
        {
            RaporTarihi = PersistedDate,
            RaporTuru = KasaRaporTuru.Genel,
            SourceEvidenceJson = null
        };

        var (controller, resolver, orchestrator, _, tempData, _) = CreateController(realUploadFolder: folder);
        SetupResolverTierWithPersistedSnapshot(resolver, PersistedDate, snapshot);

        var result = await controller.AutoRunStaleAnalysis("Aksam", null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(KasaPreviewController.Index), redirect.ActionName);
        Assert.False(tempData.ContainsKey(AutoPostGuardKey));
        orchestrator.Verify(
            o => o.LoadActiveFormulaSetByScopeAsync(It.IsAny<KasaPreviewDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── 3/4. Stale triggers only one automatic attempt (arms once, blocks on next GET) ───

    [Fact]
    public async Task Index_Get_StaleAnalysis_FirstVisit_SetsCanAutoPostAndArmsGuard()
    {
        var (controller, resolver, _, _, tempData, _) = CreateController();
        SetupResolverTier(resolver, AnalysisDateSourceTier.AnalyzableExcel, ExcelDate);

        var result = await controller.Index("Aksam", CancellationToken.None);

        var model = Assert.IsType<KasaPreviewViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.IsStaleAnalysis);
        Assert.True(model.CanAutoPost);
        Assert.True(tempData.ContainsKey(AutoPostGuardKey));
    }

    [Fact]
    public async Task Index_Get_StaleAnalysis_GuardAlreadyArmed_DoesNotReTriggerCanAutoPost()
    {
        var (controller, resolver, _, _, tempData, _) = CreateController();
        SetupResolverTier(resolver, AnalysisDateSourceTier.AnalyzableExcel, ExcelDate);
        // Bir önceki GET/POST turunda guard zaten set edilmiş — ASP.NET Core TempData "bir sonraki
        // request'te mevcut" garantisini simüle ediyoruz.
        tempData[AutoPostGuardKey] = true;

        var result = await controller.Index("Aksam", CancellationToken.None);

        var model = Assert.IsType<KasaPreviewViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.IsStaleAnalysis);
        Assert.False(model.CanAutoPost); // guard bloke ediyor — tekrar otomatik POST tetiklenmiyor
    }

    // ─── 5. Failed automatic POST does not loop (re-arms guard on every failure mode) ───

    [Fact]
    public async Task AutoRunStaleAnalysis_EmptyKasaType_RedirectsToIndexAndArmsGuard()
    {
        var (controller, _, orchestrator, _, tempData, _) = CreateController();

        var result = await controller.AutoRunStaleAnalysis(null, null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(KasaPreviewController.Index), redirect.ActionName);
        Assert.True(tempData.ContainsKey(AutoPostGuardKey));
        orchestrator.Verify(
            o => o.LoadActiveFormulaSetByScopeAsync(It.IsAny<KasaPreviewDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AutoRunStaleAnalysis_ResolverThrows_RedirectsToIndexAndArmsGuard()
    {
        var (controller, resolver, _, _, tempData, _) = CreateController();
        resolver
            .Setup(r => r.ResolveAsync(null, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await controller.AutoRunStaleAnalysis("Aksam", null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(KasaPreviewController.Index), redirect.ActionName);
        Assert.True(tempData.ContainsKey(AutoPostGuardKey));
    }

    [Fact]
    public async Task AutoRunStaleAnalysis_NoLongerStale_RedirectsWithoutArmingGuardOrTouchingOrchestrator()
    {
        var (controller, resolver, orchestrator, _, tempData, _) = CreateController();
        // Sunucu tarafı yeniden çözümleme: başka bir istek arada güncellemiş olabilir — artık stale değil.
        SetupResolverTier(resolver, AnalysisDateSourceTier.SuccessfulPersistedKasa, PersistedDate);

        var result = await controller.AutoRunStaleAnalysis("Aksam", null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(KasaPreviewController.Index), redirect.ActionName);
        Assert.False(tempData.ContainsKey(AutoPostGuardKey));
        orchestrator.Verify(
            o => o.LoadActiveFormulaSetByScopeAsync(It.IsAny<KasaPreviewDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AutoRunStaleAnalysis_LoadAndCalculateFails_RedirectsToIndexAndArmsGuard()
    {
        // Yetkisiz aktör (LoadAndCalculate'in TryResolveHesapKontrolActor guard'ı) burada
        // LoadAndCalculate'in "başarısız" dönmesini SAFE ve DETERMİNİSTİK şekilde tetiklemek için
        // kullanılıyor — formül motorunun tam başarı akışını taklit etmeden, "herhangi bir başarısızlık
        // guard'ı tekrar arms eder" kontratını doğrular.
        var (controller, resolver, _, _, tempData, _) = CreateController(authenticated: false);
        SetupResolverTier(resolver, AnalysisDateSourceTier.AnalyzableExcel, ExcelDate);

        var result = await controller.AutoRunStaleAnalysis("Aksam", null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(KasaPreviewController.Index), redirect.ActionName);
        Assert.True(tempData.ContainsKey(AutoPostGuardKey));
    }

    // ─── Helpy final closure task 1: explicit/context date preserved GET → auto POST ───

    [Fact]
    public async Task AutoRunStaleAnalysis_ValidContextDate_PassesItThroughToResolverAsExplicitDate()
    {
        // GET'in ekranda gösterdiği tarih (ör. AnalyzableExcel tier'ından gelen) hidden form ile POST'a
        // taşınıyor; sunucu bunu explicitContextDate olarak resolver'a geçiriyor (Tier 0 adayı).
        var (controller, resolver, _, _, _, _) = CreateController();
        DateOnly? capturedExplicitDate = null;
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<DateOnly?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<DateOnly?, string, string, CancellationToken>((d, _, _, _) => capturedExplicitDate = d)
            .ReturnsAsync(new EffectiveAnalysisDateResult(ExcelDate, AnalysisDateSourceTier.ExplicitContext, "test"));

        await controller.AutoRunStaleAnalysis("Aksam", "2026-08-18", CancellationToken.None);

        Assert.Equal(ExcelDate, capturedExplicitDate);
    }

    [Fact]
    public async Task AutoRunStaleAnalysis_InvalidContextDate_FallsBackToNullExplicitDate()
    {
        // Bozuk/parse edilemeyen contextDate — resolver'ın KENDİ Tier 0 sözdizim kuralına göre
        // (default(DateOnly) kabul edilmez) otomatik olarak null'a düşer ve Tier 1/2/3'e devreder.
        // Yeni bir doğrulama kuralı İCAT EDİLMEDİ — mevcut resolver kontratı aynen kullanıldı.
        var (controller, resolver, _, _, _, _) = CreateController();
        DateOnly? capturedExplicitDate = ExcelDate; // sentinel — null'a düşmeli
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<DateOnly?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<DateOnly?, string, string, CancellationToken>((d, _, _, _) => capturedExplicitDate = d)
            .ReturnsAsync(new EffectiveAnalysisDateResult(ExcelDate, AnalysisDateSourceTier.AnalyzableExcel, "test"));

        await controller.AutoRunStaleAnalysis("Aksam", "not-a-date", CancellationToken.None);

        Assert.Null(capturedExplicitDate);
    }

    // Not: "Tier 0, persisted Kasa ve Excel'in önüne geçer" zaten
    // EffectiveAnalysisDateResolverTests.Tier0_ExplicitContextDate_TakesPrecedenceOverEverythingElse
    // tarafından izole şekilde kanıtlanıyor (resolver'ın kendisi, controller'dan bağımsız). Yukarıdaki
    // 2 test + o test birlikte: "GET'in tarihi POST'a doğrulanarak taşınır VE Tier 0 kazanır" uçtan uca
    // kanıtlanmış olur.

    // ─── 6. Manual retry remains possible ───

    [Fact]
    public void ManualRetryActions_NeverGateOnAutoPostGuardKey()
    {
        // LoadAndCalculate/Calculate/RunHesapKontrolFromContext — kullanıcının mevcut manuel
        // "Hesapla"/"Hesap Kontrolü Çalıştır" akışları — guard anahtarına ASLA referans vermemeli.
        // Guard yalnızca Index GET'in CanAutoPost kararını ve AutoRunStaleAnalysis'i etkiler; manuel
        // retry her zaman mümkün kalmalı.
        var controllerSource = File.ReadAllText(GetRepositoryPath(
            "src", "KasaManager.Web", "Controllers", "KasaPreviewController.cs"));

        var loadAndCalcStart = controllerSource.IndexOf(
            "public async Task<IActionResult> LoadAndCalculate", StringComparison.Ordinal);
        var loadAndCalcEnd = controllerSource.IndexOf(
            "public async Task<IActionResult> AutoRunStaleAnalysis", loadAndCalcStart, StringComparison.Ordinal);
        Assert.True(loadAndCalcStart >= 0 && loadAndCalcEnd > loadAndCalcStart);
        Assert.DoesNotContain(AutoPostGuardKey, controllerSource[loadAndCalcStart..loadAndCalcEnd]);

        var calcStart = controllerSource.IndexOf(
            "public async Task<IActionResult> Calculate(KasaPreviewViewModel", StringComparison.Ordinal);
        Assert.True(calcStart > loadAndCalcEnd);
        Assert.DoesNotContain(AutoPostGuardKey, controllerSource[calcStart..]);
    }
}
