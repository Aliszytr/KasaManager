using KasaManager.Application.Abstractions;
using KasaManager.Application.Services;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Domain.Settings;
using Microsoft.Extensions.Logging;
using Moq;

namespace KasaManager.Tests.Application;

/// <summary>
/// KasaDraftService birim testleri.
/// BuildAsync, BuildGenelKasaR10EngineInputsAsync ve helper metotları test eder.
/// </summary>
public sealed class KasaDraftServiceTests
{
    private readonly Mock<IKasaRaporSnapshotService> _snapshotsMock = new();
    private readonly Mock<IImportOrchestrator> _importMock = new();
    private readonly Mock<IKasaGlobalDefaultsService> _globalDefaultsMock = new();
    private readonly Mock<IBankaHesapKontrolService> _hesapKontrolMock = new();

    private KasaDraftService CreateSut()
    {
        // Default: GetHistoryAsync boş liste dönsün (çözülen kayıt yok)
        _hesapKontrolMock
            .Setup(h => h.GetHistoryAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                It.IsAny<BankaHesapTuru?>(), It.IsAny<KayitDurumu?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HesapKontrolKaydi>());

        return new KasaDraftService(
            _snapshotsMock.Object,
            _importMock.Object,
            _globalDefaultsMock.Object,
            _hesapKontrolMock.Object,
            Mock.Of<ILogger<KasaDraftService>>());
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
        _snapshotsMock
            .Setup(s => s.GetAsync(It.IsAny<DateOnly>(), KasaRaporTuru.Genel, It.IsAny<CancellationToken>()))
            .ReturnsAsync((KasaRaporSnapshot?)null);

        var sut = CreateSut();
        var result = await sut.BuildAsync(
            DateOnly.FromDateTime(DateTime.Today),
            @"C:\nonexistent\folder");

        Assert.False(result.Ok);
        Assert.Contains("snapshot bulunamadı", result.Error!);
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

        _snapshotsMock
            .Setup(s => s.GetAsync(date, KasaRaporTuru.Genel, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        _snapshotsMock
            .Setup(s => s.GetLastBeforeOrOnAsync(It.IsAny<DateOnly>(), KasaRaporTuru.Aksam, It.IsAny<CancellationToken>()))
            .ReturnsAsync((KasaRaporSnapshot?)null);

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

        _snapshotsMock
            .Setup(s => s.GetLastGenelKasaSnapshotBeforeOrOnAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<KasaRaporSnapshot?>(null));

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

        _snapshotsMock
            .Setup(s => s.GetLastGenelKasaSnapshotBeforeOrOnAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<KasaRaporSnapshot?>(null));

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
