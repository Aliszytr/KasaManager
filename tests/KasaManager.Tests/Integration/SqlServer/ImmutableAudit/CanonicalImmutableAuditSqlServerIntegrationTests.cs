using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Orchestration;
using KasaManager.Application.Services;
using KasaManager.Domain.Abstractions;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using KasaManager.Tests.Integration.SqlServer.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KasaManager.Tests.Integration.SqlServer.ImmutableAudit;

[Collection(SqlServerIntegrationCollection.Name)]
public sealed class CanonicalImmutableAuditSqlServerIntegrationTests(SqlServerIntegrationFixture fixture)
{
    [SqlServerFact]
    public async Task CanonicalSourceSets_RespectSelectedDateStatusClassificationAccountAndDirectionBoundaries()
    {
        var selectedDate = new DateOnly(2071, 1, 15);
        await using var scope = new SqlServerHesapKontrolScope(fixture);
        var context = scope.Context;
        var activeTracked = NewRecord(
            selectedDate, BankaHesapTuru.Tahsilat, KayitYonu.Eksik, 10m,
            KayitDurumu.Takipte, FarkSinifi.Bilinmeyen,
            trackingDate: selectedDate);
        var activeOpen = NewRecord(
            selectedDate, BankaHesapTuru.Harc, KayitYonu.Fazla, 20m,
            KayitDurumu.Acik, FarkSinifi.Bilinmeyen);
        var previousOpen = NewRecord(
            selectedDate.AddDays(-1), BankaHesapTuru.Tahsilat, KayitYonu.Fazla, 30m,
            KayitDurumu.Acik, FarkSinifi.Bilinmeyen);
        var resolvedTracked = NewRecord(
            selectedDate.AddDays(-2), BankaHesapTuru.Harc, KayitYonu.Eksik, 40m,
            KayitDurumu.Cozuldu, FarkSinifi.Bilinmeyen,
            trackingDate: selectedDate.AddDays(-2), resolutionDate: selectedDate);
        var reconciliation = NewRecord(
            selectedDate, BankaHesapTuru.Tahsilat, KayitYonu.Eksik, 50m,
            KayitDurumu.Cozuldu, FarkSinifi.Askida,
            resolutionDate: selectedDate);
        var excludedPreviousExpected = NewRecord(
            selectedDate.AddDays(-1), BankaHesapTuru.Harc, KayitYonu.Eksik, 600m,
            KayitDurumu.Acik, FarkSinifi.Beklenen);
        var excludedWrongResolutionDate = NewRecord(
            selectedDate.AddDays(-2), BankaHesapTuru.Harc, KayitYonu.Fazla, 700m,
            KayitDurumu.Cozuldu, FarkSinifi.Bilinmeyen,
            resolutionDate: selectedDate.AddDays(-1));
        var excludedStopaj = NewRecord(
            selectedDate, BankaHesapTuru.Stopaj, KayitYonu.Fazla, 800m,
            KayitDurumu.Takipte, FarkSinifi.Bilinmeyen,
            trackingDate: selectedDate);
        var excludedFutureTracked = NewRecord(
            selectedDate.AddDays(1), BankaHesapTuru.Tahsilat, KayitYonu.Eksik, 900m,
            KayitDurumu.Takipte, FarkSinifi.Bilinmeyen,
            trackingDate: selectedDate.AddDays(1));
        var excludedFutureTrackingStart = NewRecord(
            selectedDate.AddDays(-1), BankaHesapTuru.Harc, KayitYonu.Eksik, 950m,
            KayitDurumu.Takipte, FarkSinifi.Bilinmeyen,
            trackingDate: selectedDate.AddDays(1));
        scope.AddRange(
            activeTracked, activeOpen, previousOpen, resolvedTracked, reconciliation,
            excludedPreviousExpected, excludedWrongResolutionDate, excludedStopaj,
            excludedFutureTracked, excludedFutureTrackingStart);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var scalar = await service.GetAutoFillDataAsync(selectedDate);
        var immutable = await service.GetImmutableAuditSnapshotAsync(selectedDate);

        Assert.Equal(JsonSerializer.Serialize(scalar), JsonSerializer.Serialize(immutable.Summary));
        Assert.Equal(
            new[] { activeTracked.Id, activeOpen.Id }.OrderBy(id => id),
            immutable.Details.Groups.AktifKayitlar);
        Assert.Equal(
            new[] { previousOpen.Id, excludedFutureTrackingStart.Id }.OrderBy(id => id),
            immutable.Details.Groups.OncekiAciklar);
        Assert.Equal(new[] { resolvedTracked.Id }, immutable.Details.Groups.BugunCozulenler);
        Assert.Equal(new[] { reconciliation.Id }, immutable.Details.Groups.ReconciliationKayitlar);
        Assert.Equal(
            new[] { activeTracked.Id, excludedFutureTrackingStart.Id }.OrderBy(id => id),
            immutable.Details.Groups.TakipteKayitlar);
        Assert.Equal(new[] { resolvedTracked.Id }, immutable.Details.Groups.BugunTakipCozulenler);
        Assert.DoesNotContain(immutable.Details.Records, record =>
            record.KayitId == excludedPreviousExpected.Id
            || record.KayitId == excludedWrongResolutionDate.Id
            || record.KayitId == excludedStopaj.Id
            || record.KayitId == excludedFutureTracked.Id);
        Assert.Equal(2, immutable.Summary.TakipteSayisi);
        Assert.Equal(10m, immutable.Summary.TakipteEksikTahsilat);
        Assert.Equal(-10m, immutable.Summary.GuneAitEksikFazlaTahsilat);
        Assert.Equal(-950m, immutable.Summary.GuneAitEksikFazlaHarc);
        Assert.Equal(30m, immutable.Summary.OncekiGunAcikTahsilat);
        Assert.Equal(40m, immutable.Summary.BugunCozulenHarc);
        Assert.Equal(50m, immutable.Summary.TakipKasaEtkisiTahsilat);
    }

    [SqlServerFact]
    public async Task StructuredDetails_DeduplicateCrossGroupRecordsExcludeStopajAndContainOnlySafeFields()
    {
        var selectedDate = new DateOnly(2071, 2, 16);
        await using var scope = new SqlServerHesapKontrolScope(fixture);
        var context = scope.Context;
        var today = NewRecord(
            selectedDate, BankaHesapTuru.Tahsilat, KayitYonu.Fazla, 11.1234m,
            KayitDurumu.Acik, FarkSinifi.Bilinmeyen);
        var shared = NewRecord(
            selectedDate.AddDays(-1), BankaHesapTuru.Harc, KayitYonu.Eksik, 22.5678m,
            KayitDurumu.Cozuldu, FarkSinifi.Bilinmeyen,
            trackingDate: selectedDate.AddDays(-1), resolutionDate: selectedDate);
        shared.OnayTarihi = new DateTime(2071, 2, 16, 8, 30, 15, DateTimeKind.Utc);
        var stopaj = NewRecord(
            selectedDate, BankaHesapTuru.Stopaj, KayitYonu.Fazla, 999m,
            KayitDurumu.Takipte, FarkSinifi.Bilinmeyen,
            trackingDate: selectedDate);
        SetForbiddenAuditValues(today, 101);
        SetForbiddenAuditValues(shared, 201);
        SetForbiddenAuditValues(stopaj, 301);
        scope.AddRange(today, shared, stopaj);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var before = await service.GetImmutableAuditSnapshotAsync(selectedDate);

        Assert.Single(before.Details.Records, record => record.KayitId == shared.Id);
        Assert.Contains(shared.Id, before.Details.Groups.BugunCozulenler);
        Assert.Contains(shared.Id, before.Details.Groups.BugunTakipCozulenler);
        Assert.DoesNotContain(before.Details.Records, record => record.KayitId == stopaj.Id);
        Assert.Equal(SafeRecordPropertyNames, typeof(HesapKontrolImmutableAuditRecord)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray());
        using (var document = JsonDocument.Parse(JsonSerializer.Serialize(before.Details)))
            AssertNoForbiddenPropertyNames(document.RootElement);

        SetActorAuditValues(today, 401);
        SetActorAuditValues(shared, 501);
        await context.SaveChangesAsync();
        var after = await service.GetImmutableAuditSnapshotAsync(selectedDate);

        Assert.Equal(JsonSerializer.Serialize(before.Summary), JsonSerializer.Serialize(after.Summary));
        Assert.Equal(JsonSerializer.Serialize(before.Details), JsonSerializer.Serialize(after.Details));
    }

    [SqlServerFact]
    public async Task TrackingCanonical_LegacyNullDateIsIncludedWithinAnalysisBoundaryAndFutureLegacyIsExcluded()
    {
        var selectedDate = new DateOnly(2071, 4, 18);
        await using var scope = new SqlServerHesapKontrolScope(fixture);
        var context = scope.Context;
        var includedLegacy = NewRecord(
            selectedDate, BankaHesapTuru.Harc, KayitYonu.Fazla, 123.45m,
            KayitDurumu.Takipte, FarkSinifi.Bilinmeyen);
        var excludedFutureLegacy = NewRecord(
            selectedDate.AddDays(1), BankaHesapTuru.Harc, KayitYonu.Fazla, 876.55m,
            KayitDurumu.Takipte, FarkSinifi.Bilinmeyen);
        scope.AddRange(includedLegacy, excludedFutureLegacy);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var scalar = await service.GetAutoFillDataAsync(selectedDate);
        var immutable = await service.GetImmutableAuditSnapshotAsync(selectedDate);

        Assert.Equal(JsonSerializer.Serialize(scalar), JsonSerializer.Serialize(immutable.Summary));
        Assert.Equal(new[] { includedLegacy.Id }, immutable.Details.Groups.TakipteKayitlar);
        Assert.Contains(immutable.Details.Records, record =>
            record.KayitId == includedLegacy.Id && record.TakipBaslangicTarihi is null);
        Assert.DoesNotContain(immutable.Details.Records, record =>
            record.KayitId == excludedFutureLegacy.Id);
        Assert.Equal(1, immutable.Summary.TakipteSayisi);
        Assert.Equal(0m, immutable.Summary.TakipteEksikTahsilat);
        Assert.Equal(0m, immutable.Summary.TakipteEksikHarc);
        Assert.Equal(0m, immutable.Summary.TakipteFazlaTahsilat);
        Assert.Equal(123.45m, immutable.Summary.TakipteFazlaHarc);
    }

    [SqlServerFact]
    public async Task TrackingCanonical_IncludesPastAndExactBoundaryDatesInDetailsAndEveryTrackingScalar()
    {
        var selectedDate = new DateOnly(2071, 5, 19);
        await using var scope = new SqlServerHesapKontrolScope(fixture);
        var context = scope.Context;
        var exactBoundary = NewRecord(
            selectedDate, BankaHesapTuru.Tahsilat, KayitYonu.Eksik, 100.25m,
            KayitDurumu.Takipte, FarkSinifi.Bilinmeyen,
            trackingDate: selectedDate);
        var validPast = NewRecord(
            selectedDate.AddDays(-2), BankaHesapTuru.Harc, KayitYonu.Fazla, 200.75m,
            KayitDurumu.Takipte, FarkSinifi.Bilinmeyen,
            trackingDate: selectedDate.AddDays(-1));
        scope.AddRange(exactBoundary, validPast);
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var scalar = await service.GetAutoFillDataAsync(selectedDate);
        var immutable = await service.GetImmutableAuditSnapshotAsync(selectedDate);

        Assert.Equal(JsonSerializer.Serialize(scalar), JsonSerializer.Serialize(immutable.Summary));
        Assert.Equal(
            new[] { exactBoundary.Id, validPast.Id }.OrderBy(id => id),
            immutable.Details.Groups.TakipteKayitlar);
        Assert.Equal(2, immutable.Summary.TakipteSayisi);
        Assert.Equal(100.25m, immutable.Summary.TakipteEksikTahsilat);
        Assert.Equal(0m, immutable.Summary.TakipteEksikHarc);
        Assert.Equal(0m, immutable.Summary.TakipteFazlaTahsilat);
        Assert.Equal(200.75m, immutable.Summary.TakipteFazlaHarc);
    }

    [SqlServerFact]
    public async Task StructuredDetails_AreDeterministicAcrossOppositeSqlInsertOrders()
    {
        var selectedDate = new DateOnly(2071, 3, 17);
        await using var scope = new SqlServerHesapKontrolScope(fixture);
        var context = scope.Context;
        var records = new[]
        {
            NewRecord(selectedDate, BankaHesapTuru.Harc, KayitYonu.Fazla, 30m,
                KayitDurumu.Acik, FarkSinifi.Askida,
                id: Guid.Parse("ffffffff-ffff-ffff-ffff-fffffffffff3")),
            NewRecord(selectedDate, BankaHesapTuru.Tahsilat, KayitYonu.Eksik, 10m,
                KayitDurumu.Acik, FarkSinifi.Bilinmeyen,
                id: Guid.Parse("00000000-0000-0000-0000-000000000001")),
            NewRecord(selectedDate, BankaHesapTuru.Tahsilat, KayitYonu.Fazla, 20m,
                KayitDurumu.Acik, FarkSinifi.Beklenen,
                id: Guid.Parse("77777777-7777-7777-7777-777777777772"))
        };
        scope.AddRange(records.Reverse().ToArray());
        await context.SaveChangesAsync();
        var service = CreateService(context);
        var reverseInsert = await service.GetImmutableAuditSnapshotAsync(selectedDate);

        context.RemoveRange(records);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var forwardRecords = records.Select(Clone).ToArray();
        context.AddRange(forwardRecords);
        await context.SaveChangesAsync();
        var forwardInsert = await CreateService(context).GetImmutableAuditSnapshotAsync(selectedDate);

        Assert.Equal(
            HesapKontrolImmutableAuditDetailsValidator.OrderRecords(reverseInsert.Details.Records),
            reverseInsert.Details.Records);
        Assert.Equal(
            reverseInsert.Details.Groups.AktifKayitlar.OrderBy(id => id),
            reverseInsert.Details.Groups.AktifKayitlar);
        Assert.Equal(
            JsonSerializer.Serialize(reverseInsert.Details),
            JsonSerializer.Serialize(forwardInsert.Details));
    }

    private static readonly string[] SafeRecordPropertyNames =
    [
        "AnalizTarihi", "BirimAdi", "CozulmeTarihi", "DosyaNo", "HesapTuru",
        "KaydetmeAnindakiDurum", "KayitId", "OnayTarihi", "Sinif",
        "TakipBaslangicTarihi", "TespitEdilenTip", "Tutar", "Yon"
    ];

    private static readonly HashSet<string> ForbiddenPropertyNames = new(
        new[]
        {
            "Aciklama", "Notlar", "CreatedBy", "CreatedByUserId", "OnaylayanKullanici",
            "GeriAlanKullanici", "TrackingStartedByUserId", "ResolvedByUserId",
            "ApprovedByUserId", "CancelledByUserId", "CalculatedByUserId", "DeletedByUserId",
            "Path", "ArchivePath", "CurrentPath", "SourceMetadata"
        },
        StringComparer.OrdinalIgnoreCase);

    private static void AssertNoForbiddenPropertyNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                Assert.DoesNotContain(property.Name, ForbiddenPropertyNames);
                AssertNoForbiddenPropertyNames(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                AssertNoForbiddenPropertyNames(item);
        }
    }

    private static void SetForbiddenAuditValues(HesapKontrolKaydi record, int actorBase)
    {
        record.Aciklama = $"forbidden-description-{actorBase}";
        record.Notlar = $"forbidden-notes-{actorBase}";
        SetActorAuditValues(record, actorBase);
    }

    private static void SetActorAuditValues(HesapKontrolKaydi record, int actorBase)
    {
        record.CreatedBy = $"creator-{actorBase}";
        record.OnaylayanKullanici = $"approver-{actorBase}";
        record.GeriAlanKullanici = $"reverter-{actorBase}";
        record.CreatedByUserId = actorBase;
        record.TrackingStartedByUserId = actorBase + 1;
        record.ResolvedByUserId = actorBase + 2;
        record.ApprovedByUserId = actorBase + 3;
        record.CancelledByUserId = actorBase + 4;
    }

    private static HesapKontrolKaydi Clone(HesapKontrolKaydi source) => NewRecord(
        source.AnalizTarihi,
        source.HesapTuru,
        source.Yon,
        source.Tutar,
        source.Durum,
        source.Sinif,
        source.TespitEdilenTip,
        source.TakipBaslangicTarihi,
        source.CozulmeTarihi,
        source.Id);

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
        DosyaNo = $"C4B-FILE-{amount}",
        BirimAdi = $"C4B-UNIT-{amount}"
    };

    private static BankaHesapKontrolService CreateService(KasaManagerDbContext context) => new(
        context,
        Mock.Of<IComparisonService>(),
        Mock.Of<IImportOrchestrator>(),
        Mock.Of<IHesapKontrolSourceResolver>(),
        NullLogger<BankaHesapKontrolService>.Instance);
}
