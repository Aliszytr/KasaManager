using KasaManager.Application.Abstractions;
using KasaManager.Application.Services;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Calculation;
using KasaManager.Domain.Constants;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.Reports;
using Microsoft.Extensions.Logging;
using Moq;

namespace KasaManager.Tests.Application;

/// <summary>
/// GenelKasaRaporService birim testleri.
/// BuildCalculationRunAsync ve BuildReportDataAsync iş mantığını doğrular.
/// </summary>
public sealed class GenelKasaRaporServiceTests
{
    private readonly Mock<IKasaDraftService> _draftsMock = new();
    private readonly Mock<IFormulaEngineService> _engineMock = new();
    private readonly Mock<ILogger<GenelKasaRaporService>> _logMock = new();
    private readonly GenelKasaRaporService _sut;

    public GenelKasaRaporServiceTests()
    {
        _sut = new GenelKasaRaporService(_draftsMock.Object, _engineMock.Object, _logMock.Object);
    }

    [Fact]
    public async Task BuildCalculationRunAsync_InputsFail_ReturnsError()
    {
        _draftsMock
            .Setup(d => d.BuildGenelKasaR10EngineInputsAsync(It.IsAny<DateOnly?>(), It.IsAny<decimal?>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<GenelKasaR10EngineInputBundle>.Fail("Test hatası"));

        var (run, error) = await _sut.BuildCalculationRunAsync(null, null, "/tmp", false, CancellationToken.None);

        Assert.Null(run);
        Assert.Contains("hatası", error!);
    }

    [Fact]
    public async Task BuildCalculationRunAsync_NoFormulaSet_ReturnsError()
    {
        var bundle = new GenelKasaR10EngineInputBundle
        {
            BaslangicTarihi = new DateOnly(2026, 1, 1),
            BitisTarihi = new DateOnly(2026, 1, 31),
            DevredenSonTarihi = new DateOnly(2025, 12, 31),
            PoolEntries = new List<UnifiedPoolEntry>()
        };

        _draftsMock
            .Setup(d => d.BuildGenelKasaR10EngineInputsAsync(It.IsAny<DateOnly?>(), It.IsAny<decimal?>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<GenelKasaR10EngineInputBundle>.Success(bundle));

        // GetBuiltInFormulaSets boş dönecek — FormulaSet bulunamaz
        _engineMock
            .Setup(e => e.GetBuiltInFormulaSets())
            .Returns(new List<FormulaSet>());

        var (run, error) = await _sut.BuildCalculationRunAsync(null, null, "/tmp", false, CancellationToken.None);

        Assert.Null(run);
        Assert.Contains("bulunamadı", error!);
    }

    [Fact]
    public async Task BuildCalculationRunAsync_Success_ReturnsRun()
    {
        var bundle = new GenelKasaR10EngineInputBundle
        {
            BaslangicTarihi = new DateOnly(2026, 1, 1),
            BitisTarihi = new DateOnly(2026, 1, 31),
            DevredenSonTarihi = new DateOnly(2025, 12, 31),
            PoolEntries = new List<UnifiedPoolEntry>()
        };

        var calcRun = new CalculationRun
        {
            ReportDate = new DateOnly(2026, 1, 31),
            FormulaSetId = BuiltInFormulaSetIds.GenelKasaR10
        };

        _draftsMock
            .Setup(d => d.BuildGenelKasaR10EngineInputsAsync(It.IsAny<DateOnly?>(), It.IsAny<decimal?>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<GenelKasaR10EngineInputBundle>.Success(bundle));

        _engineMock
            .Setup(e => e.GetBuiltInFormulaSets())
            .Returns(new List<FormulaSet>
            {
                new() { Id = BuiltInFormulaSetIds.GenelKasaR10, Name = "Genel Kasa R10", Templates = new() }
            });

        _engineMock
            .Setup(e => e.Run(It.IsAny<DateOnly>(), It.IsAny<FormulaSet>(), It.IsAny<IReadOnlyList<UnifiedPoolEntry>>(), null))
            .Returns(Result<CalculationRun>.Success(calcRun));

        var (run, error) = await _sut.BuildCalculationRunAsync(null, null, "/tmp", false, CancellationToken.None);

        Assert.NotNull(run);
        Assert.Null(error);
        Assert.Equal(BuiltInFormulaSetIds.GenelKasaR10, run.FormulaSetId);
    }

    [Fact]
    public async Task BuildReportDataAsync_Failure_ReturnsIssues()
    {
        _draftsMock
            .Setup(d => d.BuildGenelKasaR10EngineInputsAsync(It.IsAny<DateOnly?>(), It.IsAny<decimal?>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<GenelKasaR10EngineInputBundle>.Fail("Veri yok"));

        var data = await _sut.BuildReportDataAsync(null, null, "/tmp", false, CancellationToken.None);

        Assert.NotEmpty(data.Issues);
    }

    [Fact]
    public void GetDecimal_OutputsFirst_ThenOverrides_ThenInputs()
    {
        var run = new CalculationRun
        {
            Inputs = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { { "key1", 10m } },
            Overrides = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { { "key2", 20m } },
            Outputs = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { { "key3", 30m } }
        };

        Assert.Equal(10m, IGenelKasaRaporService.GetDecimal(run, "key1"));
        Assert.Equal(20m, IGenelKasaRaporService.GetDecimal(run, "key2"));
        Assert.Equal(30m, IGenelKasaRaporService.GetDecimal(run, "key3"));
    }

    [Fact]
    public void GetDecimal_MissingKey_ReturnsNullAndAddsIssue()
    {
        var run = new CalculationRun();
        var issues = new List<string>();

        var result = IGenelKasaRaporService.GetDecimal(run, "nonexistent", issues);

        Assert.Null(result);
        Assert.Single(issues);
        Assert.Contains("nonexistent", issues[0]);
    }
}
