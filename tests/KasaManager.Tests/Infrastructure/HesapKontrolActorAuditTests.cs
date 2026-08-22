using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KasaManager.Tests.Infrastructure;

public sealed class HesapKontrolActorAuditTests
{
    private static readonly DateOnly TestDate = new(2026, 7, 18);

    [Fact]
    public async Task InteractiveCommands_RejectNonPositiveActorBeforeDatabaseAccess()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ConfirmMatchAsync(id, 0, "user", null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.CancelAsync(id, -1, "user", null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.StartTrackingAsync(id, 0, "user", null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ResolveTrackedStopajAsync(id, -1, "user", null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ResolveTrackedFinancialAsync(id, TestDate, -1, "user", null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.RevertAsync(id, 0, "user", null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ApprovePotentialMatchAsync(id, Guid.NewGuid(), -1, "user"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.RejectPotentialMatchAsync(id, Guid.NewGuid(), 0, "user"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.AnalyzeFromComparisonAsync(TestDate, "unused", -1));

        Assert.Empty(db.HesapKontrolKayitlari);
    }

    // RealTransitions_WriteMatchingActorFieldsAndPreserveUsernameMetadata removed (Stage 2 test
    // contract closure): Revision 3's ExecutePairResolutionAsync persists via ExecuteUpdateAsync,
    // which the EF Core InMemory provider cannot translate ("could not be translated" InvalidOperationException)
    // — this test's ApprovePotentialMatchAsync step could never run against this class's InMemory
    // CreateDb(). The same actor/audit contract for StartTracking/ResolveTracked/ConfirmMatch/Cancel/
    // ApprovePotentialMatch is already proven against real SQL Server in
    // HesapKontrolActorSqlServerIntegrationTests (TrackingTransition_..., ResolveTransition_...,
    // ApprovalAndCancellation_..., PotentialMatch_UnequalAmountsRemainActive_...).

    [Fact]
    public async Task TrackingNoOp_DoesNotOverwriteExistingActor()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var record = NewRecord();
        record.Durum = KayitDurumu.Takipte;
        record.TrackingStartedByUserId = 17;
        record.OnaylayanKullanici = "first-user";
        record.TakipBaslangicTarihi = TestDate.AddDays(-1);
        db.Add(record);
        await db.SaveChangesAsync();

        Assert.False(await service.StartTrackingAsync(record.Id, 29, "second-user", null));
        Assert.Equal(17, record.TrackingStartedByUserId);
        Assert.Equal("first-user", record.OnaylayanKullanici);
    }

    [Theory]
    [InlineData(100, 80)]
    [InlineData(80, 100)]
    public async Task ApprovePotentialMatch_UnequalAmounts_PreservesBothActiveRecords(
        decimal missingAmount,
        decimal surplusAmount)
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var missing = NewRecord(KayitYonu.Eksik);
        missing.Durum = KayitDurumu.Takipte;
        missing.TakipBaslangicTarihi = TestDate.AddDays(-1);
        missing.Tutar = missingAmount;
        var surplus = NewRecord(KayitYonu.Fazla);
        surplus.Tutar = surplusAmount;
        db.AddRange(missing, surplus);
        await db.SaveChangesAsync();

        var approved = await service.ApprovePotentialMatchAsync(
            missing.Id, surplus.Id, 51, "matcher");

        Assert.False(approved);
        Assert.Equal(KayitDurumu.Takipte, missing.Durum);
        Assert.Equal(missingAmount, missing.Tutar);
        Assert.Null(missing.CozulmeTarihi);
        Assert.Null(missing.CozulmeKaynakId);
        Assert.Equal(KayitDurumu.Acik, surplus.Durum);
        Assert.Equal(surplusAmount, surplus.Tutar);
        Assert.Null(surplus.CozulmeTarihi);
        Assert.Null(surplus.CozulmeKaynakId);
        Assert.Equal(2, await db.HesapKontrolKayitlari.CountAsync());
    }

    // ApprovePotentialMatch_SecondApproval_IsIdempotent removed (Stage 2 test contract closure):
    // same InMemory/ExecuteUpdateAsync incompatibility as RealTransitions_... above. The idempotent
    // second-approval-returns-false contract is already proven against real SQL Server by
    // HesapKontrolActorSqlServerIntegrationTests.PotentialMatch_UnequalAmountsRemainActive_
    // WhileEqualAmountsResolveAtomicallyAndIdempotently ("replay-matcher" step).

    [Fact]
    public async Task Revert_ClearsOnlyRevertedTransitionActorsAndPreservesCreationActor()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var cancelled = NewRecord();
        cancelled.Durum = KayitDurumu.Iptal;
        cancelled.CreatedByUserId = 5;
        cancelled.CancelledByUserId = 6;
        var approved = NewRecord();
        approved.Durum = KayitDurumu.Onaylandi;
        approved.CreatedByUserId = 5;
        approved.ApprovedByUserId = 7;
        var resolved = NewRecord();
        resolved.Durum = KayitDurumu.Onaylandi;
        resolved.CreatedByUserId = 5;
        resolved.TrackingStartedByUserId = 8;
        resolved.ResolvedByUserId = 9;
        resolved.ApprovedByUserId = 10;
        resolved.TakipBaslangicTarihi = TestDate.AddDays(-2);
        var resolvedTrackingDate = resolved.TakipBaslangicTarihi;
        var tracked = NewRecord();
        tracked.Durum = KayitDurumu.Takipte;
        tracked.CreatedByUserId = 5;
        tracked.TrackingStartedByUserId = 11;
        tracked.ApprovedByUserId = 12;
        tracked.TakipBaslangicTarihi = TestDate.AddDays(-3);
        db.AddRange(cancelled, approved, resolved, tracked);
        await db.SaveChangesAsync();

        Assert.True(await service.RevertAsync(cancelled.Id, 90, "reverter", null));
        Assert.True(await service.RevertAsync(approved.Id, 90, "reverter", null));
        Assert.True(await service.RevertAsync(resolved.Id, 90, "reverter", null));
        Assert.True(await service.RevertAsync(tracked.Id, 90, "reverter", null));
        Assert.False(await service.RevertAsync(resolved.Id, 91, "second-reverter", null));
        Assert.False(await service.RevertAsync(tracked.Id, 91, "second-reverter", null));

        Assert.Equal(5, cancelled.CreatedByUserId);
        Assert.Null(cancelled.CancelledByUserId);
        Assert.Equal(5, approved.CreatedByUserId);
        Assert.Null(approved.ApprovedByUserId);
        Assert.Equal(5, resolved.CreatedByUserId);
        Assert.Null(resolved.ResolvedByUserId);
        Assert.Equal(8, resolved.TrackingStartedByUserId);
        Assert.Equal(resolvedTrackingDate, resolved.TakipBaslangicTarihi);
        Assert.Equal(10, resolved.ApprovedByUserId);
        Assert.Equal(5, tracked.CreatedByUserId);
        Assert.Null(tracked.TrackingStartedByUserId);
        Assert.Null(tracked.TakipBaslangicTarihi);
        Assert.Equal(12, tracked.ApprovedByUserId);
        Assert.All(new[] { cancelled, approved, resolved, tracked }, record =>
        {
            Assert.Equal(KayitDurumu.Acik, record.Durum);
            Assert.Equal("reverter", record.GeriAlanKullanici);
        });
    }

    [Fact]
    public async Task LegacyNullActor_RemainsVisibleInSharedQueriesAndCanTransition()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var legacy = NewRecord();
        legacy.CreatedBy = "legacy-name";
        legacy.CreatedByUserId = null;
        db.Add(legacy);
        await db.SaveChangesAsync();

        var userAView = await service.GetOpenItemsAsync(bitis: TestDate);
        var userAAutoFill = await service.GetAutoFillDataAsync(TestDate);
        var userBAutoFill = await service.GetAutoFillDataAsync(TestDate);

        Assert.Contains(userAView, record => record.Id == legacy.Id);
        Assert.True(userAAutoFill.HasData);
        Assert.Equal(userAAutoFill.GuneAitEksikFazlaTahsilat,
            userBAutoFill.GuneAitEksikFazlaTahsilat);
        Assert.Equal(userAAutoFill.ToplamFarkTahsilat,
            userBAutoFill.ToplamFarkTahsilat);

        Assert.True(await service.ConfirmMatchAsync(legacy.Id, 44, "new-user", null));
        Assert.Null(legacy.CreatedByUserId);
        Assert.Equal("legacy-name", legacy.CreatedBy);
        Assert.Equal(44, legacy.ApprovedByUserId);
    }

    // SystemReconciliation_DoesNotChangeExistingActorFields removed (Stage 2 test contract closure):
    // same InMemory/ExecuteUpdateAsync incompatibility — CrossDayReconcileAsync's auto-match step
    // reaches ExecutePairResolutionAsync here too. The identical scenario (DosyaNo-matched missing/
    // surplus pair, actor fields preserved through CrossDayReconcileAsync) is already proven against
    // real SQL Server by HesapKontrolActorSqlServerIntegrationTests.
    // ReconciliationAndLegacyReads_DoNotInventOwnersOrOverwriteActorAudit.

    private static HesapKontrolKaydi NewRecord(
        KayitYonu direction = KayitYonu.Eksik,
        DateOnly? date = null) => new()
    {
        AnalizTarihi = date ?? TestDate,
        HesapTuru = BankaHesapTuru.Tahsilat,
        Yon = direction,
        Tutar = 125.50m,
        Sinif = FarkSinifi.Bilinmeyen,
        Durum = KayitDurumu.Acik,
        DosyaNo = direction == KayitYonu.Eksik ? $"FILE-{Guid.NewGuid():N}" : null,
        Aciklama = direction == KayitYonu.Fazla ? $"SURPLUS-{Guid.NewGuid():N}" : null
    };

    private static KasaManagerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseInMemoryDatabase($"ActorAudit_{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings =>
                warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new KasaManagerDbContext(options);
    }

    private static BankaHesapKontrolService CreateService(KasaManagerDbContext db) =>
        new(
            db,
            Mock.Of<IComparisonService>(),
            Mock.Of<IImportOrchestrator>(),
            Mock.Of<IHesapKontrolSourceResolver>(),
            NullLogger<BankaHesapKontrolService>.Instance);
}
