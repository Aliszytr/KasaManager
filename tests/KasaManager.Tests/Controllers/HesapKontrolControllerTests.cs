using System;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Orchestration;
using KasaManager.Application.Services;
using KasaManager.Application.Services.ReadAdapter;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Domain.Validation;
using KasaManager.Web.Controllers;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace KasaManager.Tests.Controllers;

/// <summary>
/// FAZ-3: HesapKontrolController için regresyon testleri.
/// GET salt okunur, RunAnalysis tek analiz komutu, PRG korunur.
/// </summary>
public sealed class HesapKontrolControllerTests
{
    // ─── Controller Factory ───

    private static HesapKontrolController CreateController(
        Mock<IBankaHesapKontrolService>? mockService = null,
        Mock<IWebHostEnvironment>? mockEnv = null,
        Mock<IHesapKontrolSourceResolver>? mockSourceResolver = null,
        Mock<ILogger<HesapKontrolController>>? mockLogger = null,
        ICurrentUser? currentUser = null,
        Mock<IManualResolveWriteBusinessDateResolver>? mockWriteBusinessDateResolver = null)
    {
        var service = mockService ?? new Mock<IBankaHesapKontrolService>();
        var env = mockEnv ?? new Mock<IWebHostEnvironment>();
        var sourceResolver = mockSourceResolver ?? new Mock<IHesapKontrolSourceResolver>();

        env.SetupGet(e => e.WebRootPath).Returns(@"C:\FakeWebRoot");
        if (mockSourceResolver is null)
        {
            sourceResolver
                .Setup(r => r.Resolve(It.IsAny<string>(), It.IsAny<DateOnly>()))
                .Returns((string baseFolder, DateOnly _) => HesapKontrolSourceResolution.Success(baseFolder));
        }

        SetupReadOnlyStubs(service);

        var controller = new HesapKontrolController(
            service.Object,
            currentUser ?? Mock.Of<ICurrentUser>(u => u.IsAuthenticated && u.UserId == 17 && u.Username == "test-user"),
            Mock.Of<IHesapKontrolExportService>(),
            Mock.Of<IFinansalIstisnaService>(),
            sourceResolver.Object,
            (mockLogger ?? new Mock<ILogger<HesapKontrolController>>()).Object,
            env.Object,
            (mockWriteBusinessDateResolver ?? new Mock<IManualResolveWriteBusinessDateResolver>()).Object
        );

        controller.TempData = new TempDataDictionary(
            new DefaultHttpContext(),
            Mock.Of<ITempDataProvider>());

        return controller;
    }

    // ─── C1: GET salt okunur testleri ───

    [Fact]
    public async Task Index_GET_DoesNotRunAnalysis()
    {
        var mockService = new Mock<IBankaHesapKontrolService>();
        SetupReadOnlyStubs(mockService);
        var controller = CreateController(mockService: mockService);

        await controller.Index(
            tab: null, analizTarihiStr: "2026-06-01",
            hesapTuru: null, durum: null, takipDurum: null,
            baslangic: null, bitis: null, arama: null,
            ct: CancellationToken.None);

        mockService.Verify(
            s => s.AnalyzeFromComparisonAsync(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "GET isteği analiz çalıştırmamalıdır — FAZ-3.");
    }

    [Fact]
    public async Task Index_GET_DoesNotValidateExcelSource()
    {
        var mockService = new Mock<IBankaHesapKontrolService>();
        SetupReadOnlyStubs(mockService);
        var mockSourceResolver = new Mock<IHesapKontrolSourceResolver>();

        var controller = CreateController(mockService: mockService, mockSourceResolver: mockSourceResolver);

        await controller.Index(
            tab: null, analizTarihiStr: "2026-06-01",
            hesapTuru: null, durum: null, takipDurum: null,
            baslangic: null, bitis: null, arama: null,
            ct: CancellationToken.None);

        mockSourceResolver.Verify(
            r => r.Resolve(It.IsAny<string>(), It.IsAny<DateOnly>()),
            Times.Never,
            "GET salt okunurdur — arşiv veya Excel validasyonu yapmaz.");
    }

    [Fact]
    public async Task Index_GET_OnlyLoadsDashboard()
    {
        var mockService = new Mock<IBankaHesapKontrolService>();
        SetupReadOnlyStubs(mockService);
        var controller = CreateController(mockService: mockService);

        var result = await controller.Index(
            tab: null, analizTarihiStr: null,
            hesapTuru: null, durum: null, takipDurum: null,
            baslangic: null, bitis: null, arama: null,
            ct: CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        mockService.Verify(
            s => s.GetDashboardAsync(It.IsAny<DateOnly>(), It.IsAny<BankaHesapTuru?>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "GET yalnızca Dashboard okur.");
        Assert.All(mockService.Invocations,
            invocation => Assert.StartsWith("Get", invocation.Method.Name));
    }

    // ─── C2: RunAnalysis POST testleri ───

    [Fact]
    public async Task RunAnalysis_POST_PassesSelectedDate()
    {
        var mockService = new Mock<IBankaHesapKontrolService>();
        SetupReadOnlyStubs(mockService);
        var analizTarihi = new DateOnly(2026, 6, 15);
        mockService
            .Setup(s => s.AnalyzeFromComparisonAsync(analizTarihi, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HesapKontrolRapor(analizTarihi, 0, 0, 0, 0, 0,
                new StopajVirmanDurum(false, 0, null, "Test", StopajStatus.Ok),
                new(), new(), "Test Rapor"));

        var controller = CreateController(mockService: mockService);

        await controller.RunAnalysis(tarih: "2026-06-15", ct: CancellationToken.None);

        mockService.Verify(
            s => s.AnalyzeFromComparisonAsync(analizTarihi, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "RunAnalysis seçilen tarihi servise aktarmalıdır.");
    }

    [Fact]
    public async Task RunAnalysis_POST_UsesResolvedValidSource()
    {
        var archivePath = @"C:\FakeWebRoot\Data\Raporlar\2026-06-15";
        var mockSourceResolver = new Mock<IHesapKontrolSourceResolver>();
        mockSourceResolver
            .Setup(r => r.Resolve(It.IsAny<string>(), It.IsAny<DateOnly>()))
            .Returns(HesapKontrolSourceResolution.Success(archivePath));

        var mockService = new Mock<IBankaHesapKontrolService>();
        SetupReadOnlyStubs(mockService);
        var analizTarihi = new DateOnly(2026, 6, 15);
        mockService
            .Setup(s => s.AnalyzeFromComparisonAsync(analizTarihi, archivePath, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HesapKontrolRapor(analizTarihi, 0, 0, 0, 0, 0,
                new StopajVirmanDurum(false, 0, null, "Test", StopajStatus.Ok),
                new(), new(), "Test Rapor"));

        var controller = CreateController(mockService: mockService, mockSourceResolver: mockSourceResolver);

        await controller.RunAnalysis(tarih: "2026-06-15", ct: CancellationToken.None);

        mockService.Verify(
            s => s.AnalyzeFromComparisonAsync(analizTarihi, archivePath, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "Arşiv varsa arşiv klasörü kullanılmalıdır.");
    }

    [Fact]
    public async Task RunAnalysis_Success_RedirectsToIndex()
    {
        var mockService = new Mock<IBankaHesapKontrolService>();
        SetupReadOnlyStubs(mockService);
        var analizTarihi = new DateOnly(2026, 6, 15);
        mockService
            .Setup(s => s.AnalyzeFromComparisonAsync(analizTarihi, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HesapKontrolRapor(analizTarihi, 0, 0, 0, 0, 0,
                new StopajVirmanDurum(false, 0, null, "Test", StopajStatus.Ok),
                new(), new(), "Test Rapor"));

        var controller = CreateController(mockService: mockService);

        var result = await controller.RunAnalysis(tarih: "2026-06-15", ct: CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
    }

    [Fact]
    public async Task RunAnalysis_ValidationFailure_RedirectsAndSetsError()
    {
        var mockService = new Mock<IBankaHesapKontrolService>();
        SetupReadOnlyStubs(mockService);
        var mockSourceResolver = new Mock<IHesapKontrolSourceResolver>();
        mockSourceResolver
            .Setup(r => r.Resolve(It.IsAny<string>(), It.IsAny<DateOnly>()))
            .Returns(HesapKontrolSourceResolution.Fail(
                "Güncel kaynak: 'BankaTahsilat.xlsx' seçilen tarihi içermiyor."));

        var controller = CreateController(mockService: mockService, mockSourceResolver: mockSourceResolver);

        var result = await controller.RunAnalysis(tarih: "2026-06-15", ct: CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.NotNull(controller.TempData["Error"]);
        Assert.Contains("Excel kaynakları doğrulanamadı", controller.TempData["Error"]!.ToString());
        mockService.Verify(
            s => s.AnalyzeFromComparisonAsync(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Kaynak doğrulanamadığında analiz başlamamalıdır.");
    }

    [Fact]
    public async Task ResolverFailure_UserMessage_DoesNotContainPhysicalPath()
    {
        const string physicalPath = @"D:\KasaManager\secret\archive";
        var mockSourceResolver = new Mock<IHesapKontrolSourceResolver>();
        mockSourceResolver
            .Setup(r => r.Resolve(It.IsAny<string>(), It.IsAny<DateOnly>()))
            .Returns(HesapKontrolSourceResolution.Fail(
                "Seçilen tarih için gerekli Excel kaynakları doğrulanamadı.",
                $"Arşiv kaynağı ('{physicalPath}') geçersiz."));

        var controller = CreateController(mockSourceResolver: mockSourceResolver);

        await controller.RunAnalysis("2026-06-15", CancellationToken.None);

        var userMessage = Assert.IsType<string>(controller.TempData["Error"]);
        Assert.DoesNotContain(physicalPath, userMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAnalysis_Exception_UserMessage_DoesNotContainExceptionMessage()
    {
        const string technicalMessage = "Server=secret-db;Password=secret";
        var mockService = new Mock<IBankaHesapKontrolService>();
        SetupReadOnlyStubs(mockService);
        mockService
            .Setup(s => s.AnalyzeFromComparisonAsync(
                It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(technicalMessage));

        var controller = CreateController(mockService: mockService);

        await controller.RunAnalysis("2026-06-15", CancellationToken.None);

        var userMessage = Assert.IsType<string>(controller.TempData["Error"]);
        Assert.DoesNotContain(technicalMessage, userMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TechnicalFailure_IsLogged()
    {
        var exception = new InvalidOperationException("technical-analysis-detail");
        var mockService = new Mock<IBankaHesapKontrolService>();
        var mockLogger = new Mock<ILogger<HesapKontrolController>>();
        SetupReadOnlyStubs(mockService);
        mockService
            .Setup(s => s.AnalyzeFromComparisonAsync(
                It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var controller = CreateController(mockService: mockService, mockLogger: mockLogger);

        await controller.RunAnalysis("2026-06-15", CancellationToken.None);

        mockLogger.Verify(
            logger => logger.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, _) =>
                    value.ToString()!.Contains("HesapKontrol analiz hatası")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void RunAnalysis_IsProtectedByAntiForgery()
    {
        var method = typeof(HesapKontrolController)
            .GetMethod(nameof(HesapKontrolController.RunAnalysis),
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(method);
        var hasAntiForgery = method!.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: false).Length > 0;
        var hasHttpPost = method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Length > 0;

        Assert.True(hasHttpPost, "RunAnalysis [HttpPost] attribute içermelidir.");
        Assert.True(hasAntiForgery, "RunAnalysis [ValidateAntiForgeryToken] attribute içermelidir.");
    }

    // ─── Helper ───

    private static void SetupReadOnlyStubs(Mock<IBankaHesapKontrolService> mockService)
    {
        mockService.Setup(s => s.GetDashboardAsync(It.IsAny<DateOnly>(), It.IsAny<BankaHesapTuru?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new HesapKontrolDashboard(0, 0, 0, 0, 0, 0, 0, 0, 0));
        mockService.Setup(s => s.GetOpenItemsAsync(It.IsAny<BankaHesapTuru?>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new System.Collections.Generic.List<HesapKontrolKaydi>());
        mockService.Setup(s => s.GetTrackedItemsAsync(It.IsAny<BankaHesapTuru?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new System.Collections.Generic.List<HesapKontrolKaydi>());
        mockService.Setup(s => s.GetHistoryAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<BankaHesapTuru?>(), It.IsAny<KayitDurumu?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new System.Collections.Generic.List<HesapKontrolKaydi>());
        mockService.Setup(s => s.GetTrackingSummaryAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new TakipOzeti(0, 0, 0, 0, 0, new(), 0, new()));
    }
}

public sealed class KasaPreviewHesapKontrolGateTests
{
    private static readonly DateOnly TestDate = new(2026, 6, 15);
    private const string BaseFolder = @"C:\FakeWebRoot\Data\Raporlar";
    private const string ResolvedFolder = @"C:\FakeWebRoot\Data\Raporlar\archive\2026-06-15";

    [Fact]
    public async Task LoadAndCalculate_InvalidSource_DoesNotRunAnalysis()
    {
        var service = new Mock<IBankaHesapKontrolService>();
        var resolver = CreateInvalidResolver();
        var controller = CreateController(service, resolver);
        var model = CreateModel();

        await InvokeAnalysisGateAsync(controller, model, nameof(KasaPreviewController.LoadAndCalculate));

        service.Verify(s => s.AnalyzeFromComparisonAsync(
            It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains(model.Errors, error => error.Contains("Excel kaynakları doğrulanamadı"));
    }

    [Fact]
    public async Task LoadAndCalculate_ValidSource_UsesResolvedFolder()
    {
        var service = CreateSuccessfulService();
        var resolver = CreateValidResolver();
        var controller = CreateController(service, resolver);

        await InvokeAnalysisGateAsync(
            controller, CreateModel(), nameof(KasaPreviewController.LoadAndCalculate));

        service.Verify(s => s.AnalyzeFromComparisonAsync(
            TestDate, ResolvedFolder, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Calculate_InvalidSource_DoesNotRunAnalysis()
    {
        var service = new Mock<IBankaHesapKontrolService>();
        var resolver = CreateInvalidResolver();
        var controller = CreateController(service, resolver);

        await InvokeAnalysisGateAsync(
            controller, CreateModel(), nameof(KasaPreviewController.Calculate));

        service.Verify(s => s.AnalyzeFromComparisonAsync(
            It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Calculate_ValidSource_UsesResolvedFolder()
    {
        var service = CreateSuccessfulService();
        var resolver = CreateValidResolver();
        var controller = CreateController(service, resolver);

        await InvokeAnalysisGateAsync(
            controller, CreateModel(), nameof(KasaPreviewController.Calculate));

        service.Verify(s => s.AnalyzeFromComparisonAsync(
            TestDate, ResolvedFolder, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task KasaPreview_InvalidSource_DoesNotClearDraftState()
    {
        var service = new Mock<IBankaHesapKontrolService>();
        var cache = new Mock<IDistributedCache>();
        var controller = CreateController(service, CreateInvalidResolver(), cache);
        var model = CreateModel();
        model.HasResults = true;

        await InvokeAnalysisGateAsync(
            controller, model, nameof(KasaPreviewController.Calculate));

        Assert.True(model.HasResults);
        Assert.Equal(TestDate, model.SelectedDate);
        cache.Verify(c => c.RemoveAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ProductionControllers_DoNotBypassStrictSourceGate()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var controllerPath = Path.Combine(
            repositoryRoot, "src", "KasaManager.Web", "Controllers", "KasaPreviewController.cs");
        var helperPath = Path.Combine(
            repositoryRoot, "src", "KasaManager.Web", "Controllers", "KasaPreviewController.Helpers.cs");

        var controllerSource = File.ReadAllText(controllerPath);
        var helperSource = File.ReadAllText(helperPath);

        Assert.DoesNotContain("AnalyzeFromComparisonAsync", controllerSource);
        Assert.Contains("nameof(LoadAndCalculate)", controllerSource);
        Assert.Contains("nameof(Calculate)", controllerSource);

        var resolveIndex = helperSource.IndexOf(
            "_hesapKontrolSourceResolver.Resolve", StringComparison.Ordinal);
        var analysisIndex = helperSource.IndexOf(
            "_hesapKontrol.AnalyzeFromComparisonAsync", StringComparison.Ordinal);
        Assert.True(resolveIndex >= 0 && analysisIndex > resolveIndex,
            "KasaPreview üretim analizi strict resolver sonrasında çalışmalıdır.");
    }

    private static KasaPreviewViewModel CreateModel() => new()
    {
        KasaType = "Sabah",
        SelectedDate = TestDate
    };

    private static Mock<IHesapKontrolSourceResolver> CreateInvalidResolver()
    {
        var resolver = new Mock<IHesapKontrolSourceResolver>();
        resolver
            .Setup(r => r.Resolve(BaseFolder, TestDate))
            .Returns(HesapKontrolSourceResolution.Fail(
                "Seçilen tarih için gerekli Excel kaynakları doğrulanamadı.",
                $"Folder='{BaseFolder}' invalid."));
        return resolver;
    }

    private static Mock<IHesapKontrolSourceResolver> CreateValidResolver()
    {
        var resolver = new Mock<IHesapKontrolSourceResolver>();
        resolver
            .Setup(r => r.Resolve(BaseFolder, TestDate))
            .Returns(HesapKontrolSourceResolution.Success(ResolvedFolder));
        return resolver;
    }

    private static Mock<IBankaHesapKontrolService> CreateSuccessfulService()
    {
        var service = new Mock<IBankaHesapKontrolService>();
        service
            .Setup(s => s.AnalyzeFromComparisonAsync(
                TestDate, ResolvedFolder, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HesapKontrolRapor(
                TestDate, 0, 0, 0, 0, 0,
                new StopajVirmanDurum(false, 0, null, "Test", StopajStatus.Ok),
                new(), new(), "Test Rapor"));
        return service;
    }

    private static KasaPreviewController CreateController(
        Mock<IBankaHesapKontrolService> service,
        Mock<IHesapKontrolSourceResolver> resolver,
        Mock<IDistributedCache>? cache = null)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.WebRootPath).Returns(@"C:\FakeWebRoot");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Upload:SubFolder"] = @"Data\Raporlar"
            })
            .Build();

        return new KasaPreviewController(
            Mock.Of<IKasaOrchestrator>(),
            env.Object,
            configuration,
            Mock.Of<IImportOrchestrator>(),
            Mock.Of<IKasaReportDateRulesService>(),
            Mock.Of<IKasaGlobalDefaultsService>(),
            service.Object,
            Mock.Of<ICurrentUser>(u => u.IsAuthenticated && u.UserId == 17 && u.Username == "test-user"),
            resolver.Object,
            Mock.Of<IReportDataBuilder>(),
            Mock.Of<IExportService>(),
            Mock.Of<IKasaValidationService>(),
            Mock.Of<IVergideBirikenLedgerService>(),
            Mock.Of<IDocumentTemplateService>(),
            Mock.Of<IFinansalIstisnaService>(),
            Mock.Of<IFinansalIstisnaAnomaliService>(),
            (cache ?? new Mock<IDistributedCache>()).Object,
            Mock.Of<ILogger<KasaPreviewController>>(),
            Mock.Of<IKasaReadModelService>(),
            Mock.Of<ICalculatedKasaSnapshotService>(),
            Mock.Of<IKasaRaporSnapshotService>(),
            Mock.Of<IEffectiveAnalysisDateResolver>());
    }

    private static async Task InvokeAnalysisGateAsync(
        KasaPreviewController controller,
        KasaPreviewViewModel model,
        string actionName)
    {
        var method = typeof(KasaPreviewController).GetMethod(
            "TryRunHesapKontrolAnalysisAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);

        var invocation = method!.Invoke(controller, new object[]
        {
            model, TestDate, BaseFolder, actionName, 17, CancellationToken.None
        });
        await Assert.IsAssignableFrom<Task>(invocation);
    }
}
