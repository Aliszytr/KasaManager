using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KasaManager.Tests.Infrastructure;

public sealed class BankaHesapKontrolServiceDecisionMemoryTests
{
    [Fact]
    public async Task GetHistoryAsync_WithoutStatusFilter_IncludesCancelledRecords()
    {
        await using var db = CreateDb();
        var tarih = new DateOnly(2026, 6, 1);
        db.HesapKontrolKayitlari.Add(CreateRecord(tarih, 2250m, KayitDurumu.Iptal));
        await db.SaveChangesAsync();

        var result = await CreateService(db).GetHistoryAsync(tarih, tarih);

        Assert.Single(result);
        Assert.Equal(KayitDurumu.Iptal, result[0].Durum);
    }

    [Fact]
    public async Task GetDashboardForDateAsync_ReturnsProcessedSummary_WithoutCountingCancelledAsOpen()
    {
        await using var db = CreateDb();
        var tarih = new DateOnly(2026, 6, 1);
        db.HesapKontrolKayitlari.AddRange(
            CreateRecord(tarih, 2250m, KayitDurumu.Iptal),
            CreateRecord(tarih, 72454.80m, KayitDurumu.Cozuldu),
            CreateRecord(tarih, 100m, KayitDurumu.Onaylandi),
            CreateRecord(tarih, 200m, KayitDurumu.Acik));
        await db.SaveChangesAsync();

        var snapshot = await CreateService(db).GetDashboardForDateAsync(tarih);

        Assert.Equal(4, snapshot.Summary.TotalCount);
        Assert.Equal(1, snapshot.Summary.AcikCount);
        Assert.Equal(1, snapshot.Summary.IptalCount);
        Assert.Equal(1, snapshot.Summary.CozulduCount);
        Assert.Equal(1, snapshot.Summary.OnaylandiCount);
        Assert.Equal(3, snapshot.Summary.ProcessedCount);
        Assert.Equal(1, snapshot.Dashboard.AcikKayitSayisi);
    }

    [Fact]
    public async Task EnrichComparisonDecisionMemoryAsync_UsesLatestRecordInside90DayWindow_WithoutWriting()
    {
        await using var db = CreateDb();
        var tarih = new DateOnly(2026, 6, 1);
        db.HesapKontrolKayitlari.AddRange(
            CreateRecord(tarih.AddDays(-10), 2250m, KayitDurumu.Cozuldu),
            CreateRecord(tarih.AddDays(-1), 2250m, KayitDurumu.Iptal));
        await db.SaveChangesAsync();

        var report = CreateReport(2250m);
        var beforeCount = await db.HesapKontrolKayitlari.CountAsync();

        await CreateService(db).EnrichComparisonDecisionMemoryAsync(
            report, BankaHesapTuru.Tahsilat, tarih);

        Assert.Equal(KayitDurumu.Iptal, report.SurplusBankaRecords[0].HesapKontrolDurumu);
        Assert.Equal(tarih.AddDays(-1), report.SurplusBankaRecords[0].HesapKontrolAnalizTarihi);
        Assert.Equal(beforeCount, await db.HesapKontrolKayitlari.CountAsync());
        Assert.Equal(2250m, report.SurplusAmount);
    }

    [Fact]
    public async Task EnrichComparisonDecisionMemoryAsync_IgnoresRecordOutside90DayWindow()
    {
        await using var db = CreateDb();
        var tarih = new DateOnly(2026, 6, 1);
        db.HesapKontrolKayitlari.Add(CreateRecord(tarih.AddDays(-91), 2250m, KayitDurumu.Iptal));
        await db.SaveChangesAsync();

        var report = CreateReport(2250m);

        await CreateService(db).EnrichComparisonDecisionMemoryAsync(
            report, BankaHesapTuru.Tahsilat, tarih);

        Assert.Null(report.SurplusBankaRecords[0].HesapKontrolDurumu);
    }

    [Fact]
    public async Task EnrichComparisonDecisionMemoryAsync_EnrichesMissingRecordByFileAndUnit()
    {
        await using var db = CreateDb();
        var tarih = new DateOnly(2026, 6, 1);
        db.HesapKontrolKayitlari.Add(new HesapKontrolKaydi
        {
            AnalizTarihi = tarih,
            HesapTuru = BankaHesapTuru.Harc,
            Yon = KayitYonu.Eksik,
            Tutar = 750m,
            DosyaNo = "2026/77",
            BirimAdi = "Ankara 2",
            Sinif = FarkSinifi.Askida,
            Durum = KayitDurumu.Takipte
        });
        await db.SaveChangesAsync();

        var report = new ComparisonReport
        {
            Type = ComparisonType.HarcamaHarc,
            GeneratedAt = DateTime.UtcNow,
            MissingBankaRecords =
            [
                new MissingBankaRecord
                {
                    DosyaNo = "2026/77",
                    BirimAdi = "Ankara 2",
                    Miktar = 750m
                }
            ]
        };

        await CreateService(db).EnrichComparisonDecisionMemoryAsync(
            report, BankaHesapTuru.Harc, tarih);

        Assert.Equal(KayitDurumu.Takipte, report.MissingBankaRecords[0].HesapKontrolDurumu);
    }

    private static KasaManagerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseInMemoryDatabase($"DecisionMemory_{Guid.NewGuid():N}")
            .Options;
        return new KasaManagerDbContext(options);
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

    private static HesapKontrolKaydi CreateRecord(
        DateOnly tarih,
        decimal tutar,
        KayitDurumu durum)
    {
        return new HesapKontrolKaydi
        {
            AnalizTarihi = tarih,
            HesapTuru = BankaHesapTuru.Tahsilat,
            Yon = KayitYonu.Fazla,
            Tutar = tutar,
            Aciklama = $"Banka kaydi {tutar:N2}",
            Sinif = FarkSinifi.Bilinmeyen,
            Durum = durum,
            Notlar = "KasaPreview anomali kartindan yok sayildi"
        };
    }

    private static ComparisonReport CreateReport(decimal tutar)
    {
        return new ComparisonReport
        {
            Type = ComparisonType.TahsilatMasraf,
            GeneratedAt = DateTime.UtcNow,
            SurplusBankaCount = 1,
            SurplusAmount = tutar,
            SurplusBankaRecords =
            [
                new UnmatchedBankaRecord
                {
                    Tutar = tutar,
                    Aciklama = $"Banka kaydi {tutar:N2}"
                }
            ]
        };
    }
}
