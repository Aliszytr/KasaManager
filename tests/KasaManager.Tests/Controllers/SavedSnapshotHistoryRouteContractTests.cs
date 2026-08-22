using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
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

public sealed class SavedSnapshotHistoryRouteContractTests
{
    private static readonly DateOnly HistoricalDate = new(2026, 7, 13);

    [Fact]
    public void SavedAndLiveKasaForms_HaveDiscriminatedMinimalContracts()
    {
        var viewSource = File.ReadAllText(GetRepositoryPath(
            "src", "KasaManager.Web", "Views", "KasaPreview", "Index.cshtml"));
        var savedForm = ExtractForm(viewSource, "RunHesapKontrolFromSavedSnapshot");
        var liveForm = ExtractForm(viewSource, "RunHesapKontrolFromContext");

        Assert.Contains("method=\"post\"", savedForm);
        Assert.Contains("AntiForgeryToken", savedForm);
        Assert.Contains("name=\"savedSnapshotId\"", savedForm);
        Assert.DoesNotContain("name=\"kasaType\"", savedForm);
        Assert.DoesNotContain("name=\"tarih\"", savedForm);
        Assert.DoesNotContain("path", savedForm, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fingerprint", savedForm, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("name=\"kasaType\"", liveForm);
        Assert.DoesNotContain("savedSnapshotId", liveForm);
    }

    [Fact]
    public void HistoryFilter_TargetsIndexAndPreservesHistoricalContext()
    {
        var viewSource = File.ReadAllText(GetRepositoryPath(
            "src", "KasaManager.Web", "Views", "HesapKontrol", "Index.cshtml"));
        var historyMarker = "<!-- ═══════ TAB: Geçmiş ═══════ -->";
        var historyStart = viewSource.IndexOf(historyMarker, StringComparison.Ordinal);
        Assert.True(historyStart >= 0);
        var historySection = viewSource[historyStart..];
        var form = ExtractForm(historySection, "asp-action=\"Index\"");

        Assert.Contains("asp-controller=\"HesapKontrol\"", form);
        Assert.Contains("method=\"get\"", form);
        Assert.Contains("name=\"tab\" value=\"gecmis\"", form);
        Assert.Contains("name=\"analizTarihiStr\"", form);
        Assert.Contains("name=\"baslangic\"", form);
        Assert.Contains("name=\"bitis\"", form);
        Assert.Contains("name=\"hesapTuru\"", form);
        Assert.Contains("name=\"durum\"", form);
        Assert.Contains("name=\"arama\"", form);
        Assert.DoesNotContain("asp-action=\"QueryDate\"", form);
        Assert.DoesNotContain("name=\"tarih\"", form);
    }

    [Fact]
    public async Task HistoryDateRange_ReturnsNormalViewInsteadOfQueryDateBadRequest()
    {
        var service = CreateReadOnlyService();
        var controller = CreateHesapKontrolController(service);

        var result = await controller.Index(
            tab: "gecmis",
            analizTarihiStr: "2026-07-13",
            hesapTuru: null,
            durum: null,
            takipDurum: null,
            baslangic: "2026-07-13",
            bitis: "2026-07-13",
            arama: null,
            ct: CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<HesapKontrolViewModel>(view.Model);
        Assert.Equal(HistoricalDate, model.AnalizTarihi);
        Assert.Equal(HistoricalDate, model.FilterBaslangic);
        Assert.Equal(HistoricalDate, model.FilterBitis);
        service.Verify(x => x.GetHistoryAsync(
            HistoricalDate, HistoricalDate, null, null, It.IsAny<CancellationToken>()),
            Times.Once);
        service.Verify(x => x.AnalyzeFromComparisonAsync(
            It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void GetAndPostContracts_RemainExplicitAndLoadSnapshotDoesNotWriteDraft()
    {
        var loadSnapshot = typeof(KasaPreviewController).GetMethod(
            nameof(KasaPreviewController.LoadSnapshot));
        var savedPost = typeof(KasaPreviewController).GetMethod(
            nameof(KasaPreviewController.RunHesapKontrolFromSavedSnapshot));
        var livePost = typeof(KasaPreviewController).GetMethod(
            nameof(KasaPreviewController.RunHesapKontrolFromContext));
        var index = typeof(HesapKontrolController).GetMethod(
            nameof(HesapKontrolController.Index));
        var queryDate = typeof(HesapKontrolController).GetMethod(
            nameof(HesapKontrolController.QueryDate));

        Assert.NotEmpty(loadSnapshot!.GetCustomAttributes(typeof(HttpGetAttribute), true));
        Assert.NotEmpty(index!.GetCustomAttributes(typeof(HttpGetAttribute), true));
        Assert.NotEmpty(queryDate!.GetCustomAttributes(typeof(HttpGetAttribute), true));
        Assert.NotEmpty(savedPost!.GetCustomAttributes(typeof(HttpPostAttribute), true));
        Assert.NotEmpty(savedPost.GetCustomAttributes(
            typeof(ValidateAntiForgeryTokenAttribute), true));
        Assert.NotEmpty(livePost!.GetCustomAttributes(typeof(HttpPostAttribute), true));

        var controllerSource = File.ReadAllText(GetRepositoryPath(
            "src", "KasaManager.Web", "Controllers", "KasaPreviewController.cs"));
        var loadStart = controllerSource.IndexOf(
            "public async Task<IActionResult> LoadSnapshot", StringComparison.Ordinal);
        var loadEnd = controllerSource.IndexOf(
            "public async Task<IActionResult> DeleteSnapshot", loadStart, StringComparison.Ordinal);
        Assert.True(loadStart >= 0 && loadEnd > loadStart);
        var loadBlock = controllerSource[loadStart..loadEnd];
        Assert.DoesNotContain("SaveDraftAsync", loadBlock);
        Assert.DoesNotContain("AnalyzeFromComparisonAsync", loadBlock);
    }

    private static Mock<IBankaHesapKontrolService> CreateReadOnlyService()
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
                It.IsAny<BankaHesapTuru?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HesapKontrolKaydi>());
        service.Setup(x => x.GetHistoryAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<BankaHesapTuru?>(),
                It.IsAny<KayitDurumu?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HesapKontrolKaydi>());
        service.Setup(x => x.GetTrackingSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TakipOzeti(0, 0, 0, 0, 0, new(), 0, new()));
        return service;
    }

    private static HesapKontrolController CreateHesapKontrolController(
        Mock<IBankaHesapKontrolService> service)
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
        controller.TempData = new TempDataDictionary(
            httpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    private static string ExtractForm(string source, string marker)
    {
        var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Form marker bulunamadı: {marker}");
        var formStart = source.LastIndexOf("<form", markerIndex, StringComparison.Ordinal);
        var formEnd = source.IndexOf("</form>", markerIndex, StringComparison.Ordinal);
        Assert.True(formStart >= 0 && formEnd > markerIndex);
        return source[formStart..formEnd];
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(new[] { root }.Concat(segments).ToArray());
    }
}
