using System.Security.Claims;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Orchestration;
using KasaManager.Application.Services;
using KasaManager.Application.Services.ReadAdapter;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Domain.Reports.Snapshots;
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

public sealed class SavedSnapshotHesapKontrolTransitionTests
{
    private static readonly DateOnly SavedDate = new(2026, 7, 13);
    private static readonly DateOnly StaleDraftDate = new(2026, 7, 17);

    [Fact]
    public async Task SavedSnapshot_UsesEntityDateInsteadOfStaleLiveDraft()
    {
        var fixture = CreateFixture();
        await SaveStaleDraftAsync("saved-user", "Aksam");
        try
        {
            fixture.Analysis
                .Setup(x => x.AnalyzeFromComparisonAsync(
                    SavedDate, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateReport());

            var result = await fixture.Controller.RunHesapKontrolFromSavedSnapshot(
                fixture.Snapshot.Id, CancellationToken.None);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("QueryDate", redirect.ActionName);
            Assert.Equal("HesapKontrol", redirect.ControllerName);
            Assert.Equal("2026-07-13", redirect.RouteValues!["tarih"]);
            fixture.Analysis.Verify(x => x.AnalyzeFromComparisonAsync(
                SavedDate, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
            fixture.Analysis.Verify(x => x.AnalyzeFromComparisonAsync(
                StaleDraftDate, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            await KasaDraftCacheHelper.ClearDraftAsync("saved-user", "Aksam");
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task HistoricalArchive_IsUsedEvenWhenCurrentFolderExists()
    {
        var fixture = CreateFixture();
        try
        {
            fixture.Analysis
                .Setup(x => x.AnalyzeFromComparisonAsync(
                    SavedDate, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DateOnly _, string folder, int _, CancellationToken _) =>
                {
                    Assert.NotEqual(fixture.CurrentFolder, folder);
                    Assert.NotEqual(fixture.ArchiveFolder, folder);
                    Assert.Equal("archive-13", File.ReadAllText(
                        Path.Combine(folder, "bundle.xlsx")));
                    return CreateReport();
                });

            await fixture.Controller.RunHesapKontrolFromSavedSnapshot(
                fixture.Snapshot.Id, CancellationToken.None);

            fixture.Resolver.Verify(x => x.Resolve(
                fixture.CurrentFolder, SavedDate), Times.Once);
            fixture.Analysis.Verify(x => x.AnalyzeFromComparisonAsync(
                SavedDate, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task MissingHistoricalArchive_DoesNotFallbackToValidCurrentFolder()
    {
        var fixture = CreateFixture(sourceKind: HesapKontrolSourceKind.Current);
        try
        {
            var result = await fixture.Controller.RunHesapKontrolFromSavedSnapshot(
                fixture.Snapshot.Id, CancellationToken.None);

            Assert.IsType<RedirectToActionResult>(result);
            Assert.NotNull(fixture.Controller.TempData["Error"]);
            fixture.Analysis.Verify(x => x.AnalyzeFromComparisonAsync(
                It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task StaleSabahAndAksamCaches_CannotInfluenceSavedSnapshotSource()
    {
        var fixture = CreateFixture(kasaTuru: KasaRaporTuru.Sabah);
        await SaveStaleDraftAsync("saved-user", "Sabah");
        await SaveStaleDraftAsync("saved-user", "Aksam");
        try
        {
            fixture.Analysis
                .Setup(x => x.AnalyzeFromComparisonAsync(
                    SavedDate, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateReport());

            await fixture.Controller.RunHesapKontrolFromSavedSnapshot(
                fixture.Snapshot.Id, CancellationToken.None);

            fixture.Analysis.Verify(x => x.AnalyzeFromComparisonAsync(
                SavedDate, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
            fixture.Analysis.Verify(x => x.AnalyzeFromComparisonAsync(
                StaleDraftDate, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            await KasaDraftCacheHelper.ClearDraftAsync("saved-user", "Sabah");
            await KasaDraftCacheHelper.ClearDraftAsync("saved-user", "Aksam");
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task UnknownSnapshotId_IsRejectedBeforeAnalysis()
    {
        var fixture = CreateFixture(snapshotExists: false);
        try
        {
            await fixture.Controller.RunHesapKontrolFromSavedSnapshot(
                Guid.NewGuid(), CancellationToken.None);

            Assert.NotNull(fixture.Controller.TempData["Error"]);
            fixture.Analysis.Verify(x => x.AnalyzeFromComparisonAsync(
                It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task InactiveOrDeletedSnapshot_IsRejectedBeforeAnalysis(
        bool isActive,
        bool isDeleted)
    {
        var fixture = CreateFixture(isActive: isActive, isDeleted: isDeleted);
        try
        {
            var result = await fixture.Controller.RunHesapKontrolFromSavedSnapshot(
                fixture.Snapshot.Id, CancellationToken.None);

            Assert.IsType<RedirectToActionResult>(result);
            Assert.NotNull(fixture.Controller.TempData["Error"]);
            fixture.Analysis.Verify(x => x.AnalyzeFromComparisonAsync(
                It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task UnauthenticatedSnapshotId_IsRejectedBeforeDatabaseLookup()
    {
        var fixture = CreateFixture(authenticated: false);
        try
        {
            var result = await fixture.Controller.RunHesapKontrolFromSavedSnapshot(
                fixture.Snapshot.Id, CancellationToken.None);

            Assert.IsType<UnauthorizedResult>(result);
            Assert.Null(fixture.Controller.TempData["Error"]);
            fixture.Snapshots.Verify(x => x.GetByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
            fixture.Analysis.Verify(x => x.AnalyzeFromComparisonAsync(
                It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task SameSavedSnapshotTwice_DispatchesSameDateAndArchiveBytes()
    {
        var fixture = CreateFixture();
        var analyzedContents = new List<string>();
        try
        {
            fixture.Analysis
                .Setup(x => x.AnalyzeFromComparisonAsync(
                    SavedDate, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DateOnly _, string folder, int _, CancellationToken _) =>
                {
                    analyzedContents.Add(File.ReadAllText(
                        Path.Combine(folder, "bundle.xlsx")));
                    return CreateReport();
                });

            await fixture.Controller.RunHesapKontrolFromSavedSnapshot(
                fixture.Snapshot.Id, CancellationToken.None);
            await fixture.Controller.RunHesapKontrolFromSavedSnapshot(
                fixture.Snapshot.Id, CancellationToken.None);

            Assert.Equal(new[] { "archive-13", "archive-13" }, analyzedContents);
            fixture.Analysis.Verify(x => x.AnalyzeFromComparisonAsync(
                SavedDate, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static async Task SaveStaleDraftAsync(string userName, string kasaType)
    {
        await KasaDraftCacheHelper.SaveDraftAsync(
            userName,
            kasaType,
            new KasaPreviewViewModel
            {
                SelectedDate = StaleDraftDate,
                KasaType = kasaType,
                HasResults = true
            });
    }

    private static Fixture CreateFixture(
        HesapKontrolSourceKind sourceKind = HesapKontrolSourceKind.Archive,
        KasaRaporTuru kasaTuru = KasaRaporTuru.Aksam,
        bool snapshotExists = true,
        bool isActive = true,
        bool isDeleted = false,
        bool authenticated = true)
    {
        var webRoot = Path.Combine(Path.GetTempPath(), $"saved_snapshot_{Guid.NewGuid():N}");
        var currentFolder = Path.Combine(webRoot, "Data", "Raporlar");
        var archiveFolder = Path.Combine(
            currentFolder, "archive", SavedDate.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(currentFolder);
        Directory.CreateDirectory(archiveFolder);
        File.WriteAllText(Path.Combine(currentFolder, "bundle.xlsx"), "current-17");
        File.WriteAllText(Path.Combine(archiveFolder, "bundle.xlsx"), "archive-13");

        var savedSnapshot = new CalculatedKasaSnapshot
        {
            Id = Guid.NewGuid(),
            RaporTarihi = SavedDate,
            KasaTuru = kasaTuru,
            CalculatedBy = "saved-user",
            IsActive = isActive,
            IsDeleted = isDeleted
        };
        var snapshots = new Mock<ICalculatedKasaSnapshotService>();
        if (snapshotExists)
        {
            snapshots.Setup(x => x.GetByIdAsync(
                    savedSnapshot.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(savedSnapshot);
        }

        var resolver = new Mock<IHesapKontrolSourceResolver>();
        resolver.Setup(x => x.Resolve(currentFolder, SavedDate))
            .Returns(sourceKind == HesapKontrolSourceKind.Archive
                ? HesapKontrolSourceResolution.Success(
                    archiveFolder, HesapKontrolSourceKind.Archive)
                : HesapKontrolSourceResolution.Success(
                    currentFolder, HesapKontrolSourceKind.Current));

        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(x => x.WebRootPath).Returns(webRoot);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Upload:SubFolder"] = @"Data\Raporlar",
                ["Comparison:ArchiveRetentionDays"] = "60"
            })
            .Build();
        var analysis = new Mock<IBankaHesapKontrolService>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(authenticated);
        currentUser.SetupGet(x => x.UserId).Returns(authenticated ? 17 : null);
        currentUser.SetupGet(x => x.Username).Returns(authenticated ? "saved-user" : null);
        var controller = new KasaPreviewController(
            Mock.Of<IKasaOrchestrator>(), env.Object, configuration,
            Mock.Of<IImportOrchestrator>(), Mock.Of<IKasaReportDateRulesService>(),
            Mock.Of<IKasaGlobalDefaultsService>(), analysis.Object, currentUser.Object, resolver.Object,
            Mock.Of<IReportDataBuilder>(), Mock.Of<IExportService>(),
            Mock.Of<IKasaValidationService>(), Mock.Of<IVergideBirikenLedgerService>(),
            Mock.Of<IDocumentTemplateService>(), Mock.Of<IFinansalIstisnaService>(),
            Mock.Of<IFinansalIstisnaAnomaliService>(), Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<KasaPreviewController>>(), Mock.Of<IKasaReadModelService>(),
            snapshots.Object, Mock.Of<IKasaRaporSnapshotService>());

        var identity = authenticated
            ? new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "saved-user") }, "TestAuth")
            : new ClaimsIdentity();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(
            httpContext, Mock.Of<ITempDataProvider>());

        return new Fixture(
            webRoot, currentFolder, archiveFolder, savedSnapshot,
            controller, snapshots, resolver, analysis);
    }

    private static HesapKontrolRapor CreateReport() => new(
        SavedDate, 1, 1, 0, 1m, 0m,
        new StopajVirmanDurum(false, 0m, null, "Test", StopajStatus.Ok),
        new(), new(), "Test rapor");

    private sealed record Fixture(
        string WebRoot,
        string CurrentFolder,
        string ArchiveFolder,
        CalculatedKasaSnapshot Snapshot,
        KasaPreviewController Controller,
        Mock<ICalculatedKasaSnapshotService> Snapshots,
        Mock<IHesapKontrolSourceResolver> Resolver,
        Mock<IBankaHesapKontrolService> Analysis) : IDisposable
    {
        public void Dispose()
        {
            if (Directory.Exists(WebRoot))
                Directory.Delete(WebRoot, recursive: true);
        }
    }
}
