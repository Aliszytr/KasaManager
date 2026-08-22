using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Web.Controllers;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KasaManager.Tests.Controllers;

public sealed class HesapKontrolHistoricalDateContextControllerTests
{
    private static readonly DateOnly HistoricalDate = new(2026, 7, 13);

    [Fact]
    public async Task Index_WithIsoHistoricalDate_PreservesAnalysisDate()
    {
        var service = CreateReadService();
        var controller = CreateController(service);

        var result = await InvokeIndexAsync(controller, tab: "ozet");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<HesapKontrolViewModel>(view.Model);
        Assert.Equal(HistoricalDate, model.AnalizTarihi);
    }

    [Fact]
    public async Task HeaderDashboardAndList_UseSameSelectedDateContext()
    {
        var dashboard = new HesapKontrolDashboard(1, 0, 1, 25m, 0m, 2, 1, 50m, 0m);
        var open = CreateRecord(KayitDurumu.Acik, 25m);
        var tracked = CreateRecord(KayitDurumu.Takipte, 50m);
        var trackingSummary = new TakipOzeti(1, 50m, 0m, 1, 1, new(), 0m, new());
        var service = CreateReadService(dashboard, new() { open }, new() { tracked }, trackingSummary);
        var controller = CreateController(service);

        var result = await InvokeIndexAsync(controller, tab: "takipte", includeDateRange: false);

        var model = Assert.IsType<HesapKontrolViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(HistoricalDate, model.AnalizTarihi);
        Assert.Same(dashboard, model.Dashboard);
        Assert.Collection(model.AcikKayitlar, item => Assert.Equal(open.Id, item.Id));
        Assert.Collection(model.TakipteKayitlar, item => Assert.Equal(tracked.Id, item.Id));
        Assert.Same(trackingSummary, model.TakipOzeti);
        Assert.Equal(HistoricalDate.AddDays(-7), model.FilterBaslangic);
        Assert.Equal(HistoricalDate, model.FilterBitis);
        service.Verify(x => x.GetDashboardAsync(
            HistoricalDate, null, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.GetOpenItemsAsync(
            null, null, HistoricalDate, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.GetTrackedItemsAsync(
            null, HistoricalDate, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.GetTrackingSummaryAsync(
            HistoricalDate, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.GetTrackingLifecycleAsync(
            HistoricalDate.AddDays(-7), HistoricalDate, null, null,
            It.IsAny<CancellationToken>()), Times.Once);

        var viewSource = File.ReadAllText(GetRepositoryPath(
            "src", "KasaManager.Web", "Views", "HesapKontrol", "Index.cshtml"));
        Assert.Contains("value=\"@Model.AnalizTarihi.ToString(\"yyyy-MM-dd\")\"", viewSource);
    }

    [Fact]
    public async Task SavedKasaHesapKontrolRedirect_PreservesSnapshotReportDate()
    {
        var helperSource = File.ReadAllText(GetRepositoryPath(
            "src", "KasaManager.Web", "Controllers", "KasaPreviewController.Helpers.cs"));
        var actionStart = helperSource.IndexOf(
            "public async Task<IActionResult> RunHesapKontrolFromSavedSnapshot", StringComparison.Ordinal);
        var actionEnd = helperSource.IndexOf(
            "private IActionResult FailSavedSnapshotTransition", actionStart, StringComparison.Ordinal);
        Assert.True(actionStart >= 0 && actionEnd > actionStart);
        var actionBlock = helperSource[actionStart..actionEnd];
        Assert.Contains("savedSnapshot.RaporTarihi, sourceSnapshot, actorUserId, ct", actionBlock);
        Assert.Contains("RedirectToAction(\"QueryDate\", \"HesapKontrol\"", actionBlock);
        Assert.Contains("tarih = savedSnapshot.RaporTarihi.ToString(\"yyyy-MM-dd\")", actionBlock);

        var service = CreateReadService();
        service.Setup(x => x.GetDashboardForDateAsync(
                HistoricalDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot());
        var controller = CreateController(service);

        var result = await controller.QueryDate("2026-07-13", "takipte", CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<HesapKontrolViewModel>(view.Model);
        Assert.Equal(HistoricalDate, model.AnalizTarihi);
        Assert.Equal("takipte", model.ActiveTab);
    }

    [Fact]
    public void UserIsolation_RemainsEnforced()
    {
        var authorize = typeof(HesapKontrolController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);

        Assert.NotEmpty(authorize);
    }

    [Fact]
    public async Task GetActions_RemainReadOnly()
    {
        var service = CreateReadService();
        service.Setup(x => x.GetDashboardForDateAsync(
                HistoricalDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot());
        var controller = CreateController(service);

        await InvokeIndexAsync(controller, tab: "ozet");
        await controller.QueryDate("2026-07-13", "ozet", CancellationToken.None);

        Assert.NotEmpty(typeof(HesapKontrolController).GetMethod(nameof(HesapKontrolController.Index))!
            .GetCustomAttributes(typeof(HttpGetAttribute), inherit: true));
        Assert.NotEmpty(typeof(HesapKontrolController).GetMethod(nameof(HesapKontrolController.QueryDate))!
            .GetCustomAttributes(typeof(HttpGetAttribute), inherit: true));
        service.Verify(x => x.AnalyzeFromComparisonAsync(
            It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Task<IActionResult> InvokeIndexAsync(
        HesapKontrolController controller,
        string tab,
        bool includeDateRange = true)
        => controller.Index(
            tab,
            "2026-07-13",
            hesapTuru: null,
            durum: null,
            takipDurum: null,
            baslangic: includeDateRange ? "2026-07-13" : null,
            bitis: includeDateRange ? "2026-07-13" : null,
            arama: null,
            ct: CancellationToken.None);

    private static Mock<IBankaHesapKontrolService> CreateReadService(
        HesapKontrolDashboard? dashboard = null,
        List<HesapKontrolKaydi>? open = null,
        List<HesapKontrolKaydi>? tracked = null,
        TakipOzeti? trackingSummary = null)
    {
        var service = new Mock<IBankaHesapKontrolService>();
        service.Setup(x => x.GetDashboardAsync(
                HistoricalDate, It.IsAny<BankaHesapTuru?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dashboard ?? new HesapKontrolDashboard(0, 0, 0, 0, 0, 0, 0, 0, 0));
        service.Setup(x => x.GetOpenItemsAsync(
                It.IsAny<BankaHesapTuru?>(), It.IsAny<DateOnly?>(), HistoricalDate,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(open ?? new());
        service.Setup(x => x.GetTrackedItemsAsync(
                It.IsAny<BankaHesapTuru?>(), HistoricalDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tracked ?? new());
        service.Setup(x => x.GetHistoryAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<BankaHesapTuru?>(),
                It.IsAny<KayitDurumu?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HesapKontrolKaydi>());
        service.Setup(x => x.GetTrackingLifecycleAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<BankaHesapTuru?>(),
                It.IsAny<KayitDurumu?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HesapKontrolKaydi>());
        service.Setup(x => x.GetTrackingSummaryAsync(
                HistoricalDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(trackingSummary ?? new TakipOzeti(0, 0, 0, 0, 0, new(), 0, new()));
        return service;
    }

    private static HesapKontrolController CreateController(Mock<IBankaHesapKontrolService> service)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(x => x.WebRootPath).Returns(Path.GetTempPath());
        var controller = new HesapKontrolController(
            service.Object,
            Mock.Of<ICurrentUser>(u => u.IsAuthenticated && u.UserId == 17 && u.Username == "test-user"),
            Mock.Of<IHesapKontrolExportService>(),
            Mock.Of<IFinansalIstisnaService>(),
            Mock.Of<IHesapKontrolSourceResolver>(),
            NullLogger<HesapKontrolController>.Instance,
            env.Object,
            Mock.Of<IManualResolveWriteBusinessDateResolver>());
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    private static HesapKontrolKaydi CreateRecord(KayitDurumu durum, decimal tutar)
        => new()
        {
            AnalizTarihi = HistoricalDate,
            HesapTuru = BankaHesapTuru.Tahsilat,
            Yon = KayitYonu.Eksik,
            Tutar = tutar,
            Sinif = FarkSinifi.Bilinmeyen,
            Durum = durum
        };

    private static HesapKontrolDateSnapshot CreateSnapshot()
        => new(
            HistoricalDate,
            new HesapKontrolDashboard(0, 0, 0, 0, 0, 0, 0, 0, 0),
            new(), new(), new(), new(), new(),
            new HesapKontrolSnapshotSummary(0, 0, 0, 0, 0, 0, 0, 0, 0),
            new EksikFazlaAutoFill(0, 0, 0, 0, 0, 0, false, null),
            "Historical snapshot");

    private static string GetRepositoryPath(params string[] segments)
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(new[] { root }.Concat(segments).ToArray());
    }
}
