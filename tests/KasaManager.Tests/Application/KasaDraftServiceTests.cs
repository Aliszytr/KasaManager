using KasaManager.Application.Abstractions;
using KasaManager.Application.Orchestration;
using KasaManager.Application.Orchestration.Dtos;
using KasaManager.Application.Pipeline;
using KasaManager.Application.Services;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Projection;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Domain.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Globalization;

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
            .ReturnsAsync(new CarryoverResolutionResult(0m, "dunden_devreden_kasa_nakit", DateOnly.FromDateTime(DateTime.Today), null, "Default", "Default setup", true));

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

    [Fact]
    public async Task BuildUnifiedPoolAsync_SabahFollowGateway_UsesOnlyReportDayTotals()
    {
        var day2 = new DateOnly(2070, 3, 12);
        var defaults = new KasaGlobalDefaultsSettings { Id = 1 };
        var table = new ImportedTable
        {
            SourceFileName = "KasaUstRapor.xlsx",
            Kind = ImportFileKind.KasaUstRapor,
            Rows =
            {
                new Dictionary<string, string?>
                {
                    ["satir"] = "TOPLAMLAR",
                    ["tahsilat"] = "0",
                    ["reddiyat"] = "0",
                    ["harc"] = "0",
                    ["stopaj"] = "0"
                }
            }
        };

        _globalDefaultsMock.Setup(x => x.GetOrCreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaults);
        _importMock.Setup(x => x.Import(It.IsAny<string>(), ImportFileKind.KasaUstRapor))
            .Returns(Result<ImportedTable>.Success(table));
        _hesapKontrolMock.Setup(x => x.GetActiveFollowTotalsAsync(day2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveFollowTotals(day2, -32_000m, -11_000m, 32_000m, 11_000m, 0m, 0m, 4));
        _hesapKontrolMock.Setup(x => x.GetDailyFollowTotalsAsync(day2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveFollowTotals(day2, -29_000m, -7_000m, 29_000m, 7_000m, 0m, 0m, 2));

        var sut = CreateSut();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        await File.WriteAllBytesAsync(Path.Combine(tempDir, "KasaUstRapor.xlsx"), Array.Empty<byte>());

        try
        {
            var result = await sut.BuildUnifiedPoolAsync(
                day2, tempDir, kasaScope: "Sabah", skipSlimPoolFilter: true);

            Assert.True(result.Ok, result.Error);
            decimal Value(string key) => decimal.Parse(
                Assert.Single(result.Value!, x => x.CanonicalKey == key).Value,
                CultureInfo.InvariantCulture);

            Assert.Equal(-29_000m, Value("takip_kasa_etkisi_tahsilat"));
            Assert.Equal(-7_000m, Value("takip_kasa_etkisi_harc"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExcessWithdrawal_RealCalculation_KeepsDepositZeroAndLeavesExcessInGeneralCash()
    {
        var date = new DateOnly(2026, 7, 15);
        var kasaUstTable = new ImportedTable
        {
            SourceFileName = "KasaUstRapor.xlsx",
            Kind = ImportFileKind.KasaUstRapor,
            Rows =
            {
                new Dictionary<string, string?>
                {
                    ["satir"] = "TOPLAMLAR",
                    ["tahsilat"] = "0",
                    ["reddiyat"] = "0",
                    ["harc"] = "0",
                    ["stopaj"] = "0"
                }
            }
        };

        _importMock
            .Setup(i => i.Import(It.IsAny<string>(), ImportFileKind.KasaUstRapor))
            .Returns(Result<ImportedTable>.Success(kasaUstTable));
        _globalDefaultsMock
            .Setup(g => g.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KasaGlobalDefaultsSettings { Id = 1 });

        var sut = CreateSut();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        await File.WriteAllBytesAsync(
            Path.Combine(tempDir, "KasaUstRapor.xlsx"),
            Array.Empty<byte>());

        try
        {
            var minimumResult = await sut.BuildAsync(
                date,
                tempDir,
                new KasaDraftFinalizeInputs
                {
                    VergiKasaBakiyeToplam = 27_846m,
                    BankadanCekilen = 27_846m
                });
            var excessResult = await sut.BuildAsync(
                date,
                tempDir,
                new KasaDraftFinalizeInputs
                {
                    VergiKasaBakiyeToplam = 27_846m,
                    BankadanCekilen = 37_846m
                });

            Assert.True(minimumResult.Ok, minimumResult.Error);
            Assert.True(excessResult.Ok, excessResult.Error);

            static decimal Field(KasaDraftBundle bundle, string key) =>
                decimal.Parse(bundle.Aksam.Fields[key], CultureInfo.InvariantCulture);

            var minimumDeposit = Field(minimumResult.Value!, "bankaya_yatirilacak_nakit");
            var excessDeposit = Field(excessResult.Value!, "bankaya_yatirilacak_nakit");
            var minimumGeneralCash = Field(minimumResult.Value!, "genel_kasa");
            var excessGeneralCash = Field(excessResult.Value!, "genel_kasa");
            var preservedWithdrawal = Field(excessResult.Value!, "bankadan_cekilen");

            Assert.Equal(0m, minimumDeposit);
            Assert.Equal(0m, excessDeposit);
            Assert.Equal(10_000m, excessGeneralCash - minimumGeneralCash);
            Assert.Equal(37_846m, preservedWithdrawal);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}

public sealed class KasaPreviewModeScopeTests
{
    [Theory]
    [InlineData("Sabah", false)]
    [InlineData("sabah", false)]
    [InlineData("Aksam", true)]
    public void LoadDataWarning_MentionsAksamModeOnlyForAksamKasa(
        string kasaType,
        bool shouldMentionAksamMode)
    {
        var method = typeof(KasaManager.Web.Controllers.KasaPreviewController).GetMethod(
            "BuildLoadDataResultWarning",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        var warning = Assert.IsType<string>(method!.Invoke(null, new object?[] { kasaType }));

        Assert.Equal(shouldMentionAksamMode, warning.Contains("Akşam Kasa Modu"));
        Assert.Contains("Eski sonuçlar güvenlik nedeniyle gösterilmedi", warning);
    }

    [Fact]
    public void ProductionCalculatePaths_DoNotRouteSabahToHesapKontrolResolver()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var controllerPath = Path.Combine(
            repositoryRoot, "src", "KasaManager.Web", "Controllers", "KasaPreviewController.cs");
        var source = File.ReadAllText(controllerPath);

        Assert.DoesNotContain("if (isSabah || isAksamTamGun)", source);
        Assert.DoesNotContain("if (isSabahLC || isAksamTamGunLC)", source);
        Assert.Contains("if (isAksamTamGun)", source);
        Assert.Contains("if (isAksamTamGunLC)", source);
    }
}

public sealed class BankadanCekilenFormulaEnginePoolTests
{
    private static readonly DateOnly TestDate = new(2026, 7, 15);

    [Fact]
    public async Task BuildUnifiedPoolAsync_BankadanCekilen_AddsManualOverride()
    {
        using var harness = CreateHarness();

        var result = await harness.Drafts.BuildUnifiedPoolAsync(
            TestDate,
            harness.UploadFolder,
            new KasaDraftFinalizeInputs { BankadanCekilen = 28_766m },
            kasaScope: "Aksam",
            skipSlimPoolFilter: true);

        Assert.True(result.Ok, result.Error);
        var entry = Assert.Single(result.Value!, x => x.CanonicalKey == "bankadan_cekilen");
        Assert.Equal(UnifiedPoolValueType.Override, entry.Type);
        Assert.Equal(28_766m, PoolDecimal(entry));
    }

    [Fact]
    public async Task FormulaEngine_WithdrawalIncrease_RaisesGeneralCashBySameAmount()
    {
        using var harness = CreateHarness();

        var minimum = await RunFormulaEngineAsync(harness, 27_846m);
        var excess = await RunFormulaEngineAsync(harness, 37_846m);

        Assert.Equal(0m, Output(minimum, "bankaya_yatirilacak_tahsilat"));
        Assert.Equal(0m, Output(excess, "bankaya_yatirilacak_tahsilat"));
        Assert.Equal(10_000m, Output(excess, "genel_kasa") - Output(minimum, "genel_kasa"));
    }

    [Fact]
    public async Task FormulaEngine_PositiveWithdrawal_KeepsPoolKeyAvailableForRazor()
    {
        using var harness = CreateHarness();

        var dto = await RunFormulaEngineAsync(harness, 28_766m);

        var entry = Assert.Single(dto.PoolEntries, x => x.CanonicalKey == "bankadan_cekilen");
        Assert.Equal(28_766m, PoolDecimal(entry));
        Assert.NotNull(dto.FormulaRun);
    }

    [Fact]
    public async Task BuildUnifiedPoolAsync_NullWithdrawal_DoesNotCreateWithdrawalEntry()
    {
        using var harness = CreateHarness();

        var result = await harness.Drafts.BuildUnifiedPoolAsync(
            TestDate,
            harness.UploadFolder,
            new KasaDraftFinalizeInputs { BankadanCekilen = null },
            kasaScope: "Aksam",
            skipSlimPoolFilter: true);

        Assert.True(result.Ok, result.Error);
        Assert.DoesNotContain(result.Value!, x => x.CanonicalKey == "bankadan_cekilen");
    }

    [Fact]
    public async Task BuildUnifiedPoolAsync_ExplicitZero_KeepsZeroOverride()
    {
        using var harness = CreateHarness();

        var result = await harness.Drafts.BuildUnifiedPoolAsync(
            TestDate,
            harness.UploadFolder,
            new KasaDraftFinalizeInputs { BankadanCekilen = 0m },
            kasaScope: "Aksam",
            skipSlimPoolFilter: true);

        Assert.True(result.Ok, result.Error);
        var entry = Assert.Single(result.Value!, x => x.CanonicalKey == "bankadan_cekilen");
        Assert.Equal(UnifiedPoolValueType.Override, entry.Type);
        Assert.Equal(0m, PoolDecimal(entry));
    }

    private static async Task<KasaPreviewDto> RunFormulaEngineAsync(TestHarness harness, decimal withdrawal)
    {
        var dto = new KasaPreviewDto
        {
            SelectedDate = TestDate,
            BankadanCekilen = withdrawal,
            VergiKasaBakiyeToplam = 27_846m
        };

        await harness.Orchestrator.LoadActiveFormulaSetByScopeAsync(dto, "Aksam", CancellationToken.None);
        await harness.Orchestrator.RunFormulaEnginePreviewAsync(dto, harness.UploadFolder, CancellationToken.None);

        Assert.Empty(dto.Errors);
        Assert.NotNull(dto.FormulaRun);
        return dto;
    }

    private static decimal Output(KasaPreviewDto dto, string key) => dto.FormulaRun!.Outputs[key];

    private static decimal PoolDecimal(UnifiedPoolEntry entry) =>
        decimal.Parse(entry.Value, NumberStyles.Any, CultureInfo.InvariantCulture);

    private static TestHarness CreateHarness()
    {
        var import = new Mock<IImportOrchestrator>();
        var defaults = new Mock<IKasaGlobalDefaultsService>();
        var hesapKontrol = new Mock<IBankaHesapKontrolService>();
        var carryover = new Mock<ICarryoverResolver>();
        var projection = new Mock<IEksikFazlaProjectionEngine>();
        var snapshots = new Mock<IKasaRaporSnapshotService>();
        var formulaStore = new Mock<IFormulaSetStore>();
        var scopeFactory = new Mock<IServiceScopeFactory>();

        var settings = new KasaGlobalDefaultsSettings { Id = 1 };
        defaults.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        defaults.Setup(x => x.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);

        hesapKontrol
            .Setup(x => x.GetHistoryAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<BankaHesapTuru?>(),
                It.IsAny<KayitDurumu?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HesapKontrolKaydi>());

        carryover
            .Setup(x => x.ResolveAsync(It.IsAny<DateOnly>(), It.IsAny<CarryoverScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CarryoverResolutionResult(
                0m, "dunden_devreden_kasa_nakit", TestDate, null, "Test", "Test", true));

        projection
            .Setup(x => x.ProjectAsync(It.IsAny<ProjectionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectionResult(
                TestDate, true, 0m, 0m, 0m, 0m, 0m, 0m, false, new List<ProjectionDayNode>()));

        snapshots
            .Setup(x => x.GetAsync(It.IsAny<DateOnly>(), It.IsAny<KasaRaporTuru>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KasaRaporSnapshot
            {
                RaporTarihi = TestDate,
                RaporTuru = KasaRaporTuru.Genel,
                Rows =
                {
                    new KasaRaporSnapshotRow
                    {
                        Veznedar = "Test Veznedar",
                        IsSelected = true,
                        Bakiye = 27_846m
                    }
                }
            });
        formulaStore
            .Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KasaManager.Domain.FormulaEngine.Authoring.PersistedFormulaSet>());
        scopeFactory
            .Setup(x => x.CreateScope())
            .Throws(new InvalidOperationException("Comparison service is not needed by this regression test."));

        var table = new ImportedTable
        {
            SourceFileName = "KasaUstRapor.xlsx",
            Kind = ImportFileKind.KasaUstRapor,
            Rows =
            {
                new Dictionary<string, string?>
                {
                    ["satir"] = "TOPLAMLAR",
                    ["tahsilat"] = "0",
                    ["reddiyat"] = "0",
                    ["harc"] = "0",
                    ["stopaj"] = "0"
                }
            }
        };
        import
            .Setup(x => x.Import(It.IsAny<string>(), ImportFileKind.KasaUstRapor))
            .Returns(Result<ImportedTable>.Success(table));
        import
            .Setup(x => x.ImportTrueSource(It.IsAny<string>(), It.IsAny<ImportFileKind>()))
            .Returns(Result<ImportedTable>.Fail("Testte mevcut olmayan kaynak."));

        var drafts = new KasaDraftService(
            import.Object,
            defaults.Object,
            hesapKontrol.Object,
            Mock.Of<ILogger<KasaDraftService>>(),
            carryover.Object,
            Options.Create(new UstRaporSourceOptions()),
            projection.Object);

        var orchestrator = new KasaOrchestrator(
            drafts,
            new FormulaEngineService(),
            snapshots.Object,
            defaults.Object,
            formulaStore.Object,
            Mock.Of<IDataPipeline>(),
            Mock.Of<ILogger<KasaOrchestrator>>(),
            scopeFactory.Object);

        return new TestHarness(drafts, orchestrator);
    }

    private sealed class TestHarness : IDisposable
    {
        public TestHarness(KasaDraftService drafts, KasaOrchestrator orchestrator)
        {
            Drafts = drafts;
            Orchestrator = orchestrator;
            UploadFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(UploadFolder);
            File.WriteAllBytes(Path.Combine(UploadFolder, "KasaUstRapor.xlsx"), Array.Empty<byte>());
        }

        public KasaDraftService Drafts { get; }
        public KasaOrchestrator Orchestrator { get; }
        public string UploadFolder { get; }

        public void Dispose() => Directory.Delete(UploadFolder, recursive: true);
    }
}
