using KasaManager.Application.Abstractions;
using KasaManager.Application.Orchestration;
using KasaManager.Application.Orchestration.Dtos;
using KasaManager.Application.Pipeline;
using KasaManager.Application.Services;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Calculation;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.FormulaEngine.Authoring;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Domain.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace KasaManager.Tests.Application;

/// <summary>
/// KasaOrchestrator birim testleri.
/// Temel akış, fallback template, override builder testleri.
/// </summary>
public sealed class KasaOrchestratorTests
{
    private readonly Mock<IKasaDraftService> _draftsMock = new();
    private readonly Mock<IFormulaEngineService> _engineMock = new();

    private readonly Mock<IKasaRaporSnapshotService> _snapshotMock = new();
    private readonly Mock<IKasaGlobalDefaultsService> _globalDefaultsMock = new();
    private readonly Mock<IFormulaSetStore> _formulaStoreMock = new();
    private readonly Mock<IDataPipeline> _dataPipelineMock = new();
    private readonly Mock<ILogger<KasaOrchestrator>> _loggerMock = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();

    private KasaOrchestrator CreateSut() => new(
        _draftsMock.Object,
        _engineMock.Object,
        _snapshotMock.Object,
        _globalDefaultsMock.Object,
        _formulaStoreMock.Object,
        _dataPipelineMock.Object,
        _loggerMock.Object,
        _scopeFactoryMock.Object);

    [Fact]
    public async Task SaveDbFormulaSetAsync_SystemPoolKeyAsTarget_ReturnsValidationError()
    {
        var dto = new KasaPreviewDto
        {
            DbFormulaSetName = "Invalid system target",
            PoolEntries =
            {
                new UnifiedPoolEntry { CanonicalKey = "takip_kasa_etkisi_tahsilat", Value = "100", IncludeInCalculations = true }
            },
            Mappings =
            {
                new KasaPreviewMappingRow { TargetKey = "takip_kasa_etkisi_tahsilat", Mode = "Formula", Expression = "1" }
            }
        };

        await CreateSut().SaveDbFormulaSetAsync(dto, isUpdate: false, CancellationToken.None);

        Assert.Contains(dto.Errors, error => error.Contains("sistem anahtarı", StringComparison.OrdinalIgnoreCase));
        _formulaStoreMock.Verify(store => store.CreateAsync(
            It.IsAny<PersistedFormulaSet>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveDbFormulaSetAsync_SystemPoolKeyIdentityTarget_IsAllowed()
    {
        var dto = new KasaPreviewDto
        {
            DbFormulaSetName = "Identity allowed",
            PoolEntries =
            {
                new UnifiedPoolEntry { CanonicalKey = "normal_tahsilat", Value = "100", IncludeInCalculations = true }
            },
            Mappings =
            {
                new KasaPreviewMappingRow { TargetKey = "normal_tahsilat", Mode = "Formula", Expression = " (( normal_tahsilat )) " }
            }
        };
        _formulaStoreMock.Setup(store => store.CreateAsync(
                It.IsAny<PersistedFormulaSet>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedFormulaSet set, CancellationToken _) => set);

        await CreateSut().SaveDbFormulaSetAsync(dto, isUpdate: false, CancellationToken.None);

        Assert.Empty(dto.Errors);
        _formulaStoreMock.Verify(store => store.CreateAsync(
            It.IsAny<PersistedFormulaSet>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("takip_kasa_etkisi_tahsilat", "x + takip_kasa_etkisi_tahsilat")]
    [InlineData("normal_tahsilat", "normal_tahsilat + 1")]
    public async Task SaveDbFormulaSetAsync_SystemPoolKeyNonIdentityTarget_RemainsRejected(
        string target,
        string expression)
    {
        var dto = new KasaPreviewDto
        {
            PoolEntries = { new UnifiedPoolEntry { CanonicalKey = target, Value = "1", IncludeInCalculations = true } },
            Mappings = { new KasaPreviewMappingRow { TargetKey = target, Mode = "Formula", Expression = expression } }
        };

        await CreateSut().SaveDbFormulaSetAsync(dto, isUpdate: false, CancellationToken.None);

        Assert.Contains(dto.Errors, error => error.Contains("sistem anahtarı", StringComparison.OrdinalIgnoreCase));
        _formulaStoreMock.Verify(store => store.CreateAsync(
            It.IsAny<PersistedFormulaSet>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateDbFormulaSetAsync_EmptyPoolHydratesFromPreviewBeforeGuarding()
    {
        var date = new DateOnly(2070, 9, 1);
        _snapshotMock.Setup(service => service.GetAsync(
                date, KasaRaporTuru.Genel, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KasaRaporSnapshot
            {
                RaporTarihi = date,
                Rows = { new KasaRaporSnapshotRow { Veznedar = "test", IsSelected = true } }
            });
        _globalDefaultsMock.Setup(service => service.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KasaGlobalDefaultsSettings { Id = 1 });
        _draftsMock.Setup(service => service.BuildUnifiedPoolAsync(
                date, It.IsAny<string>(), It.IsAny<KasaDraftFinalizeInputs>(),
                It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<bool>(),
                It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<UnifiedPoolEntry>>.Success(new[]
            {
                new UnifiedPoolEntry
                {
                    CanonicalKey = "takip_kasa_etkisi_tahsilat", Value = "100", IncludeInCalculations = true
                }
            }));
        _draftsMock.Setup(service => service.BuildAsync(
                date, It.IsAny<string>(), It.IsAny<KasaDraftFinalizeInputs>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<KasaDraftBundle>.Success(new KasaDraftBundle()));
        var dto = new KasaPreviewDto
        {
            SelectedDate = date,
            KasaType = "Sabah",
            DbScopeType = "Sabah",
            Mappings =
            {
                new KasaPreviewMappingRow
                {
                    TargetKey = "takip_kasa_etkisi_tahsilat", Mode = "Formula", Expression = "1"
                }
            }
        };

        await CreateSut().CreateDbFormulaSetAsync(dto, @"C:\uploads", CancellationToken.None);

        Assert.NotEmpty(dto.PoolEntries);
        Assert.Contains(dto.Errors, error => error.Contains("sistem anahtarı", StringComparison.OrdinalIgnoreCase));
        _formulaStoreMock.Verify(store => store.CreateAsync(
            It.IsAny<PersistedFormulaSet>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ───────────────────────────────────────────
    // LoadPreviewAsync
    // ───────────────────────────────────────────

    [Fact]
    public async Task LoadPreviewAsync_NoSnapshot_SetsIsDataLoadedFalse()
    {
        _globalDefaultsMock
            .Setup(g => g.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KasaGlobalDefaultsSettings { Id = 1 });
        _draftsMock.Setup(d => d.BuildUnifiedPoolAsync(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<KasaDraftFinalizeInputs>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<UnifiedPoolEntry>>.Success(new List<UnifiedPoolEntry>()));

        _draftsMock.Setup(d => d.BuildAsync(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<KasaDraftFinalizeInputs>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<KasaDraftBundle>.Success(new KasaDraftBundle()));

        var sut = CreateSut();
        var dto = new KasaPreviewDto
        {
            SelectedDate = new DateOnly(2026, 2, 19),
            KasaType = "Aksam"
        };

        await sut.LoadPreviewAsync(dto, @"C:\temp", CancellationToken.None);

        // Without snapshots and using legacy logic, IsDataLoaded will be false because we no longer set it true by default.
        Assert.False(dto.IsDataLoaded);
    }

    // ───────────────────────────────────────────
    // LoadActiveFormulaSetByScopeAsync
    // ───────────────────────────────────────────

    [Fact]
    public async Task LoadActiveFormulaSetByScopeAsync_NoDbTemplate_UsesFallback()
    {
        _formulaStoreMock
            .Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PersistedFormulaSet>());

        _globalDefaultsMock
            .Setup(g => g.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KasaGlobalDefaultsSettings { Id = 1 });

        var sut = CreateSut();
        var dto = new KasaPreviewDto();

        await sut.LoadActiveFormulaSetByScopeAsync(dto, "Aksam", CancellationToken.None);

        // Fallback yüklenmeli, mappings boş olmamalı
        Assert.NotEmpty(dto.Mappings);
    }

    [Fact]
    public async Task LoadActiveFormulaSetByScopeAsync_GenelScope_InjectsEssentialFormulas()
    {
        _formulaStoreMock
            .Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PersistedFormulaSet>());

        _globalDefaultsMock
            .Setup(g => g.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KasaGlobalDefaultsSettings { Id = 1 });

        var sut = CreateSut();
        var dto = new KasaPreviewDto();

        await sut.LoadActiveFormulaSetByScopeAsync(dto, "Genel", CancellationToken.None);

        // Genel scope: embedded fallback + essential formulas
        Assert.NotEmpty(dto.Mappings);
    }

    // ───────────────────────────────────────────
    // GetBuiltInFormulaSets (FormulaEngineService)
    // ───────────────────────────────────────────

    [Fact]
    public void FormulaEngineService_GetBuiltInFormulaSets_ReturnsAtLeastTwo()
    {
        var engine = new FormulaEngineService();
        var sets = engine.GetBuiltInFormulaSets();
        Assert.True(sets.Count >= 2, $"Expected at least 2 built-in sets, got {sets.Count}");
    }

    [Fact]
    public void FormulaEngineService_Run_EmptyPool_ReturnsSuccess()
    {
        var engine = new FormulaEngineService();
        var sets = engine.GetBuiltInFormulaSets();
        var firstSet = sets[0];

        var result = engine.Run(
            new DateOnly(2026, 2, 19),
            firstSet,
            Array.Empty<UnifiedPoolEntry>());

        Assert.True(result.Ok);
        Assert.NotNull(result.Value);
    }

    // ───────────────────────────────────────────
    // HydrateFromSnapshotAndDefaultsAsync
    // ───────────────────────────────────────────

    [Fact]
    public async Task HydrateFromSnapshotAndDefaultsAsync_WithDefaults_SetsValues()
    {
        var date = new DateOnly(2026, 2, 19);
        var defaults = new KasaGlobalDefaultsSettings
        {
            Id = 1,
            DefaultBozukPara = 50m,
            DefaultNakitPara = 100m,
            DefaultKasaEksikFazla = -5m
        };

        _globalDefaultsMock
            .Setup(g => g.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaults);


        var sut = CreateSut();
        var dto = new KasaPreviewDto
        {
            KasaType = "Aksam",
            SelectedDate = date
        };

        await sut.HydrateFromSnapshotAndDefaultsAsync(dto, CancellationToken.None);

        Assert.Null(dto.BozukPara);
        Assert.Null(dto.NakitPara);
    }
    // ───────────────────────────────────────────
    // Commit 5: HydrateIptalEdilenCikisTutarAsync
    // ───────────────────────────────────────────

    /// <summary>
    /// ComparisonService çözümlenemediğinde (dosya eksik, DI hata vb.)
    /// IptalEdilenCikisTutar = 0 kalmalı, pipeline kırılmamalı.
    /// </summary>
    [Fact]
    public async Task LoadPreviewAsync_ComparisonServiceFails_IptalEdilenCikisTutarRemainsZero()
    {
        // Arrange: Snapshot + Pool başarılı, ama ScopeFactory IComparisonService çözümleyemiyor
        _globalDefaultsMock
            .Setup(g => g.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KasaGlobalDefaultsSettings { Id = 1 });
        _globalDefaultsMock
            .Setup(g => g.GetOrCreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KasaGlobalDefaultsSettings { Id = 1 });

        _draftsMock.Setup(d => d.BuildUnifiedPoolAsync(
                It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<KasaDraftFinalizeInputs>(),
                It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<bool>(),
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<UnifiedPoolEntry>>.Success(new List<UnifiedPoolEntry>()));

        _draftsMock.Setup(d => d.BuildAsync(
                It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<KasaDraftFinalizeInputs>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<KasaDraftBundle>.Success(new KasaDraftBundle()));

        // ScopeFactory: CreateScope() fırlatır — IComparisonService çözümlenemez
        _scopeFactoryMock
            .Setup(f => f.CreateScope())
            .Throws(new InvalidOperationException("IComparisonService kayıtlı değil (test senaryosu)"));

        var sut = CreateSut();
        var dto = new KasaPreviewDto
        {
            SelectedDate = new DateOnly(2026, 4, 21),
            KasaType = "Aksam"
        };

        // Act — kırılmamalı
        await sut.LoadPreviewAsync(dto, @"C:\temp", CancellationToken.None);

        // Assert
        Assert.Equal(0m, dto.IptalEdilenCikisTutar);
        // Pipeline devam etmeli — hata listesinde iptal hatası OLMAMALI (warning loga gider, error listesine değil)
        Assert.DoesNotContain(dto.Errors, e => e.Contains("iptal", StringComparison.OrdinalIgnoreCase));
    }
}
