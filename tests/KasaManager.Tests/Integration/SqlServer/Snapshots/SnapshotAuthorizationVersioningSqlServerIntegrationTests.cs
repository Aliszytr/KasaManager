using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Calculation.Data;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Infrastructure.Services;
using KasaManager.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KasaManager.Tests.Integration.SqlServer.Snapshots;

[Collection(SqlServerIntegrationCollection.Name)]
public sealed class SnapshotAuthorizationVersioningSqlServerIntegrationTests
{
    private static readonly DateOnly SaveActorDate = new(2060, 5, 1);
    private static readonly DateOnly SharedReadDate = new(2060, 5, 2);
    private static readonly DateOnly LegacyReadDate = new(2060, 5, 3);
    private static readonly DateOnly CreatorMetadataDate = new(2060, 5, 4);
    private static readonly DateOnly AdminMetadataDate = new(2060, 5, 5);
    private static readonly DateOnly ForbiddenMetadataDate = new(2060, 5, 6);
    private static readonly DateOnly LegacyMetadataDate = new(2060, 5, 7);
    private static readonly DateOnly SeparateChainsDate = new(2060, 5, 8);
    private static readonly DateOnly ActorOnlyVersionDate = new(2060, 5, 9);
    private static readonly DateOnly SameActorReplayDate = new(2060, 5, 10);
    private static readonly DateOnly FinancialChangeDate = new(2060, 5, 11);
    private static readonly DateOnly VersionCollisionDate = new(2060, 5, 12);
    private static readonly DateOnly AuditOnlyVersionDate = new(2026, 7, 14);
    private static readonly DateOnly DetailsOnlyVersionDate = new(2060, 5, 13);
    private static readonly DateOnly OutputsOnlyVersionDate = new(2060, 5, 14);
    private static readonly DateOnly InvalidPayloadBaseDate = new(2060, 5, 15);
    private static readonly DateOnly InputsOnlyVersionDate = new(2060, 5, 20);
    private readonly SqlServerIntegrationFixture _fixture;

    public SnapshotAuthorizationVersioningSqlServerIntegrationTests(
        SqlServerIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [SqlServerFact]
    public async Task Save_UsesServerActorRejectsInvalidActorsAndPreservesFinancialFingerprint()
    {
        var invalidDate = SaveActorDate.AddDays(20);

        try
        {
            await using (var seedContext = _fixture.CreateContext())
            {
                seedContext.DailyCalculationResults.Add(NewDailyResult(
                    SaveActorDate,
                    "Aksam",
                    "financial-fingerprint-c42d"));
                await seedContext.SaveChangesAsync();
            }

            await using (var saveContext = _fixture.CreateContext())
            {
                var service = CreateService(saveContext);
                var untrusted = NewSnapshot(SaveActorDate, KasaRaporTuru.Aksam, 10m);
                untrusted.CalculatedByUserId = 999;
                untrusted.CalculatedBy = "client-spoof";

                await service.SaveAsync(untrusted, 17, "server-creator");

                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                    service.SaveAsync(NewSnapshot(invalidDate, KasaRaporTuru.Aksam, 20m), 0, "missing"));
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                    service.SaveAsync(NewSnapshot(invalidDate, KasaRaporTuru.Sabah, 30m), -7, "invalid"));
            }

            await using var readContext = _fixture.CreateContext();
            var saved = await readContext.CalculatedKasaSnapshots
                .AsNoTracking()
                .SingleAsync(item => item.RaporTarihi == SaveActorDate);
            var daily = await readContext.DailyCalculationResults
                .AsNoTracking()
                .SingleAsync(item => item.ForDate == SaveActorDate && item.KasaTuru == "Aksam");

            Assert.Equal(17, saved.CalculatedByUserId);
            Assert.Equal("server-creator", saved.CalculatedBy);
            Assert.Equal("financial-fingerprint-c42d", daily.InputsFingerprint);
            Assert.Equal(1, daily.CalculatedVersion);
            Assert.Equal(saved.OutputsJson, daily.ResultsJson);
            Assert.DoesNotContain("server-creator", saved.KasaRaporDataJson, StringComparison.Ordinal);
            Assert.DoesNotContain("client-spoof", saved.KasaRaporDataJson, StringComparison.Ordinal);
            Assert.DoesNotContain("KasayiYapan", saved.KasaRaporDataJson, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await readContext.CalculatedKasaSnapshots
                .AsNoTracking()
                .Where(item => item.RaporTarihi == invalidDate)
                .ToListAsync());
            Assert.Empty(await readContext.DailyCalculationResults
                .AsNoTracking()
                .Where(item => item.ForDate == invalidDate)
                .ToListAsync());
        }
        finally
        {
            await CleanupAsync(SaveActorDate, invalidDate);
        }
    }

    [SqlServerFact]
    public async Task SharedReads_DoNotUseCreatorAsOwnerFilterAndRemainAuthenticated()
    {
        try
        {
            Guid ownedId;
            Guid legacyId;
            await using (var seedContext = _fixture.CreateContext())
            {
                var service = CreateService(seedContext);
                ownedId = (await service.SaveAsync(
                    NewSnapshot(SharedReadDate, KasaRaporTuru.Aksam, 11m),
                    17,
                    "creator")).Id;

                var legacy = NewSnapshot(LegacyReadDate, KasaRaporTuru.Sabah, 12m);
                legacy.CalculatedByUserId = null;
                legacy.CalculatedBy = "legacy-user";
                seedContext.CalculatedKasaSnapshots.Add(legacy);
                await seedContext.SaveChangesAsync();
                legacyId = legacy.Id;
            }

            Assert.Contains(
                typeof(KasaRaporlarController).GetCustomAttributes(inherit: true),
                attribute => attribute is AuthorizeAttribute);

            foreach (var authenticatedActor in new[] { 17, 29, 44 })
            {
                await using var readContext = _fixture.CreateContext();
                var service = CreateService(readContext);
                Assert.Equal(ownedId, (await service.GetByIdAsync(ownedId))!.Id);
                Assert.Equal(legacyId, (await service.GetByIdAsync(legacyId))!.Id);

                var search = await service.SearchAsync(new KasaReportSearchQuery
                {
                    StartDate = SharedReadDate,
                    EndDate = LegacyReadDate,
                    Page = 1,
                    PageSize = 20
                });
                Assert.Contains(search.Items, item => item.Id == ownedId);
                Assert.Contains(search.Items, item => item.Id == legacyId);
                Assert.True(authenticatedActor > 0);
            }

            await using var verifyContext = _fixture.CreateContext();
            var owned = await verifyContext.CalculatedKasaSnapshots.AsNoTracking()
                .SingleAsync(item => item.Id == ownedId);
            var legacyRead = await verifyContext.CalculatedKasaSnapshots.AsNoTracking()
                .SingleAsync(item => item.Id == legacyId);
            Assert.Equal(17, owned.CalculatedByUserId);
            Assert.Equal("creator", owned.CalculatedBy);
            Assert.Null(legacyRead.CalculatedByUserId);
            Assert.Equal("legacy-user", legacyRead.CalculatedBy);
        }
        finally
        {
            await CleanupAsync(SharedReadDate, LegacyReadDate);
        }
    }

    [SqlServerFact]
    public async Task MetadataUpdate_CreatorCanUpdateWithoutChangingFinancialState()
    {
        var id = await SeedOwnedSnapshotAsync(CreatorMetadataDate, 17, "creator");

        try
        {
            var before = await ReadStateAsync(id, CreatorMetadataDate, "Aksam");
            await using (var updateContext = _fixture.CreateContext())
            {
                var result = await CreateService(updateContext).UpdateAsync(
                    id, "creator-name", "creator-description", "creator-notes", 17, false);
                Assert.Equal(SnapshotMutationResult.Success, result);
            }

            var after = await ReadStateAsync(id, CreatorMetadataDate, "Aksam");
            Assert.Equal("creator-name", after.Snapshot.Name);
            Assert.Equal("creator-description", after.Snapshot.Description);
            Assert.Equal("creator-notes", after.Snapshot.Notes);
            AssertFinancialStateUnchanged(before, after);
        }
        finally
        {
            await CleanupAsync(CreatorMetadataDate);
        }
    }

    [SqlServerFact]
    public async Task MetadataUpdate_AdminCanUpdateAnyCreatorWithoutChangingFinancialState()
    {
        var id = await SeedOwnedSnapshotAsync(AdminMetadataDate, 17, "creator");

        try
        {
            var before = await ReadStateAsync(id, AdminMetadataDate, "Aksam");
            await using (var updateContext = _fixture.CreateContext())
            {
                var result = await CreateService(updateContext).UpdateAsync(
                    id, "admin-name", "admin-description", "admin-notes", 44, true);
                Assert.Equal(SnapshotMutationResult.Success, result);
            }

            var after = await ReadStateAsync(id, AdminMetadataDate, "Aksam");
            Assert.Equal("admin-name", after.Snapshot.Name);
            AssertFinancialStateUnchanged(before, after);
        }
        finally
        {
            await CleanupAsync(AdminMetadataDate);
        }
    }

    [SqlServerFact]
    public async Task MetadataUpdate_NonCreatorIsForbiddenAndDatabaseRemainsUnchanged()
    {
        var id = await SeedOwnedSnapshotAsync(ForbiddenMetadataDate, 17, "creator");

        try
        {
            var before = await ReadStateAsync(id, ForbiddenMetadataDate, "Aksam");
            await using (var updateContext = _fixture.CreateContext())
            {
                var result = await CreateService(updateContext).UpdateAsync(
                    id, "spoofed", "spoofed", "spoofed", 29, false);
                Assert.Equal(SnapshotMutationResult.Forbidden, result);
            }

            var after = await ReadStateAsync(id, ForbiddenMetadataDate, "Aksam");
            Assert.Equal(before.Snapshot.Name, after.Snapshot.Name);
            Assert.Equal(before.Snapshot.Description, after.Snapshot.Description);
            Assert.Equal(before.Snapshot.Notes, after.Snapshot.Notes);
            AssertFinancialStateUnchanged(before, after);
        }
        finally
        {
            await CleanupAsync(ForbiddenMetadataDate);
        }
    }

    [SqlServerFact]
    public async Task MetadataUpdate_LegacyCreatorNullIsAdminOnlyAtServiceBoundary()
    {
        Guid id;
        try
        {
            await using (var seedContext = _fixture.CreateContext())
            {
                var legacy = NewSnapshot(LegacyMetadataDate, KasaRaporTuru.Aksam, 16m);
                legacy.CalculatedByUserId = null;
                legacy.CalculatedBy = "legacy-creator";
                seedContext.CalculatedKasaSnapshots.Add(legacy);
                seedContext.DailyCalculationResults.Add(NewDailyResult(
                    LegacyMetadataDate, "Aksam", "legacy-fingerprint"));
                await seedContext.SaveChangesAsync();
                id = legacy.Id;
            }

            var before = await ReadStateAsync(id, LegacyMetadataDate, "Aksam");
            await using (var normalContext = _fixture.CreateContext())
            {
                Assert.Equal(
                    SnapshotMutationResult.Forbidden,
                    await CreateService(normalContext).UpdateAsync(
                        id, "normal", null, null, 17, false));
            }

            var afterForbidden = await ReadStateAsync(id, LegacyMetadataDate, "Aksam");
            Assert.Equal(before.Snapshot.Name, afterForbidden.Snapshot.Name);
            AssertFinancialStateUnchanged(before, afterForbidden);

            await using (var adminContext = _fixture.CreateContext())
            {
                Assert.Equal(
                    SnapshotMutationResult.Success,
                    await CreateService(adminContext).UpdateAsync(
                        id, "admin", "legacy-admin-description", "legacy-admin-notes", 44, true));
            }

            var afterAdmin = await ReadStateAsync(id, LegacyMetadataDate, "Aksam");
            Assert.Equal("admin", afterAdmin.Snapshot.Name);
            Assert.Null(afterAdmin.Snapshot.CalculatedByUserId);
            Assert.Equal("legacy-creator", afterAdmin.Snapshot.CalculatedBy);
            AssertFinancialStateUnchanged(before, afterAdmin);
        }
        finally
        {
            await CleanupAsync(LegacyMetadataDate);
        }
    }

    [SqlServerFact]
    public async Task SabahAndAksam_HaveIndependentBusinessKeysAndVersionChains()
    {
        try
        {
            await using (var saveContext = _fixture.CreateContext())
            {
                var service = CreateService(saveContext);
                var sabahV1 = await service.SaveAsync(
                    NewSnapshot(SeparateChainsDate, KasaRaporTuru.Sabah, 101m), 17, "sabah-user");
                await service.SaveAsync(
                    NewSnapshot(SeparateChainsDate, KasaRaporTuru.Sabah, 202m), 17, "sabah-user");
                await service.SaveAsync(
                    NewSnapshot(SeparateChainsDate, KasaRaporTuru.Aksam, 301m), 29, "aksam-user");
                await service.SaveAsync(
                    NewSnapshot(SeparateChainsDate, KasaRaporTuru.Aksam, 302m), 29, "aksam-user");
                await service.SaveAsync(
                    NewSnapshot(SeparateChainsDate, KasaRaporTuru.Aksam, 303m), 29, "aksam-user");

                Assert.Equal(
                    SnapshotMutationResult.Success,
                    await service.ActivateVersionAsync(sabahV1.Id, 44, true));
                await service.SaveAsync(
                    NewSnapshot(SeparateChainsDate, KasaRaporTuru.Sabah, 404m), 17, "sabah-user");
            }

            await using var readContext = _fixture.CreateContext();
            var snapshots = await readContext.CalculatedKasaSnapshots.AsNoTracking()
                .Where(item => item.RaporTarihi == SeparateChainsDate)
                .OrderBy(item => item.KasaTuru)
                .ThenBy(item => item.Version)
                .ToListAsync();
            var daily = await readContext.DailyCalculationResults.AsNoTracking()
                .Where(item => item.ForDate == SeparateChainsDate)
                .OrderBy(item => item.KasaTuru)
                .ToListAsync();

            var sabah = snapshots.Where(item => item.KasaTuru == KasaRaporTuru.Sabah).ToArray();
            var aksam = snapshots.Where(item => item.KasaTuru == KasaRaporTuru.Aksam).ToArray();
            Assert.Equal(3, sabah.Length);
            Assert.Equal(3, aksam.Length);
            Assert.Equal(new[] { 1, 2, 3 }, sabah.Select(item => item.Version));
            Assert.Equal(new[] { 1, 2, 3 }, aksam.Select(item => item.Version));
            Assert.False(sabah[0].IsActive);
            Assert.False(sabah[1].IsActive);
            Assert.True(sabah[2].IsActive);
            Assert.Equal(404m, ReadFinancialValue(sabah[2].KasaRaporDataJson));
            Assert.False(aksam[0].IsActive);
            Assert.False(aksam[1].IsActive);
            Assert.True(aksam[2].IsActive);
            Assert.Equal(303m, ReadFinancialValue(aksam[2].KasaRaporDataJson));
            Assert.Equal(2, daily.Count);
            Assert.Equal(3, daily.Single(item => item.KasaTuru == "Sabah").CalculatedVersion);
            Assert.Equal(3, daily.Single(item => item.KasaTuru == "Aksam").CalculatedVersion);
        }
        finally
        {
            await CleanupAsync(SeparateChainsDate);
        }
    }

    [SqlServerFact]
    public async Task Save_ActorOnlyChange_DoesNotCreateANewFinancialVersion()
    {
        try
        {
            Guid firstId;
            SnapshotState before;
            await using (var saveContext = _fixture.CreateContext())
            {
                var service = CreateService(saveContext);
                firstId = (await service.SaveAsync(
                    NewSnapshot(ActorOnlyVersionDate, KasaRaporTuru.Aksam, 404m),
                    17,
                    "first-actor")).Id;
            }

            before = await ReadStateAsync(firstId, ActorOnlyVersionDate, "Aksam");

            await using (var saveContext = _fixture.CreateContext())
            {
                var replay = NewSnapshot(ActorOnlyVersionDate, KasaRaporTuru.Aksam, 404m);
                replay.CalculatedByUserId = 999;
                replay.CalculatedBy = "client-spoof";
                var returned = await CreateService(saveContext).SaveAsync(
                    replay,
                    29,
                    "second-actor");
                Assert.Equal(firstId, returned.Id);
            }

            await using var readContext = _fixture.CreateContext();
            var versions = await readContext.CalculatedKasaSnapshots.AsNoTracking()
                .Where(item => item.RaporTarihi == ActorOnlyVersionDate
                    && item.KasaTuru == KasaRaporTuru.Aksam)
                .OrderBy(item => item.Version)
                .ToListAsync();

            Assert.Single(versions);
            Assert.Equal(1, versions[0].Version);
            Assert.True(versions[0].IsActive);
            Assert.Equal(17, versions[0].CalculatedByUserId);
            Assert.Equal("first-actor", versions[0].CalculatedBy);
            Assert.Equal(404m, ReadFinancialValue(versions[0].KasaRaporDataJson));

            var daily = await readContext.DailyCalculationResults.AsNoTracking()
                .SingleAsync(item => item.ForDate == ActorOnlyVersionDate && item.KasaTuru == "Aksam");
            Assert.Equal(before.Snapshot.KasaRaporDataJson, versions[0].KasaRaporDataJson);
            Assert.Equal(before.Daily.InputsFingerprint, daily.InputsFingerprint);
            Assert.Equal(before.Daily.ResultsJson, daily.ResultsJson);
            Assert.Equal(before.Daily.CalculatedVersion, daily.CalculatedVersion);
            Assert.Equal(before.Daily.CalculatedAt, daily.CalculatedAt);
        }
        finally
        {
            await CleanupAsync(ActorOnlyVersionDate);
        }
    }

    [SqlServerFact]
    public async Task Save_SameActorAndSameFinancialPayload_ReusesActiveVersion()
    {
        try
        {
            Guid firstId;
            await using (var firstContext = _fixture.CreateContext())
            {
                firstId = (await CreateService(firstContext).SaveAsync(
                    NewSnapshot(SameActorReplayDate, KasaRaporTuru.Aksam, 505m),
                    17,
                    "same-actor")).Id;
            }

            var before = await ReadStateAsync(firstId, SameActorReplayDate, "Aksam");
            await using (var replayContext = _fixture.CreateContext())
            {
                var replayed = await CreateService(replayContext).SaveAsync(
                    NewSnapshot(SameActorReplayDate, KasaRaporTuru.Aksam, 505m),
                    17,
                    "same-actor");
                Assert.Equal(firstId, replayed.Id);
            }

            var after = await ReadStateAsync(firstId, SameActorReplayDate, "Aksam");
            Assert.Single(await ReadVersionsAsync(SameActorReplayDate, KasaRaporTuru.Aksam));
            AssertFinancialStateUnchanged(before, after);
            Assert.Equal(before.Daily.CalculatedAt, after.Daily.CalculatedAt);
        }
        finally
        {
            await CleanupAsync(SameActorReplayDate);
        }
    }

    [SqlServerFact]
    public async Task Save_FinancialChange_CreatesNextVersionWithNewActor()
    {
        try
        {
            await using (var saveContext = _fixture.CreateContext())
            {
                var service = CreateService(saveContext);
                await service.SaveAsync(
                    NewSnapshot(FinancialChangeDate, KasaRaporTuru.Aksam, 601m), 17, "actor-a");
                await service.SaveAsync(
                    NewSnapshot(FinancialChangeDate, KasaRaporTuru.Aksam, 602m), 29, "actor-b");
            }

            var versions = await ReadVersionsAsync(FinancialChangeDate, KasaRaporTuru.Aksam);
            Assert.Equal(2, versions.Count);
            Assert.Equal((1, false, 17, "actor-a"),
                (versions[0].Version, versions[0].IsActive, versions[0].CalculatedByUserId, versions[0].CalculatedBy));
            Assert.Equal((2, true, 29, "actor-b"),
                (versions[1].Version, versions[1].IsActive, versions[1].CalculatedByUserId, versions[1].CalculatedBy));
            Assert.NotEqual(versions[0].KasaRaporDataJson, versions[1].KasaRaporDataJson);

            await using var readContext = _fixture.CreateContext();
            var daily = await readContext.DailyCalculationResults.AsNoTracking()
                .SingleAsync(item => item.ForDate == FinancialChangeDate && item.KasaTuru == "Aksam");
            Assert.Equal(2, daily.CalculatedVersion);
            Assert.Equal(versions[1].OutputsJson, daily.ResultsJson);
        }
        finally
        {
            await CleanupAsync(FinancialChangeDate);
        }
    }

    [SqlServerFact]
    public async Task Save_AuditOnlyChange_CreatesNextVersionAndKeepsNewestActive()
    {
        try
        {
            Guid firstId;
            Guid secondId;
            string firstJson;
            await using (var seedContext = _fixture.CreateContext())
            {
                seedContext.DailyCalculationResults.Add(NewDailyResult(
                    AuditOnlyVersionDate, "Sabah", "audit-only-stable-fingerprint"));
                await seedContext.SaveChangesAsync();
            }

            await using (var firstContext = _fixture.CreateContext())
            {
                var service = CreateService(firstContext);
                firstId = (await service.SaveAsync(
                    NewAuditAwareSnapshot(AuditOnlyVersionDate, 0m, 0m, KasaRaporTuru.Sabah),
                    17,
                    "actor-a")).Id;
            }

            var firstState = await ReadStateAsync(firstId, AuditOnlyVersionDate, "Sabah");
            firstJson = firstState.Snapshot.KasaRaporDataJson!;
            Assert.Equal(1, firstState.Snapshot.Version);
            Assert.True(firstState.Snapshot.IsActive);
            Assert.Equal(1, firstState.Daily.CalculatedVersion);
            Assert.Equal("audit-only-stable-fingerprint", firstState.Daily.InputsFingerprint);

            await using (var secondContext = _fixture.CreateContext())
            {
                var service = CreateService(secondContext);
                var second = await service.SaveAsync(
                    NewAuditAwareSnapshot(
                        AuditOnlyVersionDate, -3000.00m, -29873.80m, KasaRaporTuru.Sabah),
                    29,
                    "actor-b");
                secondId = second.Id;
                Assert.Equal(2, second.Version);
            }

            Assert.NotEqual(firstId, secondId);
            var versions = await ReadVersionsAsync(AuditOnlyVersionDate, KasaRaporTuru.Sabah);
            Assert.Equal(2, versions.Count);
            Assert.Equal(new[] { 1, 2 }, versions.Select(item => item.Version));
            Assert.False(versions[0].IsActive);
            Assert.True(versions[1].IsActive);
            Assert.Equal(17, versions[0].CalculatedByUserId);
            Assert.Equal("actor-a", versions[0].CalculatedBy);
            Assert.Equal(29, versions[1].CalculatedByUserId);
            Assert.Equal("actor-b", versions[1].CalculatedBy);
            Assert.Equal(firstJson, versions[0].KasaRaporDataJson);

            var payload = JsonSerializer.Deserialize<KasaRaporData>(versions[1].KasaRaporDataJson!);
            Assert.NotNull(payload?.ImmutableAudit);
            Assert.Equal(-3000.00m, payload.ImmutableAudit.GuneAitEksikFazlaTahsilat);
            Assert.Equal(-29873.80m, payload.ImmutableAudit.GuneAitEksikFazlaHarc);
            Assert.Equal(-3000.00m, payload.GuneAitEksikFazlaTahsilat);
            Assert.Equal(-29873.80m, payload.GuneAitEksikFazlaHarc);
            var details = payload.ImmutableAuditDetails!.Value
                .Deserialize<HesapKontrolImmutableAuditDetails>();
            Assert.True(HesapKontrolImmutableAuditDetailsValidator.TryValidate(
                details, out var detailsError), detailsError);
            Assert.Equal(2, details!.Records.Count);

            var beforeNoOp = await ReadStateAsync(secondId, AuditOnlyVersionDate, "Sabah");
            await using (var noOpContext = _fixture.CreateContext())
            {
                var replay = NewAuditAwareSnapshot(
                    AuditOnlyVersionDate, -3000.00m, -29873.80m, KasaRaporTuru.Sabah);
                replay.Name = "metadata-only-change";
                replay.Description = "must not version";
                replay.Notes = "must not version";
                var returned = await CreateService(noOpContext).SaveAsync(
                    replay, 44, "actor-c");
                Assert.Equal(secondId, returned.Id);
            }

            var afterNoOp = await ReadStateAsync(secondId, AuditOnlyVersionDate, "Sabah");
            AssertFinancialStateUnchanged(beforeNoOp, afterNoOp);
            Assert.Equal(beforeNoOp.Daily.CalculatedAt, afterNoOp.Daily.CalculatedAt);
            Assert.Equal(2, (await ReadVersionsAsync(
                AuditOnlyVersionDate, KasaRaporTuru.Sabah)).Count);
            Assert.Equal("audit-only-stable-fingerprint", afterNoOp.Daily.InputsFingerprint);
            Assert.Equal(versions[1].OutputsJson, afterNoOp.Daily.ResultsJson);
        }
        finally
        {
            await CleanupAsync(AuditOnlyVersionDate);
        }
    }

    [SqlServerFact]
    public async Task Save_DetailsOnlyChangeVersionsButEquivalentListOrderDoesNot()
    {
        try
        {
            var firstDetails = CreateAuditDetails(1, reverse: false);
            var secondDetails = CreateAuditDetails(101, reverse: false);
            Guid secondId;
            string firstJson;

            await using (var saveContext = _fixture.CreateContext())
            {
                var service = CreateService(saveContext);
                var first = await service.SaveAsync(
                    NewAuditAwareSnapshot(
                        DetailsOnlyVersionDate, -3000m, -29873.80m,
                        KasaRaporTuru.Aksam, firstDetails),
                    17,
                    "details-a");
                firstJson = first.KasaRaporDataJson!;
                var second = await service.SaveAsync(
                    NewAuditAwareSnapshot(
                        DetailsOnlyVersionDate, -3000m, -29873.80m,
                        KasaRaporTuru.Aksam, secondDetails),
                    29,
                    "details-b");
                secondId = second.Id;
                Assert.Equal(2, second.Version);
            }

            await using (var replayContext = _fixture.CreateContext())
            {
                var reordered = CreateAuditDetails(101, reverse: true);
                var returned = await CreateService(replayContext).SaveAsync(
                    NewAuditAwareSnapshot(
                        DetailsOnlyVersionDate, -3000m, -29873.80m,
                        KasaRaporTuru.Aksam, reordered),
                    44,
                    "details-order-only");
                Assert.Equal(secondId, returned.Id);
            }

            var versions = await ReadVersionsAsync(DetailsOnlyVersionDate, KasaRaporTuru.Aksam);
            Assert.Equal(2, versions.Count);
            Assert.Equal(firstJson, versions[0].KasaRaporDataJson);
            Assert.False(versions[0].IsActive);
            Assert.True(versions[1].IsActive);
            Assert.Equal(29, versions[1].CalculatedByUserId);
        }
        finally
        {
            await CleanupAsync(DetailsOnlyVersionDate);
        }
    }

    [SqlServerFact]
    public async Task Save_OutputsOnlyChangeCreatesNextVersion()
    {
        try
        {
            await using (var saveContext = _fixture.CreateContext())
            {
                var service = CreateService(saveContext);
                await service.SaveAsync(
                    NewAuditAwareSnapshot(OutputsOnlyVersionDate, 0m, 0m),
                    17,
                    "outputs-a");
                var changed = NewAuditAwareSnapshot(OutputsOnlyVersionDate, 0m, 0m);
                changed.OutputsJson = JsonSerializer.Serialize(new { financialOutput = 1801m });
                var second = await service.SaveAsync(changed, 29, "outputs-b");
                Assert.Equal(2, second.Version);
            }

            var versions = await ReadVersionsAsync(OutputsOnlyVersionDate, KasaRaporTuru.Aksam);
            Assert.Equal(2, versions.Count);
            Assert.NotEqual(versions[0].OutputsJson, versions[1].OutputsJson);
            Assert.False(versions[0].IsActive);
            Assert.True(versions[1].IsActive);
        }
        finally
        {
            await CleanupAsync(OutputsOnlyVersionDate);
        }
    }

    [SqlServerFact]
    public async Task Save_InputsUseSemanticCanonicalEqualityAndRealChangeVersions()
    {
        try
        {
            Guid firstId;
            await using (var firstContext = _fixture.CreateContext())
            {
                var first = NewAuditAwareSnapshot(InputsOnlyVersionDate, 0m, 0m);
                first.InputsJson = "{\"amount\":1,\"count\":2}";
                firstId = (await CreateService(firstContext).SaveAsync(
                    first, 17, "inputs-a")).Id;
            }

            await using (var orderOnlyContext = _fixture.CreateContext())
            {
                var orderOnly = NewAuditAwareSnapshot(InputsOnlyVersionDate, 0m, 0m);
                orderOnly.InputsJson = "{\"count\":2.0,\"amount\":1.00}";
                var returned = await CreateService(orderOnlyContext).SaveAsync(
                    orderOnly, 29, "inputs-order-only");
                Assert.Equal(firstId, returned.Id);
            }

            await using (var changedContext = _fixture.CreateContext())
            {
                var changed = NewAuditAwareSnapshot(InputsOnlyVersionDate, 0m, 0m);
                changed.InputsJson = "{\"amount\":1,\"count\":3}";
                var second = await CreateService(changedContext).SaveAsync(
                    changed, 44, "inputs-b");
                Assert.Equal(2, second.Version);
            }

            var versions = await ReadVersionsAsync(InputsOnlyVersionDate, KasaRaporTuru.Aksam);
            Assert.Equal(2, versions.Count);
            Assert.Equal(firstId, versions[0].Id);
            Assert.False(versions[0].IsActive);
            Assert.True(versions[1].IsActive);
        }
        finally
        {
            await CleanupAsync(InputsOnlyVersionDate);
        }
    }

    [SqlServerFact]
    public async Task Save_LegacyCorruptAndUnsupportedPayloadsFailSafeToNewValidVersion()
    {
        var payloads = new[]
        {
            "{\"PayloadVersion\":0}",
            "{\"PayloadVersion\":1,\"ImmutableAudit\":{}}",
            "{\"PayloadVersion\":2,\"ImmutableAudit\":{},\"ImmutableAuditDetails\":{\"Records\":[]}}",
            "{\"PayloadVersion\":2,\"ImmutableAudit\":null,\"ImmutableAuditDetails\":{\"Records\":[],\"Groups\":{\"AktifKayitlar\":[],\"OncekiAciklar\":[],\"BugunCozulenler\":[],\"ReconciliationKayitlar\":[],\"TakipteKayitlar\":[],\"BugunTakipCozulenler\":[]}}}",
            "{\"PayloadVersion\":3,\"ImmutableAudit\":{},\"ImmutableAuditDetails\":{\"Records\":[],\"Groups\":{\"AktifKayitlar\":[],\"OncekiAciklar\":[],\"BugunCozulenler\":[],\"ReconciliationKayitlar\":[],\"TakipteKayitlar\":[],\"BugunTakipCozulenler\":[]}}}"
        };
        var dates = payloads.Select((_, index) => InvalidPayloadBaseDate.AddDays(index)).ToArray();

        try
        {
            for (var index = 0; index < payloads.Length; index++)
            {
                var date = dates[index];
                Guid legacyId;
                await using (var firstContext = _fixture.CreateContext())
                {
                    var legacy = NewAuditAwareSnapshot(date, 0m, 0m);
                    legacy.KasaRaporDataJson = payloads[index];
                    legacyId = (await CreateService(firstContext).SaveAsync(
                        legacy, 17, "legacy-actor")).Id;
                }

                await using (var secondContext = _fixture.CreateContext())
                {
                    var valid = await CreateService(secondContext).SaveAsync(
                        NewAuditAwareSnapshot(date, 0m, 0m), 29, "valid-v2-actor");
                    Assert.NotEqual(legacyId, valid.Id);
                    Assert.Equal(2, valid.Version);
                }

                var versions = await ReadVersionsAsync(date, KasaRaporTuru.Aksam);
                Assert.Equal(2, versions.Count);
                Assert.Equal(payloads[index], versions[0].KasaRaporDataJson);
                Assert.False(versions[0].IsActive);
                Assert.True(versions[1].IsActive);
                Assert.Equal(2, JsonSerializer.Deserialize<KasaRaporData>(
                    versions[1].KasaRaporDataJson!)!.PayloadVersion);
            }
        }
        finally
        {
            await CleanupAsync(dates);
        }
    }

    [SqlServerFact]
    public async Task Save_AfterOlderVersionActivation_UsesMaximumVersionPlusOne()
    {
        try
        {
            await using (var saveContext = _fixture.CreateContext())
            {
                var service = CreateService(saveContext);
                var first = await service.SaveAsync(
                    NewSnapshot(VersionCollisionDate, KasaRaporTuru.Aksam, 701m), 17, "actor-a");
                await service.SaveAsync(
                    NewSnapshot(VersionCollisionDate, KasaRaporTuru.Aksam, 702m), 29, "actor-b");
                await service.SaveAsync(
                    NewSnapshot(VersionCollisionDate, KasaRaporTuru.Aksam, 703m), 29, "actor-b");
                Assert.Equal(
                    SnapshotMutationResult.Success,
                    await service.ActivateVersionAsync(first.Id, 44, true));

                var fourth = await service.SaveAsync(
                    NewSnapshot(VersionCollisionDate, KasaRaporTuru.Aksam, 704m), 55, "actor-c");
                Assert.Equal(4, fourth.Version);
            }

            var versions = await ReadVersionsAsync(VersionCollisionDate, KasaRaporTuru.Aksam);
            Assert.Equal(new[] { 1, 2, 3, 4 }, versions.Select(item => item.Version));
            Assert.Single(versions, item => item.IsActive && item.Version == 4);
            Assert.DoesNotContain(versions, item => item.IsActive && item.Version != 4);
        }
        finally
        {
            await CleanupAsync(VersionCollisionDate);
        }
    }

    private async Task<Guid> SeedOwnedSnapshotAsync(DateOnly date, int actorId, string actorName)
    {
        await using var context = _fixture.CreateContext();
        context.DailyCalculationResults.Add(NewDailyResult(
            date, "Aksam", $"fingerprint-{date:yyyyMMdd}"));
        await context.SaveChangesAsync();
        return (await CreateService(context).SaveAsync(
            NewSnapshot(date, KasaRaporTuru.Aksam, date.Day), actorId, actorName)).Id;
    }

    private async Task<SnapshotState> ReadStateAsync(Guid id, DateOnly date, string kasaTuru)
    {
        await using var context = _fixture.CreateContext();
        return new SnapshotState(
            await context.CalculatedKasaSnapshots.AsNoTracking().SingleAsync(item => item.Id == id),
            await context.DailyCalculationResults.AsNoTracking().SingleAsync(
                item => item.ForDate == date && item.KasaTuru == kasaTuru));
    }

    private async Task<List<CalculatedKasaSnapshot>> ReadVersionsAsync(
        DateOnly date,
        KasaRaporTuru kasaTuru)
    {
        await using var context = _fixture.CreateContext();
        return await context.CalculatedKasaSnapshots.AsNoTracking()
            .Where(item => item.RaporTarihi == date && item.KasaTuru == kasaTuru)
            .OrderBy(item => item.Version)
            .ToListAsync();
    }

    private static void AssertFinancialStateUnchanged(SnapshotState before, SnapshotState after)
    {
        Assert.Equal(before.Snapshot.KasaRaporDataJson, after.Snapshot.KasaRaporDataJson);
        Assert.Equal(before.Snapshot.InputsJson, after.Snapshot.InputsJson);
        Assert.Equal(before.Snapshot.OutputsJson, after.Snapshot.OutputsJson);
        Assert.Equal(before.Snapshot.FinancialExceptionsSummaryJson, after.Snapshot.FinancialExceptionsSummaryJson);
        Assert.Equal(before.Snapshot.Version, after.Snapshot.Version);
        Assert.Equal(before.Snapshot.CalculatedByUserId, after.Snapshot.CalculatedByUserId);
        Assert.Equal(before.Snapshot.CalculatedBy, after.Snapshot.CalculatedBy);
        Assert.Equal(before.Snapshot.RaporTarihi, after.Snapshot.RaporTarihi);
        Assert.Equal(before.Snapshot.KasaTuru, after.Snapshot.KasaTuru);
        Assert.Equal(before.Daily.InputsFingerprint, after.Daily.InputsFingerprint);
        Assert.Equal(before.Daily.ResultsJson, after.Daily.ResultsJson);
        Assert.Equal(before.Daily.CalculatedVersion, after.Daily.CalculatedVersion);
    }

    private static CalculatedKasaSnapshotService CreateService(
        KasaManager.Infrastructure.Persistence.KasaManagerDbContext context) =>
        new(context, NullLogger<CalculatedKasaSnapshotService>.Instance);

    private static CalculatedKasaSnapshot NewSnapshot(
        DateOnly date,
        KasaRaporTuru kasaTuru,
        decimal financialValue) => new()
    {
        RaporTarihi = date,
        KasaTuru = kasaTuru,
        Name = "original-name",
        Description = "original-description",
        Notes = "original-notes",
        InputsJson = JsonSerializer.Serialize(new { financialInput = financialValue }),
        OutputsJson = JsonSerializer.Serialize(new { financialOutput = financialValue * 2m }),
        KasaRaporDataJson = JsonSerializer.Serialize(new
        {
            PayloadVersion = 2,
            Tarih = date,
            KasaTuru = kasaTuru.ToString(),
            FinancialValue = financialValue,
            ImmutableAudit = new KasaImmutableAuditData(),
            ImmutableAuditDetails = EmptyAuditDetails()
        }),
        FinancialExceptionsSummaryJson = JsonSerializer.Serialize(new { total = financialValue }),
        FormulaSetName = "formula-c42d"
    };

    private static CalculatedKasaSnapshot NewAuditAwareSnapshot(
        DateOnly date,
        decimal auditTahsilat,
        decimal auditHarc,
        KasaRaporTuru kasaTuru = KasaRaporTuru.Aksam,
        HesapKontrolImmutableAuditDetails? suppliedDetails = null)
    {
        var details = suppliedDetails
            ?? (auditTahsilat == 0m && auditHarc == 0m
                ? EmptyAuditDetails()
                : CreateAuditDetails(1, reverse: false));
        var audit = new KasaImmutableAuditData
        {
            GuneAitEksikFazlaTahsilat = auditTahsilat,
            GuneAitEksikFazlaHarc = auditHarc
        };
        var payload = new KasaRaporData
        {
            PayloadVersion = 2,
            Tarih = date,
            KasaTuru = kasaTuru.ToString(),
            ImmutableAudit = audit,
            ImmutableAuditDetails = JsonSerializer.SerializeToElement(details),
            GuneAitEksikFazlaTahsilat = auditTahsilat,
            GuneAitEksikFazlaHarc = auditHarc
        };

        return new CalculatedKasaSnapshot
        {
            RaporTarihi = date,
            KasaTuru = kasaTuru,
            InputsJson = JsonSerializer.Serialize(new { financialInput = 900m }),
            OutputsJson = JsonSerializer.Serialize(new { financialOutput = 1800m }),
            KasaRaporDataJson = JsonSerializer.Serialize(payload),
            FinancialExceptionsSummaryJson = "{}",
            FormulaSetName = "formula-pre-c5-r1"
        };
    }

    private static HesapKontrolImmutableAuditDetails CreateAuditDetails(
        int idOffset,
        bool reverse)
    {
        var tahsilatId = Guid.Parse($"00000000-0000-0000-0000-{idOffset:D12}");
        var harcId = Guid.Parse($"00000000-0000-0000-0000-{idOffset + 1:D12}");
        var records = new[]
        {
            new HesapKontrolImmutableAuditRecord(
                tahsilatId,
                AuditOnlyVersionDate,
                BankaHesapTuru.Tahsilat,
                KayitYonu.Eksik,
                3000m,
                KayitDurumu.Acik,
                FarkSinifi.Bilinmeyen,
                "2026/T",
                "Tahsilat Birimi",
                "BILINMEYEN",
                null,
                null,
                null),
            new HesapKontrolImmutableAuditRecord(
                harcId,
                AuditOnlyVersionDate,
                BankaHesapTuru.Harc,
                KayitYonu.Eksik,
                29873.80m,
                KayitDurumu.Acik,
                FarkSinifi.Bilinmeyen,
                "2026/H",
                "Harç Birimi",
                "BILINMEYEN",
                null,
                null,
                null)
        };
        var orderedRecords = HesapKontrolImmutableAuditDetailsValidator.OrderRecords(records);
        var orderedIds = records.Select(record => record.KayitId).OrderBy(id => id).ToArray();

        return new HesapKontrolImmutableAuditDetails(
            reverse ? orderedRecords.Reverse().ToArray() : orderedRecords,
            new HesapKontrolImmutableAuditGroups(
                reverse ? orderedIds.Reverse().ToArray() : orderedIds,
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                Array.Empty<Guid>()));
    }

    private static HesapKontrolImmutableAuditDetails EmptyAuditDetails() => new(
        Array.Empty<HesapKontrolImmutableAuditRecord>(),
        new HesapKontrolImmutableAuditGroups(
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>()));

    private static DailyCalculationResult NewDailyResult(
        DateOnly date,
        string kasaTuru,
        string fingerprint) => new()
    {
        ForDate = date,
        KasaTuru = kasaTuru,
        InputsFingerprint = fingerprint,
        ResultsJson = "{}",
        CalculatedVersion = 0
    };

    private static decimal ReadFinancialValue(string? json)
    {
        using var document = JsonDocument.Parse(json!);
        return document.RootElement.GetProperty("FinancialValue").GetDecimal();
    }

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

    private sealed record SnapshotState(
        CalculatedKasaSnapshot Snapshot,
        DailyCalculationResult Daily);
}
