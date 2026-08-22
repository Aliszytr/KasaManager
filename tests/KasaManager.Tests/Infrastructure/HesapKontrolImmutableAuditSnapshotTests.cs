using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Orchestration;
using KasaManager.Application.Services;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KasaManager.Tests.Infrastructure;

public sealed class HesapKontrolImmutableAuditSnapshotTests
{
    private static readonly DateOnly AuditDate = new(2026, 7, 18);

    [Fact]
    public async Task DailyFollowTotals_ActiveCrossDayRecordsRemainAppliedForTahsilatAndHarc()
    {
        // Revision 3 ayrık iki olaylı model: GetDailyFollowTotalsAsync ARTIK yalnızca o günün
        // KasaEtkisiIsTarihi/KasaEtkisiTersDonusIsTarihi'ne sahip kayıtlarını okur (Durum/Takipte
        // "standing balance" semantiği kaldırıldı) — bu yüzden authoritative alanlar doğrudan
        // seed edilir (Helpy: "helper/fixture'da authoritative persisted alanları doğrudan seed et").
        // GetActiveFollowTotalsAsync (cumulative) hâlâ eski Durum/Takipte tabanlı "aktif havuz"
        // semantiğini kullanıyor — o kısım DEĞİŞMEDİ.
        var day1 = new DateOnly(2070, 3, 11);
        var day2 = day1.AddDays(1);
        await using var db = CreateDb();
        var day1Tahsilat = NewRecord(day1, BankaHesapTuru.Tahsilat, KayitYonu.Eksik, 3_000m,
            KayitDurumu.Takipte, FarkSinifi.Bilinmeyen, trackingDate: day1);
        day1Tahsilat.SetKasaEtkisi(-3_000m, day1);
        var day2Tahsilat = NewRecord(day2, BankaHesapTuru.Tahsilat, KayitYonu.Eksik, 29_000m,
            KayitDurumu.Takipte, FarkSinifi.Bilinmeyen, trackingDate: day2);
        day2Tahsilat.SetKasaEtkisi(-29_000m, day2);
        var day1Harc = NewRecord(day1, BankaHesapTuru.Harc, KayitYonu.Eksik, 4_000m,
            KayitDurumu.Takipte, FarkSinifi.Bilinmeyen, trackingDate: day1);
        day1Harc.SetKasaEtkisi(-4_000m, day1);
        var day2Harc = NewRecord(day2, BankaHesapTuru.Harc, KayitYonu.Eksik, 7_000m,
            KayitDurumu.Takipte, FarkSinifi.Bilinmeyen, trackingDate: day2);
        day2Harc.SetKasaEtkisi(-7_000m, day2);
        db.AddRange(day1Tahsilat, day2Tahsilat, day1Harc, day2Harc);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var cumulative = await service.GetActiveFollowTotalsAsync(day2);
        var daily = await service.GetDailyFollowTotalsAsync(day2);

        // Cumulative/active pool: her iki günün de hâlâ açık (Takipte) kayıtlarının toplamı — değişmedi.
        Assert.Equal(-32_000m, cumulative.TahsilatNet);
        Assert.Equal(-11_000m, cumulative.HarcNet);
        // Daily: yalnızca day2'nin KENDİ origin günü olan kayıtları (day1'inkiler day1'de kalır).
        Assert.Equal(-29_000m, daily.TahsilatNet);
        Assert.Equal(-7_000m, daily.HarcNet);
        Assert.Equal(2, daily.KayitSayisi);
    }

    [Fact]
    public async Task DailyFollowTotals_CrossDayResolution_AppliesCompensationOnce()
    {
        // Revision 3 ayrık iki olaylı model: authoritative alanlar doğrudan seed edilir (production
        // command üzerinden değil — bu InMemory fixture zaten ExecuteUpdateAsync'i desteklemiyor).
        // Origin (day1) etkiyi bir kez uygular; reversal (day2) tam tersini uygular; sonrasında (day3)
        // katkı sıfırdır — "compensation exactly once" canonical kontratı budur.
        var day1 = new DateOnly(2026, 7, 29);
        var day2 = day1.AddDays(1);
        var day3 = day2.AddDays(1);
        await using var db = CreateDb();
        var tracked = NewRecord(
            day1, BankaHesapTuru.Tahsilat, KayitYonu.Eksik, 29_500m,
            KayitDurumu.Takipte, FarkSinifi.Bilinmeyen, trackingDate: day1);
        tracked.SetKasaEtkisi(-29_500m, day1);
        db.Add(tracked);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        // Origin günü: etki tam olarak bir kez, kendi gününde görünür.
        var originDayTotals = await service.GetDailyFollowTotalsAsync(day1);
        Assert.Equal(-29_500m, originDayTotals.TahsilatNet);

        // Reversal henüz set edilmedi: ara gün (day2) hiçbir katkı vermez.
        var beforeReversal = await service.GetDailyFollowTotalsAsync(day2);
        Assert.Equal(0m, beforeReversal.TahsilatNet);

        tracked.SetKasaEtkisiTersDonus(day2);
        await db.SaveChangesAsync();

        var resolutionDay = await service.GetDailyFollowTotalsAsync(day2);
        var resolutionDayRefresh = await service.GetDailyFollowTotalsAsync(day2);
        var followingDay = await service.GetDailyFollowTotalsAsync(day3);

        Assert.Equal(29_500m, resolutionDay.TahsilatNet);
        Assert.Equal(resolutionDay, resolutionDayRefresh);
        Assert.Equal(0m, followingDay.TahsilatNet);
        Assert.Equal(0, followingDay.KayitSayisi);
    }

    [Fact]
    public async Task DailyFollowTotals_SameDayTrackedAndResolved_DoesNotCompensate()
    {
        var day = new DateOnly(2026, 7, 30);
        await using var db = CreateDb();
        db.Add(NewRecord(
            day, BankaHesapTuru.Tahsilat, KayitYonu.Eksik, 29_500m,
            KayitDurumu.Cozuldu, FarkSinifi.Bilinmeyen,
            trackingDate: day, resolutionDate: day));
        await db.SaveChangesAsync();

        var daily = await CreateService(db).GetDailyFollowTotalsAsync(day);

        Assert.Equal(0m, daily.TahsilatNet);
        Assert.Equal(0, daily.KayitSayisi);
    }

    [Theory]
    [InlineData(BankaHesapTuru.Tahsilat, KayitYonu.Eksik, 100, 0)]
    [InlineData(BankaHesapTuru.Tahsilat, KayitYonu.Fazla, -100, 0)]
    [InlineData(BankaHesapTuru.Harc, KayitYonu.Eksik, 0, 100)]
    [InlineData(BankaHesapTuru.Harc, KayitYonu.Fazla, 0, -100)]
    public async Task DailyFollowTotals_ResolutionCompensation_IsMathematicalInverse(
        BankaHesapTuru account,
        KayitYonu direction,
        decimal expectedTahsilat,
        decimal expectedHarc)
    {
        var day = new DateOnly(2026, 7, 30);
        await using var db = CreateDb();
        var record = NewRecord(
            day.AddDays(-1), account, direction, 100m,
            KayitDurumu.Onaylandi, FarkSinifi.Bilinmeyen,
            trackingDate: day.AddDays(-1), resolutionDate: day);
        // KasaEtkisiTutari zaten imzalı (Fazla:+Tutar, Eksik:-Tutar) — canonical two-event model.
        var originImpact = direction == KayitYonu.Fazla ? 100m : -100m;
        record.SetKasaEtkisi(originImpact, day.AddDays(-1));
        record.SetKasaEtkisiTersDonus(day);
        db.Add(record);
        await db.SaveChangesAsync();

        var daily = await CreateService(db).GetDailyFollowTotalsAsync(day);

        Assert.Equal(expectedTahsilat, daily.TahsilatNet);
        Assert.Equal(expectedHarc, daily.HarcNet);
    }

    [Fact]
    public async Task ImmutableSnapshot_UsesCanonicalPredicatesAndPreservesEveryScalarParityRule()
    {
        await using var db = CreateDb();
        var activeExpected = NewRecord(
            AuditDate, BankaHesapTuru.Tahsilat, KayitYonu.Eksik, 20m,
            KayitDurumu.Takipte, FarkSinifi.Beklenen,
            type: "EFT_OTOMATIK_IADE", trackingDate: AuditDate);
        var activeUnexpected = NewRecord(
            AuditDate, BankaHesapTuru.Harc, KayitYonu.Fazla, 30m,
            KayitDurumu.Acik, FarkSinifi.Bilinmeyen);
        var previousTracked = NewRecord(
            AuditDate.AddDays(-2), BankaHesapTuru.Tahsilat, KayitYonu.Eksik, 100m,
            KayitDurumu.Takipte, FarkSinifi.Bilinmeyen,
            trackingDate: AuditDate.AddDays(-2));
        var previousOpen = NewRecord(
            AuditDate.AddDays(-1), BankaHesapTuru.Tahsilat, KayitYonu.Fazla, 40m,
            KayitDurumu.Acik, FarkSinifi.Bilinmeyen);
        var resolvedTracked = NewRecord(
            AuditDate.AddDays(-1), BankaHesapTuru.Harc, KayitYonu.Eksik, 50m,
            KayitDurumu.Cozuldu, FarkSinifi.Bilinmeyen,
            trackingDate: AuditDate.AddDays(-1), resolutionDate: AuditDate);
        var reconciliationTahsilat = NewRecord(
            AuditDate, BankaHesapTuru.Tahsilat, KayitYonu.Eksik, 60m,
            KayitDurumu.Cozuldu, FarkSinifi.Askida,
            resolutionDate: AuditDate);
        var reconciliationHarc = NewRecord(
            AuditDate, BankaHesapTuru.Harc, KayitYonu.Fazla, 70m,
            KayitDurumu.Cozuldu, FarkSinifi.Askida,
            resolutionDate: AuditDate);
        var excludedPreviousExpected = NewRecord(
            AuditDate.AddDays(-1), BankaHesapTuru.Harc, KayitYonu.Eksik, 999m,
            KayitDurumu.Acik, FarkSinifi.Beklenen);
        var excludedStopaj = NewRecord(
            AuditDate, BankaHesapTuru.Stopaj, KayitYonu.Fazla, 777m,
            KayitDurumu.Takipte, FarkSinifi.Bilinmeyen,
            trackingDate: AuditDate);
        db.AddRange(
            activeExpected, activeUnexpected, previousTracked, previousOpen,
            resolvedTracked, reconciliationTahsilat, reconciliationHarc,
            excludedPreviousExpected, excludedStopaj);
        await db.SaveChangesAsync();

        var snapshot = await CreateService(db).GetImmutableAuditSnapshotAsync(AuditDate);

        Assert.Equal(-100m, snapshot.Summary.GuneAitEksikFazlaTahsilat);
        Assert.Equal(0m, snapshot.Summary.GuneAitEksikFazlaHarc);
        Assert.Equal(-60m, snapshot.Summary.OncekiGunAcikTahsilat);
        Assert.Equal(0m, snapshot.Summary.OncekiGunAcikHarc);
        Assert.Equal(0m, snapshot.Summary.BugunCozulenTahsilat);
        Assert.Equal(50m, snapshot.Summary.BugunCozulenHarc);
        Assert.Equal(120m, snapshot.Summary.TakipteEksikTahsilat);
        Assert.Equal(2, snapshot.Summary.TakipteSayisi);
        Assert.Equal(-20m, snapshot.Summary.BeklenenTahsilat);
        Assert.Equal(30m, snapshot.Summary.OlaganDisiHarc);
        Assert.Equal(60m, snapshot.Summary.TakipKasaEtkisiTahsilat);
        Assert.Equal(70m, snapshot.Summary.TakipKasaEtkisiHarc);
        Assert.Equal(-10m, snapshot.Summary.TakipKasaEtkisiNet);
        Assert.Contains("EFT iade", snapshot.Summary.BreakdownMesajTahsilat);

        Assert.Equal(
            new[] { activeExpected.Id, activeUnexpected.Id }.OrderBy(id => id),
            snapshot.Details.Groups.AktifKayitlar);
        Assert.Equal(
            new[] { previousTracked.Id, previousOpen.Id }.OrderBy(id => id),
            snapshot.Details.Groups.OncekiAciklar);
        Assert.Equal(new[] { resolvedTracked.Id }, snapshot.Details.Groups.BugunCozulenler);
        Assert.Equal(
            new[] { reconciliationTahsilat.Id, reconciliationHarc.Id }.OrderBy(id => id),
            snapshot.Details.Groups.ReconciliationKayitlar);
        Assert.Equal(
            new[] { activeExpected.Id, previousTracked.Id }.OrderBy(id => id),
            snapshot.Details.Groups.TakipteKayitlar);
        Assert.Equal(new[] { resolvedTracked.Id }, snapshot.Details.Groups.BugunTakipCozulenler);
        Assert.DoesNotContain(snapshot.Details.Records, record =>
            record.KayitId == excludedPreviousExpected.Id || record.KayitId == excludedStopaj.Id);
        Assert.True(HesapKontrolImmutableAuditDetailsValidator.TryValidate(
            snapshot.Details, out var validationError), validationError);
    }

    [Fact]
    public async Task ImmutableSnapshot_DeduplicatesCrossGroupRecordAndSortsRecordsAndGroupsDeterministically()
    {
        await using var db = CreateDb();
        var shared = NewRecord(
            AuditDate.AddDays(-1), BankaHesapTuru.Harc, KayitYonu.Eksik, 50m,
            KayitDurumu.Cozuldu, FarkSinifi.Bilinmeyen,
            trackingDate: AuditDate.AddDays(-1), resolutionDate: AuditDate,
            id: Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        var low = NewRecord(
            AuditDate, BankaHesapTuru.Tahsilat, KayitYonu.Eksik, 10m,
            KayitDurumu.Acik, FarkSinifi.Bilinmeyen,
            id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        db.AddRange(shared, low);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var first = await service.GetImmutableAuditSnapshotAsync(AuditDate);
        var second = await service.GetImmutableAuditSnapshotAsync(AuditDate);

        Assert.Equal(2, first.Details.Records.Count);
        Assert.Single(first.Details.Records, record => record.KayitId == shared.Id);
        Assert.Contains(shared.Id, first.Details.Groups.BugunCozulenler);
        Assert.Contains(shared.Id, first.Details.Groups.BugunTakipCozulenler);
        Assert.Equal(
            HesapKontrolImmutableAuditDetailsValidator.OrderRecords(first.Details.Records),
            first.Details.Records);
        Assert.Equal(
            JsonSerializer.Serialize(first.Details),
            JsonSerializer.Serialize(second.Details));
    }

    [Fact]
    public async Task EmptyToday_PastTrackedRecordsAlignPoolAutoFillAndImmutableAudit()
    {
        await using var db = CreateDb();
        db.Add(NewRecord(
            AuditDate.AddDays(-1), BankaHesapTuru.Tahsilat, KayitYonu.Eksik, 100m,
            KayitDurumu.Takipte, FarkSinifi.Bilinmeyen,
            trackingDate: AuditDate.AddDays(-1)));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var pool = await service.GetActiveFollowTotalsAsync(AuditDate);
        var autoFill = await service.GetAutoFillDataAsync(AuditDate);
        var snapshot = await service.GetImmutableAuditSnapshotAsync(AuditDate);

        Assert.True(snapshot.Summary.HasData);
        Assert.Equal(-100m, pool.TahsilatNet);
        Assert.Equal(pool.TahsilatNet, autoFill.GuneAitEksikFazlaTahsilat);
        Assert.Equal(pool.HarcNet, autoFill.GuneAitEksikFazlaHarc);
        Assert.Equal(pool.TahsilatNet, snapshot.Summary.GuneAitEksikFazlaTahsilat);
        Assert.Equal(pool.HarcNet, snapshot.Summary.GuneAitEksikFazlaHarc);
        Assert.Equal(-100m, snapshot.Summary.OncekiGunAcikTahsilat);
        Assert.Single(snapshot.Details.Records);
        Assert.Empty(snapshot.Details.Groups.AktifKayitlar);
        Assert.Single(snapshot.Details.Groups.OncekiAciklar);
        Assert.Single(snapshot.Details.Groups.TakipteKayitlar);
        Assert.True(HesapKontrolImmutableAuditDetailsValidator.TryValidate(
            snapshot.Details, out _));
    }

    [Fact]
    public async Task GetAutoFillCompatibility_AndImmutableSummaryReturnIdenticalScalars()
    {
        await using var db = CreateDb();
        db.Add(NewRecord(
            AuditDate, BankaHesapTuru.Tahsilat, KayitYonu.Eksik, 88m,
            KayitDurumu.Takipte, FarkSinifi.Bilinmeyen,
            trackingDate: AuditDate));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var live = await service.GetAutoFillDataAsync(AuditDate);
        var immutable = await service.GetImmutableAuditSnapshotAsync(AuditDate);

        Assert.Equal(
            JsonSerializer.Serialize(live),
            JsonSerializer.Serialize(immutable.Summary));
        Assert.Equal(-88m, immutable.Summary.GuneAitEksikFazlaTahsilat);
        Assert.Single(immutable.Details.Groups.AktifKayitlar);
        Assert.Single(immutable.Details.Groups.TakipteKayitlar);
    }

    [Fact]
    public void CanonicalSourceCode_MaterializesOnceAndSummaryDetailsDoNotQueryAgain()
    {
        var source = File.ReadAllText(GetRepositoryPath(
            "src", "KasaManager.Infrastructure", "Services",
            "BankaHesapKontrolService.Queries.cs"));
        var load = ExtractMethod(source, "private async Task<AutoFillSourceSets> LoadAutoFillSourceSetsAsync", "private static HesapKontrolImmutableAuditDetails BuildImmutableAuditDetails");
        var summary = ExtractMethod(source, "private EksikFazlaAutoFill BuildAutoFillSummary", "private async Task<AutoFillSourceSets> LoadAutoFillSourceSetsAsync");
        var details = ExtractMethod(source, "private static HesapKontrolImmutableAuditDetails BuildImmutableAuditDetails", "private static HesapKontrolImmutableAuditRecord ToImmutableAuditRecord");
        var immutable = ExtractMethod(source, "public async Task<HesapKontrolImmutableAuditSnapshot> GetImmutableAuditSnapshotAsync", "private EksikFazlaAutoFill BuildAutoFillSummary");

        Assert.Equal(4, CountOccurrences(load, ".ToListAsync(ct)"));
        Assert.Contains("LoadActiveFollowRecordsAsync(analizTarihi, ct)", load);
        Assert.Equal(3, CountOccurrences(source, "LoadActiveFollowRecordsAsync"));
        Assert.DoesNotContain("_db.", summary);
        Assert.DoesNotContain("ToListAsync", summary);
        Assert.DoesNotContain("_db.", details);
        Assert.DoesNotContain("ToListAsync", details);
        Assert.Contains("existing != projected", details);
        Assert.Contains("Conflicting immutable audit record view", details);
        Assert.Equal(1, CountOccurrences(immutable, "LoadAutoFillSourceSetsAsync"));
    }

    [Fact]
    public async Task ImmutableSnapshot_IsHistoricalAfterEntityFieldsAndStatusChange()
    {
        await using var db = CreateDb();
        var entity = NewRecord(
            AuditDate, BankaHesapTuru.Tahsilat, KayitYonu.Eksik, 25m,
            KayitDurumu.Acik, FarkSinifi.Bilinmeyen);
        entity.DosyaNo = "OLD-FILE";
        entity.BirimAdi = "OLD-UNIT";
        db.Add(entity);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var immutable = await service.GetImmutableAuditSnapshotAsync(AuditDate);
        var savedJson = JsonSerializer.Serialize(immutable.Details);
        entity.Durum = KayitDurumu.Iptal;
        entity.DosyaNo = "NEW-FILE";
        entity.BirimAdi = "NEW-UNIT";
        await db.SaveChangesAsync();

        var historical = JsonSerializer.Deserialize<HesapKontrolImmutableAuditDetails>(savedJson);
        var record = Assert.Single(historical!.Records);
        Assert.Equal(KayitDurumu.Acik, record.KaydetmeAnindakiDurum);
        Assert.Equal("OLD-FILE", record.DosyaNo);
        Assert.Equal("OLD-UNIT", record.BirimAdi);
    }

    [Fact]
    public void ImmutableRecordContract_ContainsOnlyExplicitMinimumProperties()
    {
        var names = typeof(HesapKontrolImmutableAuditRecord)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[]
        {
            "AnalizTarihi", "BirimAdi", "CozulmeTarihi", "DosyaNo", "HesapTuru",
            "KaydetmeAnindakiDurum", "KayitId", "OnayTarihi", "Sinif",
            "TakipBaslangicTarihi", "TespitEdilenTip", "Tutar", "Yon"
        }, names);
    }

    private static HesapKontrolKaydi NewRecord(
        DateOnly date,
        BankaHesapTuru account,
        KayitYonu direction,
        decimal amount,
        KayitDurumu status,
        FarkSinifi classification,
        string? type = null,
        DateOnly? trackingDate = null,
        DateOnly? resolutionDate = null,
        Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        AnalizTarihi = date,
        HesapTuru = account,
        Yon = direction,
        Tutar = amount,
        Durum = status,
        Sinif = classification,
        TespitEdilenTip = type,
        TakipBaslangicTarihi = trackingDate,
        CozulmeTarihi = resolutionDate,
        DosyaNo = $"FILE-{amount}",
        BirimAdi = $"UNIT-{amount}",
        CreatedBy = "must-not-serialize",
        CreatedByUserId = 17,
        TrackingStartedByUserId = 18,
        ResolvedByUserId = 19,
        ApprovedByUserId = 20,
        CancelledByUserId = 21,
        Aciklama = "must-not-serialize",
        Notlar = "must-not-serialize"
    };

    private static KasaManagerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseInMemoryDatabase($"C3ImmutableAudit_{Guid.NewGuid():N}")
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

    private static string GetRepositoryPath(params string[] segments)
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(new[] { root }.Concat(segments).ToArray());
    }

    private static string ExtractMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Method markers not found: {startMarker}");
        return source[start..end];
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
