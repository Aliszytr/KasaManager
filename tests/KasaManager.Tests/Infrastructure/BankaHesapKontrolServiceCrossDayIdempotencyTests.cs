using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KasaManager.Tests.Infrastructure;

public sealed class BankaHesapKontrolServiceCrossDayIdempotencyTests
{
    [Fact]
    public async Task CrossDayReconcileAsync_SamePairSecondRun_DoesNotReturnDuplicateMatchOrAppendNotes_SqlServer()
    {
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            return;

        var testDbName = $"CrossDayIdempotency_{Guid.NewGuid():N}";
        var connStr = $"Server=(localdb)\\mssqllocaldb;Database={testDbName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseSqlServer(connStr, sql => sql.EnableRetryOnFailure(3))
            .Options;

        bool localDbAvailable;
        await using (var setupDb = new KasaManagerDbContext(options))
        {
            try
            {
                await setupDb.Database.EnsureCreatedAsync();
                localDbAvailable = true;
            }
            catch (Exception)
            {
                localDbAvailable = false;
            }
        }

        if (!localDbAvailable)
            return;

        try
        {
            var bugun = new DateOnly(2026, 5, 13);
            var eksikId = Guid.NewGuid();
            var fazlaId = Guid.NewGuid();

            await using (var seedDb = new KasaManagerDbContext(options))
            {
                seedDb.HesapKontrolKayitlari.AddRange(
                    new HesapKontrolKaydi
                    {
                        Id = eksikId,
                        AnalizTarihi = bugun.AddDays(-1),
                        HesapTuru = BankaHesapTuru.Tahsilat,
                        Yon = KayitYonu.Eksik,
                        Tutar = 1250m,
                        DosyaNo = "2026/42",
                        BirimAdi = "Ankara 1",
                        Sinif = FarkSinifi.Askida,
                        Durum = KayitDurumu.Acik
                    },
                    new HesapKontrolKaydi
                    {
                        Id = fazlaId,
                        AnalizTarihi = bugun,
                        HesapTuru = BankaHesapTuru.Tahsilat,
                        Yon = KayitYonu.Fazla,
                        Tutar = 1250m,
                        Aciklama = "Banka iade 2026/42 Ankara 1",
                        Sinif = FarkSinifi.Askida,
                        Durum = KayitDurumu.Acik
                    });

                await seedDb.SaveChangesAsync();
            }

            await using (var firstDb = new KasaManagerDbContext(options))
            {
                var firstResult = await CreateService(firstDb).CrossDayReconcileAsync(bugun);
                Assert.Single(firstResult.KesirEslesmeler);
                Assert.Empty(firstResult.PotansiyelEslesmeler);
            }

            string? eksikNotesAfterFirst;
            string? fazlaNotesAfterFirst;
            await using (var checkDb = new KasaManagerDbContext(options))
            {
                var eksik = await checkDb.HesapKontrolKayitlari.AsNoTracking().SingleAsync(x => x.Id == eksikId);
                var fazla = await checkDb.HesapKontrolKayitlari.AsNoTracking().SingleAsync(x => x.Id == fazlaId);

                Assert.Equal(KayitDurumu.Cozuldu, eksik.Durum);
                Assert.Equal(KayitDurumu.Cozuldu, fazla.Durum);
                Assert.Equal(fazlaId, eksik.CozulmeKaynakId);
                Assert.Equal(eksikId, fazla.CozulmeKaynakId);

                eksikNotesAfterFirst = eksik.Notlar;
                fazlaNotesAfterFirst = fazla.Notlar;
                Assert.Contains("Eşleşen fazla", eksikNotesAfterFirst);
                Assert.Contains("Eşleşen eksik", fazlaNotesAfterFirst);
            }

            await using (var secondDb = new KasaManagerDbContext(options))
            {
                var secondResult = await CreateService(secondDb).CrossDayReconcileAsync(bugun);
                Assert.Empty(secondResult.KesirEslesmeler);
                Assert.Empty(secondResult.PotansiyelEslesmeler);
            }

            await using (var finalDb = new KasaManagerDbContext(options))
            {
                var eksik = await finalDb.HesapKontrolKayitlari.AsNoTracking().SingleAsync(x => x.Id == eksikId);
                var fazla = await finalDb.HesapKontrolKayitlari.AsNoTracking().SingleAsync(x => x.Id == fazlaId);

                Assert.Equal(KayitDurumu.Cozuldu, eksik.Durum);
                Assert.Equal(KayitDurumu.Cozuldu, fazla.Durum);
                Assert.Equal(eksikNotesAfterFirst, eksik.Notlar);
                Assert.Equal(fazlaNotesAfterFirst, fazla.Notlar);
            }
        }
        finally
        {
            await using var cleanDb = new KasaManagerDbContext(options);
            await cleanDb.Database.EnsureDeletedAsync();
        }
    }


    [Fact]
    public async Task CrossDayReconcileAsync_ParallelServices_OnlyOneCallerReturnsMatch_SqlServer()
    {
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            return;

        var testDbName = $"CrossDayParallel_{Guid.NewGuid():N}";
        var connStr = $"Server=(localdb)\\mssqllocaldb;Database={testDbName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseSqlServer(connStr, sql => sql.EnableRetryOnFailure(3))
            .Options;

        bool localDbAvailable;
        await using (var setupDb = new KasaManagerDbContext(options))
        {
            try
            {
                await setupDb.Database.EnsureCreatedAsync();
                localDbAvailable = true;
            }
            catch (Exception)
            {
                localDbAvailable = false;
            }
        }

        if (!localDbAvailable)
            return;

        try
        {
            var bugun = new DateOnly(2026, 5, 13);
            var eksikId = Guid.NewGuid();
            var fazlaId = Guid.NewGuid();

            await using (var seedDb = new KasaManagerDbContext(options))
            {
                seedDb.HesapKontrolKayitlari.AddRange(
                    new HesapKontrolKaydi
                    {
                        Id = eksikId,
                        AnalizTarihi = bugun.AddDays(-1),
                        HesapTuru = BankaHesapTuru.Harc,
                        Yon = KayitYonu.Eksik,
                        Tutar = 750m,
                        DosyaNo = "2026/77",
                        BirimAdi = "Izmir 2",
                        Sinif = FarkSinifi.Askida,
                        Durum = KayitDurumu.Acik
                    },
                    new HesapKontrolKaydi
                    {
                        Id = fazlaId,
                        AnalizTarihi = bugun,
                        HesapTuru = BankaHesapTuru.Harc,
                        Yon = KayitYonu.Fazla,
                        Tutar = 750m,
                        Aciklama = "Banka iade 2026/77 Izmir 2",
                        Sinif = FarkSinifi.Askida,
                        Durum = KayitDurumu.Acik
                    });

                await seedDb.SaveChangesAsync();
            }

            async Task<CrossDayResult> RunAsync()
            {
                await using var db = new KasaManagerDbContext(options);
                return await CreateService(db).CrossDayReconcileAsync(bugun);
            }

            var results = await Task.WhenAll(Task.Run(RunAsync), Task.Run(RunAsync));

            Assert.Equal(1, results.Count(r => r.KesirEslesmeler.Count == 1));
            Assert.Equal(1, results.Count(r => r.KesirEslesmeler.Count == 0));

            await using var finalDb = new KasaManagerDbContext(options);
            var records = await finalDb.HesapKontrolKayitlari.AsNoTracking().ToListAsync();
            Assert.Equal(2, records.Count);
            Assert.All(records, r => Assert.Equal(KayitDurumu.Cozuldu, r.Durum));
            Assert.Equal(fazlaId, records.Single(r => r.Id == eksikId).CozulmeKaynakId);
            Assert.Equal(eksikId, records.Single(r => r.Id == fazlaId).CozulmeKaynakId);
        }
        finally
        {
            await using var cleanDb = new KasaManagerDbContext(options);
            await cleanDb.Database.EnsureDeletedAsync();
        }
    }
    [Fact]
    public async Task CrossDayReconcileAsync_ParallelServices_LoserDoesNotReportAsPotentialMatch_SqlServer()
    {
        // Helpy closure task 3 regresyon kanıtı: Tam-güven eşleşmede CAS reddi (gerçek concurrency
        // çakışması) artık business-confidence (potansiyel) sonucu olarak YANLIŞ raporlanmıyor.
        // Kaybeden concurrent çağrı ne KesirEslesmeler'de ne de PotansiyelEslesmeler'de görünmeli —
        // yalnızca log'lanır, kayıt "Açık" kalır ve bir sonraki run'da yeniden değerlendirilir.
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            return;

        var testDbName = $"CrossDayCasLoser_{Guid.NewGuid():N}";
        var connStr = $"Server=(localdb)\\mssqllocaldb;Database={testDbName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseSqlServer(connStr, sql => sql.EnableRetryOnFailure(3))
            .Options;

        bool localDbAvailable;
        await using (var setupDb = new KasaManagerDbContext(options))
        {
            try
            {
                await setupDb.Database.EnsureCreatedAsync();
                localDbAvailable = true;
            }
            catch (Exception)
            {
                localDbAvailable = false;
            }
        }

        if (!localDbAvailable)
            return;

        try
        {
            var bugun = new DateOnly(2026, 5, 13);
            var eksikId = Guid.NewGuid();
            var fazlaId = Guid.NewGuid();

            await using (var seedDb = new KasaManagerDbContext(options))
            {
                seedDb.HesapKontrolKayitlari.AddRange(
                    new HesapKontrolKaydi
                    {
                        Id = eksikId,
                        AnalizTarihi = bugun.AddDays(-1),
                        HesapTuru = BankaHesapTuru.Tahsilat,
                        Yon = KayitYonu.Eksik,
                        Tutar = 3300m,
                        DosyaNo = "2026/99",
                        BirimAdi = "Bursa 3",
                        Sinif = FarkSinifi.Askida,
                        Durum = KayitDurumu.Acik
                    },
                    new HesapKontrolKaydi
                    {
                        Id = fazlaId,
                        AnalizTarihi = bugun,
                        HesapTuru = BankaHesapTuru.Tahsilat,
                        Yon = KayitYonu.Fazla,
                        Tutar = 3300m,
                        Aciklama = "Banka iade 2026/99 Bursa 3",
                        Sinif = FarkSinifi.Askida,
                        Durum = KayitDurumu.Acik
                    });

                await seedDb.SaveChangesAsync();
            }

            async Task<CrossDayResult> RunAsync()
            {
                await using var db = new KasaManagerDbContext(options);
                return await CreateService(db).CrossDayReconcileAsync(bugun);
            }

            var results = await Task.WhenAll(Task.Run(RunAsync), Task.Run(RunAsync));

            Assert.Equal(1, results.Count(r => r.KesirEslesmeler.Count == 1));
            var loser = Assert.Single(results, r => r.KesirEslesmeler.Count == 0);

            // Asıl regresyon kanıtı: kaybeden, CAS reddini potansiyel eşleşme olarak YANLIŞ raporlamıyor.
            Assert.Empty(loser.PotansiyelEslesmeler);
        }
        finally
        {
            await using var cleanDb = new KasaManagerDbContext(options);
            await cleanDb.Database.EnsureDeletedAsync();
        }
    }

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
}
