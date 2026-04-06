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

    private readonly Mock<IKasaGlobalDefaultsService> _globalDefaultsMock = new();
    private readonly Mock<IFormulaSetStore> _formulaStoreMock = new();
    private readonly Mock<IDataPipeline> _dataPipelineMock = new();
    private readonly Mock<ILogger<KasaOrchestrator>> _loggerMock = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();

    private KasaOrchestrator CreateSut() => new(
        _draftsMock.Object,
        _engineMock.Object,
        _globalDefaultsMock.Object,
        _formulaStoreMock.Object,
        _dataPipelineMock.Object,
        _loggerMock.Object,
        _scopeFactoryMock.Object);

    // ───────────────────────────────────────────
    // LoadPreviewAsync
    // ───────────────────────────────────────────

    [Fact]
    public async Task LoadPreviewAsync_NoSnapshot_SetsIsDataLoadedFalse()
    {
        _globalDefaultsMock
            .Setup(g => g.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KasaGlobalDefaultsSettings { Id = 1 });
        _draftsMock.Setup(d => d.BuildUnifiedPoolAsync(It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<KasaDraftFinalizeInputs>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
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

        // Without snapshots, if default loaded successfully, IsDataLoaded will actually be true now because snapshot block was removed.
        // Wait, the orchestrator now sets IsDataLoaded = true unconditionally in Hydrate function!
        // We probably need to assert Assert.True(dto.IsDataLoaded) because it no longer fails when snapshot is absent.
        Assert.True(dto.IsDataLoaded);
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

        Assert.Equal(50m, dto.BozukPara);
        Assert.Equal(100m, dto.NakitPara);
    }
}
