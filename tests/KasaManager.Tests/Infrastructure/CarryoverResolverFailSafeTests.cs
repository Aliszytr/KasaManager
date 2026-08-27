using KasaManager.Application.Abstractions;
using KasaManager.Domain.Calculation.Data;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Domain.Settings;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KasaManager.Tests.Infrastructure;

/// <summary>
/// Regression coverage for CarryoverResolver's fail-safe contract around the
/// DailyCalculationResults-first / CalculatedKasaSnapshots-fallback chain, once the
/// producer-side bug (ParityCheckService writing to the authoritative table — see
/// ParityCheckServiceAuthorityTests) is fixed.
///
/// Two distinct notions of "the DailyCalculationResult row wasn't usable as-is" exist
/// and must not be conflated:
///   - Case A: JSON parses AND the required carryover key is present — even if its
///     value is genuinely 0. This is an authoritative, valid answer. No fallback.
///   - Case B/C: JSON fails to parse, or the required key is absent/unparseable. The
///     row exists but cannot be trusted as an answer — CarryoverResolver must fall
///     back to CalculatedKasaSnapshot exactly as it would if the row didn't exist at
///     all, and must NEVER silently report 0 as if it were an authoritative zero.
///   - Case D: the row is unusable (as in B/C) AND no usable snapshot exists either.
///     The terminal fallback is still 0, but it must be provenance-marked distinctly
///     from a genuine authoritative zero (Case A) via SourceCode/UsedFallback.
/// </summary>
public sealed class CarryoverResolverFailSafeTests : IDisposable
{
    private readonly KasaManagerDbContext _db;
    private readonly Mock<IKasaGlobalDefaultsService> _defaultsMock = new();
    private readonly CarryoverResolver _sut;

    public CarryoverResolverFailSafeTests()
    {
        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseInMemoryDatabase($"CarryoverFailSafe_{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new KasaManagerDbContext(options);

        var settings = new KasaGlobalDefaultsSettings { Id = 1 }; // no seed overrides — force DB/fallback path
        _defaultsMock.Setup(s => s.GetOrCreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);

        _sut = new CarryoverResolver(_defaultsMock.Object, _db, NullLogger<CarryoverResolver>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private static void SeedDailyResult(KasaManagerDbContext db, DateOnly date, string resultsJson)
    {
        db.DailyCalculationResults.Add(new DailyCalculationResult
        {
            Id = Guid.NewGuid(),
            ForDate = date,
            KasaTuru = "Aksam",
            CalculatedVersion = 1,
            ResultsJson = resultsJson,
            CalculatedAt = DateTime.UtcNow
        });
    }

    private static void SeedSnapshot(KasaManagerDbContext db, DateOnly date, decimal genelKasa, KasaRaporTuru kasaTuru = KasaRaporTuru.Aksam)
    {
        db.CalculatedKasaSnapshots.Add(new CalculatedKasaSnapshot
        {
            Id = Guid.NewGuid(),
            RaporTarihi = date,
            KasaTuru = kasaTuru,
            Version = 1,
            IsActive = true,
            IsDeleted = false,
            OutputsJson = kasaTuru == KasaRaporTuru.Genel
                ? $"{{\"sonraki_kasaya_devredecek\":{genelKasa}}}"
                : $"{{\"genel_kasa\":{genelKasa}}}"
        });
    }

    /// <summary>
    /// Test 3: 24→25→26 carryover regression.
    /// 25 Aug Akşam finalizes at genel_kasa=10000. 26 Aug must carry that value forward.
    /// </summary>
    [Fact]
    public async Task ResolveAksamKasaNakit_PriorDayFinalizedAt10000_CarriesForwardExactValue()
    {
        var aug25 = new DateOnly(2026, 8, 25);
        var aug26 = new DateOnly(2026, 8, 26);

        SeedDailyResult(_db, aug25, "{\"genel_kasa\":10000}");
        await _db.SaveChangesAsync();

        var result = await _sut.ResolveAsync(aug26, CarryoverScope.AksamKasaNakit);

        Assert.Equal(10000m, result.Value);
        Assert.Equal("DailyCalculationResult", result.SourceCode);
        Assert.False(result.UsedFallback);
    }

    /// <summary>
    /// Test 4 (Case A): Genuine zero. A finalized canonical result of exactly 0 must
    /// resolve to 0 — not be treated as missing/invalid just because it's the numeric
    /// zero. JSON parses and the required key is present, so it's authoritative.
    /// </summary>
    [Fact]
    public async Task ResolveAksamKasaNakit_PriorDayGenuineZero_ResolvesToZero_NotFallback()
    {
        var aug25 = new DateOnly(2026, 8, 25);
        var aug26 = new DateOnly(2026, 8, 26);

        SeedDailyResult(_db, aug25, "{\"genel_kasa\":0}");
        await _db.SaveChangesAsync();

        var result = await _sut.ResolveAsync(aug26, CarryoverScope.AksamKasaNakit);

        Assert.Equal(0m, result.Value);
        Assert.Equal("DailyCalculationResult", result.SourceCode);
        Assert.False(result.UsedFallback);
    }

    /// <summary>
    /// Test 5: Missing result fallback. No DailyCalculationResult and no
    /// CalculatedKasaSnapshot exist — must fall back to the documented DefaultZero
    /// contract without throwing.
    /// </summary>
    [Fact]
    public async Task ResolveAksamKasaNakit_NoPriorRecordAtAll_FallsBackToDefaultZero()
    {
        var aug26 = new DateOnly(2026, 8, 26);

        var result = await _sut.ResolveAsync(aug26, CarryoverScope.AksamKasaNakit);

        Assert.Equal(0m, result.Value);
        Assert.Equal("DefaultZero", result.SourceCode);
        Assert.True(result.UsedFallback);
    }

    /// <summary>
    /// Test 6 (Case B): Malformed DailyCalculationResult + valid CalculatedKasaSnapshot.
    /// The DB row exists but its JSON is unparseable — it must be treated as unusable
    /// (not a valid zero) and CarryoverResolver must fall through to the snapshot,
    /// exactly as if the row were missing. Must NOT silently report 0.
    /// </summary>
    [Fact]
    public async Task ResolveAksamKasaNakit_MalformedResultsJson_FallsBackToValidSnapshot()
    {
        var aug25 = new DateOnly(2026, 8, 25);
        var aug26 = new DateOnly(2026, 8, 26);

        SeedDailyResult(_db, aug25, "{not-valid-json");
        SeedSnapshot(_db, aug25, 10000m);
        await _db.SaveChangesAsync();

        var result = await _sut.ResolveAsync(aug26, CarryoverScope.AksamKasaNakit);

        Assert.Equal(10000m, result.Value);
        Assert.Equal("CalculatedKasaSnapshot", result.SourceCode);
        Assert.False(result.UsedFallback);
    }

    /// <summary>
    /// Test 6b (Case C): DailyCalculationResult JSON is syntactically valid but the
    /// required `genel_kasa`/carryover key is absent. Same contract as Test 6 —
    /// unusable record, fall through to the valid snapshot, never a silent 0.
    /// </summary>
    [Fact]
    public async Task ResolveAksamKasaNakit_ResultsJsonMissingRequiredKey_FallsBackToValidSnapshot()
    {
        var aug25 = new DateOnly(2026, 8, 25);
        var aug26 = new DateOnly(2026, 8, 26);

        SeedDailyResult(_db, aug25, "{\"some_unrelated_key\":123}");
        SeedSnapshot(_db, aug25, 10000m);
        await _db.SaveChangesAsync();

        var result = await _sut.ResolveAsync(aug26, CarryoverScope.AksamKasaNakit);

        Assert.Equal(10000m, result.Value);
        Assert.Equal("CalculatedKasaSnapshot", result.SourceCode);
        Assert.False(result.UsedFallback);
    }

    /// <summary>
    /// Test (Case D): invalid DailyCalculationResult AND no usable snapshot either.
    /// The safest terminal fallback (0) is used, but it must not be indistinguishable
    /// from a genuine authoritative zero (Test 4) — SourceCode carries distinct,
    /// explicit "invalid/unavailable" provenance instead of silently reusing
    /// "DailyCalculationResult" or being conflated with "DefaultZero" (the
    /// truly-no-record case, Test 5).
    /// </summary>
    [Fact]
    public async Task ResolveAksamKasaNakit_MalformedResultAndNoSnapshot_FallsBackToZero_MarkedDistinctlyFromGenuineZero()
    {
        var aug25 = new DateOnly(2026, 8, 25);
        var aug26 = new DateOnly(2026, 8, 26);

        SeedDailyResult(_db, aug25, "{not-valid-json");
        await _db.SaveChangesAsync();

        var result = await _sut.ResolveAsync(aug26, CarryoverScope.AksamKasaNakit);

        Assert.Equal(0m, result.Value);
        Assert.True(result.UsedFallback);
        Assert.NotEqual("DailyCalculationResult", result.SourceCode);
        Assert.Equal("InvalidResultFallbackZero", result.SourceCode);
    }

    /// <summary>
    /// Same as above but for the missing-required-key variant, to confirm both
    /// unusable-record shapes converge on the same distinctly-marked terminal fallback.
    /// </summary>
    [Fact]
    public async Task ResolveAksamKasaNakit_MissingKeyResultAndNoSnapshot_FallsBackToZero_MarkedDistinctlyFromGenuineZero()
    {
        var aug25 = new DateOnly(2026, 8, 25);
        var aug26 = new DateOnly(2026, 8, 26);

        SeedDailyResult(_db, aug25, "{\"some_unrelated_key\":123}");
        await _db.SaveChangesAsync();

        var result = await _sut.ResolveAsync(aug26, CarryoverScope.AksamKasaNakit);

        Assert.Equal(0m, result.Value);
        Assert.True(result.UsedFallback);
        Assert.NotEqual("DailyCalculationResult", result.SourceCode);
        Assert.Equal("InvalidResultFallbackZero", result.SourceCode);
    }

    /// <summary>
    /// Same Case B contract as Test 6, but exercised through ResolveGenelKasaAsync,
    /// whose fallback path uses a different (date-range) snapshot query than
    /// Sabah/Akşam. Confirms the fix applies uniformly across all three resolver
    /// methods, not just AksamKasaNakit.
    /// </summary>
    [Fact]
    public async Task ResolveGenelKasa_MalformedResultsJson_FallsBackToValidSnapshot()
    {
        var aug25 = new DateOnly(2026, 8, 25);
        var aug26 = new DateOnly(2026, 8, 26);

        _db.DailyCalculationResults.Add(new DailyCalculationResult
        {
            Id = Guid.NewGuid(),
            ForDate = aug25,
            KasaTuru = "Genel",
            CalculatedVersion = 1,
            ResultsJson = "{not-valid-json",
            CalculatedAt = DateTime.UtcNow
        });
        SeedSnapshot(_db, aug25, 10000m, KasaRaporTuru.Genel);
        await _db.SaveChangesAsync();

        var result = await _sut.ResolveAsync(aug26, CarryoverScope.GenelKasa);

        Assert.Equal(10000m, result.Value);
        Assert.Equal("CalculatedKasaSnapshot", result.SourceCode);
        Assert.False(result.UsedFallback);
    }
}
