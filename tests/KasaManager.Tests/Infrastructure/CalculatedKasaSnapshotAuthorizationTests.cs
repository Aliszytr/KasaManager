using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace KasaManager.Tests.Infrastructure;

public sealed class CalculatedKasaSnapshotAuthorizationTests : IDisposable
{
    private static readonly DateOnly TestDate = new(2026, 7, 18);
    private readonly KasaManagerDbContext _db;
    private readonly CalculatedKasaSnapshotService _service;

    public CalculatedKasaSnapshotAuthorizationTests()
    {
        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseInMemoryDatabase($"SnapshotAuth_{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings =>
                warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new KasaManagerDbContext(options);
        _service = new CalculatedKasaSnapshotService(
            _db, NullLogger<CalculatedKasaSnapshotService>.Instance);
    }

    [Fact]
    public async Task Save_UsesServerActorAndOverridesUntrustedCreatorMetadata()
    {
        var snapshot = NewSnapshot();
        snapshot.CalculatedBy = "client-spoof";
        snapshot.CalculatedByUserId = 999;

        var saved = await _service.SaveAsync(snapshot, 17, "server-user");

        Assert.Equal(17, saved.CalculatedByUserId);
        Assert.Equal("server-user", saved.CalculatedBy);
        Assert.Equal(1, saved.Version);
        Assert.Equal(NewSnapshot().KasaRaporDataJson, saved.KasaRaporDataJson);
    }

    [Fact]
    public async Task Save_ActorOnlyReplayReusesActiveFinancialVersionAndPreservesCreator()
    {
        var first = await _service.SaveAsync(NewSnapshot(), 17, "user-a");
        var daily = Assert.Single(_db.DailyCalculationResults);
        var dailyCalculatedAt = daily.CalculatedAt;
        var second = await _service.SaveAsync(NewSnapshot(), 29, "user-b");

        Assert.Equal(first.Id, second.Id);
        Assert.Single(_db.CalculatedKasaSnapshots);
        Assert.Equal(1, first.Version);
        Assert.True(first.IsActive);
        Assert.Equal(17, first.CalculatedByUserId);
        Assert.Equal("user-a", first.CalculatedBy);
        Assert.Equal(NewSnapshot().KasaRaporDataJson, first.KasaRaporDataJson);
        Assert.Equal(1, daily.CalculatedVersion);
        Assert.Equal("{\"output\":20}", daily.ResultsJson);
        Assert.Equal(dailyCalculatedAt, daily.CalculatedAt);
    }

    [Fact]
    public async Task Save_FinancialChangeCreatesVersionWithTheNewActor()
    {
        var first = await _service.SaveAsync(NewSnapshot(), 17, "user-a");
        var second = await _service.SaveAsync(NewSnapshot(financial: 20), 29, "user-b");

        Assert.Equal(2, _db.CalculatedKasaSnapshots.Count());
        Assert.Equal(1, first.Version);
        Assert.False(first.IsActive);
        Assert.Equal(17, first.CalculatedByUserId);
        Assert.Equal("user-a", first.CalculatedBy);
        Assert.Equal(2, second.Version);
        Assert.True(second.IsActive);
        Assert.Equal(29, second.CalculatedByUserId);
        Assert.Equal("user-b", second.CalculatedBy);
    }

    [Fact]
    public async Task Save_MaxVersionIncludesInactiveAndDeletedHistoricalVersions()
    {
        var first = await _service.SaveAsync(NewSnapshot(financial: 10), 17, "user-a");
        await _service.SaveAsync(NewSnapshot(financial: 20), 29, "user-b");
        var third = await _service.SaveAsync(NewSnapshot(financial: 30), 29, "user-b");

        Assert.Equal(
            SnapshotMutationResult.Success,
            await _service.ActivateVersionAsync(first.Id, 44, true));
        third.IsDeleted = true;
        third.IsActive = false;
        await _db.SaveChangesAsync();
        var fourth = await _service.SaveAsync(NewSnapshot(financial: 40), 55, "user-c");

        Assert.Equal(4, fourth.Version);
        Assert.Equal(4, _db.CalculatedKasaSnapshots.Count());
        Assert.Single(_db.CalculatedKasaSnapshots.Where(snapshot => snapshot.IsActive));
        Assert.True(fourth.IsActive);
    }

    [Fact]
    public async Task InteractiveMutationsRejectNonPositiveActorDefensively()
    {
        var snapshot = NewSnapshot();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.SaveAsync(snapshot, 0, "user"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.UpdateAsync(Guid.NewGuid(), null, null, null, -1, false));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.DeleteAsync(Guid.NewGuid(), 0, true, "admin"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.RestoreAsync(Guid.NewGuid(), -1, true));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.ActivateVersionAsync(Guid.NewGuid(), 0, true));

        Assert.Empty(_db.CalculatedKasaSnapshots);
        Assert.Empty(_db.DailyCalculationResults);
    }

    [Fact]
    public async Task MetadataUpdate_EnforcesCreatorAdminAndLegacyPolicy()
    {
        var owned = await _service.SaveAsync(NewSnapshot(), 17, "creator");
        var originalJson = owned.KasaRaporDataJson;
        var originalInputs = owned.InputsJson;
        var originalOutputs = owned.OutputsJson;
        var originalVersion = owned.Version;

        var forbidden = await _service.UpdateAsync(
            owned.Id, "forbidden", "forbidden", "forbidden", 29, false);
        Assert.Equal(SnapshotMutationResult.Forbidden, forbidden);
        Assert.Equal("Original", owned.Name);

        var creatorResult = await _service.UpdateAsync(
            owned.Id, "Creator name", "Creator description", "Creator notes", 17, false);
        Assert.Equal(SnapshotMutationResult.Success, creatorResult);
        Assert.Equal("Creator name", owned.Name);
        Assert.Equal(17, owned.CalculatedByUserId);
        Assert.Equal("creator", owned.CalculatedBy);
        Assert.Equal(originalJson, owned.KasaRaporDataJson);
        Assert.Equal(originalInputs, owned.InputsJson);
        Assert.Equal(originalOutputs, owned.OutputsJson);
        Assert.Equal(originalVersion, owned.Version);

        var adminResult = await _service.UpdateAsync(
            owned.Id, "Admin name", "Admin description", "Admin notes", 44, true);
        Assert.Equal(SnapshotMutationResult.Success, adminResult);
        Assert.Equal(17, owned.CalculatedByUserId);
        Assert.Equal("creator", owned.CalculatedBy);

        var legacy = NewSnapshot(TestDate.AddDays(-1));
        legacy.CalculatedBy = "legacy-string";
        legacy.CalculatedByUserId = null;
        _db.CalculatedKasaSnapshots.Add(legacy);
        await _db.SaveChangesAsync();

        Assert.Equal(SnapshotMutationResult.Forbidden,
            await _service.UpdateAsync(legacy.Id, "normal", null, null, 17, false));
        Assert.Equal(SnapshotMutationResult.Success,
            await _service.UpdateAsync(legacy.Id, "admin", null, null, 44, true));
        Assert.Null(legacy.CalculatedByUserId);
        Assert.Equal("legacy-string", legacy.CalculatedBy);
    }

    [Fact]
    public async Task Delete_IsAdminOnlySoftDeleteAndRepeatedCallPreservesFirstActor()
    {
        var snapshot = await _service.SaveAsync(NewSnapshot(), 17, "creator");
        var originalJson = snapshot.KasaRaporDataJson;
        var originalVersion = snapshot.Version;

        Assert.Equal(SnapshotMutationResult.Forbidden,
            await _service.DeleteAsync(snapshot.Id, 29, false, "normal-user"));
        Assert.False(snapshot.IsDeleted);

        Assert.Equal(SnapshotMutationResult.Success,
            await _service.DeleteAsync(snapshot.Id, 44, true, "admin-a"));
        var firstDeletedAt = snapshot.DeletedAtUtc;
        Assert.True(snapshot.IsDeleted);
        Assert.False(snapshot.IsActive);
        Assert.Equal(44, snapshot.DeletedByUserId);
        Assert.Equal("admin-a", snapshot.DeletedBy);
        Assert.NotNull(firstDeletedAt);

        Assert.Equal(SnapshotMutationResult.NoChange,
            await _service.DeleteAsync(snapshot.Id, 55, true, "admin-b"));
        Assert.Equal(44, snapshot.DeletedByUserId);
        Assert.Equal("admin-a", snapshot.DeletedBy);
        Assert.Equal(firstDeletedAt, snapshot.DeletedAtUtc);
        Assert.Equal(originalJson, snapshot.KasaRaporDataJson);
        Assert.Equal(originalVersion, snapshot.Version);

        var legacy = NewSnapshot(TestDate.AddDays(-1));
        legacy.CalculatedByUserId = null;
        _db.CalculatedKasaSnapshots.Add(legacy);
        await _db.SaveChangesAsync();
        Assert.Equal(SnapshotMutationResult.Forbidden,
            await _service.DeleteAsync(legacy.Id, 29, false, "normal-user"));
        Assert.Equal(SnapshotMutationResult.Success,
            await _service.DeleteAsync(legacy.Id, 44, true, "admin-a"));
        Assert.Equal(44, legacy.DeletedByUserId);
    }

    [Fact]
    public async Task Restore_IsAdminOnlyAndClearsAllDeleteMetadataOnly()
    {
        var snapshot = await _service.SaveAsync(NewSnapshot(), 17, "creator");
        await _service.DeleteAsync(snapshot.Id, 44, true, "admin");
        var originalJson = snapshot.KasaRaporDataJson;
        var originalVersion = snapshot.Version;

        Assert.Equal(SnapshotMutationResult.Forbidden,
            await _service.RestoreAsync(snapshot.Id, 29, false));
        Assert.True(snapshot.IsDeleted);

        Assert.Equal(SnapshotMutationResult.Success,
            await _service.RestoreAsync(snapshot.Id, 44, true));
        Assert.False(snapshot.IsDeleted);
        Assert.True(snapshot.IsActive);
        Assert.Null(snapshot.DeletedAtUtc);
        Assert.Null(snapshot.DeletedBy);
        Assert.Null(snapshot.DeletedByUserId);
        Assert.Equal(17, snapshot.CalculatedByUserId);
        Assert.Equal("creator", snapshot.CalculatedBy);
        Assert.Equal(originalJson, snapshot.KasaRaporDataJson);
        Assert.Equal(originalVersion, snapshot.Version);
    }

    [Fact]
    public async Task SharedReadsDoNotFilterByCreatorAndLegacyNullRemainsReadable()
    {
        var userASnapshot = await _service.SaveAsync(NewSnapshot(), 17, "user-a");
        var legacy = NewSnapshot(TestDate.AddDays(-1));
        legacy.CalculatedByUserId = null;
        legacy.CalculatedBy = "legacy";
        _db.CalculatedKasaSnapshots.Add(legacy);
        await _db.SaveChangesAsync();

        Assert.Equal(userASnapshot.Id, (await _service.GetByIdAsync(userASnapshot.Id))!.Id);
        Assert.Equal(legacy.Id, (await _service.GetByIdAsync(legacy.Id))!.Id);
        Assert.Contains(await _service.ListByDateRangeAsync(
            TestDate.AddDays(-2), TestDate.AddDays(1)), item => item.Id == userASnapshot.Id);
        var search = await _service.SearchAsync(new KasaReportSearchQuery
        {
            StartDate = TestDate.AddDays(-2),
            EndDate = TestDate.AddDays(1),
            Page = 1,
            PageSize = 20
        });
        Assert.Contains(search.Items, item => item.Id == userASnapshot.Id);
        Assert.Contains(search.Items, item => item.Id == legacy.Id);
    }

    [Fact]
    public async Task VersionActivation_IsAdminOnlyAtServiceBoundary()
    {
        var first = await _service.SaveAsync(NewSnapshot(), 17, "creator");
        await _service.SaveAsync(NewSnapshot(financial: 20), 29, "second");

        Assert.Equal(SnapshotMutationResult.Forbidden,
            await _service.ActivateVersionAsync(first.Id, 17, false));
        Assert.False(first.IsActive);
        Assert.Equal(SnapshotMutationResult.Success,
            await _service.ActivateVersionAsync(first.Id, 44, true));
        Assert.True(first.IsActive);
    }

    private static CalculatedKasaSnapshot NewSnapshot(DateOnly? date = null, int financial = 10) => new()
    {
        RaporTarihi = date ?? TestDate,
        KasaTuru = KasaRaporTuru.Aksam,
        Name = "Original",
        Description = "Original description",
        Notes = "Original notes",
        InputsJson = $"{{\"input\":{financial}}}",
        OutputsJson = $"{{\"output\":{financial * 2}}}",
        KasaRaporDataJson = JsonSerializer.Serialize(new
        {
            PayloadVersion = 2,
            financial,
            ImmutableAudit = new KasaImmutableAuditData(),
            ImmutableAuditDetails = EmptyAuditDetails()
        }),
        FinancialExceptionsSummaryJson = $"{{\"summary\":{financial * 3}}}",
        FormulaSetName = "formula-v1"
    };

    private static HesapKontrolImmutableAuditDetails EmptyAuditDetails() => new(
        Array.Empty<HesapKontrolImmutableAuditRecord>(),
        new HesapKontrolImmutableAuditGroups(
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>()));

    public void Dispose() => _db.Dispose();
}
