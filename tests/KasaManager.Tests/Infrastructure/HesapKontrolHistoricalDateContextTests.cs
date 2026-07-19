using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KasaManager.Tests.Infrastructure;

public sealed class HesapKontrolHistoricalDateContextTests
{
    private static readonly DateOnly HistoricalDate = new(2026, 7, 13);
    private static readonly DateOnly LaterDate = new(2026, 7, 17);

    [Fact]
    public async Task GetDashboardAsync_SelectedHistoricalDate_UsesSelectedDateForResolvedCount()
    {
        await using var db = CreateDb();
        db.HesapKontrolKayitlari.AddRange(
            CreateRecord(HistoricalDate, KayitDurumu.Cozuldu, cozulmeTarihi: HistoricalDate),
            CreateRecord(HistoricalDate, KayitDurumu.Onaylandi, cozulmeTarihi: LaterDate),
            CreateRecord(LaterDate, KayitDurumu.Cozuldu, cozulmeTarihi: HistoricalDate));
        await db.SaveChangesAsync();

        var dashboard = await CreateService(db).GetDashboardAsync(HistoricalDate);

        Assert.Equal(1, dashboard.BugunCozulenSayisi);
    }

    [Fact]
    public async Task GetDashboardAsync_SelectedHistoricalDate_ExcludesRecordsCreatedAfterSelectedDate()
    {
        await using var db = CreateDb();
        db.HesapKontrolKayitlari.AddRange(
            CreateRecord(HistoricalDate, KayitDurumu.Takipte, 125m),
            CreateRecord(LaterDate, KayitDurumu.Takipte, 875m));
        await db.SaveChangesAsync();

        var dashboard = await CreateService(db).GetDashboardAsync(HistoricalDate);

        Assert.Equal(1, dashboard.TakipteSayisi);
        Assert.Equal(125m, dashboard.TakipteEksikToplam);
    }

    [Fact]
    public async Task GetTrackedItemsAsync_WithHistoricalDate_ExcludesLaterRecords()
    {
        await using var db = CreateDb();
        var historical = CreateRecord(HistoricalDate, KayitDurumu.Takipte, 125m);
        var later = CreateRecord(LaterDate, KayitDurumu.Takipte, 875m);
        db.HesapKontrolKayitlari.AddRange(historical, later);
        await db.SaveChangesAsync();

        var records = await CreateService(db).GetTrackedItemsAsync(null, HistoricalDate);

        Assert.Collection(records, item => Assert.Equal(historical.Id, item.Id));
        Assert.DoesNotContain(records, item => item.Id == later.Id);
    }

    [Fact]
    public async Task StopajSummary_SelectedHistoricalDate_DoesNotUseLaterRecord()
    {
        await using var db = CreateDb();
        db.HesapKontrolKayitlari.AddRange(
            CreateRecord(HistoricalDate, KayitDurumu.Acik, 130m,
                BankaHesapTuru.Stopaj, "STOPAJ_BEKLIYOR", "13 Temmuz stopaj"),
            CreateRecord(LaterDate, KayitDurumu.Acik, 170m,
                BankaHesapTuru.Stopaj, "STOPAJ_VIRMAN_OK", "17 Temmuz stopaj"));
        await db.SaveChangesAsync();

        var dashboard = await CreateService(db).GetDashboardAsync(HistoricalDate);

        var stopaj = Assert.IsType<StopajVirmanDurum>(dashboard.LastStopajDurum);
        Assert.Equal(130m, stopaj.BeklenenTutar);
        Assert.Equal("13 Temmuz stopaj", stopaj.Mesaj);
        Assert.False(stopaj.VirmanYapildiMi);
    }

    [Fact]
    public async Task GetTrackingSummaryAsync_SelectedHistoricalDate_UsesBoundedCurrentStateUniverse()
    {
        await using var db = CreateDb();
        db.HesapKontrolKayitlari.AddRange(
            CreateRecord(HistoricalDate, KayitDurumu.Takipte, 125m,
                takipBaslangicTarihi: HistoricalDate.AddDays(-2)),
            CreateRecord(LaterDate, KayitDurumu.Takipte, 875m,
                takipBaslangicTarihi: LaterDate),
            CreateRecord(HistoricalDate, KayitDurumu.Cozuldu, 50m,
                cozulmeTarihi: HistoricalDate,
                takipBaslangicTarihi: HistoricalDate.AddDays(-1)),
            CreateRecord(HistoricalDate, KayitDurumu.Cozuldu, 70m,
                cozulmeTarihi: LaterDate,
                takipBaslangicTarihi: HistoricalDate));
        await db.SaveChangesAsync();

        var summary = await CreateService(db).GetTrackingSummaryAsync(HistoricalDate);

        Assert.Equal(1, summary.AktifTakipSayisi);
        Assert.Equal(125m, summary.ToplamEksik);
        Assert.Equal(2, summary.EnEskiGun);
        Assert.Collection(summary.BugunCozulenler,
            item => Assert.Equal(HistoricalDate, item.CozulmeTarihi));
        Assert.Equal(50m, summary.BugunCozulenToplam);
    }

    private static HesapKontrolKaydi CreateRecord(
        DateOnly analizTarihi,
        KayitDurumu durum,
        decimal tutar = 100m,
        BankaHesapTuru hesapTuru = BankaHesapTuru.Tahsilat,
        string? tespitEdilenTip = null,
        string? aciklama = null,
        DateOnly? cozulmeTarihi = null,
        DateOnly? takipBaslangicTarihi = null)
        => new()
        {
            AnalizTarihi = analizTarihi,
            HesapTuru = hesapTuru,
            Yon = KayitYonu.Eksik,
            Tutar = tutar,
            Sinif = FarkSinifi.Bilinmeyen,
            Durum = durum,
            TespitEdilenTip = tespitEdilenTip,
            Aciklama = aciklama,
            CozulmeTarihi = cozulmeTarihi,
            TakipBaslangicTarihi = takipBaslangicTarihi
        };

    private static KasaManagerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseInMemoryDatabase($"HistoricalDateContext_{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new KasaManagerDbContext(options);
    }

    private static BankaHesapKontrolService CreateService(KasaManagerDbContext db)
        => new(
            db,
            Mock.Of<IComparisonService>(),
            Mock.Of<IImportOrchestrator>(),
            Mock.Of<IHesapKontrolSourceResolver>(),
            NullLogger<BankaHesapKontrolService>.Instance);
}
