using System.Runtime.InteropServices;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KasaManager.Tests.Infrastructure;

/// <summary>
/// KASAMANAGER-2026-08-21-FINAL-PLAN-CLOSURE Revision 3, Section 9 — hedeflenen RED testleri.
/// LocalDB fixture deseni BankaHesapKontrolServiceCrossDayIdempotencyTests.cs'ten alınmıştır.
/// Bu testler CI'da yalnızca Windows + (localdb)\mssqllocaldb erişilebilirse çalışır; aksi halde
/// no-op olarak geçer (mevcut repo konvansiyonu).
/// </summary>
public sealed class BankaHesapKontrolServiceKasaEtkisiCasTests
{

    // ═════════════════════════════════════════════════════════════
    // Domain guard testleri — DB gerektirmez (hızlı, saf unit test)
    // ═════════════════════════════════════════════════════════════

    [Fact]
    public void SetKasaEtkisi_NullToX_Succeeds()
    {
        var kayit = new HesapKontrolKaydi();
        kayit.SetKasaEtkisi(-4240m, new DateOnly(2026, 8, 19));
        Assert.Equal(-4240m, kayit.KasaEtkisiTutari);
        Assert.Equal(new DateOnly(2026, 8, 19), kayit.KasaEtkisiIsTarihi);
    }

    [Fact]
    public void SetKasaEtkisi_SameXTwice_IsIdempotent()
    {
        var kayit = new HesapKontrolKaydi();
        kayit.SetKasaEtkisi(-4240m, new DateOnly(2026, 8, 19));
        kayit.SetKasaEtkisi(-4240m, new DateOnly(2026, 8, 19)); // no throw
        Assert.Equal(-4240m, kayit.KasaEtkisiTutari);
    }

    [Fact]
    public void SetKasaEtkisi_DifferentY_ThrowsFailClosed()
    {
        var kayit = new HesapKontrolKaydi();
        kayit.SetKasaEtkisi(-4240m, new DateOnly(2026, 8, 19));
        Assert.Throws<InvalidOperationException>(() => kayit.SetKasaEtkisi(-9999m, new DateOnly(2026, 8, 19)));
    }

    [Fact]
    public void SetKasaEtkisiTersDonus_BeforeOrigin_ThrowsFailClosed()
    {
        var kayit = new HesapKontrolKaydi();
        kayit.SetKasaEtkisi(-4240m, new DateOnly(2026, 8, 20));
        Assert.Throws<InvalidOperationException>(() => kayit.SetKasaEtkisiTersDonus(new DateOnly(2026, 8, 19)));
    }

    [Fact]
    public void SetKasaEtkisiTersDonus_WithoutOrigin_ThrowsFailClosed()
    {
        var kayit = new HesapKontrolKaydi();
        Assert.Throws<InvalidOperationException>(() => kayit.SetKasaEtkisiTersDonus(new DateOnly(2026, 8, 20)));
    }

    // ═════════════════════════════════════════════════════════════
    // DB-backed (LocalDB) — Ayrık İki Olaylı Zamansal Model proof'ları
    // ═════════════════════════════════════════════════════════════

    [Fact]
    public async Task RealRecords_19Aug_Minus4240_20Aug_Plus4240_SqlServer()
    {
        await using var fx = await LocalDbFixture.CreateAsync("HK_1920Proof");
        if (fx == null) return;

        var older = new DateOnly(2026, 8, 19);
        var later = new DateOnly(2026, 8, 20);
        var olderId = Guid.NewGuid();
        var laterId = Guid.NewGuid();

        await fx.SeedAsync(
            Eksik(olderId, older, 4240m),
            Fazla(laterId, later, 4240m));

        await using (var db = fx.NewContext())
        {
            var svc = CreateService(db);
            var result = await svc.ExecutePairResolutionAsync(
                olderId, older, -4240m,
                laterId, later, 4240m,
                later);
            Assert.True(result.Success, result.ConflictReason);
        }

        await using (var db = fx.NewContext())
        {
            var svc = CreateService(db);
            var t19 = await svc.GetDailyFollowTotalsAsync(older);
            var t20 = await svc.GetDailyFollowTotalsAsync(later);
            Assert.Equal(-4240m, t19.TahsilatNet);
            Assert.Equal(4240m, t20.TahsilatNet);
        }
    }

    [Fact]
    public async Task RealRecords_R5_ResolutionDay_Plus29500_SqlServer()
    {
        await using var fx = await LocalDbFixture.CreateAsync("HK_R5Proof");
        if (fx == null) return;

        var resolutionDay = new DateOnly(2070, 3, 12);
        var originDay = resolutionDay.AddDays(-3);
        var olderId = Guid.NewGuid();
        var laterId = Guid.NewGuid();

        await fx.SeedAsync(
            Eksik(olderId, originDay, 29500m),
            Fazla(laterId, resolutionDay, 29500m));

        await using (var db = fx.NewContext())
        {
            var svc = CreateService(db);
            var result = await svc.ExecutePairResolutionAsync(
                olderId, originDay, -29500m,
                laterId, resolutionDay, 29500m,
                resolutionDay);
            Assert.True(result.Success, result.ConflictReason);
        }

        await using (var db = fx.NewContext())
        {
            var totals = await CreateService(db).GetDailyFollowTotalsAsync(resolutionDay);
            Assert.Equal(29500m, totals.TahsilatNet);
        }
    }

    [Fact]
    public async Task RealRecords_SameDayOriginAndReversal_NetsZero_SqlServer()
    {
        await using var fx = await LocalDbFixture.CreateAsync("HK_SameDayZero");
        if (fx == null) return;

        var day = new DateOnly(2026, 6, 1);
        var olderId = Guid.NewGuid();
        var laterId = Guid.NewGuid();

        await fx.SeedAsync(Eksik(olderId, day, 500m), Fazla(laterId, day, 500m));

        await using (var db = fx.NewContext())
        {
            var result = await CreateService(db).ExecutePairResolutionAsync(
                olderId, day, -500m, laterId, day, 500m, day);
            Assert.True(result.Success, result.ConflictReason);
        }

        await using (var db = fx.NewContext())
        {
            var totals = await CreateService(db).GetDailyFollowTotalsAsync(day);
            Assert.Equal(0m, totals.TahsilatNet);
        }
    }

    [Fact]
    public async Task RealRecords_MiddleDaysBetweenOriginAndReversal_ContributeZero_SqlServer()
    {
        await using var fx = await LocalDbFixture.CreateAsync("HK_MiddleZero");
        if (fx == null) return;

        var origin = new DateOnly(2026, 6, 1);
        var reversal = new DateOnly(2026, 6, 10);
        var olderId = Guid.NewGuid();
        var laterId = Guid.NewGuid();

        await fx.SeedAsync(Eksik(olderId, origin, 800m), Fazla(laterId, reversal, 800m));

        await using (var db = fx.NewContext())
        {
            var result = await CreateService(db).ExecutePairResolutionAsync(
                olderId, origin, -800m, laterId, reversal, 800m, reversal);
            Assert.True(result.Success, result.ConflictReason);
        }

        await using (var db = fx.NewContext())
        {
            var svc = CreateService(db);
            for (var d = origin.AddDays(1); d < reversal; d = d.AddDays(1))
            {
                var totals = await svc.GetDailyFollowTotalsAsync(d);
                Assert.Equal(0m, totals.TahsilatNet);
            }
        }
    }

    [Fact]
    public async Task RealRecords_HarcSignMatrix_SqlServer()
    {
        await using var fx = await LocalDbFixture.CreateAsync("HK_HarcSign");
        if (fx == null) return;

        var origin = new DateOnly(2026, 6, 1);
        var reversal = new DateOnly(2026, 6, 3);
        var olderId = Guid.NewGuid();
        var laterId = Guid.NewGuid();

        await fx.SeedAsync(
            Eksik(olderId, origin, 300m, BankaHesapTuru.Harc),
            Fazla(laterId, reversal, 300m, BankaHesapTuru.Harc));

        await using (var db = fx.NewContext())
        {
            var result = await CreateService(db).ExecutePairResolutionAsync(
                olderId, origin, -300m, laterId, reversal, 300m, reversal);
            Assert.True(result.Success, result.ConflictReason);
        }

        await using (var db = fx.NewContext())
        {
            var svc = CreateService(db);
            var t0 = await svc.GetDailyFollowTotalsAsync(origin);
            var t1 = await svc.GetDailyFollowTotalsAsync(reversal);
            Assert.Equal(-300m, t0.HarcNet);
            Assert.Equal(0m, t0.TahsilatNet); // Harc/Tahsilat karışmaz
            Assert.Equal(300m, t1.HarcNet);
        }
    }

    [Fact]
    public async Task RealRecords_TahsilatSignMatrix_SqlServer()
    {
        await using var fx = await LocalDbFixture.CreateAsync("HK_TahsilatSign");
        if (fx == null) return;

        var origin = new DateOnly(2026, 6, 1);
        var reversal = new DateOnly(2026, 6, 3);
        var olderId = Guid.NewGuid();
        var laterId = Guid.NewGuid();

        await fx.SeedAsync(
            Eksik(olderId, origin, 300m, BankaHesapTuru.Tahsilat),
            Fazla(laterId, reversal, 300m, BankaHesapTuru.Tahsilat));

        await using (var db = fx.NewContext())
        {
            var result = await CreateService(db).ExecutePairResolutionAsync(
                olderId, origin, -300m, laterId, reversal, 300m, reversal);
            Assert.True(result.Success, result.ConflictReason);
        }

        await using (var db = fx.NewContext())
        {
            var svc = CreateService(db);
            var t0 = await svc.GetDailyFollowTotalsAsync(origin);
            var t1 = await svc.GetDailyFollowTotalsAsync(reversal);
            Assert.Equal(-300m, t0.TahsilatNet); // Tahsilat/Eksik → -Tutar
            Assert.Equal(0m, t0.HarcNet); // Tahsilat/Harc karışmaz
            Assert.Equal(300m, t1.TahsilatNet); // Tahsilat/Fazla → +Tutar
        }
    }

    // ═════════════════════════════════════════════════════════════
    // Origin materialization: StartTrackingAsync artık origin'i (KasaEtkisiTutari/IsTarihi) ile
    // lifecycle transition'ı AYNI transaction içinde yazar; ResolveTrackedAsync reversal'ı yazar.
    // Stage 2 blocker kapanışı — Section 5.A/B/C.
    // ═════════════════════════════════════════════════════════════

    [Fact]
    public async Task RealRecords_StartTracking_MaterializesOriginImmediately_SqlServer()
    {
        await using var fx = await LocalDbFixture.CreateAsync("HK_StartTrackOrigin");
        if (fx == null) return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var day1 = today.AddDays(-10);
        var middleDay = today.AddDays(-5);
        var id = Guid.NewGuid();
        await fx.SeedAsync(Eksik(id, day1, 29_500m));

        await using (var db = fx.NewContext())
        {
            var started = await CreateService(db).StartTrackingAsync(id, 51, "tracker", null);
            Assert.True(started);
        }

        await using (var db = fx.NewContext())
        {
            var kayit = await db.HesapKontrolKayitlari.AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.Equal(-29_500m, kayit.KasaEtkisiTutari);
            Assert.Equal(day1, kayit.KasaEtkisiIsTarihi);
            Assert.Null(kayit.KasaEtkisiTersDonusIsTarihi);
            Assert.Equal(KayitDurumu.Takipte, kayit.Durum);

            var svc = CreateService(db);
            var day1Totals = await svc.GetDailyFollowTotalsAsync(day1);
            Assert.Equal(-29_500m, day1Totals.TahsilatNet);
            var middleTotals = await svc.GetDailyFollowTotalsAsync(middleDay);
            Assert.Equal(0m, middleTotals.TahsilatNet);
        }
    }

    [Fact]
    public async Task RealRecords_ResolveTracked_WritesReversalOnResolutionDay_SqlServer()
    {
        await using var fx = await LocalDbFixture.CreateAsync("HK_ResolveReversal");
        if (fx == null) return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var originDay = today.AddDays(-3);
        var id = Guid.NewGuid();
        await fx.SeedAsync(Eksik(id, originDay, 100m));

        await using (var db = fx.NewContext())
            Assert.True(await CreateService(db).StartTrackingAsync(id, 51, "tracker", null));

        var resolutionDay = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var db = fx.NewContext())
        {
            Assert.True(await CreateService(db).ResolveTrackedFinancialAsync(id, resolutionDay, 52, "resolver", null));
            var kayit = await db.HesapKontrolKayitlari.AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.NotNull(kayit.CozulmeTarihi);
            Assert.Equal(resolutionDay, kayit.KasaEtkisiTersDonusIsTarihi);
            Assert.Equal(-100m, kayit.KasaEtkisiTutari); // origin değişmedi
            Assert.Equal(originDay, kayit.KasaEtkisiIsTarihi);
        }

        await using (var db = fx.NewContext())
        {
            var svc = CreateService(db);
            var originTotals = await svc.GetDailyFollowTotalsAsync(originDay);
            var resolutionTotals = await svc.GetDailyFollowTotalsAsync(resolutionDay);
            var afterTotals = await svc.GetDailyFollowTotalsAsync(resolutionDay.AddDays(1));

            Assert.Equal(-100m, originTotals.TahsilatNet);
            Assert.Equal(100m, resolutionTotals.TahsilatNet);
            Assert.Equal(0m, afterTotals.TahsilatNet);
        }
    }

    /// <summary>
    /// Revision 3 Manual Resolve Write-BusinessDate Surgical Closure: CozulmeTarihi (lifecycle/audit,
    /// her zaman kayıt anının sistem tarihidir) ve KasaEtkisiTersDonusIsTarihi (financial reversal,
    /// çağıranın sağladığı authoritative WRITE BusinessDate'dir) kasıtlı olarak AYRI semantiklere
    /// sahiptir ve farklı değerler taşıyabilir — burada reversalBusinessDate audit "bugün"ünden
    /// bilinçli olarak farklı (ileri) bir tarih olarak sağlanır ve ikisinin de kalıcı olarak
    /// birbirinden bağımsız kaldığı ispatlanır.
    /// </summary>
    [Fact]
    public async Task RealRecords_ResolveTrackedFinancial_ReversalDateCanDifferFromAuditCozulmeTarihi_SqlServer()
    {
        await using var fx = await LocalDbFixture.CreateAsync("HK_ReversalVsAudit");
        if (fx == null) return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var originDay = today.AddDays(-3);
        var reversalBusinessDate = today.AddDays(5); // authoritative Excel BusinessDate — audit "bugün"den farklı
        var id = Guid.NewGuid();
        await fx.SeedAsync(Eksik(id, originDay, 250m));

        await using (var db = fx.NewContext())
            Assert.True(await CreateService(db).StartTrackingAsync(id, 51, "tracker", null));

        await using (var db = fx.NewContext())
        {
            Assert.True(await CreateService(db).ResolveTrackedFinancialAsync(id, reversalBusinessDate, 52, "resolver", null));
            var kayit = await db.HesapKontrolKayitlari.AsNoTracking().SingleAsync(x => x.Id == id);

            Assert.Equal(today, kayit.CozulmeTarihi); // audit/lifecycle: kayıt anının sistem tarihi
            Assert.Equal(reversalBusinessDate, kayit.KasaEtkisiTersDonusIsTarihi); // financial: çağıranın sağladığı tarih
            Assert.NotEqual(kayit.CozulmeTarihi, kayit.KasaEtkisiTersDonusIsTarihi);
        }
    }

    [Fact]
    public async Task RealRecords_StartTracking_ThenPairResolution_TreatsExistingOriginAsIdempotent_SqlServer()
    {
        await using var fx = await LocalDbFixture.CreateAsync("HK_StartTrackThenPair");
        if (fx == null) return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var olderDate = today.AddDays(-3);
        var laterDate = today;
        var olderId = Guid.NewGuid();
        var laterId = Guid.NewGuid();

        await fx.SeedAsync(
            Eksik(olderId, olderDate, 29_500m),
            Fazla(laterId, laterDate, 29_500m));

        // Origin, StartTrackingAsync üzerinden ÖNCEDEN materialize edilir — karşı taraf (later) henüz
        // eşleştirilmedi.
        await using (var db = fx.NewContext())
            Assert.True(await CreateService(db).StartTrackingAsync(olderId, 51, "tracker", null));

        await using (var db = fx.NewContext())
        {
            var pre = await db.HesapKontrolKayitlari.AsNoTracking().SingleAsync(x => x.Id == olderId);
            Assert.Equal(-29_500m, pre.KasaEtkisiTutari);
            Assert.Equal(olderDate, pre.KasaEtkisiIsTarihi);
            Assert.Null(pre.KasaEtkisiTersDonusIsTarihi);
        }

        // Gerçek pair resolution şimdi çalışır — mevcut origin exact-match idempotent kabul edilmeli,
        // farklı bir değerle üzerine yazılmamalı, conflict oluşmamalı.
        await using (var db = fx.NewContext())
        {
            var result = await CreateService(db).ExecutePairResolutionAsync(
                olderId, olderDate, -29_500m, laterId, laterDate, 29_500m, laterDate);
            Assert.True(result.Success, result.ConflictReason);
        }

        await using (var db = fx.NewContext())
        {
            var older = await db.HesapKontrolKayitlari.AsNoTracking().SingleAsync(x => x.Id == olderId);
            var later = await db.HesapKontrolKayitlari.AsNoTracking().SingleAsync(x => x.Id == laterId);

            Assert.Equal(-29_500m, older.KasaEtkisiTutari); // origin tekrar/farklı yazılmadı
            Assert.Equal(olderDate, older.KasaEtkisiIsTarihi);
            Assert.Equal(laterDate, older.KasaEtkisiTersDonusIsTarihi);
            Assert.Equal(KayitDurumu.Cozuldu, older.Durum);
            Assert.Equal(29_500m, later.KasaEtkisiTutari);
            Assert.Equal(laterDate, later.KasaEtkisiIsTarihi);
            Assert.Equal(laterDate, later.KasaEtkisiTersDonusIsTarihi);
            Assert.Equal(KayitDurumu.Cozuldu, later.Durum);

            var svc = CreateService(db);
            var olderDayTotals = await svc.GetDailyFollowTotalsAsync(olderDate);
            var laterDayTotals = await svc.GetDailyFollowTotalsAsync(laterDate);
            // Duplicate finansal etki yok: origin günü tam olarak bir kez -29500, resolution günü net +29500.
            Assert.Equal(-29_500m, olderDayTotals.TahsilatNet);
            Assert.Equal(29_500m, laterDayTotals.TahsilatNet);
        }
    }

    // ═════════════════════════════════════════════════════════════
    // CAS: NULL→X / X→X idempotent / X→Y fail-closed / mid-pair rollback / concurrency
    // ═════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecutePairResolutionAsync_MidPairConflict_RollsBackBothRecordsFully_SqlServer()
    {
        await using var fx = await LocalDbFixture.CreateAsync("HK_MidPairRollback");
        if (fx == null) return;

        var older = new DateOnly(2026, 7, 1);
        var later = new DateOnly(2026, 7, 5);
        var olderId = Guid.NewGuid();
        var laterId = Guid.NewGuid();

        await fx.SeedAsync(Eksik(olderId, older, 1000m), Fazla(laterId, later, 1000m));

        // later'ı ÖNCEDEN, ExecutePairResolutionAsync'in yazacağından FARKLI bir değerle "kirlet" —
        // 2. adımdaki (later impact) CAS'ın çakışıp reddetmesini garanti eder.
        await using (var corruptDb = fx.NewContext())
        {
            var laterEntity = await corruptDb.HesapKontrolKayitlari.FirstAsync(x => x.Id == laterId);
            laterEntity.SetKasaEtkisi(9999m, later);
            await corruptDb.SaveChangesAsync();
        }

        await using (var db = fx.NewContext())
        {
            var result = await CreateService(db).ExecutePairResolutionAsync(
                olderId, older, -1000m, laterId, later, 1000m, later);
            Assert.False(result.Success);
        }

        await using (var verifyDb = fx.NewContext())
        {
            var olderEntity = await verifyDb.HesapKontrolKayitlari.AsNoTracking().SingleAsync(x => x.Id == olderId);
            var laterEntity = await verifyDb.HesapKontrolKayitlari.AsNoTracking().SingleAsync(x => x.Id == laterId);

            // older'ın 1. adımı "başarıyla" yazılmıştı — ama transaction rollback olduğu için NULL'a dönmeli.
            Assert.Null(olderEntity.KasaEtkisiTutari);
            Assert.Equal(KayitDurumu.Acik, olderEntity.Durum);

            // later'ın ÖNCEDEN kirletilmiş değeri de DEĞİŞMEMİŞ olmalı (rollback her iki kaydı da kapsar).
            Assert.Equal(9999m, laterEntity.KasaEtkisiTutari);
        }
    }

    [Fact]
    public async Task ExecutePairResolutionAsync_ExactRetry_SameValues_IsIdempotentSuccess_SqlServer()
    {
        await using var fx = await LocalDbFixture.CreateAsync("HK_ExactRetry");
        if (fx == null) return;

        var older = new DateOnly(2026, 7, 1);
        var later = new DateOnly(2026, 7, 5);
        var olderId = Guid.NewGuid();
        var laterId = Guid.NewGuid();
        await fx.SeedAsync(Eksik(olderId, older, 1000m), Fazla(laterId, later, 1000m));

        await using (var db = fx.NewContext())
        {
            var first = await CreateService(db).ExecutePairResolutionAsync(
                olderId, older, -1000m, laterId, later, 1000m, later);
            Assert.True(first.Success);
        }

        await using (var db = fx.NewContext())
        {
            // Aynı çift, aynı değerler, ikinci kez — idempotent success (lifecycle CAS Cozuldu->Cozuldu
            // aynı CozulmeTarihi/CozulmeKaynakId ile no-op olarak eşleşir).
            var second = await CreateService(db).ExecutePairResolutionAsync(
                olderId, older, -1000m, laterId, later, 1000m, later);
            Assert.True(second.Success, second.ConflictReason);
        }
    }

    [Fact]
    public async Task ExecutePairResolutionAsync_ConflictingRetry_DifferentValue_FailsClosed_SqlServer()
    {
        await using var fx = await LocalDbFixture.CreateAsync("HK_ConflictRetry");
        if (fx == null) return;

        var older = new DateOnly(2026, 7, 1);
        var later = new DateOnly(2026, 7, 5);
        var olderId = Guid.NewGuid();
        var laterId = Guid.NewGuid();
        await fx.SeedAsync(Eksik(olderId, older, 1000m), Fazla(laterId, later, 1000m));

        await using (var db = fx.NewContext())
        {
            var first = await CreateService(db).ExecutePairResolutionAsync(
                olderId, older, -1000m, laterId, later, 1000m, later);
            Assert.True(first.Success);
        }

        await using (var db = fx.NewContext())
        {
            // Aynı older, farklı impact değeri talep — reddedilmeli.
            var second = await CreateService(db).ExecutePairResolutionAsync(
                olderId, older, -777m, laterId, later, 1000m, later);
            Assert.False(second.Success);
        }
    }

    [Fact]
    public async Task ExecutePairResolutionAsync_TwoDbContexts_SameValue_BothSucceed_SqlServer()
    {
        await using var fx = await LocalDbFixture.CreateAsync("HK_TwoCtxSame");
        if (fx == null) return;

        var older = new DateOnly(2026, 7, 1);
        var later = new DateOnly(2026, 7, 5);
        var olderId = Guid.NewGuid();
        var laterId = Guid.NewGuid();
        await fx.SeedAsync(Eksik(olderId, older, 1000m), Fazla(laterId, later, 1000m));

        async Task<bool> RunAsync()
        {
            await using var db = fx.NewContext();
            var r = await CreateService(db).ExecutePairResolutionAsync(
                olderId, older, -1000m, laterId, later, 1000m, later);
            return r.Success;
        }

        var results = await Task.WhenAll(Task.Run(RunAsync), Task.Run(RunAsync));
        Assert.All(results, Assert.True); // aynı değer → her iki taraf da harmless-race, ikisi de başarı
    }

    [Fact]
    public async Task ExecutePairResolutionAsync_TwoDbContexts_DifferentValue_ExactlyOneWins_SqlServer()
    {
        await using var fx = await LocalDbFixture.CreateAsync("HK_TwoCtxDiff");
        if (fx == null) return;

        var older = new DateOnly(2026, 7, 1);
        var later = new DateOnly(2026, 7, 5);
        var olderId = Guid.NewGuid();
        var laterId = Guid.NewGuid();
        await fx.SeedAsync(Eksik(olderId, older, 1000m), Fazla(laterId, later, 1000m));

        async Task<bool> RunAsync(decimal olderImpact)
        {
            await using var db = fx.NewContext();
            var r = await CreateService(db).ExecutePairResolutionAsync(
                olderId, older, olderImpact, laterId, later, 1000m, later);
            return r.Success;
        }

        var results = await Task.WhenAll(Task.Run(() => RunAsync(-1000m)), Task.Run(() => RunAsync(-1234m)));
        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(1, results.Count(r => !r));
    }

    // ═════════════════════════════════════════════════════════════
    // Stopaj: KasaEtkisi mekanizmasının tamamen dışında tutulur
    // ═════════════════════════════════════════════════════════════

    [Fact]
    public async Task ApprovePotentialMatchAsync_StopajHesapTuru_FailsClosed_NeverGeneratesKasaEtkisi_SqlServer()
    {
        await using var fx = await LocalDbFixture.CreateAsync("HK_StopajGuard");
        if (fx == null) return;

        var origin = new DateOnly(2026, 6, 1);
        var today = new DateOnly(2026, 6, 3);
        var eksikId = Guid.NewGuid();
        var fazlaId = Guid.NewGuid();

        await fx.SeedAsync(
            Eksik(eksikId, origin, 450m, BankaHesapTuru.Stopaj),
            Fazla(fazlaId, today, 450m, BankaHesapTuru.Stopaj));

        await using (var db = fx.NewContext())
        {
            // Gerçek üretim yolu: ApprovePotentialMatchAsync, ExecutePairResolutionAsync'e ulaşmadan
            // önce eksik.HesapTuru == BankaHesapTuru.Stopaj ise reddeder (BankaHesapKontrolService.Commands.cs).
            var approved = await CreateService(db).ApprovePotentialMatchAsync(eksikId, fazlaId, actorUserId: 1, actorUsername: "test-actor");
            Assert.False(approved);
        }

        await using (var verifyDb = fx.NewContext())
        {
            var eksik = await verifyDb.HesapKontrolKayitlari.AsNoTracking().SingleAsync(x => x.Id == eksikId);
            var fazla = await verifyDb.HesapKontrolKayitlari.AsNoTracking().SingleAsync(x => x.Id == fazlaId);

            Assert.Null(eksik.KasaEtkisiTutari);
            Assert.Null(eksik.KasaEtkisiIsTarihi);
            Assert.Null(eksik.KasaEtkisiTersDonusIsTarihi);
            Assert.Null(fazla.KasaEtkisiTutari);

            // Reddedilen çift işlenmemiş kalmalı — Cozuldu'ya geçmemiş.
            Assert.Equal(KayitDurumu.Acik, eksik.Durum);
            Assert.Equal(KayitDurumu.Acik, fazla.Durum);
        }
    }

    // ═════════════════════════════════════════════════════════════
    // Legacy materialization: reciprocal doğrulama, NULL-stays-NULL fail-closed
    // ═════════════════════════════════════════════════════════════

    [Fact]
    public async Task MaterializeLegacy_ReciprocalValid_MaterializesSignedAmount_SqlServer()
    {
        await using var fx = await LocalDbFixture.CreateAsync("HK_LegacyValid");
        if (fx == null) return;

        var cozulmeTarihi = new DateOnly(2026, 5, 20);
        var eksikId = Guid.NewGuid();
        var fazlaId = Guid.NewGuid();

        await fx.SeedAsync(
            TerminalEksik(eksikId, new DateOnly(2026, 5, 18), 640m, fazlaId, cozulmeTarihi),
            TerminalFazla(fazlaId, cozulmeTarihi, 640m, eksikId, cozulmeTarihi));

        await using var db = fx.NewContext();
        var result = await CreateService(db).MaterializeLegacyKasaEtkisiAsync(eksikId);

        Assert.Equal(BankaHesapKontrolService.LegacyMaterializationOutcome.Materialized, result.Outcome);

        await using var verifyDb = fx.NewContext();
        var eksik = await verifyDb.HesapKontrolKayitlari.AsNoTracking().SingleAsync(x => x.Id == eksikId);
        Assert.Equal(-640m, eksik.KasaEtkisiTutari);
    }

    [Fact]
    public async Task MaterializeLegacy_OrphanCounterpart_InsufficientProvenance_LeavesAllThreeFieldsNull_SqlServer()
    {
        await using var fx = await LocalDbFixture.CreateAsync("HK_LegacyOrphan");
        if (fx == null) return;

        var eksikId = Guid.NewGuid();
        var missingCounterpartId = Guid.NewGuid(); // hiç var olmayan / orphan referans

        await fx.SeedAsync(TerminalEksik(eksikId, new DateOnly(2026, 5, 18), 640m, missingCounterpartId, new DateOnly(2026, 5, 20)));

        await using (var db = fx.NewContext())
        {
            var result = await CreateService(db).MaterializeLegacyKasaEtkisiAsync(eksikId);
            Assert.Equal(BankaHesapKontrolService.LegacyMaterializationOutcome.InsufficientProvenance, result.Outcome);
        }

        await using (var verifyDb = fx.NewContext())
        {
            var eksik = await verifyDb.HesapKontrolKayitlari.AsNoTracking().SingleAsync(x => x.Id == eksikId);
            // Helpy override 3: NULL kalmalı — ASLA 0 yazılmamalı.
            Assert.Null(eksik.KasaEtkisiTutari);
            Assert.Null(eksik.KasaEtkisiIsTarihi);
            Assert.Null(eksik.KasaEtkisiTersDonusIsTarihi);
        }

        // GetDailyFollowTotalsAsync bu kayıttan HİÇBİR katkı görmemeli (örtük inference yok).
        await using (var svcDb = fx.NewContext())
        {
            var totals = await CreateService(svcDb).GetDailyFollowTotalsAsync(new DateOnly(2026, 5, 18));
            Assert.Equal(0m, totals.TahsilatNet);
        }

        // Tekrar deneme de non-mutating fail-closed kalmalı (idempotent InsufficientProvenance).
        await using (var retryDb = fx.NewContext())
        {
            var retry = await CreateService(retryDb).MaterializeLegacyKasaEtkisiAsync(eksikId);
            Assert.Equal(BankaHesapKontrolService.LegacyMaterializationOutcome.InsufficientProvenance, retry.Outcome);
        }
    }

    [Fact]
    public async Task MaterializeLegacy_AlreadyMaterialized_IsIdempotentNoOp_SqlServer()
    {
        await using var fx = await LocalDbFixture.CreateAsync("HK_LegacyAlready");
        if (fx == null) return;

        var cozulmeTarihi = new DateOnly(2026, 5, 20);
        var eksikId = Guid.NewGuid();
        var fazlaId = Guid.NewGuid();
        await fx.SeedAsync(
            TerminalEksik(eksikId, new DateOnly(2026, 5, 18), 640m, fazlaId, cozulmeTarihi),
            TerminalFazla(fazlaId, cozulmeTarihi, 640m, eksikId, cozulmeTarihi));

        await using (var db = fx.NewContext())
            await CreateService(db).MaterializeLegacyKasaEtkisiAsync(eksikId);

        await using var db2 = fx.NewContext();
        var second = await CreateService(db2).MaterializeLegacyKasaEtkisiAsync(eksikId);
        Assert.Equal(BankaHesapKontrolService.LegacyMaterializationOutcome.AlreadyMaterialized, second.Outcome);
    }

    // ═════════════════════════════════════════════════════════════
    // Yardımcılar
    // ═════════════════════════════════════════════════════════════

    private static HesapKontrolKaydi Eksik(Guid id, DateOnly tarih, decimal tutar, BankaHesapTuru hesap = BankaHesapTuru.Tahsilat) => new()
    {
        Id = id,
        AnalizTarihi = tarih,
        HesapTuru = hesap,
        Yon = KayitYonu.Eksik,
        Tutar = tutar,
        Sinif = FarkSinifi.Beklenen,
        Durum = KayitDurumu.Acik
    };

    private static HesapKontrolKaydi Fazla(Guid id, DateOnly tarih, decimal tutar, BankaHesapTuru hesap = BankaHesapTuru.Tahsilat) => new()
    {
        Id = id,
        AnalizTarihi = tarih,
        HesapTuru = hesap,
        Yon = KayitYonu.Fazla,
        Tutar = tutar,
        Sinif = FarkSinifi.Beklenen,
        Durum = KayitDurumu.Acik
    };

    private static HesapKontrolKaydi TerminalEksik(Guid id, DateOnly tarih, decimal tutar, Guid cozulmeKaynakId, DateOnly cozulmeTarihi) => new()
    {
        Id = id,
        AnalizTarihi = tarih,
        HesapTuru = BankaHesapTuru.Tahsilat,
        Yon = KayitYonu.Eksik,
        Tutar = tutar,
        Sinif = FarkSinifi.Beklenen,
        Durum = KayitDurumu.Cozuldu,
        CozulmeTarihi = cozulmeTarihi,
        CozulmeKaynakId = cozulmeKaynakId
    };

    private static HesapKontrolKaydi TerminalFazla(Guid id, DateOnly tarih, decimal tutar, Guid cozulmeKaynakId, DateOnly cozulmeTarihi) => new()
    {
        Id = id,
        AnalizTarihi = tarih,
        HesapTuru = BankaHesapTuru.Tahsilat,
        Yon = KayitYonu.Fazla,
        Tutar = tutar,
        Sinif = FarkSinifi.Beklenen,
        Durum = KayitDurumu.Cozuldu,
        CozulmeTarihi = cozulmeTarihi,
        CozulmeKaynakId = cozulmeKaynakId
    };

    private static BankaHesapKontrolService CreateService(KasaManagerDbContext db)
    {
        var sourceResolver = new Mock<IHesapKontrolSourceResolver>();
        sourceResolver
            .Setup(r => r.Validate(It.IsAny<string>(), It.IsAny<DateOnly>()))
            .Returns((string?)null);

        return new BankaHesapKontrolService(
            db,
            Mock.Of<IComparisonService>(),
            Mock.Of<IImportOrchestrator>(),
            sourceResolver.Object,
            NullLogger<BankaHesapKontrolService>.Instance);
    }

    /// <summary>
    /// LocalDB fixture — BankaHesapKontrolServiceCrossDayIdempotencyTests.cs'teki desenin ortak hale
    /// getirilmiş hali. Windows + (localdb)\mssqllocaldb yoksa CreateAsync null döner (test no-op geçer).
    /// </summary>
    private sealed class LocalDbFixture : IAsyncDisposable
    {
        private readonly DbContextOptions<KasaManagerDbContext> _options;
        private LocalDbFixture(DbContextOptions<KasaManagerDbContext> options) => _options = options;

        public static async Task<LocalDbFixture?> CreateAsync(string namePrefix)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return null;

            var dbName = $"{namePrefix}_{Guid.NewGuid():N}";
            var connStr = $"Server=(localdb)\\mssqllocaldb;Database={dbName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";
            var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
                .UseSqlServer(connStr, sql => sql.EnableRetryOnFailure(3))
                .Options;

            try
            {
                await using var setupDb = new KasaManagerDbContext(options);
                await setupDb.Database.EnsureCreatedAsync();
            }
            catch (Exception)
            {
                return null;
            }

            return new LocalDbFixture(options);
        }

        public KasaManagerDbContext NewContext() => new(_options);

        public async Task SeedAsync(params HesapKontrolKaydi[] kayitlar)
        {
            await using var db = NewContext();
            db.HesapKontrolKayitlari.AddRange(kayitlar);
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = NewContext();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
