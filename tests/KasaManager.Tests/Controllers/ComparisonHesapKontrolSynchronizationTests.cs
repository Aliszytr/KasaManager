using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Web.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace KasaManager.Tests.Controllers;

public sealed class ComparisonHesapKontrolSynchronizationTests
{
    private static readonly DateOnly ArchiveDate = new(2026, 6, 15);

    [Fact]
    public async Task ArchivedComparison_PreservesDateInReportAndHesapKontrolFlow()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), $"comparison-web-{Guid.NewGuid():N}");
        var baseFolder = Path.Combine(webRoot, "Data", "Raporlar");
        var archiveFolder = Path.Combine(Path.GetTempPath(), $"comparison-archive-{Guid.NewGuid():N}");
        var comparison = new Mock<IComparisonService>();
        var archive = new Mock<IComparisonArchiveService>();
        var hesapKontrol = new Mock<IBankaHesapKontrolService>();

        comparison
            .Setup(x => x.CompareTahsilatMasrafAsync(
                archiveFolder, ArchiveDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ComparisonReport>.Success(CreateReport(ArchiveDate)));
        archive.Setup(x => x.GetArchiveFolder(baseFolder, ArchiveDate)).Returns(archiveFolder);

        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(x => x.WebRootPath).Returns(webRoot);

        var controller = new ComparisonController(
            comparison.Object,
            Mock.Of<IComparisonExportService>(),
            Mock.Of<IComparisonDecisionService>(x =>
                x.GetDecisionsAsync(ComparisonType.TahsilatMasraf, It.IsAny<CancellationToken>()) ==
                Task.FromResult(new List<KasaManager.Domain.Entities.ComparisonDecision>())),
            archive.Object,
            hesapKontrol.Object,
            env.Object);

        var result = await controller.CompareTahsilatMasraf(
            ArchiveDate.ToString("yyyy-MM-dd"), CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var report = Assert.IsType<ComparisonReport>(view.Model);
        Assert.Equal(ArchiveDate, report.ReportDate);
        comparison.VerifyAll();
        hesapKontrol.Verify(x => x.EnrichComparisonDecisionMemoryAsync(
            report,
            KasaManager.Domain.Reports.HesapKontrol.BankaHesapTuru.Tahsilat,
            ArchiveDate,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ResultsView_UsesExplicitAntiforgeryPostWithSelectedDate()
    {
        var repositoryRoot = FindRepositoryRoot();
        var viewSource = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "KasaManager.Web", "Views", "Comparison", "Results.cshtml"));

        Assert.Contains("asp-controller=\"HesapKontrol\" asp-action=\"RunAnalysis\" method=\"post\"", viewSource);
        Assert.Contains("name=\"tarih\" value=\"@Model.ReportDate.Value.ToString(\"yyyy-MM-dd\")\"", viewSource);
        Assert.Contains("Hesap Kontrol'e Aktar ve Aç", viewSource);
        Assert.DoesNotContain("/HesapKontrol/QueryDate?tarih=", viewSource);
    }

    private static ComparisonReport CreateReport(DateOnly date) => new()
    {
        Type = ComparisonType.TahsilatMasraf,
        GeneratedAt = DateTime.UtcNow,
        ReportDate = date
    };

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KasaManager.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root bulunamadı.");
    }
}
