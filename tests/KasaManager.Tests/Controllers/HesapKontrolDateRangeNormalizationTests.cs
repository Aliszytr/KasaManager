using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Web.Controllers;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KasaManager.Tests.Controllers;

public sealed class HesapKontrolDateRangeNormalizationTests
{
    private static readonly DateOnly AnalysisDate = new(2026, 7, 13);
    private const string ValidationMessage =
        "Başlangıç tarihi bitiş tarihinden sonra olamaz ve tarih aralığı seçilen analiz tarihini aşamaz.";

    [Fact]
    public async Task Index_WithoutDates_UsesAnalysisDateDefaultRange()
    {
        var service = CreateReadService();
        var controller = CreateController(service);

        var model = await InvokeIndexAndGetModelAsync(controller, "gecmis", "2026-07-13", null, null);

        Assert.Equal(new DateOnly(2026, 7, 6), model.FilterBaslangic);
        Assert.Equal(AnalysisDate, model.FilterBitis);
        VerifyHistory(service, new DateOnly(2026, 7, 6), AnalysisDate);
    }

    [Fact]
    public async Task Index_WithoutAnalysisDate_UsesTodayAndDefaultRange()
    {
        var service = CreateReadService();
        var controller = CreateController(service);
        var before = DateOnly.FromDateTime(DateTime.Now);

        var model = await InvokeIndexAndGetModelAsync(controller, "gecmis", null, null, null);

        var after = DateOnly.FromDateTime(DateTime.Now);
        Assert.True(model.AnalizTarihi == before || model.AnalizTarihi == after);
        Assert.Equal(model.AnalizTarihi.AddDays(-7), model.FilterBaslangic);
        Assert.Equal(model.AnalizTarihi, model.FilterBitis);
        VerifyHistory(service, model.FilterBaslangic, model.FilterBitis);
    }

    [Fact]
    public async Task Index_WithOnlyStartDate_UsesAnalysisDateAsEnd()
    {
        var service = CreateReadService();
        var controller = CreateController(service);

        var model = await InvokeIndexAndGetModelAsync(
            controller, "gecmis", "2026-07-13", "2026-07-01", null);

        Assert.Equal(new DateOnly(2026, 7, 1), model.FilterBaslangic);
        Assert.Equal(AnalysisDate, model.FilterBitis);
        VerifyHistory(service, new DateOnly(2026, 7, 1), AnalysisDate);
    }

    [Fact]
    public async Task Index_WithOnlyEndDate_DerivesStartFromEndDate()
    {
        var service = CreateReadService();
        var controller = CreateController(service);

        var model = await InvokeIndexAndGetModelAsync(
            controller, "gecmis", "2026-07-13", null, "2026-07-01");

        Assert.Equal(new DateOnly(2026, 6, 24), model.FilterBaslangic);
        Assert.Equal(new DateOnly(2026, 7, 1), model.FilterBitis);
        VerifyHistory(service, new DateOnly(2026, 6, 24), new DateOnly(2026, 7, 1));
    }

    [Fact]
    public async Task Index_WithExplicitValidStartAndEnd_PreservesBoth()
    {
        var service = CreateReadService();
        var controller = CreateController(service);

        var model = await InvokeIndexAndGetModelAsync(
            controller, "gecmis", "2026-07-13", "2026-07-01", "2026-07-10");

        Assert.Equal(new DateOnly(2026, 7, 1), model.FilterBaslangic);
        Assert.Equal(new DateOnly(2026, 7, 10), model.FilterBitis);
        VerifyHistory(service, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 10));
    }

    [Fact]
    public async Task Index_WithStartAfterEnd_ReturnsValidationAndSkipsRangeQueries()
    {
        var service = CreateReadService();
        var controller = CreateController(service);

        var model = await InvokeIndexAndGetModelAsync(
            controller, "takipte", "2026-07-13", "2026-07-10", "2026-07-01");

        Assert.Equal(ValidationMessage, controller.ViewData["DateRangeValidationError"]);
        Assert.Equal(new DateOnly(2026, 7, 10), model.FilterBaslangic);
        Assert.Equal(new DateOnly(2026, 7, 1), model.FilterBitis);
        VerifyRangeQueriesNeverCalled(service);
    }

    [Fact]
    public async Task Index_WithStartAfterAnalysisDate_IsRejected()
    {
        var service = CreateReadService();
        var controller = CreateController(service);

        await InvokeIndexAndGetModelAsync(
            controller, "gecmis", "2026-07-13", "2026-07-14", null);

        Assert.Equal(ValidationMessage, controller.ViewData["DateRangeValidationError"]);
        VerifyRangeQueriesNeverCalled(service);
    }

    [Fact]
    public async Task Index_WithEndAfterAnalysisDate_IsRejected()
    {
        var service = CreateReadService();
        var controller = CreateController(service);

        await InvokeIndexAndGetModelAsync(
            controller, "takipte", "2026-07-13", null, "2026-07-14");

        Assert.Equal(ValidationMessage, controller.ViewData["DateRangeValidationError"]);
        VerifyRangeQueriesNeverCalled(service);
    }

    [Fact]
    public async Task Index_HistoryTab_UsesNormalizedRange()
    {
        var service = CreateReadService();
        var controller = CreateController(service);

        await InvokeIndexAndGetModelAsync(
            controller, "gecmis", "2026-07-13", null, "2026-07-01");

        VerifyHistory(service, new DateOnly(2026, 6, 24), new DateOnly(2026, 7, 1));
        service.Verify(x => x.GetTrackingLifecycleAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<BankaHesapTuru?>(),
            It.IsAny<KayitDurumu?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Index_TrackingTab_UsesNormalizedRange()
    {
        var service = CreateReadService();
        var controller = CreateController(service);
        var start = new DateOnly(2026, 6, 24);
        var end = new DateOnly(2026, 7, 1);

        await InvokeIndexAndGetModelAsync(
            controller, "takipte", "2026-07-13", null, "2026-07-01");

        VerifyHistory(service, start, end);
        service.Verify(x => x.GetTrackingLifecycleAsync(
            start, end, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Index_OpenTab_UsesSelectedAnalysisDate()
    {
        var service = CreateReadService();
        var controller = CreateController(service);

        await InvokeIndexAndGetModelAsync(controller, "acik", "2026-07-13", null, null);

        service.Verify(x => x.GetOpenItemsAsync(
            null, null, AnalysisDate, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PdfExport_PreservesExistingIndependentDateContract()
    {
        var service = CreateReadService();
        var export = new Mock<IHesapKontrolExportService>();
        export.Setup(x => x.ExportToPdfAsync(
                It.IsAny<HesapKontrolDashboard>(), It.IsAny<List<HesapKontrolKaydi>>(),
                It.IsAny<List<HesapKontrolKaydi>>(), new DateOnly(2026, 7, 10),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1, 2, 3 });
        var controller = CreateController(service, export);

        var result = await controller.ExportPdf(
            "2026-07-01", "2026-07-10", null, null, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("hesap-kontrol-20260701-20260710.pdf", file.FileDownloadName);
        VerifyHistory(service, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 10));
        export.Verify(x => x.ExportToPdfAsync(
            It.IsAny<HesapKontrolDashboard>(), It.IsAny<List<HesapKontrolKaydi>>(),
            It.IsAny<List<HesapKontrolKaydi>>(), new DateOnly(2026, 7, 10),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CsvExport_PreservesExistingIndependentDateContract()
    {
        var service = CreateReadService();
        var controller = CreateController(service);

        var result = await controller.ExportCsv(
            "2026-07-01", "2026-07-10", null, null, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("hesap-kontrol-20260701-20260710.csv", file.FileDownloadName);
        VerifyHistory(service, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 10));
    }

    [Fact]
    public async Task GetActions_RemainReadOnly()
    {
        var service = CreateReadService();
        var controller = CreateController(service);

        await InvokeIndexAndGetModelAsync(controller, "ozet", "2026-07-13", null, null);

        Assert.All(service.Invocations,
            invocation => Assert.StartsWith("Get", invocation.Method.Name));
    }

    [Fact]
    public async Task InvalidDateRange_DoesNotUsePersistentTempData()
    {
        var service = CreateReadService();
        var controller = CreateController(service);

        await InvokeIndexAndGetModelAsync(
            controller, "gecmis", "2026-07-13", "2026-07-10", "2026-07-01");

        Assert.False(controller.TempData.ContainsKey("Error"));
        Assert.Equal(ValidationMessage, controller.ViewData["DateRangeValidationError"]);
    }

    [Fact]
    public async Task InvalidDateRange_ShowsUserFriendlyMessage()
    {
        var service = CreateReadService();
        var controller = CreateController(service);

        await InvokeIndexAndGetModelAsync(
            controller, "gecmis", "2026-07-13", "2026-07-10", "2026-07-01");

        Assert.Equal(ValidationMessage, controller.ViewData["DateRangeValidationError"]);
    }

    [Fact]
    public async Task InvalidDateRange_PreservesSubmittedDates()
    {
        var service = CreateReadService();
        var controller = CreateController(service);

        var model = await InvokeIndexAndGetModelAsync(
            controller, "gecmis", "2026-07-13", "2026-07-10", "2026-07-01");

        Assert.Equal(AnalysisDate, model.AnalizTarihi);
        Assert.Equal(new DateOnly(2026, 7, 10), model.FilterBaslangic);
        Assert.Equal(new DateOnly(2026, 7, 1), model.FilterBitis);
    }

    [Fact]
    public async Task InvalidDateRange_SkipsAllServiceQueries()
    {
        var service = CreateReadService();
        var controller = CreateController(service);

        await InvokeIndexAndGetModelAsync(
            controller, "takipte", "2026-07-13", "2026-07-10", "2026-07-01");

        Assert.Empty(service.Invocations);
    }

    [Fact]
    public async Task IndexGet_RemainsDatabaseWriteFree()
    {
        var service = CreateReadService();
        var export = new Mock<IHesapKontrolExportService>();
        var financialException = new Mock<IFinansalIstisnaService>();
        var sourceResolver = new Mock<IHesapKontrolSourceResolver>();
        var controller = CreateController(service, export, financialException, sourceResolver);

        await InvokeIndexAndGetModelAsync(controller, "ozet", "2026-07-13", null, null);

        Assert.NotEmpty(service.Invocations);
        Assert.All(service.Invocations,
            invocation => Assert.StartsWith("Get", invocation.Method.Name));
        Assert.Empty(export.Invocations);
        Assert.Empty(financialException.Invocations);
        Assert.Empty(sourceResolver.Invocations);
    }

    private static async Task<HesapKontrolViewModel> InvokeIndexAndGetModelAsync(
        HesapKontrolController controller,
        string tab,
        string? analysisDate,
        string? start,
        string? end)
    {
        var result = await controller.Index(
            tab,
            analysisDate,
            hesapTuru: null,
            durum: null,
            takipDurum: null,
            baslangic: start,
            bitis: end,
            arama: null,
            ct: CancellationToken.None);

        return Assert.IsType<HesapKontrolViewModel>(Assert.IsType<ViewResult>(result).Model);
    }

    private static Mock<IBankaHesapKontrolService> CreateReadService()
    {
        var service = new Mock<IBankaHesapKontrolService>();
        service.Setup(x => x.GetDashboardAsync(
                It.IsAny<DateOnly?>(), It.IsAny<BankaHesapTuru?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HesapKontrolDashboard(0, 0, 0, 0, 0, 0, 0, 0, 0));
        service.Setup(x => x.GetOpenItemsAsync(
                It.IsAny<BankaHesapTuru?>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HesapKontrolKaydi>());
        service.Setup(x => x.GetTrackedItemsAsync(
                It.IsAny<BankaHesapTuru?>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HesapKontrolKaydi>());
        service.Setup(x => x.GetHistoryAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<BankaHesapTuru?>(),
                It.IsAny<KayitDurumu?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HesapKontrolKaydi>());
        service.Setup(x => x.GetTrackingLifecycleAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<BankaHesapTuru?>(),
                It.IsAny<KayitDurumu?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HesapKontrolKaydi>());
        service.Setup(x => x.GetTrackingSummaryAsync(
                It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TakipOzeti(0, 0, 0, 0, 0, new(), 0, new()));
        return service;
    }

    private static HesapKontrolController CreateController(
        Mock<IBankaHesapKontrolService> service,
        Mock<IHesapKontrolExportService>? export = null,
        Mock<IFinansalIstisnaService>? financialException = null,
        Mock<IHesapKontrolSourceResolver>? sourceResolver = null)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(x => x.WebRootPath).Returns(Path.GetTempPath());
        var controller = new HesapKontrolController(
            service.Object,
            Mock.Of<ICurrentUser>(u => u.IsAuthenticated && u.UserId == 17 && u.Username == "test-user"),
            export?.Object ?? Mock.Of<IHesapKontrolExportService>(),
            financialException?.Object ?? Mock.Of<IFinansalIstisnaService>(),
            sourceResolver?.Object ?? Mock.Of<IHesapKontrolSourceResolver>(),
            NullLogger<HesapKontrolController>.Instance,
            env.Object);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    private static void VerifyHistory(
        Mock<IBankaHesapKontrolService> service,
        DateOnly start,
        DateOnly end)
        => service.Verify(x => x.GetHistoryAsync(
            start, end, null, null, It.IsAny<CancellationToken>()), Times.Once);

    private static void VerifyRangeQueriesNeverCalled(Mock<IBankaHesapKontrolService> service)
    {
        service.Verify(x => x.GetHistoryAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<BankaHesapTuru?>(),
            It.IsAny<KayitDurumu?>(), It.IsAny<CancellationToken>()), Times.Never);
        service.Verify(x => x.GetTrackingLifecycleAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<BankaHesapTuru?>(),
            It.IsAny<KayitDurumu?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
