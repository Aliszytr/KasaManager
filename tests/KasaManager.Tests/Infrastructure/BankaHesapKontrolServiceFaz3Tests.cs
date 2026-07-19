using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Constants;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using KasaManager.Application.Services.Comparison;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace KasaManager.Tests.Infrastructure;

/// <summary>
/// FAZ-3: BankaHesapKontrolService — tarih aktarımı ve kaynak doğrulama testleri.
/// </summary>
public sealed class BankaHesapKontrolServiceFaz3Tests
{
    private static readonly DateOnly TestDate = new(2026, 6, 15);

    // ─── Factory ───

    private static (BankaHesapKontrolService Service, Mock<IComparisonService> MockComparison,
        Mock<IImportOrchestrator> MockImport)
        CreateService(
            KasaManagerDbContext? db = null,
            Mock<IHesapKontrolSourceResolver>? sourceResolver = null)
    {
        var mockComparison = new Mock<IComparisonService>();
        var mockImport = new Mock<IImportOrchestrator>();
        var mockSourceResolver = sourceResolver ?? new Mock<IHesapKontrolSourceResolver>();
        if (sourceResolver is null)
        {
            mockSourceResolver
                .Setup(r => r.Validate(It.IsAny<string>(), It.IsAny<DateOnly>()))
                .Returns((string?)null);
        }
        var service = new BankaHesapKontrolService(
            db ?? CreateInMemoryDb(),
            mockComparison.Object,
            mockImport.Object,
            mockSourceResolver.Object,
            NullLogger<BankaHesapKontrolService>.Instance);

        return (service, mockComparison, mockImport);
    }

    private static KasaManagerDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseInMemoryDatabase($"Faz3_{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new KasaManagerDbContext(options);
    }

    private static ComparisonReport EmptyReport() => new ComparisonReport
    {
        Type = ComparisonType.TahsilatMasraf,
        GeneratedAt = DateTime.UtcNow,
        Results = new(),
        SurplusBankaRecords = new(),
        MissingBankaRecords = new(),
        Issues = new()
    };

    // ─── Tarih aktarımı testleri ───

    [Fact]
    public async Task Analysis_PassesFilterDateToTahsilatMasraf()
    {
        var (service, mockComparison, _) = CreateService();
        SetupComparisonSuccess(mockComparison);

        await service.AnalyzeFromComparisonAsync(TestDate, "validated-source", 17, CancellationToken.None);

        mockComparison.Verify(
            c => c.CompareTahsilatMasrafAsync(It.IsAny<string>(), TestDate, It.IsAny<CancellationToken>()),
            Times.Once,
            "CompareTahsilatMasrafAsync'a seçilen tarih aktarılmalıdır.");
    }

    [Fact]
    public async Task Analysis_PassesFilterDateToHarcamaHarc()
    {
        var (service, mockComparison, _) = CreateService();
        SetupComparisonSuccess(mockComparison);

        await service.AnalyzeFromComparisonAsync(TestDate, "validated-source", 17, CancellationToken.None);

        mockComparison.Verify(
            c => c.CompareHarcamaHarcAsync(It.IsAny<string>(), TestDate, It.IsAny<CancellationToken>()),
            Times.Once,
            "CompareHarcamaHarcAsync'a seçilen tarih aktarılmalıdır.");
    }

    [Fact]
    public async Task Analysis_PassesFilterDateToReddiyatCikis()
    {
        var (service, mockComparison, _) = CreateService();
        SetupComparisonSuccess(mockComparison);

        await service.AnalyzeFromComparisonAsync(TestDate, "validated-source", 17, CancellationToken.None);

        mockComparison.Verify(
            c => c.CompareReddiyatCikisAsync(It.IsAny<string>(), TestDate, It.IsAny<CancellationToken>()),
            Times.Once,
            "CompareReddiyatCikisAsync'a seçilen tarih aktarılmalıdır.");
    }

    [Fact]
    public async Task AnalysisEntry_RejectsUnvalidatedSource()
    {
        await using var db = CreateInMemoryDb();
        var sourceResolver = new Mock<IHesapKontrolSourceResolver>();
        sourceResolver
            .Setup(r => r.Validate("unvalidated-source", TestDate))
            .Returns("Eksik zorunlu dosya: 'BankaTahsilat.xlsx'.");
        var (service, comparison, import) = CreateService(db, sourceResolver);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AnalyzeFromComparisonAsync(
                TestDate, "unvalidated-source", 17, CancellationToken.None));

        Assert.Empty(db.HesapKontrolKayitlari);
        Assert.Empty(comparison.Invocations);
        Assert.Empty(import.Invocations);
    }

    // ─── Helpers ───

    private void SetupComparisonSuccess(Mock<IComparisonService> mockComparison)
    {
        var report = EmptyReport();
        mockComparison
            .Setup(c => c.CompareTahsilatMasrafAsync(It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ComparisonReport>.Success(report));
        mockComparison
            .Setup(c => c.CompareHarcamaHarcAsync(It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ComparisonReport>.Success(report));
        mockComparison
            .Setup(c => c.CompareReddiyatCikisAsync(It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ComparisonReport>.Success(report));
    }
}

/// <summary>
/// FAZ-3: ValidateExcelSource metodu için birim testleri.
/// </summary>
public sealed class HesapKontrolSourceResolverTests
{
    private static readonly DateOnly TestDate = new(2026, 6, 15);

    private static HesapKontrolSourceResolver CreateResolver(
        Mock<IExcelTableReader> mockReader,
        Mock<IComparisonArchiveService>? mockArchive = null)
    {
        return new HesapKontrolSourceResolver(
            (mockArchive ?? new Mock<IComparisonArchiveService>()).Object,
            mockReader.Object,
            NullLogger<HesapKontrolSourceResolver>.Instance);
    }

    private static ImportedTable BuildTable(DateOnly date)
    {
        return new ImportedTable
        {
            SourceFileName = "test.xlsx",
            Kind = ImportFileKind.BankaTahsilat,
            Columns = new System.Collections.Generic.List<string> { "tarih" },
            Rows = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, string?>>
            {
                new() { { "tarih", date.ToString("dd.MM.yyyy") } }
            }
        };
    }

    private static string CreateTempDirWithFiles(IEnumerable<string> files)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hk_vld_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        foreach (var f in files)
            File.WriteAllText(Path.Combine(dir, f), "fake xlsx");
        return dir;
    }

    [Fact]
    public void ValidArchive_IsSelected_WhenAllFilesAndDatePresent()
    {
        var mockReader = new Mock<IExcelTableReader>();
        mockReader.Setup(r => r.ReadTable(It.IsAny<string>(), It.IsAny<ExcelReadOptions>()))
                  .Returns(Result<ImportedTable>.Success(BuildTable(TestDate)));

        var archive = new Mock<IComparisonArchiveService>();
        var baseDir = Path.Combine(Path.GetTempPath(), $"hk_base_{Guid.NewGuid():N}");
        var archiveDir = CreateTempDirWithFiles(ExcelFileNames.ComparisonFiles);
        archive.Setup(a => a.GetArchiveFolder(baseDir, TestDate)).Returns(archiveDir);
        var resolver = CreateResolver(mockReader, archive);
        try
        {
            var result = resolver.Resolve(baseDir, TestDate);
            Assert.True(result.IsValid);
            Assert.Equal(archiveDir, result.Folder);
        }
        finally { Directory.Delete(archiveDir, recursive: true); }
    }

    [Fact]
    public void MissingRequiredFile_PreventsAnalysis()
    {
        var mockReader = new Mock<IExcelTableReader>();
        mockReader.Setup(r => r.ReadTable(It.IsAny<string>(), It.IsAny<ExcelReadOptions>()))
                  .Returns(Result<ImportedTable>.Success(BuildTable(TestDate)));

        var resolver = CreateResolver(mockReader);
        var files = ExcelFileNames.ComparisonFiles;
        // 4 dosya — son dosya eksik
        var tempDir = CreateTempDirWithFiles(files.Take(files.Length - 1));
        try
        {
            var error = resolver.Validate(tempDir, TestDate);
            Assert.NotNull(error);
            Assert.Contains(files[files.Length - 1], error);
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [Fact]
    public void UnparseableRequiredFile_PreventsAnalysis()
    {
        var mockReader = new Mock<IExcelTableReader>();
        mockReader.Setup(r => r.ReadTable(It.IsAny<string>(), It.IsAny<ExcelReadOptions>()))
                  .Returns(Result<ImportedTable>.Fail("Excel parse hatası"));

        var resolver = CreateResolver(mockReader);
        var tempDir = CreateTempDirWithFiles(ExcelFileNames.ComparisonFiles);
        try
        {
            var error = resolver.Validate(tempDir, TestDate);
            Assert.NotNull(error);
            Assert.Contains("okunamadı", error);
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [Fact]
    public void RequiredFileWithoutSelectedDate_PreventsAnalysis()
    {
        var wrongDate = TestDate.AddDays(-1);
        var mockReader = new Mock<IExcelTableReader>();
        mockReader.Setup(r => r.ReadTable(It.IsAny<string>(), It.IsAny<ExcelReadOptions>()))
                  .Returns(Result<ImportedTable>.Success(BuildTable(wrongDate)));

        var resolver = CreateResolver(mockReader);
        var tempDir = CreateTempDirWithFiles(ExcelFileNames.ComparisonFiles);
        try
        {
            var error = resolver.Validate(tempDir, TestDate);
            Assert.NotNull(error);
            Assert.Contains("tarihine ait satır bulunamadı", error);
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [Fact]
    public void OnlySomeFilesContainingDate_IsInvalid()
    {
        var files = ExcelFileNames.ComparisonFiles;
        var mockReader = new Mock<IExcelTableReader>();

        // İlk 4 dosya için TestDate, son dosya için farklı tarih
        for (int i = 0; i < files.Length - 1; i++)
        {
            var fileName = files[i];
            mockReader.Setup(r => r.ReadTable(
                            It.Is<string>(p => p.EndsWith(fileName)),
                            It.IsAny<ExcelReadOptions>()))
                      .Returns(Result<ImportedTable>.Success(BuildTable(TestDate)));
        }
        mockReader.Setup(r => r.ReadTable(
                        It.Is<string>(p => p.EndsWith(files[files.Length - 1])),
                        It.IsAny<ExcelReadOptions>()))
                  .Returns(Result<ImportedTable>.Success(BuildTable(TestDate.AddDays(-1))));

        var resolver = CreateResolver(mockReader);
        var tempDir = CreateTempDirWithFiles(files);
        try
        {
            var error = resolver.Validate(tempDir, TestDate);
            Assert.NotNull(error);
            Assert.Contains(files[files.Length - 1], error);
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [Fact]
    public void MissingArchive_UsesValidCurrentSource()
    {
        var mockReader = new Mock<IExcelTableReader>();
        mockReader.Setup(r => r.ReadTable(It.IsAny<string>(), It.IsAny<ExcelReadOptions>()))
                  .Returns(Result<ImportedTable>.Success(BuildTable(TestDate)));
        var archive = new Mock<IComparisonArchiveService>();
        var currentDir = CreateTempDirWithFiles(ExcelFileNames.ComparisonFiles);
        var resolver = CreateResolver(mockReader, archive);

        try
        {
            var result = resolver.Resolve(currentDir, TestDate);
            Assert.True(result.IsValid);
            Assert.Equal(currentDir, result.Folder);
        }
        finally { Directory.Delete(currentDir, recursive: true); }
    }

    [Fact]
    public void InvalidArchive_UsesValidCurrentSource()
    {
        var mockReader = new Mock<IExcelTableReader>();
        mockReader.Setup(r => r.ReadTable(It.IsAny<string>(), It.IsAny<ExcelReadOptions>()))
                  .Returns(Result<ImportedTable>.Success(BuildTable(TestDate)));
        var archive = new Mock<IComparisonArchiveService>();
        var currentDir = CreateTempDirWithFiles(ExcelFileNames.ComparisonFiles);
        var archiveDir = CreateTempDirWithFiles(ExcelFileNames.ComparisonFiles.Take(4));
        archive.Setup(a => a.GetArchiveFolder(currentDir, TestDate)).Returns(archiveDir);
        var resolver = CreateResolver(mockReader, archive);

        try
        {
            var result = resolver.Resolve(currentDir, TestDate);
            Assert.True(result.IsValid);
            Assert.Equal(currentDir, result.Folder);
        }
        finally
        {
            Directory.Delete(currentDir, recursive: true);
            Directory.Delete(archiveDir, recursive: true);
        }
    }

    [Fact]
    public void BothSourcesInvalid_ReportsArchiveAndCurrentSource()
    {
        var mockReader = new Mock<IExcelTableReader>();
        var archive = new Mock<IComparisonArchiveService>();
        var currentDir = CreateTempDirWithFiles(ExcelFileNames.ComparisonFiles.Take(4));
        var archiveDir = CreateTempDirWithFiles(ExcelFileNames.ComparisonFiles.Take(4));
        archive.Setup(a => a.GetArchiveFolder(currentDir, TestDate)).Returns(archiveDir);
        var resolver = CreateResolver(mockReader, archive);

        try
        {
            var result = resolver.Resolve(currentDir, TestDate);
            Assert.False(result.IsValid);
            Assert.Contains("Arşiv kaynağı", result.Error);
            Assert.Contains("Güncel kaynak", result.Error);
            Assert.Contains(ExcelFileNames.OnlineReddiyat, result.Error);
            Assert.DoesNotContain(archiveDir, result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(currentDir, result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(archiveDir, result.TechnicalError);
            Assert.Contains(currentDir, result.TechnicalError);
        }
        finally
        {
            Directory.Delete(currentDir, recursive: true);
            Directory.Delete(archiveDir, recursive: true);
        }
    }

    [Fact]
    public void FolderDoesNotExist_PreventsAnalysis()
    {
        var mockReader = new Mock<IExcelTableReader>();
        var resolver = CreateResolver(mockReader);

        var error = resolver.Validate(@"C:\NonExistent\Path_" + Guid.NewGuid(), TestDate);

        Assert.NotNull(error);
        Assert.Contains("bulunamadı", error);
    }

    [Fact]
    public void Validation_UsesOnlyExcelTableReader()
    {
        var mockReader = new Mock<IExcelTableReader>();
        mockReader.Setup(r => r.ReadTable(It.IsAny<string>(), It.IsAny<ExcelReadOptions>()))
                  .Returns(Result<ImportedTable>.Success(BuildTable(TestDate)));

        var resolver = CreateResolver(mockReader);

        var tempDir = CreateTempDirWithFiles(ExcelFileNames.ComparisonFiles);
        try
        {
            var error = resolver.Validate(tempDir, TestDate);

            Assert.Null(error);
            mockReader.Verify(r => r.ReadTable(It.IsAny<string>(), It.IsAny<ExcelReadOptions>()), Times.Exactly(5),
                "Validasyon IExcelTableReader.ReadTable kullanmalıdır.");
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [Fact]
    public void Resolver_HasNoImportShadowIngestionOrDbDependency()
    {
        var constructorParameterTypes = typeof(HesapKontrolSourceResolver)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToList();

        Assert.DoesNotContain(typeof(IImportOrchestrator), constructorParameterTypes);
        Assert.DoesNotContain(typeof(KasaManagerDbContext), constructorParameterTypes);
        Assert.DoesNotContain(typeof(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory), constructorParameterTypes);
        Assert.Contains(typeof(IExcelTableReader), constructorParameterTypes);
    }

    [Fact]
    public void Validation_DoesNotWriteToDb()
    {
        var mockReader = new Mock<IExcelTableReader>();
        mockReader.Setup(r => r.ReadTable(It.IsAny<string>(), It.IsAny<ExcelReadOptions>()))
                  .Returns(Result<ImportedTable>.Success(BuildTable(TestDate)));

        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseInMemoryDatabase($"ValExcel_NoDb_{Guid.NewGuid():N}")
            .Options;
        var db = new KasaManagerDbContext(options);
        var resolver = CreateResolver(mockReader);

        var tempDir = CreateTempDirWithFiles(ExcelFileNames.ComparisonFiles);
        try
        {
            resolver.Validate(tempDir, TestDate);
            Assert.Equal(0, db.HesapKontrolKayitlari.Count());
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }
}
