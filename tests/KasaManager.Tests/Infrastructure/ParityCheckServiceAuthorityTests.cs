using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Calculation;
using KasaManager.Domain.Calculation.Data;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KasaManager.Tests.Infrastructure;

/// <summary>
/// Regression coverage for the 25→26 Aug carryover corruption incident: a stale
/// fire-and-forget shadow/parity run overwrote an already-saved authoritative
/// DailyCalculationResult with a zeroed-out value, which the next day's
/// CarryoverResolver then read back as if it were the real answer.
///
/// Root cause: ParityCheckService.RunShadowCheckAsync used to Add/Update
/// DailyCalculationResults directly. That authority has been removed — shadow/parity
/// is now strictly observational (in-memory comparison + CalculationParityDrift log
/// only). These tests pin that invariant.
/// </summary>
public sealed class ParityCheckServiceAuthorityTests : IDisposable
{
    private readonly KasaManagerDbContext _db;
    private readonly Mock<IFormulaEngineService> _engineMock = new();
    private readonly ParityCheckService _sut;

    public ParityCheckServiceAuthorityTests()
    {
        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseInMemoryDatabase($"ParityAuth_{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new KasaManagerDbContext(options);
        _sut = new ParityCheckService(_db, _engineMock.Object, NullLogger<ParityCheckService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private static FormulaSet MakeFormulaSet() => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = "test-set",
        AppliesTo = AppliesToKasa.Any
    };

    private static CalculationRun MakeRun(DateOnly date, decimal genelKasa) => new()
    {
        ReportDate = date,
        Outputs = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["genel_kasa"] = genelKasa
        }
    };

    private void StubShadowEngineResult(DateOnly date, decimal shadowGenelKasa)
    {
        _engineMock
            .Setup(e => e.Run(
                date,
                It.IsAny<FormulaSet>(),
                It.IsAny<IReadOnlyList<UnifiedPoolEntry>>(),
                It.IsAny<IReadOnlyDictionary<string, decimal>>()))
            .Returns(Result<CalculationRun>.Success(MakeRun(date, shadowGenelKasa)));
    }

    /// <summary>
    /// Test 1: Preview is zero-write for authority.
    /// Running the shadow check when no authoritative row exists yet must not create one.
    /// </summary>
    [Fact]
    public async Task RunShadowCheckAsync_NoExistingAuthoritativeRow_CreatesNoDailyCalculationResult()
    {
        var date = new DateOnly(2026, 8, 26);
        StubShadowEngineResult(date, shadowGenelKasa: 0m);

        await _sut.RunShadowCheckAsync(
            date,
            "Aksam",
            legacyInputs: new List<UnifiedPoolEntry>(),
            legacyFormulaRun: MakeRun(date, 10000m),
            formulaSet: MakeFormulaSet(),
            currentOverrides: new Dictionary<string, decimal>(),
            ct: CancellationToken.None);

        Assert.Empty(_db.DailyCalculationResults);
    }

    /// <summary>
    /// Test 2: Stale shadow cannot overwrite saved result.
    /// Deterministic race reproduction: the correct result (10000) is already persisted
    /// (simulating the user's Save completing first), then a stale shadow run — computing
    /// a divergent 0 — is executed afterwards (simulating the earlier-started background
    /// task finishing late). The authoritative row must be untouched.
    /// </summary>
    [Fact]
    public async Task RunShadowCheckAsync_StaleShadowAfterSave_NeverMutatesAuthoritativeRow()
    {
        var date = new DateOnly(2026, 8, 25);
        var saved = new DailyCalculationResult
        {
            Id = Guid.NewGuid(),
            ForDate = date,
            KasaTuru = "Aksam",
            CalculatedVersion = 1,
            ResultsJson = "{\"genel_kasa\":10000}",
            CalculatedAt = new DateTime(2026, 8, 25, 20, 0, 0, DateTimeKind.Utc)
        };
        _db.DailyCalculationResults.Add(saved);
        await _db.SaveChangesAsync();

        // The stale shadow run computes a divergent (wrong) value — this is exactly the
        // shape of the production incident: shadow saw incomplete DailyFacts and got 0.
        StubShadowEngineResult(date, shadowGenelKasa: 0m);

        await _sut.RunShadowCheckAsync(
            date,
            "Aksam",
            legacyInputs: new List<UnifiedPoolEntry>(),
            legacyFormulaRun: MakeRun(date, 0m),
            formulaSet: MakeFormulaSet(),
            currentOverrides: new Dictionary<string, decimal>(),
            ct: CancellationToken.None);

        var row = Assert.Single(_db.DailyCalculationResults);
        Assert.Equal("{\"genel_kasa\":10000}", row.ResultsJson);
        Assert.Equal(1, row.CalculatedVersion);
        Assert.Equal(saved.CalculatedAt, row.CalculatedAt);
    }

    /// <summary>
    /// Shadow/parity's observational purpose (drift logging) must still function after
    /// write authority is removed — only the authoritative table write is gone.
    /// </summary>
    [Fact]
    public async Task RunShadowCheckAsync_StillLogsParityDrifts_WhenLegacyAndShadowDiverge()
    {
        var date = new DateOnly(2026, 8, 25);
        StubShadowEngineResult(date, shadowGenelKasa: 0m);

        await _sut.RunShadowCheckAsync(
            date,
            "Aksam",
            legacyInputs: new List<UnifiedPoolEntry>(),
            legacyFormulaRun: MakeRun(date, 10000m),
            formulaSet: MakeFormulaSet(),
            currentOverrides: new Dictionary<string, decimal>(),
            ct: CancellationToken.None);

        Assert.Contains(_db.CalculationParityDrifts, d =>
            d.FieldKey == "[OUTPUT] genel_kasa" && d.AbsoluteDifference == 10000m);
    }
}
