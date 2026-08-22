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

/// <summary>
/// KasaManager Revision 3 Manual Resolve Write-BusinessDate Surgical Closure — targeted tests for
/// the ResolveTrackedAsync → ResolveTrackedStopajAsync/ResolveTrackedFinancialAsync split and the
/// GetResolveTargetKindAsync server-side classifier. Proves: Stopaj resolves without any financial
/// business date; a missing record is never mistaken for Stopaj via enum-default behavior; and
/// wrong-type/wrong-command routing fails closed server-side in both directions. These tests use
/// the EF Core InMemory provider because none of the paths under test reach ExecuteUpdateAsync-based
/// CAS (the financial success path with a real reversal CAS write is proven separately against real
/// SQL Server in BankaHesapKontrolServiceKasaEtkisiCasTests/HesapKontrolActorSqlServerIntegrationTests).
/// </summary>
public sealed class HesapKontrolResolveTrackedSplitTests
{
    private static readonly DateOnly TestDate = new(2026, 7, 18);

    [Fact]
    public async Task ResolveTrackedStopajAsync_TrackedStopajRecord_ResolvesWithoutAnyFinancialDate()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var record = NewRecord(BankaHesapTuru.Stopaj);
        record.Durum = KayitDurumu.Takipte;
        db.Add(record);
        await db.SaveChangesAsync();

        var result = await service.ResolveTrackedStopajAsync(record.Id, 52, "resolver", null);

        Assert.True(result);
        Assert.Equal(KayitDurumu.Onaylandi, record.Durum);
        Assert.Null(record.KasaEtkisiTutari);
        Assert.Null(record.KasaEtkisiIsTarihi);
        Assert.Null(record.KasaEtkisiTersDonusIsTarihi); // Stopaj never receives a reversal business date
    }

    [Fact]
    public async Task GetResolveTargetKindAsync_TrackedStopajRecord_ReturnsStopaj()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var record = NewRecord(BankaHesapTuru.Stopaj);
        record.Durum = KayitDurumu.Takipte;
        db.Add(record);
        await db.SaveChangesAsync();

        var kind = await service.GetResolveTargetKindAsync(record.Id);

        Assert.Equal(HesapKontrolResolveTargetKind.Stopaj, kind);
    }

    [Fact]
    public async Task GetResolveTargetKindAsync_TrackedFinancialRecord_ReturnsFinancial()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var record = NewRecord(BankaHesapTuru.Tahsilat);
        record.Durum = KayitDurumu.Takipte;
        db.Add(record);
        await db.SaveChangesAsync();

        var kind = await service.GetResolveTargetKindAsync(record.Id);

        Assert.Equal(HesapKontrolResolveTargetKind.Financial, kind);
    }

    [Fact]
    public async Task GetResolveTargetKindAsync_MissingRecord_ReturnsNotFound_NotEnumDefaultFinancial()
    {
        // HesapKontrolResolveTargetKind.NotFound == 0, and Tahsilat (financial) == 0 on BankaHesapTuru
        // — this test proves a missing record cannot silently fall through to Financial via an
        // unguarded projection defaulting to the enum's zero value.
        await using var db = CreateDb();
        var service = CreateService(db);

        var kind = await service.GetResolveTargetKindAsync(Guid.NewGuid());

        Assert.Equal(HesapKontrolResolveTargetKind.NotFound, kind);
    }

    [Fact]
    public async Task GetResolveTargetKindAsync_RecordNotTakipte_ReturnsNotFound()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var record = NewRecord(BankaHesapTuru.Tahsilat);
        record.Durum = KayitDurumu.Acik;
        db.Add(record);
        await db.SaveChangesAsync();

        var kind = await service.GetResolveTargetKindAsync(record.Id);

        Assert.Equal(HesapKontrolResolveTargetKind.NotFound, kind);
    }

    [Fact]
    public async Task ResolveTrackedStopajAsync_FinancialRecordSentToStopajCommand_FailsClosed()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var record = NewRecord(BankaHesapTuru.Tahsilat);
        record.Durum = KayitDurumu.Takipte;
        db.Add(record);
        await db.SaveChangesAsync();

        var result = await service.ResolveTrackedStopajAsync(record.Id, 52, "resolver", null);

        Assert.False(result);
        Assert.Equal(KayitDurumu.Takipte, record.Durum); // untouched
    }

    [Fact]
    public async Task ResolveTrackedFinancialAsync_StopajRecordSentToFinancialCommand_FailsClosed()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var record = NewRecord(BankaHesapTuru.Stopaj);
        record.Durum = KayitDurumu.Takipte;
        db.Add(record);
        await db.SaveChangesAsync();

        var result = await service.ResolveTrackedFinancialAsync(record.Id, TestDate, 52, "resolver", null);

        Assert.False(result);
        Assert.Equal(KayitDurumu.Takipte, record.Durum); // untouched
        Assert.Null(record.KasaEtkisiTersDonusIsTarihi);
    }

    [Fact]
    public async Task InteractiveCommands_RejectNonPositiveActorBeforeDatabaseAccess()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ResolveTrackedStopajAsync(id, 0, "user", null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ResolveTrackedFinancialAsync(id, TestDate, -1, "user", null));

        Assert.Empty(db.HesapKontrolKayitlari);
    }

    private static HesapKontrolKaydi NewRecord(BankaHesapTuru hesapTuru) => new()
    {
        AnalizTarihi = TestDate,
        HesapTuru = hesapTuru,
        Yon = KayitYonu.Eksik,
        Tutar = 125.50m,
        Sinif = FarkSinifi.Bilinmeyen,
        Durum = KayitDurumu.Acik,
        DosyaNo = $"FILE-{Guid.NewGuid():N}"
    };

    private static KasaManagerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseInMemoryDatabase($"ResolveSplit_{Guid.NewGuid():N}")
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
