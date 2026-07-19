using KasaManager.Application.Abstractions;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KasaManager.Tests.Integration.SqlServer.HesapKontrol;

[Collection(SqlServerIntegrationCollection.Name)]
public sealed class HesapKontrolActorSqlServerIntegrationTests(SqlServerIntegrationFixture fixture)
{
    [SqlServerFact]
    public async Task AnalysisCreation_UsesServerActorAndIdempotentReplayPreservesFirstActorAndStringAudit()
    {
        var date = new DateOnly(2081, 1, 11);
        await using var context = fixture.CreateContext();
        var service = CreateAnalysisService(context, date, 4567.8912m);

        await service.AnalyzeFromComparisonAsync(date, "validated-c4-source", 17);
        var created = await context.HesapKontrolKayitlari.SingleAsync(x =>
            x.AnalizTarihi == date && x.HesapTuru == BankaHesapTuru.Tahsilat);
        Assert.Equal(17, created.CreatedByUserId);
        Assert.Null(created.CreatedBy);

        created.CreatedBy = "first-server-audit";
        await context.SaveChangesAsync();
        await service.AnalyzeFromComparisonAsync(date, "validated-c4-source", 29);
        context.ChangeTracker.Clear();

        var replayed = await context.HesapKontrolKayitlari.SingleAsync(x =>
            x.AnalizTarihi == date && x.HesapTuru == BankaHesapTuru.Tahsilat);
        Assert.Equal(17, replayed.CreatedByUserId);
        Assert.Equal("first-server-audit", replayed.CreatedBy);
    }

    [SqlServerFact]
    public async Task InteractiveCommands_RejectInvalidActorBeforeRelationalDatabaseMutation()
    {
        await using var context = fixture.CreateContext();
        var service = CreateService(context);
        var before = await context.HesapKontrolKayitlari.CountAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ConfirmMatchAsync(Guid.NewGuid(), 0, "user", null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.CancelAsync(Guid.NewGuid(), -1, "user", null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.StartTrackingAsync(Guid.NewGuid(), 0, "user", null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ResolveTrackedAsync(Guid.NewGuid(), -1, "user", null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.RevertAsync(Guid.NewGuid(), 0, "user", null));

        Assert.Equal(before, await context.HesapKontrolKayitlari.CountAsync());
    }

    [SqlServerFact]
    public async Task TrackingTransition_IsIdempotentAndRevertClearsOnlyTrackingActor()
    {
        await using var context = fixture.CreateContext();
        var record = NewRecord(new DateOnly(2081, 2, 12));
        record.CreatedByUserId = 5;
        record.ApprovedByUserId = 41;
        record.CancelledByUserId = 42;
        record.ResolvedByUserId = 43;
        context.Add(record);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        Assert.True(await service.StartTrackingAsync(record.Id, 21, "first-tracker", "start"));
        Assert.False(await service.StartTrackingAsync(record.Id, 22, "second-tracker", "repeat"));
        context.ChangeTracker.Clear();
        var tracked = await context.HesapKontrolKayitlari.SingleAsync(x => x.Id == record.Id);
        var trackingDate = tracked.TakipBaslangicTarihi;
        Assert.Equal(21, tracked.TrackingStartedByUserId);
        Assert.NotNull(trackingDate);
        Assert.Equal(5, tracked.CreatedByUserId);
        Assert.Equal("first-tracker", tracked.OnaylayanKullanici);

        Assert.True(await service.RevertAsync(record.Id, 90, "reverter", null));
        Assert.False(await service.RevertAsync(record.Id, 91, "second-reverter", null));
        context.ChangeTracker.Clear();
        var reverted = await context.HesapKontrolKayitlari.SingleAsync(x => x.Id == record.Id);
        Assert.Equal(KayitDurumu.Acik, reverted.Durum);
        Assert.Null(reverted.TrackingStartedByUserId);
        Assert.Null(reverted.TakipBaslangicTarihi);
        Assert.Equal(5, reverted.CreatedByUserId);
        Assert.Equal(41, reverted.ApprovedByUserId);
        Assert.Equal(42, reverted.CancelledByUserId);
        Assert.Equal(43, reverted.ResolvedByUserId);
    }

    [SqlServerFact]
    public async Task ResolveTransition_IsIdempotentAndRevertPreservesUnrelatedCreationAndTrackingActors()
    {
        await using var context = fixture.CreateContext();
        var record = NewRecord(new DateOnly(2081, 3, 13));
        record.CreatedByUserId = 5;
        record.Durum = KayitDurumu.Takipte;
        record.TakipBaslangicTarihi = record.AnalizTarihi;
        record.TrackingStartedByUserId = 21;
        record.ApprovedByUserId = 41;
        record.CancelledByUserId = 42;
        context.Add(record);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        Assert.True(await service.ResolveTrackedAsync(record.Id, 31, "first-resolver", "done"));
        Assert.False(await service.ResolveTrackedAsync(record.Id, 32, "second-resolver", "repeat"));
        context.ChangeTracker.Clear();
        var resolved = await context.HesapKontrolKayitlari.SingleAsync(x => x.Id == record.Id);
        Assert.Equal(31, resolved.ResolvedByUserId);
        Assert.Equal(21, resolved.TrackingStartedByUserId);
        Assert.Equal(record.AnalizTarihi, resolved.TakipBaslangicTarihi);

        Assert.True(await service.RevertAsync(record.Id, 90, "reverter", null));
        Assert.False(await service.RevertAsync(record.Id, 91, "second-reverter", null));
        context.ChangeTracker.Clear();
        var reverted = await context.HesapKontrolKayitlari.SingleAsync(x => x.Id == record.Id);
        Assert.Null(reverted.ResolvedByUserId);
        Assert.Equal(21, reverted.TrackingStartedByUserId);
        Assert.Equal(record.AnalizTarihi, reverted.TakipBaslangicTarihi);
        Assert.Equal(5, reverted.CreatedByUserId);
        Assert.Equal(41, reverted.ApprovedByUserId);
        Assert.Equal(42, reverted.CancelledByUserId);
    }

    [SqlServerFact]
    public async Task ResolveRevert_WithoutTrackingAudit_ClearsResolverWithoutInventingTrackingAudit()
    {
        await using var context = fixture.CreateContext();
        var record = NewRecord(new DateOnly(2081, 3, 14));
        record.CreatedByUserId = 6;
        record.Durum = KayitDurumu.Takipte;
        context.Add(record);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        Assert.True(await service.ResolveTrackedAsync(record.Id, 31, "resolver", "legacy tracking audit"));
        Assert.True(await service.RevertAsync(record.Id, 90, "reverter", null));
        Assert.False(await service.RevertAsync(record.Id, 91, "second-reverter", null));
        context.ChangeTracker.Clear();

        var reverted = await context.HesapKontrolKayitlari.SingleAsync(x => x.Id == record.Id);
        Assert.Equal(KayitDurumu.Acik, reverted.Durum);
        Assert.Null(reverted.ResolvedByUserId);
        Assert.Null(reverted.TrackingStartedByUserId);
        Assert.Null(reverted.TakipBaslangicTarihi);
        Assert.Equal(6, reverted.CreatedByUserId);
    }

    [SqlServerFact]
    public async Task ApprovalAndCancellation_AreIdempotentAndTheirRevertsPreserveUnrelatedActors()
    {
        await using var context = fixture.CreateContext();
        var approved = NewRecord(new DateOnly(2081, 4, 14));
        approved.CreatedByUserId = 5;
        var cancelled = NewRecord(new DateOnly(2081, 4, 15));
        cancelled.CreatedByUserId = 6;
        context.AddRange(approved, cancelled);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        Assert.True(await service.ConfirmMatchAsync(approved.Id, 41, "first-approver", "ok"));
        Assert.False(await service.ConfirmMatchAsync(approved.Id, 42, "second-approver", "repeat"));
        Assert.True(await service.CancelAsync(cancelled.Id, 51, "first-canceller", "invalid"));
        Assert.False(await service.CancelAsync(cancelled.Id, 52, "second-canceller", "repeat"));
        context.ChangeTracker.Clear();
        var persistedApproved = await context.HesapKontrolKayitlari.SingleAsync(x => x.Id == approved.Id);
        var persistedCancelled = await context.HesapKontrolKayitlari.SingleAsync(x => x.Id == cancelled.Id);
        Assert.Equal(41, persistedApproved.ApprovedByUserId);
        Assert.NotNull(persistedApproved.OnayTarihi);
        Assert.Equal(51, persistedCancelled.CancelledByUserId);

        Assert.True(await service.RevertAsync(approved.Id, 90, "reverter", null));
        Assert.True(await service.RevertAsync(cancelled.Id, 90, "reverter", null));
        context.ChangeTracker.Clear();
        persistedApproved = await context.HesapKontrolKayitlari.SingleAsync(x => x.Id == approved.Id);
        persistedCancelled = await context.HesapKontrolKayitlari.SingleAsync(x => x.Id == cancelled.Id);
        Assert.Null(persistedApproved.ApprovedByUserId);
        Assert.Equal(5, persistedApproved.CreatedByUserId);
        Assert.Null(persistedCancelled.CancelledByUserId);
        Assert.Equal(6, persistedCancelled.CreatedByUserId);
    }

    [SqlServerFact]
    public async Task ReconciliationAndLegacyReads_DoNotInventOwnersOrOverwriteActorAudit()
    {
        var date = new DateOnly(2081, 5, 16);
        await using var context = fixture.CreateContext();
        var missing = NewRecord(date.AddDays(-1), KayitYonu.Eksik);
        missing.DosyaNo = "C4-2081-RECON";
        missing.Durum = KayitDurumu.Takipte;
        missing.TakipBaslangicTarihi = date.AddDays(-1);
        missing.CreatedByUserId = 11;
        missing.TrackingStartedByUserId = 12;
        var surplus = NewRecord(date, KayitYonu.Fazla);
        surplus.Aciklama = "Payment C4-2081-RECON";
        surplus.CreatedByUserId = 21;
        surplus.ApprovedByUserId = 22;
        var legacy = NewRecord(date);
        legacy.CreatedBy = "legacy-name";
        legacy.CreatedByUserId = null;
        context.AddRange(missing, surplus, legacy);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var reconciliation = await service.CrossDayReconcileAsync(date);
        Assert.Single(reconciliation.KesirEslesmeler);
        context.ChangeTracker.Clear();
        missing = await context.HesapKontrolKayitlari.SingleAsync(x => x.Id == missing.Id);
        surplus = await context.HesapKontrolKayitlari.SingleAsync(x => x.Id == surplus.Id);
        Assert.Equal(11, missing.CreatedByUserId);
        Assert.Equal(12, missing.TrackingStartedByUserId);
        Assert.Null(missing.ResolvedByUserId);
        Assert.Equal(21, surplus.CreatedByUserId);
        Assert.Equal(22, surplus.ApprovedByUserId);

        var sharedRead = await service.GetOpenItemsAsync(bitis: date);
        Assert.Contains(sharedRead, item => item.Id == legacy.Id && item.CreatedByUserId == null);
        Assert.True(await service.ConfirmMatchAsync(legacy.Id, 61, "authenticated-user", null));
        context.ChangeTracker.Clear();
        legacy = await context.HesapKontrolKayitlari.SingleAsync(x => x.Id == legacy.Id);
        Assert.Null(legacy.CreatedByUserId);
        Assert.Equal("legacy-name", legacy.CreatedBy);
        Assert.Equal(61, legacy.ApprovedByUserId);
    }

    private static BankaHesapKontrolService CreateAnalysisService(
        KasaManagerDbContext context,
        DateOnly date,
        decimal amount)
    {
        var comparison = new Mock<IComparisonService>();
        comparison.Setup(x => x.CompareTahsilatMasrafAsync(
                It.IsAny<string>(), date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ComparisonReport>.Success(new ComparisonReport
            {
                Type = ComparisonType.TahsilatMasraf,
                GeneratedAt = DateTime.UtcNow,
                ReportDate = date,
                SurplusBankaCount = 1,
                SurplusAmount = amount,
                SurplusBankaRecords =
                [
                    new UnmatchedBankaRecord
                    {
                        RowIndex = 0,
                        Tutar = amount,
                        Aciklama = "C4 SQL actor creation",
                        DetectedType = "C4_SQL"
                    }
                ]
            }));
        comparison.Setup(x => x.CompareHarcamaHarcAsync(
                It.IsAny<string>(), date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ComparisonReport>.Success(new ComparisonReport
            {
                Type = ComparisonType.HarcamaHarc,
                GeneratedAt = DateTime.UtcNow,
                ReportDate = date
            }));
        comparison.Setup(x => x.CompareReddiyatCikisAsync(
                It.IsAny<string>(), date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ComparisonReport>.Fail("C4 test does not need reddiyat"));

        var resolver = new Mock<IHesapKontrolSourceResolver>();
        resolver.As<IPartialHesapKontrolSourceValidator>()
            .Setup(x => x.ValidateForAnalysis(It.IsAny<string>(), date))
            .Returns((string?)null);
        return new BankaHesapKontrolService(
            context,
            comparison.Object,
            Mock.Of<IImportOrchestrator>(),
            resolver.Object,
            NullLogger<BankaHesapKontrolService>.Instance);
    }

    private static BankaHesapKontrolService CreateService(KasaManagerDbContext context) => new(
        context,
        Mock.Of<IComparisonService>(),
        Mock.Of<IImportOrchestrator>(),
        Mock.Of<IHesapKontrolSourceResolver>(),
        NullLogger<BankaHesapKontrolService>.Instance);

    private static HesapKontrolKaydi NewRecord(DateOnly date, KayitYonu direction = KayitYonu.Eksik) => new()
    {
        Id = Guid.NewGuid(),
        AnalizTarihi = date,
        HesapTuru = BankaHesapTuru.Tahsilat,
        Yon = direction,
        Tutar = 125.50m,
        Sinif = FarkSinifi.Bilinmeyen,
        Durum = KayitDurumu.Acik,
        DosyaNo = direction == KayitYonu.Eksik ? $"C4-{Guid.NewGuid():N}" : null,
        Aciklama = direction == KayitYonu.Fazla ? $"C4-SURPLUS-{Guid.NewGuid():N}" : null
    };
}
