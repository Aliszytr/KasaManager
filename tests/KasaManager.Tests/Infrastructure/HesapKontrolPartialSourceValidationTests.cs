using KasaManager.Application.Abstractions;
using KasaManager.Application.Services.Comparison;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Constants;
using KasaManager.Domain.FinancialExceptions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using KasaManager.Web.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KasaManager.Tests.Infrastructure;

public sealed class HesapKontrolPartialSourceValidationTests
{
    private static readonly DateOnly TestDate = new(2026, 7, 10);

    [Fact]
    public async Task PartialBundle_PersistsMasrafAndHarc_WhenReddiyatCannotRun()
    {
        await using var db = CreateDb();
        var comparison = CreatePartialComparison();
        var service = CreateService(db, comparison);

        var report = await service.AnalyzeFromComparisonAsync(
            TestDate, "canonical-archive", 17, CancellationToken.None);

        var records = await db.HesapKontrolKayitlari.AsNoTracking().ToListAsync();
        Assert.Contains(records, x => x.HesapTuru == BankaHesapTuru.Tahsilat
                                      && x.Tutar == 28063.50m
                                      && x.TespitEdilenTip == "MASRAF");
        Assert.Contains(records, x => x.HesapTuru == BankaHesapTuru.Harc
                                      && x.Tutar == 89619.00m
                                      && x.TespitEdilenTip == "HARÇ");
        Assert.Equal(2, records.Count);
        Assert.All(records, record => Assert.Equal(17, record.CreatedByUserId));
        Assert.All(records, record => Assert.Null(record.CreatedBy));
        Assert.Contains("Reddiyat çalıştırılamadı", report.OzetMesaj);
        Assert.Contains(ExcelFileNames.OnlineReddiyat, report.OzetMesaj);
    }

    [Fact]
    public async Task PartialBundle_IsIdempotent_ForSameHistoricalDate()
    {
        await using var db = CreateDb();
        var service = CreateService(db, CreatePartialComparison());

        await service.AnalyzeFromComparisonAsync(TestDate, "canonical-archive", 17);
        await service.AnalyzeFromComparisonAsync(TestDate, "canonical-archive", 29);

        var open = await db.HesapKontrolKayitlari
            .Where(x => x.Durum == KayitDurumu.Acik)
            .ToListAsync();
        Assert.Equal(2, open.Count);
        Assert.Equal(117682.50m, open.Sum(x => x.Tutar));
        Assert.All(open, record => Assert.Equal(17, record.CreatedByUserId));
    }

    [Fact]
    public async Task NoComparisonCanRun_ThrowsAndDoesNotWrite()
    {
        await using var db = CreateDb();
        var comparison = new Mock<IComparisonService>();
        comparison.Setup(x => x.CompareTahsilatMasrafAsync(
                It.IsAny<string>(), TestDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ComparisonReport>.Fail("Tahsilat kaynakları geçersiz"));
        comparison.Setup(x => x.CompareHarcamaHarcAsync(
                It.IsAny<string>(), TestDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ComparisonReport>.Fail("Harç kaynakları geçersiz"));
        comparison.Setup(x => x.CompareReddiyatCikisAsync(
                It.IsAny<string>(), TestDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ComparisonReport>.Fail("Reddiyat kaynakları geçersiz"));

        var service = CreateService(db, comparison);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AnalyzeFromComparisonAsync(TestDate, "canonical-archive", 17));

        Assert.Empty(await db.HesapKontrolKayitlari.ToListAsync());
    }

    [Fact]
    public async Task EmptyOnlineSide_IsValidAndProducesRealSurplus()
    {
        var folder = CreateFolderWithFiles(
            ExcelFileNames.BankaTahsilat,
            ExcelFileNames.OnlineMasraf);
        try
        {
            var import = new Mock<IImportOrchestrator>();
            import.Setup(x => x.ImportTrueSource(
                    It.IsAny<string>(), ImportFileKind.BankaTahsilat))
                .Returns(Result<ImportedTable>.Success(CreateBankTable(28063.50m, "MASRAF")));
            import.Setup(x => x.ImportTrueSource(
                    It.IsAny<string>(), ImportFileKind.OnlineMasraf))
                .Returns(Result<ImportedTable>.Success(CreateEmptyOnlineTable(ImportFileKind.OnlineMasraf)));
            var comparison = new ComparisonService(import.Object, new BankaAciklamaParser());

            var result = await comparison.CompareTahsilatMasrafAsync(folder, TestDate);

            Assert.True(result.Ok, result.Error);
            var surplus = Assert.Single(result.Value!.SurplusBankaRecords);
            Assert.Equal(28063.50m, surplus.Tutar);
            Assert.Equal("MASRAF", surplus.DetectedType);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void CanonicalArchive_WithTwoReadablePairs_IsAcceptedWithoutReddiyatFile()
    {
        var baseFolder = Path.Combine(Path.GetTempPath(), $"hk_partial_{Guid.NewGuid():N}");
        var archiveFolder = Path.Combine(baseFolder, "archive", TestDate.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(archiveFolder);
        foreach (var file in new[]
                 {
                     ExcelFileNames.BankaTahsilat,
                     ExcelFileNames.OnlineMasraf,
                     ExcelFileNames.BankaHarc,
                     ExcelFileNames.OnlineHarc
                 })
            File.WriteAllText(Path.Combine(archiveFolder, file), "fake xlsx");

        var archive = new Mock<IComparisonArchiveService>();
        archive.Setup(x => x.GetArchiveFolder(baseFolder, TestDate)).Returns(archiveFolder);
        var reader = new Mock<IExcelTableReader>();
        reader.Setup(x => x.ReadTable(It.IsAny<string>(), It.IsAny<ExcelReadOptions>()))
            .Returns(Result<ImportedTable>.Success(CreateEmptyOnlineTable(ImportFileKind.OnlineMasraf)));
        var resolver = new HesapKontrolSourceResolver(
            archive.Object, reader.Object, NullLogger<HesapKontrolSourceResolver>.Instance);

        try
        {
            var result = resolver.Resolve(baseFolder, TestDate);

            Assert.True(result.IsValid, result.Error);
            Assert.Equal(archiveFolder, result.Folder);
            Assert.Equal(HesapKontrolSourceKind.Archive, result.SourceKind);
        }
        finally
        {
            Directory.Delete(baseFolder, recursive: true);
        }
    }

    [Fact]
    public void PartialValidation_RejectsBundle_WhenNoCompletePairExists()
    {
        var folder = CreateFolderWithFiles(
            ExcelFileNames.BankaTahsilat,
            ExcelFileNames.BankaHarc);
        var reader = new Mock<IExcelTableReader>();
        reader.Setup(x => x.ReadTable(It.IsAny<string>(), It.IsAny<ExcelReadOptions>()))
            .Returns(Result<ImportedTable>.Success(CreateEmptyOnlineTable(ImportFileKind.OnlineMasraf)));
        var resolver = new HesapKontrolSourceResolver(
            Mock.Of<IComparisonArchiveService>(), reader.Object,
            NullLogger<HesapKontrolSourceResolver>.Instance);

        try
        {
            var error = resolver.ValidateForAnalysis(folder, TestDate);
            Assert.Contains("kaynak çifti bulunamadı", error);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task HistoricalAnalysis_RejectsCurrentFallback_WithoutWriting()
    {
        var service = new Mock<IBankaHesapKontrolService>();
        var resolver = new Mock<IHesapKontrolSourceResolver>();
        resolver.Setup(x => x.Resolve(It.IsAny<string>(), TestDate))
            .Returns(HesapKontrolSourceResolution.Success(
                "current-folder", HesapKontrolSourceKind.Current));
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(x => x.WebRootPath).Returns(@"C:\FakeWebRoot");
        var controller = new HesapKontrolController(
            service.Object,
            Mock.Of<ICurrentUser>(u => u.IsAuthenticated && u.UserId == 17 && u.Username == "test-user"),
            Mock.Of<IHesapKontrolExportService>(),
            Mock.Of<IFinansalIstisnaService>(),
            resolver.Object,
            NullLogger<HesapKontrolController>.Instance,
            env.Object,
            Mock.Of<IManualResolveWriteBusinessDateResolver>());
        controller.TempData = new TempDataDictionary(
            new DefaultHttpContext(), Mock.Of<ITempDataProvider>());

        var result = await controller.RunAnalysis(
            TestDate.ToString("yyyy-MM-dd"), CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(controller.TempData["Error"]);
        service.Verify(x => x.AnalyzeFromComparisonAsync(
            It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void CurrentBundle_WithReadableComparisonPair_RemainsUsable()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var folder = CreateFolderWithFiles(
            ExcelFileNames.BankaTahsilat,
            ExcelFileNames.OnlineMasraf);
        var reader = new Mock<IExcelTableReader>();
        reader.Setup(x => x.ReadTable(It.IsAny<string>(), It.IsAny<ExcelReadOptions>()))
            .Returns(Result<ImportedTable>.Success(CreateEmptyOnlineTable(ImportFileKind.OnlineMasraf)));
        var resolver = new HesapKontrolSourceResolver(
            Mock.Of<IComparisonArchiveService>(), reader.Object,
            NullLogger<HesapKontrolSourceResolver>.Instance);

        try
        {
            var result = resolver.Resolve(folder, today);
            Assert.True(result.IsValid, result.Error);
            Assert.Equal(folder, result.Folder);
            Assert.Equal(HesapKontrolSourceKind.Current, result.SourceKind);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ComparisonHistoricalRequest_DoesNotFallbackToCurrentFolder()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), $"comparison_root_{Guid.NewGuid():N}");
        var baseFolder = Path.Combine(webRoot, "Data", "Raporlar");
        var expectedArchive = Path.Combine(
            baseFolder, "archive", TestDate.ToString("yyyy-MM-dd"));
        var comparison = new Mock<IComparisonService>();
        comparison.Setup(x => x.CompareTahsilatMasrafAsync(
                expectedArchive, TestDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ComparisonReport>.Fail("Arşiv kaynağı bulunamadı."));
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(x => x.WebRootPath).Returns(webRoot);
        var controller = new ComparisonController(
            comparison.Object,
            Mock.Of<IComparisonExportService>(),
            Mock.Of<IComparisonDecisionService>(),
            Mock.Of<IComparisonArchiveService>(),
            Mock.Of<IBankaHesapKontrolService>(),
            env.Object);
        controller.TempData = new TempDataDictionary(
            new DefaultHttpContext(), Mock.Of<ITempDataProvider>());

        var result = await controller.CompareTahsilatMasraf(
            TestDate.ToString("yyyy-MM-dd"), CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        comparison.Verify(x => x.CompareTahsilatMasrafAsync(
            expectedArchive, TestDate, It.IsAny<CancellationToken>()), Times.Once);
        comparison.Verify(x => x.CompareTahsilatMasrafAsync(
            baseFolder, It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static BankaHesapKontrolService CreateService(
        KasaManagerDbContext db,
        Mock<IComparisonService> comparison)
    {
        var resolver = new Mock<IHesapKontrolSourceResolver>();
        resolver.As<IPartialHesapKontrolSourceValidator>()
            .Setup(x => x.ValidateForAnalysis(It.IsAny<string>(), It.IsAny<DateOnly>()))
            .Returns((string?)null);
        return new BankaHesapKontrolService(
            db,
            comparison.Object,
            Mock.Of<IImportOrchestrator>(),
            resolver.Object,
            NullLogger<BankaHesapKontrolService>.Instance);
    }

    private static Mock<IComparisonService> CreatePartialComparison()
    {
        var comparison = new Mock<IComparisonService>();
        comparison.Setup(x => x.CompareTahsilatMasrafAsync(
                It.IsAny<string>(), TestDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ComparisonReport>.Success(
                CreateSurplusReport(ComparisonType.TahsilatMasraf, 28063.50m, "MASRAF")));
        comparison.Setup(x => x.CompareHarcamaHarcAsync(
                It.IsAny<string>(), TestDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ComparisonReport>.Success(
                CreateSurplusReport(ComparisonType.HarcamaHarc, 89619.00m, "HARÇ")));
        comparison.Setup(x => x.CompareReddiyatCikisAsync(
                It.IsAny<string>(), TestDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ComparisonReport>.Fail(
                $"{ExcelFileNames.OnlineReddiyat} bulunamadı."));
        return comparison;
    }

    private static ComparisonReport CreateSurplusReport(
        ComparisonType type,
        decimal amount,
        string detectedType) => new()
    {
        Type = type,
        GeneratedAt = DateTime.UtcNow,
        ReportDate = TestDate,
        SurplusBankaCount = 1,
        SurplusAmount = amount,
        SurplusBankaRecords =
        [
            new UnmatchedBankaRecord
            {
                RowIndex = 0,
                Tutar = amount,
                Aciklama = detectedType,
                DetectedType = detectedType
            }
        ]
    };

    private static KasaManagerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseInMemoryDatabase($"PartialSource_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new KasaManagerDbContext(options);
    }

    private static ImportedTable CreateBankTable(decimal amount, string description) => new()
    {
        SourceFileName = ExcelFileNames.BankaTahsilat,
        Kind = ImportFileKind.BankaTahsilat,
        Columns = ["islem_tarihi", "islem_tutari", "aciklama", "borc_alacak"],
        Rows =
        [
            new Dictionary<string, string?>
            {
                ["islem_tarihi"] = TestDate.ToString("dd.MM.yyyy"),
                ["islem_tutari"] = amount.ToString("N2", new System.Globalization.CultureInfo("tr-TR")),
                ["aciklama"] = description,
                ["borc_alacak"] = "ALACAK"
            }
        ]
    };

    private static ImportedTable CreateEmptyOnlineTable(ImportFileKind kind) => new()
    {
        SourceFileName = "empty.xlsx",
        Kind = kind,
        Columns = ["tarih", "miktar", "dosya_no", "birim_adi"],
        Rows = []
    };

    private static string CreateFolderWithFiles(params string[] files)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"hk_files_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        foreach (var file in files)
            File.WriteAllText(Path.Combine(folder, file), "fake xlsx");
        return folder;
    }
}
