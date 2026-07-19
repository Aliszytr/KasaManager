using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

namespace KasaManager.Tests.Infrastructure;

public sealed class CalculatedKasaSnapshotImmutableAuditTests : IDisposable
{
    private readonly KasaManagerDbContext _db;
    private readonly CalculatedKasaSnapshotService _service;

    public CalculatedKasaSnapshotImmutableAuditTests()
    {
        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseInMemoryDatabase($"ImmutableAudit_{Guid.NewGuid():N}")
            .Options;
        _db = new KasaManagerDbContext(options);
        _service = new CalculatedKasaSnapshotService(
            _db,
            Mock.Of<ILogger<CalculatedKasaSnapshotService>>());
    }

    [Fact]
    public async Task SameDateAndType_AppendsVersionWithoutChangingPreviousAuditJson()
    {
        var date = new DateOnly(2026, 7, 14);
        const string firstJson =
            "{\"PayloadVersion\":1,\"ImmutableAudit\":{\"TakipteEksikTahsilat\":3000.00}}";
        const string secondJson =
            "{\"PayloadVersion\":1,\"ImmutableAudit\":{\"TakipteEksikTahsilat\":0.00}}";

        await _service.SaveAsync(NewSnapshot(date, firstJson), 17, "first-user");
        await _service.SaveAsync(NewSnapshot(date, secondJson), 29, "second-user");

        var versions = await _db.CalculatedKasaSnapshots
            .OrderBy(snapshot => snapshot.Version)
            .ToListAsync();

        Assert.Equal(2, versions.Count);
        Assert.Equal(1, versions[0].Version);
        Assert.False(versions[0].IsActive);
        Assert.Equal(firstJson, versions[0].KasaRaporDataJson);
        Assert.Equal(2, versions[1].Version);
        Assert.True(versions[1].IsActive);
        Assert.Equal(secondJson, versions[1].KasaRaporDataJson);
    }

    [Fact]
    public async Task VersionTwoPayloads_RemainIndependentAcrossVersionsAndKasaTypes()
    {
        var date = new DateOnly(2026, 7, 18);
        const string morningV1 =
            "{\"PayloadVersion\":2,\"ImmutableAudit\":{\"TakipteSayisi\":1},\"ImmutableAuditDetails\":{\"Records\":[],\"Groups\":{\"AktifKayitlar\":[],\"OncekiAciklar\":[],\"BugunCozulenler\":[],\"ReconciliationKayitlar\":[],\"TakipteKayitlar\":[],\"BugunTakipCozulenler\":[]}}}";
        const string morningV2 =
            "{\"PayloadVersion\":2,\"ImmutableAudit\":{\"TakipteSayisi\":2},\"ImmutableAuditDetails\":{\"Records\":[],\"Groups\":{\"AktifKayitlar\":[],\"OncekiAciklar\":[],\"BugunCozulenler\":[],\"ReconciliationKayitlar\":[],\"TakipteKayitlar\":[],\"BugunTakipCozulenler\":[]}}}";
        const string evening =
            "{\"PayloadVersion\":2,\"ImmutableAudit\":{\"TakipteSayisi\":9},\"ImmutableAuditDetails\":{\"Records\":[],\"Groups\":{\"AktifKayitlar\":[],\"OncekiAciklar\":[],\"BugunCozulenler\":[],\"ReconciliationKayitlar\":[],\"TakipteKayitlar\":[],\"BugunTakipCozulenler\":[]}}}";

        await _service.SaveAsync(
            NewSnapshot(date, morningV1, KasaRaporTuru.Sabah), 17, "morning-1");
        await _service.SaveAsync(
            NewSnapshot(date, evening, KasaRaporTuru.Aksam), 18, "evening");
        await _service.SaveAsync(
            NewSnapshot(date, morningV2, KasaRaporTuru.Sabah), 19, "morning-2");

        var rows = await _db.CalculatedKasaSnapshots
            .OrderBy(snapshot => snapshot.KasaTuru)
            .ThenBy(snapshot => snapshot.Version)
            .ToListAsync();
        var morning = rows.Where(snapshot => snapshot.KasaTuru == KasaRaporTuru.Sabah).ToArray();
        var savedEvening = Assert.Single(rows, snapshot => snapshot.KasaTuru == KasaRaporTuru.Aksam);

        Assert.Equal(2, morning.Length);
        Assert.Equal(morningV1, morning[0].KasaRaporDataJson);
        Assert.Equal(morningV2, morning[1].KasaRaporDataJson);
        Assert.Equal(1, savedEvening.Version);
        Assert.Equal(evening, savedEvening.KasaRaporDataJson);
    }

    [Fact]
    public async Task DailyNoteOnlyChange_RemainsNoOpAndDoesNotCreateVersion()
    {
        var date = new DateOnly(2026, 7, 19);
        var first = NewMetadataSnapshot(date, "ilk not");
        var second = NewMetadataSnapshot(date, "değişen not");

        var persistedFirst = await _service.SaveAsync(first, 17, "admin");
        var persistedSecond = await _service.SaveAsync(second, 17, "admin");

        Assert.Equal(persistedFirst.Id, persistedSecond.Id);
        var saved = Assert.Single(await _db.CalculatedKasaSnapshots.ToListAsync());
        Assert.Equal(1, saved.Version);
        var payload = JsonSerializer.Deserialize<KasaRaporData>(saved.KasaRaporDataJson!);
        Assert.Equal("ilk not", payload!.GunlukNot);
    }

    public void Dispose() => _db.Dispose();

    private static CalculatedKasaSnapshot NewSnapshot(
        DateOnly date,
        string json,
        KasaRaporTuru type = KasaRaporTuru.Aksam) => new()
    {
        RaporTarihi = date,
        KasaTuru = type,
        InputsJson = json,
        OutputsJson = "{}",
        KasaRaporDataJson = json
    };

    private static CalculatedKasaSnapshot NewMetadataSnapshot(DateOnly date, string note) => new()
    {
        RaporTarihi = date,
        KasaTuru = KasaRaporTuru.Aksam,
        InputsJson = "{}",
        OutputsJson = "{}",
        KasaRaporDataJson = JsonSerializer.Serialize(new KasaRaporData
        {
            PayloadVersion = 2,
            KasayiYapan = "ESRA DAĞAŞAN",
            Aciklama = "aynı açıklama",
            GunlukNot = note,
            ImmutableAudit = new KasaImmutableAuditData(),
            ImmutableAuditDetails = JsonSerializer.SerializeToElement(
                new HesapKontrolImmutableAuditDetails(
                    Array.Empty<HesapKontrolImmutableAuditRecord>(),
                    new HesapKontrolImmutableAuditGroups(
                        Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>(),
                        Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>())))
        })
    };
}
