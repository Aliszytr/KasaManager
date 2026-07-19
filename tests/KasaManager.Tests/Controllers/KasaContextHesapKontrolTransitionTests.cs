using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Orchestration;
using KasaManager.Application.Services;
using KasaManager.Application.Services.ReadAdapter;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Domain.Validation;
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
using Moq;

namespace KasaManager.Tests.Controllers;

public sealed class KasaContextHesapKontrolTransitionTests
{
    private static readonly DateOnly TestDate = new(2026, 6, 3);

    [Theory]
    [InlineData("Sabah")]
    [InlineData("Aksam")]
    public async Task VerifiedServerDraft_RunsAnalysisWithExactCurrentBundle(string kasaType)
    {
        var fixture = CreateFixture("user-a");
        try
        {
            var context = CreateSourceContext(fixture.UploadFolder, kasaType);
            await SaveSuccessfulDraftAsync("user-a", kasaType, context);
            fixture.Service
                .Setup(x => x.AnalyzeFromComparisonAsync(
                    TestDate, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DateOnly _, string folder, int _, CancellationToken _) =>
                {
                    Assert.NotEqual(fixture.UploadFolder, folder);
                    Assert.Equal(context.Fingerprint,
                        CreateSourceContext(folder, kasaType).Fingerprint);
                    return CreateReport();
                });

            var result = await fixture.Controller.RunHesapKontrolFromContext(
                kasaType, CancellationToken.None);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Equal("HesapKontrol", redirect.ControllerName);
            Assert.Equal(TestDate.ToString("yyyy-MM-dd"),
                redirect.RouteValues!["analizTarihiStr"]);
            fixture.Service.Verify(x => x.AnalyzeFromComparisonAsync(
                TestDate, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            await KasaDraftCacheHelper.ClearDraftAsync("user-a", kasaType);
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task ChangedBundle_FailsClosedBeforeAnalysis()
    {
        var fixture = CreateFixture("user-a");
        try
        {
            var context = CreateSourceContext(fixture.UploadFolder, "Sabah");
            await SaveSuccessfulDraftAsync("user-a", "Sabah", context);
            await File.WriteAllTextAsync(
                Path.Combine(fixture.UploadFolder, "bundle.xlsx"), "changed-content");

            var result = await fixture.Controller.RunHesapKontrolFromContext(
                "Sabah", CancellationToken.None);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Null(redirect.ControllerName);
            Assert.NotNull(fixture.Controller.TempData["Error"]);
            fixture.Service.Verify(x => x.AnalyzeFromComparisonAsync(
                It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            await KasaDraftCacheHelper.ClearDraftAsync("user-a", "Sabah");
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task AnotherUsersDraft_CannotBeSelectedByPostedKasaType()
    {
        var fixture = CreateFixture("user-b");
        try
        {
            var context = CreateSourceContext(fixture.UploadFolder, "Aksam");
            await SaveSuccessfulDraftAsync("user-a", "Aksam", context);

            await fixture.Controller.RunHesapKontrolFromContext(
                "Aksam", CancellationToken.None);

            Assert.NotNull(fixture.Controller.TempData["Error"]);
            fixture.Service.Verify(x => x.AnalyzeFromComparisonAsync(
                It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            await KasaDraftCacheHelper.ClearDraftAsync("user-a", "Aksam");
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task LegacyDraftWithoutSourceContext_FailsClosedBeforeAnalysis()
    {
        var fixture = CreateFixture("user-a");
        try
        {
            await SaveSuccessfulDraftAsync("user-a", "Aksam", sourceContext: null);

            await fixture.Controller.RunHesapKontrolFromContext(
                "Aksam", CancellationToken.None);

            Assert.NotNull(fixture.Controller.TempData["Error"]);
            fixture.Service.Verify(x => x.AnalyzeFromComparisonAsync(
                It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            await KasaDraftCacheHelper.ClearDraftAsync("user-a", "Aksam");
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task MissingDraft_FailsClosedBeforeAnalysis()
    {
        var fixture = CreateFixture("missing-user");
        try
        {
            await fixture.Controller.RunHesapKontrolFromContext(
                "Sabah", CancellationToken.None);

            Assert.NotNull(fixture.Controller.TempData["Error"]);
            fixture.Service.Verify(x => x.AnalyzeFromComparisonAsync(
                It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task DraftWithoutResults_FailsClosedBeforeAnalysis()
    {
        var fixture = CreateFixture("user-a");
        try
        {
            var context = CreateSourceContext(fixture.UploadFolder, "Sabah");
            await KasaDraftCacheHelper.SaveDraftAsync(
                "user-a", "Sabah",
                new KasaPreviewViewModel
                {
                    SelectedDate = TestDate,
                    KasaType = "Sabah",
                    HasResults = false
                },
                sourceContext: context);

            await fixture.Controller.RunHesapKontrolFromContext(
                "Sabah", CancellationToken.None);

            Assert.NotNull(fixture.Controller.TempData["Error"]);
            fixture.Service.Verify(x => x.AnalyzeFromComparisonAsync(
                It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            await KasaDraftCacheHelper.ClearDraftAsync("user-a", "Sabah");
            fixture.Dispose();
        }
    }

    [Theory]
    [InlineData("Genel")]
    [InlineData("../../Aksam")]
    [InlineData("")]
    public async Task ManipulatedKasaType_IsRejected(string kasaType)
    {
        var fixture = CreateFixture("user-a");
        try
        {
            await fixture.Controller.RunHesapKontrolFromContext(
                kasaType, CancellationToken.None);

            Assert.NotNull(fixture.Controller.TempData["Error"]);
            fixture.Service.Verify(x => x.AnalyzeFromComparisonAsync(
                It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task CacheKeyAndDraftKasaTypeMismatch_IsRejected()
    {
        var fixture = CreateFixture("user-a");
        try
        {
            var context = CreateSourceContext(fixture.UploadFolder, "Aksam");
            await KasaDraftCacheHelper.SaveDraftAsync(
                "user-a", "Aksam",
                new KasaPreviewViewModel
                {
                    SelectedDate = TestDate,
                    KasaType = "Sabah",
                    HasResults = true
                },
                sourceContext: context);

            await fixture.Controller.RunHesapKontrolFromContext(
                "Aksam", CancellationToken.None);

            Assert.NotNull(fixture.Controller.TempData["Error"]);
            fixture.Service.Verify(x => x.AnalyzeFromComparisonAsync(
                It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            await KasaDraftCacheHelper.ClearDraftAsync("user-a", "Aksam");
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task SameContextTwice_PassesSameServerIdentityToIdempotentService()
    {
        var fixture = CreateFixture("user-a");
        try
        {
            var context = CreateSourceContext(fixture.UploadFolder, "Aksam");
            await SaveSuccessfulDraftAsync("user-a", "Aksam", context);
            fixture.Service
                .Setup(x => x.AnalyzeFromComparisonAsync(
                    TestDate, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DateOnly _, string folder, int _, CancellationToken _) =>
                {
                    Assert.Equal(context.Fingerprint,
                        CreateSourceContext(folder, "Aksam").Fingerprint);
                    return CreateReport();
                });

            await fixture.Controller.RunHesapKontrolFromContext(
                "Aksam", CancellationToken.None);
            await fixture.Controller.RunHesapKontrolFromContext(
                "Aksam", CancellationToken.None);

            fixture.Service.Verify(x => x.AnalyzeFromComparisonAsync(
                TestDate, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
        finally
        {
            await KasaDraftCacheHelper.ClearDraftAsync("user-a", "Aksam");
            fixture.Dispose();
        }
    }

    [Fact]
    public void FormAndActionContract_IsPostOnlyAndDoesNotExposeSourceOrDate()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var viewSource = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "KasaManager.Web", "Views", "KasaPreview", "Index.cshtml"));
        var formStart = viewSource.IndexOf(
            "<form id=\"kasaHesapKontrolContextForm\"", StringComparison.Ordinal);
        var formEnd = viewSource.IndexOf("</form>", formStart, StringComparison.Ordinal);
        Assert.True(formStart >= 0 && formEnd > formStart);
        var form = viewSource[formStart..formEnd];

        Assert.Contains("asp-action=\"RunHesapKontrolFromContext\"", form);
        Assert.Contains("method=\"post\"", form);
        Assert.Contains("AntiForgeryToken", form);
        Assert.Contains("name=\"kasaType\"", form);
        Assert.DoesNotContain("name=\"tarih\"", form, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("path", form, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("source", form, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/HesapKontrol/QueryDate?tarih=", viewSource);

        var action = typeof(KasaPreviewController).GetMethod(
            nameof(KasaPreviewController.RunHesapKontrolFromContext));
        Assert.NotNull(action);
        Assert.NotEmpty(action!.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true));
        Assert.NotEmpty(action.GetCustomAttributes(
            typeof(ValidateAntiForgeryTokenAttribute), inherit: true));
    }

    [Fact]
    public void ExistingGetManualAndComparisonContracts_RemainSeparate()
    {
        var index = typeof(HesapKontrolController).GetMethod(nameof(HesapKontrolController.Index));
        var queryDate = typeof(HesapKontrolController).GetMethod(nameof(HesapKontrolController.QueryDate));
        var runAnalysis = typeof(HesapKontrolController).GetMethod(nameof(HesapKontrolController.RunAnalysis));
        Assert.NotEmpty(index!.GetCustomAttributes(typeof(HttpGetAttribute), inherit: true));
        Assert.NotEmpty(queryDate!.GetCustomAttributes(typeof(HttpGetAttribute), inherit: true));
        Assert.NotEmpty(runAnalysis!.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true));
        Assert.NotEmpty(runAnalysis.GetCustomAttributes(
            typeof(ValidateAntiForgeryTokenAttribute), inherit: true));

        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var comparisonView = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "KasaManager.Web", "Views", "Comparison", "Results.cshtml"));
        Assert.Contains("asp-action=\"RunAnalysis\" method=\"post\"", comparisonView);
    }

    private static async Task SaveSuccessfulDraftAsync(
        string userName,
        string kasaType,
        KasaDraftSourceContext? sourceContext)
    {
        await KasaDraftCacheHelper.SaveDraftAsync(
            userName,
            kasaType,
            new KasaPreviewViewModel
            {
                SelectedDate = TestDate,
                KasaType = kasaType,
                HasResults = true
            },
            sourceContext: sourceContext);
    }

    private static KasaDraftSourceContext CreateSourceContext(
        string folder,
        string kasaType)
    {
        var files = Directory.GetFiles(folder, "*.xls*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var manifest = new StringBuilder();
        var fileNames = new List<string>();
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            fileNames.Add(fileName);
            manifest.Append(fileName.ToUpperInvariant())
                .Append(':')
                .Append(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))))
                .Append('\n');
        }

        return new KasaDraftSourceContext(
            1,
            TestDate,
            kasaType,
            nameof(HesapKontrolSourceKind.Current),
            "current-upload-bundle",
            fileNames,
            Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(manifest.ToString()))));
    }

    private static TestFixture CreateFixture(string userName)
    {
        var webRoot = Path.Combine(Path.GetTempPath(), $"kasa_context_{Guid.NewGuid():N}");
        var uploadFolder = Path.Combine(webRoot, "Data", "Raporlar");
        Directory.CreateDirectory(uploadFolder);
        File.WriteAllText(Path.Combine(uploadFolder, "bundle.xlsx"), "original-content");

        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(x => x.WebRootPath).Returns(webRoot);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Upload:SubFolder"] = @"Data\Raporlar"
            })
            .Build();
        var service = new Mock<IBankaHesapKontrolService>();
        var controller = new KasaPreviewController(
            Mock.Of<IKasaOrchestrator>(),
            env.Object,
            configuration,
            Mock.Of<IImportOrchestrator>(),
            Mock.Of<IKasaReportDateRulesService>(),
            Mock.Of<IKasaGlobalDefaultsService>(),
            service.Object,
            Mock.Of<ICurrentUser>(u => u.IsAuthenticated && u.UserId == 17 && u.Username == userName),
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
            Mock.Of<IKasaRaporSnapshotService>());

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, userName) }, "TestAuth"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(
            httpContext, Mock.Of<ITempDataProvider>());

        return new TestFixture(webRoot, uploadFolder, controller, service);
    }

    private static HesapKontrolRapor CreateReport() => new(
        TestDate, 0, 0, 0, 0, 0,
        new StopajVirmanDurum(false, 0, null, "Test", StopajStatus.Ok),
        new(), new(), "Test rapor");

    private sealed record TestFixture(
        string WebRoot,
        string UploadFolder,
        KasaPreviewController Controller,
        Mock<IBankaHesapKontrolService> Service) : IDisposable
    {
        public void Dispose()
        {
            if (Directory.Exists(WebRoot))
                Directory.Delete(WebRoot, recursive: true);
        }
    }
}
