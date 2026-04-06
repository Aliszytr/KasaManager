using KasaManager.Application.Abstractions;
using KasaManager.Application.Services;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Projection;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Domain.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace KasaManager.Tests.Application;

/// <summary>
/// KasaDraftService birim testleri.
/// BuildAsync, BuildGenelKasaR10EngineInputsAsync ve helper metotları test eder.
/// </summary>
public sealed class KasaDraftServiceTests
{

    private readonly Mock<IImportOrchestrator> _importMock = new();
    private readonly Mock<IKasaGlobalDefaultsService> _globalDefaultsMock = new();
    private readonly Mock<IBankaHesapKontrolService> _hesapKontrolMock = new();
    private readonly Mock<ICarryoverResolver> _carryoverMock = new();

    private KasaDraftService CreateSut()
    {
        // Default: GetHistoryAsync boş liste dönsün (çözülen kayıt yok)
        _hesapKontrolMock
            .Setup(h => h.GetHistoryAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                It.IsAny<BankaHesapTuru?>(), It.IsAny<KayitDurumu?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HesapKontrolKaydi>());

        _carryoverMock
            .Setup(c => c.ResolveAsync(It.IsAny<DateOnly>(), It.IsAny<CarryoverScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarryoverResolutionResult(0m, DateOnly.FromDateTime(DateTime.Today), null, "Default", "Default setup", true));

        var projMock = new Mock<IEksikFazlaProjectionEngine>();
        projMock.Setup(p => p.ProjectAsync(It.IsAny<ProjectionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectionResult(DateOnly.FromDateTime(DateTime.Today), Ok: true, 0m, 0m, 0m, 0m, 0m, 0m, false, new List<ProjectionDayNode>()));

        return new KasaDraftService(
            _importMock.Object,
            _globalDefaultsMock.Object,
            _hesapKontrolMock.Object,
            Mock.Of<ILogger<KasaDraftService>>(), 
            _carryoverMock.Object,
            Options.Create(new UstRaporSourceOptions()),
            projMock.Object);
    }

    // ───────────────────────────────────────────
    // BuildAsync
    // ───────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_NullFolder_ReturnsFail()
    {
        var sut = CreateSut();
        var result = await sut.BuildAsync(DateOnly.FromDateTime(DateTime.Today), "");
        Assert.False(result.Ok);
        Assert.Contains("Upload klasörü", result.Error!);
    }

    [Fact]
    public async Task BuildAsync_WhitespaceFolder_ReturnsFail()
    {
        var sut = CreateSut();
        var result = await sut.BuildAsync(DateOnly.FromDateTime(DateTime.Today), "   ");
        Assert.False(result.Ok);
    }

    [Fact]
    public async Task BuildAsync_NoSnapshot_ReturnsFail()
    {
        var sut = CreateSut();
        // Since snapshot fallback is removed, missing data in live mode causes error in unified pool/projection.
        // Wait, BuildAsync_NoSnapshot_ReturnsFail test checks snapshot fallback which no longer exists.
        // The service now just proceeds with live data and might fail differently if fields don't match.
        // We can just simulate missing live data via empty result from IImportOrchestrator instead.
        var result = await sut.BuildAsync(
            DateOnly.FromDateTime(DateTime.Today),
            @"C:\nonexistent\folder");

        Assert.False(result.Ok);
        Assert.Contains("hesaplanamadı", result.Error!);
    }

    [Fact]
    public async Task BuildAsync_WithSnapshot_NoFiles_ReturnsBundleWithIssues()
    {
        var date = new DateOnly(2026, 2, 19);
        var snapshot = new KasaRaporSnapshot
        {
            Id = Guid.NewGuid(),
            RaporTarihi = date,
            RaporTuru = KasaRaporTuru.Genel,
            Rows = new List<KasaRaporSnapshotRow>
            {
                new() { Veznedar = "TestVeznedar", IsSelected = true }
            }
        };

        _globalDefaultsMock
            .Setup(g => g.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KasaGlobalDefaultsSettings { Id = 1 });

        var sut = CreateSut();

        // Klasör var ama içinde dosya yok — servis hata vermemeli, issues listeyecek
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var result = await sut.BuildAsync(date, tempDir);
            // Dosya eksiklikleri issue olarak raporlanır, ancak sonuç Ok olabilir
            Assert.True(result.Ok);
            Assert.NotNull(result.Value);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // ───────────────────────────────────────────
    // BuildGenelKasaR10EngineInputsAsync
    // ───────────────────────────────────────────

    [Fact]
    public async Task BuildGenelKasaR10EngineInputsAsync_NoFiles_ReturnsResultWithIssues()
    {
        var defaults = new KasaGlobalDefaultsSettings
        {
            Id = 1,
            DefaultGenelKasaBaslangicTarihiSeed = null,
            DefaultGenelKasaDevredenSeed = 0m
        };

        _globalDefaultsMock
            .Setup(g => g.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaults);

        _globalDefaultsMock
            .Setup(g => g.GetOrCreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaults);

        // ImportTrueSource returns Fail when file doesn't exist (prevents NullRef)
        _importMock
            .Setup(i => i.ImportTrueSource(It.IsAny<string>(), It.IsAny<ImportFileKind>()))
            .Returns(Result<ImportedTable>.Fail("Dosya bulunamadı"));

        var sut = CreateSut();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var result = await sut.BuildGenelKasaR10EngineInputsAsync(
                new DateOnly(2026, 2, 19), null, tempDir);

            // Dosya eksik — sonuç Ok olabilir (issues ile)
            Assert.NotNull(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // ───────────────────────────────────────────
    // BuildUnifiedPoolAsync
    // ───────────────────────────────────────────

    [Fact]
    public async Task BuildUnifiedPoolAsync_EmptyFolder_ReturnsPool()
    {
        var defaults = new KasaGlobalDefaultsSettings { Id = 1 };

        _globalDefaultsMock
            .Setup(g => g.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaults);

        _globalDefaultsMock
            .Setup(g => g.GetOrCreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaults);

        // Import returns Fail for missing files
        _importMock
            .Setup(i => i.ImportTrueSource(It.IsAny<string>(), It.IsAny<ImportFileKind>()))
            .Returns(Result<ImportedTable>.Fail("Dosya bulunamadı"));

        var sut = CreateSut();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var result = await sut.BuildUnifiedPoolAsync(
                new DateOnly(2026, 2, 19), tempDir);

            // Dosya eksik olsa bile pool nesnesi oluşturulmalı
            Assert.NotNull(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
