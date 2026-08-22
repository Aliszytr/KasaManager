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
            service.ResolveTrackedStopajAsync(Guid.NewGuid(), -1, "user", null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ResolveTrackedFinancialAsync(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), -1, "user", null));
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
        // Revision 3: ResolveTrackedFinancialAsync artık reversalBusinessDate'i çağırandan (burada
        // test kendisi, üretimde IManualResolveWriteBusinessDateResolver) required parametre olarak
        // alır — reversal her zaman >= origin olmalıdır, bu yüzden AnalizTarihi bu iki test için repo
        // genelindeki "2081" sabit-gelecek konvansiyonu yerine gerçek bugünden ÖNCEKİ bir tarih olmalı
        // (yalnızca bu iki test StartTracking+ResolveTrackedFinancial'ı birlikte, gerçek sistem
        // tarihine karşı çalıştırıyor).
        var record = NewRecord(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30));
        record.CreatedByUserId = 5;
        record.ApprovedByUserId = 41;
        record.CancelledByUserId = 42;
        context.Add(record);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        try
        {
            // ResolveTrackedAsync artık origin'in gerçekten materialize edilmiş olmasını (reversal
            // CAS'ın şart koştuğu) gerektiriyor, bu yüzden Takipte'ye doğrudan seed yerine gerçek
            // StartTrackingAsync çağrılır. TakipBaslangicTarihi artık "bugün" (StartTracking anı)
            // olduğundan, sabit record.AnalizTarihi yerine gerçekleşen değer dinamik olarak yakalanır.
            Assert.True(await service.StartTrackingAsync(record.Id, 21, "first-tracker", null));
            context.ChangeTracker.Clear();
            var trackingDate = (await context.HesapKontrolKayitlari.SingleAsync(x => x.Id == record.Id))
                .TakipBaslangicTarihi;

            var reversalBusinessDate = DateOnly.FromDateTime(DateTime.UtcNow);
            Assert.True(await service.ResolveTrackedFinancialAsync(record.Id, reversalBusinessDate, 31, "first-resolver", "done"));
            Assert.False(await service.ResolveTrackedFinancialAsync(record.Id, reversalBusinessDate, 32, "second-resolver", "repeat"));
            context.ChangeTracker.Clear();
            var resolved = await context.HesapKontrolKayitlari.SingleAsync(x => x.Id == record.Id);
            Assert.Equal(31, resolved.ResolvedByUserId);
            Assert.Equal(21, resolved.TrackingStartedByUserId);
            Assert.Equal(trackingDate, resolved.TakipBaslangicTarihi);

            Assert.True(await service.RevertAsync(record.Id, 90, "reverter", null));
            Assert.False(await service.RevertAsync(record.Id, 91, "second-reverter", null));
            context.ChangeTracker.Clear();
            var reverted = await context.HesapKontrolKayitlari.SingleAsync(x => x.Id == record.Id);
            Assert.Null(reverted.ResolvedByUserId);
            Assert.Equal(21, reverted.TrackingStartedByUserId);
            Assert.Equal(trackingDate, reverted.TakipBaslangicTarihi);
            Assert.Equal(5, reverted.CreatedByUserId);
            Assert.Equal(41, reverted.ApprovedByUserId);
            Assert.Equal(42, reverted.CancelledByUserId);
        }
        finally
        {
            // Bu dosyadaki diğer tüm testler "2081+" sabit-gelecek konvansiyonunu kullanır ve hiç
            // temizlik yapmaz (paylaşılan koleksiyon fixture DB'sinde kalıcı kalırlar) — çünkü hiçbir
            // zaman gerçek bugünden ÖNCE bir tarih kullanmazlar. Bu iki test (reversal "bugün" olduğu
            // için) ilk kez gerçek/yakın bir tarih (AnalizTarihi < 2071/2081) kullanıyor — bu, "before
            // selectedDate" gibi sınırsız-geriye-dönük sorgular yapan diğer paylaşılan-fixture testlerini
            // (ör. CanonicalImmutableAuditSqlServerIntegrationTests) kirletir. Bu yüzden yalnızca bu iki
            // test kendi kaydını açıkça temizler.
            await using var cleanup = fixture.CreateContext();
            await cleanup.HesapKontrolKayitlari.Where(x => x.Id == record.Id).ExecuteDeleteAsync();
        }
    }

    [SqlServerFact]
    public async Task ResolveRevert_WithoutTrackingAudit_ClearsResolverWithoutInventingTrackingAudit()
    {
        await using var context = fixture.CreateContext();
        // Aynı gerekçe: reversal date "bugün" olacağı için AnalizTarihi geçmişte olmalı.
        var record = NewRecord(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-31));
        record.CreatedByUserId = 6;
        context.Add(record);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        try
        {
            // Revision 3: origin materialization için gerçek StartTrackingAsync çağrılır (aksi halde
            // ResolveTrackedFinancialAsync artık reddeder). Testin asıl amacı — "tracking actor hiç
            // kaydedilmemiş legacy senaryosunda revert bunu uydurmaz" — korunmak için, yalnızca audit
            // alanları (TrackingStartedByUserId/TakipBaslangicTarihi) StartTracking sonrası elle
            // null'a döndürülür; finansal KasaEtkisi* alanlarına DOKUNULMAZ.
            Assert.True(await service.StartTrackingAsync(record.Id, 15, "system-tracker", null));
            await using (var mutate = fixture.CreateContext())
            {
                var tracked = await mutate.HesapKontrolKayitlari.SingleAsync(x => x.Id == record.Id);
                tracked.TrackingStartedByUserId = null;
                tracked.TakipBaslangicTarihi = null;
                await mutate.SaveChangesAsync();
            }
            context.ChangeTracker.Clear();

            Assert.True(await service.ResolveTrackedFinancialAsync(
                record.Id, DateOnly.FromDateTime(DateTime.UtcNow), 31, "resolver", "legacy tracking audit"));
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
        finally
        {
            // Bkz. ResolveTransition_...'daki aynı gerekçe: bu test de gerçek/yakın bir tarih
            // kullanıyor (2081+ konvansiyonunun aksine), bu yüzden paylaşılan koleksiyon fixture
            // DB'sini kirletmemek için kendi kaydını açıkça temizler.
            await using var cleanup = fixture.CreateContext();
            await cleanup.HesapKontrolKayitlari.Where(x => x.Id == record.Id).ExecuteDeleteAsync();
        }
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
        Assert.Null(persistedCancelled.ResolvedByUserId);
        Assert.Contains("first-canceller", persistedCancelled.Notlar);

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
    public async Task PotentialMatch_UnequalAmountsRemainActive_WhileEqualAmountsResolveAtomicallyAndIdempotently()
    {
        var date = new DateOnly(2081, 4, 16);
        await using var context = fixture.CreateContext();
        var largerMissing = NewRecord(date.AddDays(-1), KayitYonu.Eksik);
        largerMissing.Durum = KayitDurumu.Takipte;
        largerMissing.TakipBaslangicTarihi = date.AddDays(-1);
        largerMissing.Tutar = 100m;
        var smallerSurplus = NewRecord(date, KayitYonu.Fazla);
        smallerSurplus.Tutar = 80m;
        var equalMissing = NewRecord(date.AddDays(-1), KayitYonu.Eksik);
        equalMissing.Durum = KayitDurumu.Takipte;
        equalMissing.TakipBaslangicTarihi = date.AddDays(-1);
        equalMissing.Tutar = 125m;
        var equalSurplus = NewRecord(date, KayitYonu.Fazla);
        equalSurplus.Tutar = 125m;
        context.AddRange(
            largerMissing, smallerSurplus, equalMissing, equalSurplus);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        Assert.False(await service.ApprovePotentialMatchAsync(
            largerMissing.Id, smallerSurplus.Id, 61, "partial-matcher"));
        Assert.True(await service.ApprovePotentialMatchAsync(
            equalMissing.Id, equalSurplus.Id, 62, "equal-matcher"));

        // ExecutePairResolutionAsync writes via ExecuteUpdateAsync, which never updates this
        // context's tracked entity state — without clearing here, the replay call's FindAsync would
        // return the same stale (pre-CAS) tracked instances instead of querying the DB, so it would
        // never observe the real committed Durum=Cozuldu and would incorrectly proceed past
        // ApprovePotentialMatchAsync's Acik/Takipte guard. A real second HTTP request gets a fresh
        // DbContext and would see the committed state directly; this Clear() simulates that.
        context.ChangeTracker.Clear();
        Assert.False(await service.ApprovePotentialMatchAsync(
            equalMissing.Id, equalSurplus.Id, 63, "replay-matcher"));
        context.ChangeTracker.Clear();

        largerMissing = await context.HesapKontrolKayitlari
            .SingleAsync(record => record.Id == largerMissing.Id);
        smallerSurplus = await context.HesapKontrolKayitlari
            .SingleAsync(record => record.Id == smallerSurplus.Id);
        equalMissing = await context.HesapKontrolKayitlari
            .SingleAsync(record => record.Id == equalMissing.Id);
        equalSurplus = await context.HesapKontrolKayitlari
            .SingleAsync(record => record.Id == equalSurplus.Id);

        Assert.Equal(KayitDurumu.Takipte, largerMissing.Durum);
        Assert.Equal(100m, largerMissing.Tutar);
        Assert.Null(largerMissing.CozulmeTarihi);
        Assert.Equal(KayitDurumu.Acik, smallerSurplus.Durum);
        Assert.Equal(80m, smallerSurplus.Tutar);
        Assert.Null(smallerSurplus.CozulmeTarihi);
        Assert.Equal(KayitDurumu.Cozuldu, equalMissing.Durum);
        Assert.Equal(KayitDurumu.Cozuldu, equalSurplus.Durum);
        Assert.Equal(62, equalMissing.ApprovedByUserId);
        Assert.Equal(62, equalSurplus.ApprovedByUserId);
        Assert.NotNull(equalMissing.CozulmeTarihi);
        Assert.Equal(equalMissing.CozulmeTarihi, equalSurplus.CozulmeTarihi);
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
