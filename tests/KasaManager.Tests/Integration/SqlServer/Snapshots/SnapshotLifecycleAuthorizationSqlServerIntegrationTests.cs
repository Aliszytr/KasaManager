using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Calculation.Data;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KasaManager.Tests.Integration.SqlServer.Snapshots;

[Collection(SqlServerIntegrationCollection.Name)]
public sealed class SnapshotLifecycleAuthorizationSqlServerIntegrationTests
{
    private static readonly DateOnly DeleteDate = new(2060, 6, 1);
    private static readonly DateOnly RestoreDate = new(2060, 6, 2);
    private static readonly DateOnly ActivationDate = new(2060, 6, 3);
    private static readonly DateOnly ActivationOtherDate = new(2060, 6, 4);
    private static readonly DateOnly DailySynchronizationDate = new(2060, 6, 5);
    private static readonly DateOnly IsolationDate = new(2060, 6, 6);
    private static readonly DateOnly IsolationOtherDate = new(2060, 6, 7);
    private static readonly DateOnly MissingDailyDate = new(2060, 6, 8);
    private static readonly DateOnly AtomicityDate = new(2060, 6, 9);
    private readonly SqlServerIntegrationFixture _fixture;

    public SnapshotLifecycleAuthorizationSqlServerIntegrationTests(
        SqlServerIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [SqlServerFact]
    public async Task Delete_IsAdminOnlyFailClosedAndReplayPreservesFirstAuditAndFinancialState()
    {
        try
        {
            Guid id;
            await using (var saveContext = _fixture.CreateContext())
            {
                var untrusted = NewSnapshot(DeleteDate, KasaRaporTuru.Aksam, 101m);
                untrusted.CalculatedByUserId = 999;
                untrusted.CalculatedBy = "client-creator-spoof";
                id = (await CreateService(saveContext).SaveAsync(
                    untrusted, 17, "server-creator")).Id;
            }

            var original = await ReadStateAsync(id, DeleteDate, "Aksam");

            await using (var normalCreatorContext = _fixture.CreateContext())
            {
                Assert.Equal(
                    SnapshotMutationResult.Forbidden,
                    await CreateService(normalCreatorContext).DeleteAsync(
                        id, 17, false, "creator-client-spoof"));
            }

            Assert.Equal(original, await ReadStateAsync(id, DeleteDate, "Aksam"));

            await using (var otherNormalContext = _fixture.CreateContext())
            {
                Assert.Equal(
                    SnapshotMutationResult.Forbidden,
                    await CreateService(otherNormalContext).DeleteAsync(
                        id, 29, false, "other-client-spoof"));
            }

            Assert.Equal(original, await ReadStateAsync(id, DeleteDate, "Aksam"));

            foreach (var invalidActor in new[] { 0, -1 })
            {
                await using var invalidContext = _fixture.CreateContext();
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                    CreateService(invalidContext).DeleteAsync(
                        id, invalidActor, true, "invalid-admin"));
            }

            Assert.Equal(original, await ReadStateAsync(id, DeleteDate, "Aksam"));

            await using (var adminContext = _fixture.CreateContext())
            {
                Assert.Equal(
                    SnapshotMutationResult.Success,
                    await CreateService(adminContext).DeleteAsync(
                        id, 44, true, "server-admin-a"));
            }

            var deleted = await ReadStateAsync(id, DeleteDate, "Aksam");
            Assert.True(deleted.Snapshot.IsDeleted);
            Assert.False(deleted.Snapshot.IsActive);
            Assert.NotNull(deleted.Snapshot.DeletedAtUtc);
            Assert.Equal(44, deleted.Snapshot.DeletedByUserId);
            Assert.Equal("server-admin-a", deleted.Snapshot.DeletedBy);
            AssertFinancialPayloadEqual(original, deleted);

            await using (var secondAdminContext = _fixture.CreateContext())
            {
                Assert.Equal(
                    SnapshotMutationResult.NoChange,
                    await CreateService(secondAdminContext).DeleteAsync(
                        id, 55, true, "server-admin-b"));
            }

            var replayed = await ReadStateAsync(id, DeleteDate, "Aksam");
            Assert.Equal(deleted, replayed);
            Assert.Equal(1, await CountVersionsAsync(DeleteDate, KasaRaporTuru.Aksam));
        }
        finally
        {
            await CleanupAsync(DeleteDate);
        }
    }

    [SqlServerFact]
    public async Task Restore_IsAdminOnlyFailClosedClearsDeleteAuditAndIsIdempotent()
    {
        try
        {
            Guid id;
            await using (var saveContext = _fixture.CreateContext())
            {
                id = (await CreateService(saveContext).SaveAsync(
                    NewSnapshot(RestoreDate, KasaRaporTuru.Aksam, 201m),
                    17,
                    "server-creator")).Id;
            }

            await using (var deleteContext = _fixture.CreateContext())
            {
                Assert.Equal(
                    SnapshotMutationResult.Success,
                    await CreateService(deleteContext).DeleteAsync(
                        id, 44, true, "server-admin"));
            }

            var deleted = await ReadStateAsync(id, RestoreDate, "Aksam");

            foreach (var normalActor in new[] { 17, 29 })
            {
                await using var normalContext = _fixture.CreateContext();
                Assert.Equal(
                    SnapshotMutationResult.Forbidden,
                    await CreateService(normalContext).RestoreAsync(
                        id, normalActor, false));
                Assert.Equal(deleted, await ReadStateAsync(id, RestoreDate, "Aksam"));
            }

            foreach (var invalidActor in new[] { 0, -1 })
            {
                await using var invalidContext = _fixture.CreateContext();
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                    CreateService(invalidContext).RestoreAsync(id, invalidActor, true));
            }

            Assert.Equal(deleted, await ReadStateAsync(id, RestoreDate, "Aksam"));

            await using (var adminContext = _fixture.CreateContext())
            {
                Assert.Equal(
                    SnapshotMutationResult.Success,
                    await CreateService(adminContext).RestoreAsync(id, 44, true));
            }

            var restored = await ReadStateAsync(id, RestoreDate, "Aksam");
            Assert.False(restored.Snapshot.IsDeleted);
            Assert.True(restored.Snapshot.IsActive);
            Assert.Null(restored.Snapshot.DeletedAtUtc);
            Assert.Null(restored.Snapshot.DeletedBy);
            Assert.Null(restored.Snapshot.DeletedByUserId);
            AssertFinancialPayloadEqual(deleted, restored);

            await using (var replayContext = _fixture.CreateContext())
            {
                Assert.Equal(
                    SnapshotMutationResult.NoChange,
                    await CreateService(replayContext).RestoreAsync(id, 55, true));
            }

            Assert.Equal(restored, await ReadStateAsync(id, RestoreDate, "Aksam"));
            Assert.Equal(1, await CountVersionsAsync(RestoreDate, KasaRaporTuru.Aksam));
        }
        finally
        {
            await CleanupAsync(RestoreDate);
        }
    }

    [SqlServerFact]
    public async Task ActivateVersion_IsAdminOnlyRejectsDeletedTargetAndIsolatesBusinessKeyChains()
    {
        try
        {
            Guid aksamV1Id;
            Guid aksamV2Id;
            await using (var saveContext = _fixture.CreateContext())
            {
                var service = CreateService(saveContext);
                aksamV1Id = (await service.SaveAsync(
                    NewSnapshot(ActivationDate, KasaRaporTuru.Aksam, 301m),
                    17,
                    "aksam-creator")).Id;
                aksamV2Id = (await service.SaveAsync(
                    NewSnapshot(ActivationDate, KasaRaporTuru.Aksam, 302m),
                    29,
                    "aksam-second")).Id;
                await service.SaveAsync(
                    NewSnapshot(ActivationDate, KasaRaporTuru.Sabah, 401m),
                    29,
                    "sabah-creator");
                await service.SaveAsync(
                    NewSnapshot(ActivationOtherDate, KasaRaporTuru.Aksam, 501m),
                    29,
                    "other-date-creator");
            }

            var before = await ReadDatabaseStateAsync(ActivationDate, ActivationOtherDate);

            foreach (var normalActor in new[] { 17, 29 })
            {
                await using var normalContext = _fixture.CreateContext();
                Assert.Equal(
                    SnapshotMutationResult.Forbidden,
                    await CreateService(normalContext).ActivateVersionAsync(
                        aksamV1Id, normalActor, false));
                AssertDatabaseStateEqual(before, await ReadDatabaseStateAsync(
                    ActivationDate, ActivationOtherDate));
            }

            foreach (var invalidActor in new[] { 0, -1 })
            {
                await using var invalidContext = _fixture.CreateContext();
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                    CreateService(invalidContext).ActivateVersionAsync(
                        aksamV1Id, invalidActor, true));
            }

            AssertDatabaseStateEqual(before, await ReadDatabaseStateAsync(
                ActivationDate, ActivationOtherDate));

            await using (var deleteContext = _fixture.CreateContext())
            {
                Assert.Equal(
                    SnapshotMutationResult.Success,
                    await CreateService(deleteContext).DeleteAsync(
                        aksamV1Id, 44, true, "server-admin"));
            }

            var deletedState = await ReadDatabaseStateAsync(
                ActivationDate, ActivationOtherDate);
            await using (var deletedActivationContext = _fixture.CreateContext())
            {
                Assert.Equal(
                    SnapshotMutationResult.NotFound,
                    await CreateService(deletedActivationContext).ActivateVersionAsync(
                        aksamV1Id, 44, true));
            }

            AssertDatabaseStateEqual(deletedState, await ReadDatabaseStateAsync(
                ActivationDate, ActivationOtherDate));

            await using (var restoreContext = _fixture.CreateContext())
            {
                Assert.Equal(
                    SnapshotMutationResult.Success,
                    await CreateService(restoreContext).RestoreAsync(
                        aksamV1Id, 44, true));
            }

            var beforeActivation = await ReadDatabaseStateAsync(
                ActivationDate, ActivationOtherDate);
            await using (var adminContext = _fixture.CreateContext())
            {
                Assert.Equal(
                    SnapshotMutationResult.Success,
                    await CreateService(adminContext).ActivateVersionAsync(
                        aksamV1Id, 44, true));
            }

            var after = await ReadDatabaseStateAsync(ActivationDate, ActivationOtherDate);
            var selected = Assert.Single(after.Snapshots, item => item.Id == aksamV1Id);
            var priorActive = Assert.Single(after.Snapshots, item => item.Id == aksamV2Id);
            Assert.True(selected.IsActive);
            Assert.False(priorActive.IsActive);
            Assert.Single(after.Snapshots, item =>
                item.Date == ActivationDate &&
                item.KasaTuru == KasaRaporTuru.Aksam &&
                item.IsActive &&
                !item.IsDeleted);

            Assert.Equal(
                beforeActivation.Snapshots.Single(item => item.Id == aksamV1Id).Financial,
                selected.Financial);
            Assert.Equal(
                beforeActivation.Snapshots.Single(item => item.Id == aksamV2Id).Financial,
                priorActive.Financial);

            Assert.Equal(
                beforeActivation.Snapshots.Where(item => item.KasaTuru == KasaRaporTuru.Sabah),
                after.Snapshots.Where(item => item.KasaTuru == KasaRaporTuru.Sabah));
            Assert.Equal(
                beforeActivation.Snapshots.Where(item => item.Date == ActivationOtherDate),
                after.Snapshots.Where(item => item.Date == ActivationOtherDate));
            Assert.Equal(4, after.Snapshots.Count);
        }
        finally
        {
            await CleanupAsync(ActivationDate, ActivationOtherDate);
        }
    }

    [SqlServerFact]
    public async Task ActivateVersion_KeepsDailyResultConsistentWithTheSelectedSnapshot()
    {
        try
        {
            Guid firstId;
            Guid secondId;
            await using (var saveContext = _fixture.CreateContext())
            {
                var service = CreateService(saveContext);
                firstId = (await service.SaveAsync(
                    NewSnapshot(DailySynchronizationDate, KasaRaporTuru.Aksam, 601m),
                    17,
                    "first-creator")).Id;
                secondId = (await service.SaveAsync(
                    NewSnapshot(DailySynchronizationDate, KasaRaporTuru.Aksam, 602m),
                    29,
                    "second-creator")).Id;
            }

            var before = await ReadDatabaseStateAsync(DailySynchronizationDate);
            var firstBefore = Assert.Single(before.Snapshots, item => item.Id == firstId);
            var secondBefore = Assert.Single(before.Snapshots, item => item.Id == secondId);
            var dailyBefore = Assert.Single(before.Daily);

            await using (var activationContext = _fixture.CreateContext())
            {
                Assert.Equal(
                    SnapshotMutationResult.Success,
                    await CreateService(activationContext).ActivateVersionAsync(
                        firstId, 44, true));
            }

            var afterFirstActivation = await ReadDatabaseStateAsync(DailySynchronizationDate);
            Assert.True(Assert.Single(
                afterFirstActivation.Snapshots, item => item.Id == firstId).IsActive);
            Assert.False(Assert.Single(
                afterFirstActivation.Snapshots, item => item.Id == secondId).IsActive);
            var dailyAfterFirst = Assert.Single(afterFirstActivation.Daily);
            Assert.Equal(firstBefore.Version, dailyAfterFirst.CalculatedVersion);
            Assert.Equal(firstBefore.OutputsJson, dailyAfterFirst.ResultsJson);
            Assert.Equal(dailyBefore.InputsFingerprint, dailyAfterFirst.InputsFingerprint);
            Assert.Equal(dailyBefore.CalculatedAt, dailyAfterFirst.CalculatedAt);

            await using (var activationContext = _fixture.CreateContext())
            {
                Assert.Equal(
                    SnapshotMutationResult.Success,
                    await CreateService(activationContext).ActivateVersionAsync(
                        secondId, 44, true));
            }

            var afterSecondActivation = await ReadDatabaseStateAsync(DailySynchronizationDate);
            Assert.False(Assert.Single(
                afterSecondActivation.Snapshots, item => item.Id == firstId).IsActive);
            Assert.True(Assert.Single(
                afterSecondActivation.Snapshots, item => item.Id == secondId).IsActive);
            var dailyAfterSecond = Assert.Single(afterSecondActivation.Daily);
            Assert.Equal(secondBefore.Version, dailyAfterSecond.CalculatedVersion);
            Assert.Equal(secondBefore.OutputsJson, dailyAfterSecond.ResultsJson);
            Assert.Equal(dailyBefore.InputsFingerprint, dailyAfterSecond.InputsFingerprint);
            Assert.Equal(dailyBefore.CalculatedAt, dailyAfterSecond.CalculatedAt);
            Assert.Equal(firstBefore.Financial, Assert.Single(
                afterSecondActivation.Snapshots, item => item.Id == firstId).Financial);
            Assert.Equal(secondBefore.Financial, Assert.Single(
                afterSecondActivation.Snapshots, item => item.Id == secondId).Financial);
            Assert.Equal(2, afterSecondActivation.Snapshots.Count);
        }
        finally
        {
            await CleanupAsync(DailySynchronizationDate);
        }
    }

    [SqlServerFact]
    public async Task ActivateVersion_SynchronizesOnlyTheSelectedScopeAndDate()
    {
        try
        {
            Guid sabahV1Id;
            Guid aksamV1Id;
            await using (var saveContext = _fixture.CreateContext())
            {
                var service = CreateService(saveContext);
                sabahV1Id = (await service.SaveAsync(
                    NewSnapshot(IsolationDate, KasaRaporTuru.Sabah, 701m), 17, "sabah-first")).Id;
                await service.SaveAsync(
                    NewSnapshot(IsolationDate, KasaRaporTuru.Sabah, 702m), 29, "sabah-second");
                aksamV1Id = (await service.SaveAsync(
                    NewSnapshot(IsolationDate, KasaRaporTuru.Aksam, 801m), 17, "aksam-first")).Id;
                await service.SaveAsync(
                    NewSnapshot(IsolationDate, KasaRaporTuru.Aksam, 802m), 29, "aksam-second");
                await service.SaveAsync(
                    NewSnapshot(IsolationOtherDate, KasaRaporTuru.Aksam, 901m), 17, "other-first");
                await service.SaveAsync(
                    NewSnapshot(IsolationOtherDate, KasaRaporTuru.Aksam, 902m), 29, "other-second");
            }

            var before = await ReadDatabaseStateAsync(IsolationDate, IsolationOtherDate);
            await using (var sabahActivationContext = _fixture.CreateContext())
            {
                Assert.Equal(
                    SnapshotMutationResult.Success,
                    await CreateService(sabahActivationContext).ActivateVersionAsync(
                        sabahV1Id, 44, true));
            }

            var afterSabah = await ReadDatabaseStateAsync(IsolationDate, IsolationOtherDate);
            var sabahV1 = Assert.Single(afterSabah.Snapshots, item => item.Id == sabahV1Id);
            var sabahDaily = Assert.Single(afterSabah.Daily, item =>
                item.Date == IsolationDate && item.KasaTuru == "Sabah");
            Assert.True(sabahV1.IsActive);
            Assert.Equal(sabahV1.Version, sabahDaily.CalculatedVersion);
            Assert.Equal(sabahV1.OutputsJson, sabahDaily.ResultsJson);
            Assert.Equal(
                before.Snapshots.Where(item =>
                    item.KasaTuru == KasaRaporTuru.Aksam || item.Date == IsolationOtherDate),
                afterSabah.Snapshots.Where(item =>
                    item.KasaTuru == KasaRaporTuru.Aksam || item.Date == IsolationOtherDate));
            Assert.Equal(
                before.Daily.Where(item =>
                    item.KasaTuru == "Aksam" || item.Date == IsolationOtherDate),
                afterSabah.Daily.Where(item =>
                    item.KasaTuru == "Aksam" || item.Date == IsolationOtherDate));

            await using (var aksamActivationContext = _fixture.CreateContext())
            {
                Assert.Equal(
                    SnapshotMutationResult.Success,
                    await CreateService(aksamActivationContext).ActivateVersionAsync(
                        aksamV1Id, 44, true));
            }

            var afterAksam = await ReadDatabaseStateAsync(IsolationDate, IsolationOtherDate);
            var aksamV1 = Assert.Single(afterAksam.Snapshots, item => item.Id == aksamV1Id);
            var aksamDaily = Assert.Single(afterAksam.Daily, item =>
                item.Date == IsolationDate && item.KasaTuru == "Aksam");
            Assert.True(aksamV1.IsActive);
            Assert.Equal(aksamV1.Version, aksamDaily.CalculatedVersion);
            Assert.Equal(aksamV1.OutputsJson, aksamDaily.ResultsJson);
            Assert.Equal(
                afterSabah.Snapshots.Where(item =>
                    item.KasaTuru == KasaRaporTuru.Sabah || item.Date == IsolationOtherDate),
                afterAksam.Snapshots.Where(item =>
                    item.KasaTuru == KasaRaporTuru.Sabah || item.Date == IsolationOtherDate));
            Assert.Equal(
                afterSabah.Daily.Where(item =>
                    item.KasaTuru == "Sabah" || item.Date == IsolationOtherDate),
                afterAksam.Daily.Where(item =>
                    item.KasaTuru == "Sabah" || item.Date == IsolationOtherDate));
        }
        finally
        {
            await CleanupAsync(IsolationDate, IsolationOtherDate);
        }
    }

    [SqlServerFact]
    public async Task ActivateVersion_WhenDailyResultIsMissing_CreatesAConsistentResult()
    {
        try
        {
            Guid firstId;
            await using (var saveContext = _fixture.CreateContext())
            {
                var service = CreateService(saveContext);
                firstId = (await service.SaveAsync(
                    NewSnapshot(MissingDailyDate, KasaRaporTuru.Aksam, 1001m),
                    17,
                    "first-creator")).Id;
                await service.SaveAsync(
                    NewSnapshot(MissingDailyDate, KasaRaporTuru.Aksam, 1002m),
                    29,
                    "second-creator");
            }

            await using (var deleteDailyContext = _fixture.CreateContext())
            {
                await deleteDailyContext.DailyCalculationResults
                    .Where(item => item.ForDate == MissingDailyDate && item.KasaTuru == "Aksam")
                    .ExecuteDeleteAsync();
            }

            await using (var activationContext = _fixture.CreateContext())
            {
                Assert.Equal(
                    SnapshotMutationResult.Success,
                    await CreateService(activationContext).ActivateVersionAsync(
                        firstId, 44, true));
            }

            var state = await ReadDatabaseStateAsync(MissingDailyDate);
            var selected = Assert.Single(state.Snapshots, item => item.Id == firstId);
            var daily = Assert.Single(state.Daily);
            Assert.True(selected.IsActive);
            Assert.Single(state.Snapshots, item => item.IsActive && !item.IsDeleted);
            Assert.Equal(selected.Version, daily.CalculatedVersion);
            Assert.Equal(selected.OutputsJson, daily.ResultsJson);
            Assert.Equal(string.Empty, daily.InputsFingerprint);
            Assert.NotEqual(default, daily.CalculatedAt);
            Assert.Equal(2, state.Snapshots.Count);
        }
        finally
        {
            await CleanupAsync(MissingDailyDate);
        }
    }

    [SqlServerFact]
    public async Task ActivateVersion_WhenPersistenceFails_RollsBackSnapshotAndDailyState()
    {
        try
        {
            Guid firstId;
            await using (var saveContext = _fixture.CreateContext())
            {
                var service = CreateService(saveContext);
                firstId = (await service.SaveAsync(
                    NewSnapshot(AtomicityDate, KasaRaporTuru.Aksam, 1101m),
                    17,
                    "first-creator")).Id;
                await service.SaveAsync(
                    NewSnapshot(AtomicityDate, KasaRaporTuru.Aksam, 1102m),
                    29,
                    "second-creator");
            }

            var before = await ReadDatabaseStateAsync(AtomicityDate);
            await using (var failingContext = _fixture.CreateContext())
            {
                var daily = await failingContext.DailyCalculationResults.SingleAsync(item =>
                    item.ForDate == AtomicityDate && item.KasaTuru == "Aksam");
                daily.KasaTuru = new string('x', 51);

                await Assert.ThrowsAsync<DbUpdateException>(() =>
                    CreateService(failingContext).ActivateVersionAsync(firstId, 44, true));
            }

            AssertDatabaseStateEqual(before, await ReadDatabaseStateAsync(AtomicityDate));
        }
        finally
        {
            await CleanupAsync(AtomicityDate);
        }
    }

    private async Task<SnapshotState> ReadStateAsync(Guid id, DateOnly date, string kasaTuru)
    {
        await using var context = _fixture.CreateContext();
        var snapshot = await context.CalculatedKasaSnapshots.AsNoTracking()
            .SingleAsync(item => item.Id == id);
        var daily = await context.DailyCalculationResults.AsNoTracking()
            .SingleAsync(item => item.ForDate == date && item.KasaTuru == kasaTuru);
        return new SnapshotState(SnapshotRecord.From(snapshot), DailyRecord.From(daily));
    }

    private async Task<DatabaseState> ReadDatabaseStateAsync(params DateOnly[] dates)
    {
        await using var context = _fixture.CreateContext();
        var snapshots = await context.CalculatedKasaSnapshots.AsNoTracking()
            .Where(item => dates.Contains(item.RaporTarihi))
            .OrderBy(item => item.RaporTarihi)
            .ThenBy(item => item.KasaTuru)
            .ThenBy(item => item.Version)
            .Select(item => new SnapshotProjection(
                item.Id,
                item.RaporTarihi,
                item.KasaTuru,
                item.IsActive,
                item.IsDeleted,
                item.DeletedAtUtc,
                item.DeletedBy,
                item.DeletedByUserId,
                item.Version,
                item.CalculatedByUserId,
                item.CalculatedBy,
                item.InputsJson,
                item.OutputsJson,
                item.KasaRaporDataJson,
                item.FinancialExceptionsSummaryJson))
            .ToListAsync();
        var daily = await context.DailyCalculationResults.AsNoTracking()
            .Where(item => dates.Contains(item.ForDate))
            .OrderBy(item => item.ForDate)
            .ThenBy(item => item.KasaTuru)
            .Select(item => new DailyRecord(
                item.ForDate,
                item.KasaTuru,
                item.InputsFingerprint,
                item.ResultsJson,
                item.CalculatedVersion,
                item.CalculatedAt))
            .ToListAsync();
        return new DatabaseState(snapshots, daily);
    }

    private async Task<int> CountVersionsAsync(DateOnly date, KasaRaporTuru kasaTuru)
    {
        await using var context = _fixture.CreateContext();
        return await context.CalculatedKasaSnapshots.CountAsync(item =>
            item.RaporTarihi == date && item.KasaTuru == kasaTuru);
    }

    private static void AssertFinancialPayloadEqual(SnapshotState before, SnapshotState after)
    {
        Assert.Equal(before.Snapshot.Financial, after.Snapshot.Financial);
        Assert.Equal(before.Daily, after.Daily);
    }

    private static void AssertDatabaseStateEqual(DatabaseState before, DatabaseState after)
    {
        Assert.Equal(before.Snapshots, after.Snapshots);
        Assert.Equal(before.Daily, after.Daily);
    }

    private static CalculatedKasaSnapshotService CreateService(KasaManagerDbContext context) =>
        new(context, NullLogger<CalculatedKasaSnapshotService>.Instance);

    private static CalculatedKasaSnapshot NewSnapshot(
        DateOnly date,
        KasaRaporTuru kasaTuru,
        decimal financialValue) => new()
    {
        RaporTarihi = date,
        KasaTuru = kasaTuru,
        Name = "immutable-name",
        Description = "immutable-description",
        Notes = "immutable-notes",
        InputsJson = JsonSerializer.Serialize(new { financialInput = financialValue }),
        OutputsJson = JsonSerializer.Serialize(new { financialOutput = financialValue * 2m }),
        KasaRaporDataJson = JsonSerializer.Serialize(new
        {
            PayloadVersion = 2,
            Tarih = date,
            KasaTuru = kasaTuru.ToString(),
            FinancialValue = financialValue,
            ImmutableAudit = new { Net = financialValue },
            ImmutableAuditDetails = new { Records = Array.Empty<object>() }
        }),
        FinancialExceptionsSummaryJson = JsonSerializer.Serialize(new { total = financialValue }),
        FormulaSetName = "formula-c42d-r2"
    };

    private async Task CleanupAsync(params DateOnly[] dates)
    {
        await using var context = _fixture.CreateContext();
        await context.CalculatedKasaSnapshots
            .Where(item => dates.Contains(item.RaporTarihi))
            .ExecuteDeleteAsync();
        await context.DailyCalculationResults
            .Where(item => dates.Contains(item.ForDate))
            .ExecuteDeleteAsync();
    }

    private sealed record SnapshotState(SnapshotRecord Snapshot, DailyRecord Daily);

    private sealed record SnapshotRecord(
        Guid Id,
        DateOnly Date,
        KasaRaporTuru KasaTuru,
        bool IsActive,
        bool IsDeleted,
        DateTime? DeletedAtUtc,
        string? DeletedBy,
        int? DeletedByUserId,
        SnapshotFinancialState Financial)
    {
        internal static SnapshotRecord From(CalculatedKasaSnapshot snapshot) => new(
            snapshot.Id,
            snapshot.RaporTarihi,
            snapshot.KasaTuru,
            snapshot.IsActive,
            snapshot.IsDeleted,
            snapshot.DeletedAtUtc,
            snapshot.DeletedBy,
            snapshot.DeletedByUserId,
            SnapshotFinancialState.From(snapshot));
    }

    private sealed record SnapshotFinancialState(
        int Version,
        DateOnly Date,
        KasaRaporTuru KasaTuru,
        int? CalculatedByUserId,
        string? CalculatedBy,
        string InputsJson,
        string OutputsJson,
        string? KasaRaporDataJson,
        string? FinancialExceptionsSummaryJson)
    {
        internal static SnapshotFinancialState From(CalculatedKasaSnapshot snapshot) => new(
            snapshot.Version,
            snapshot.RaporTarihi,
            snapshot.KasaTuru,
            snapshot.CalculatedByUserId,
            snapshot.CalculatedBy,
            snapshot.InputsJson,
            snapshot.OutputsJson,
            snapshot.KasaRaporDataJson,
            snapshot.FinancialExceptionsSummaryJson);
    }

    private sealed record DailyRecord(
        DateOnly Date,
        string KasaTuru,
        string InputsFingerprint,
        string ResultsJson,
        int CalculatedVersion,
        DateTime CalculatedAt)
    {
        internal static DailyRecord From(DailyCalculationResult daily) => new(
            daily.ForDate,
            daily.KasaTuru,
            daily.InputsFingerprint,
            daily.ResultsJson,
            daily.CalculatedVersion,
            daily.CalculatedAt);
    }

    private sealed record SnapshotProjection(
        Guid Id,
        DateOnly Date,
        KasaRaporTuru KasaTuru,
        bool IsActive,
        bool IsDeleted,
        DateTime? DeletedAtUtc,
        string? DeletedBy,
        int? DeletedByUserId,
        int Version,
        int? CalculatedByUserId,
        string? CalculatedBy,
        string InputsJson,
        string OutputsJson,
        string? KasaRaporDataJson,
        string? FinancialExceptionsSummaryJson)
    {
        internal SnapshotFinancialState Financial => new(
            Version,
            Date,
            KasaTuru,
            CalculatedByUserId,
            CalculatedBy,
            InputsJson,
            OutputsJson,
            KasaRaporDataJson,
            FinancialExceptionsSummaryJson);
    }

    private sealed record DatabaseState(
        IReadOnlyList<SnapshotProjection> Snapshots,
        IReadOnlyList<DailyRecord> Daily);
}
