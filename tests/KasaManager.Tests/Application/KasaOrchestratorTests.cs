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
    private readonly Mock<IKasaRaporSnapshotService> _snapshotsMock = new();
    private readonly Mock<IKasaGlobalDefaultsService> _globalDefaultsMock = new();
    private readonly Mock<IFormulaSetStore> _formulaStoreMock = new();
    private readonly Mock<IDataPipeline> _dataPipelineMock = new();
    private readonly Mock<ILogger<KasaOrchestrator>> _loggerMock = new();

    private KasaOrchestrator CreateSut() => new(
        _draftsMock.Object,
        _engineMock.Object,
        _snapshotsMock.Object,
        _globalDefaultsMock.Object,
        _formulaStoreMock.Object,
        _dataPipelineMock.Object,
        _loggerMock.Object);

    // ───────────────────────────────────────────
    // LoadPreviewAsync
    // ───────────────────────────────────────────

    [Fact]
    public async Task LoadPreviewAsync_NoSnapshot_SetsIsDataLoadedFalse()
    {
        _globalDefaultsMock
            .Setup(g => g.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KasaGlobalDefaultsSettings { Id = 1 });

        _snapshotsMock
            .Setup(s => s.GetAsync(It.IsAny<DateOnly>(), It.IsAny<KasaRaporTuru>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<KasaRaporSnapshot?>(null));

        var sut = CreateSut();
        var dto = new KasaPreviewDto
        {
            SelectedDate = new DateOnly(2026, 2, 19),
            KasaType = "Aksam"
        };

        await sut.LoadPreviewAsync(dto, @"C:\temp", CancellationToken.None);

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

        // Snapshot with rows required for hydration to proceed
        var snapshot = new KasaRaporSnapshot
        {
            Id = Guid.NewGuid(),
            RaporTarihi = date,
            RaporTuru = KasaRaporTuru.Genel,
            Rows = new List<KasaRaporSnapshotRow>
            {
                new() { Veznedar = "TestVeznedar", IsSelected = true, Bakiye = 100m }
            }
        };

        _snapshotsMock
            .Setup(s => s.GetAsync(date, KasaRaporTuru.Genel, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var sut = CreateSut();
        var dto = new KasaPreviewDto
        {
            KasaType = "Aksam",
            SelectedDate = date
        };

        await sut.HydrateFromSnapshotAndDefaultsAsync(dto, CancellationToken.None);

        Assert.Equal(50m, dto.BozukPara);
        Assert.Equal(100m, dto.NakitPara);
    }
}
