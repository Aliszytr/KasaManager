using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;

namespace KasaManager.Tests.Domain;

public sealed class KasaImmutableAuditPayloadTests
{
    [Fact]
    public void LegacyJson_WithoutAuditMembers_DeserializesAsVersionZeroAndNullAudit()
    {
        const string json = """
            {"Tarih":"2026-07-14","KasaTuru":"Aksam","GenelKasa":10000.00}
            """;

        var payload = JsonSerializer.Deserialize<KasaRaporData>(json);

        Assert.NotNull(payload);
        Assert.Equal(0, payload.PayloadVersion);
        Assert.Null(payload.ImmutableAudit);
    }

    [Fact]
    public void VersionOne_RealZeroAudit_RoundTripsAsNonNullZeros()
    {
        var payload = new KasaRaporData
        {
            PayloadVersion = 1,
            ImmutableAudit = new KasaImmutableAuditData()
        };

        var roundTrip = JsonSerializer.Deserialize<KasaRaporData>(
            JsonSerializer.Serialize(payload));

        Assert.NotNull(roundTrip);
        Assert.Equal(1, roundTrip.PayloadVersion);
        Assert.NotNull(roundTrip.ImmutableAudit);

        var numericProperties = typeof(KasaImmutableAuditData).GetProperties()
            .Where(property => property.PropertyType == typeof(decimal)
                            || property.PropertyType == typeof(int));
        foreach (var property in numericProperties)
            Assert.Equal(0m, Convert.ToDecimal(property.GetValue(roundTrip.ImmutableAudit)));
    }

    [Fact]
    public void VersionOne_RealValuesAndBreakdowns_RoundTripWithoutPrecisionLoss()
    {
        var payload = new KasaRaporData
        {
            PayloadVersion = 1,
            ImmutableAudit = new KasaImmutableAuditData
            {
                TakipteEksikTahsilat = 3000.123456789m,
                TakipteEksikHarc = 29873.809876543m,
                TakipteFazlaTahsilat = 12.34m,
                TakipteFazlaHarc = 56.78m,
                TakipteSayisi = 15,
                ToplamFarkTahsilat = -3000.123456789m,
                ToplamFarkHarc = -29873.809876543m,
                BreakdownMesajTahsilat = "Tahsilat kırılımı",
                BreakdownMesajHarc = "Harç kırılımı"
            }
        };

        var roundTrip = JsonSerializer.Deserialize<KasaRaporData>(
            JsonSerializer.Serialize(payload));

        Assert.NotNull(roundTrip?.ImmutableAudit);
        Assert.Equal(3000.123456789m, roundTrip.ImmutableAudit.TakipteEksikTahsilat);
        Assert.Equal(29873.809876543m, roundTrip.ImmutableAudit.TakipteEksikHarc);
        Assert.Equal(-3000.123456789m, roundTrip.ImmutableAudit.ToplamFarkTahsilat);
        Assert.Equal(-29873.809876543m, roundTrip.ImmutableAudit.ToplamFarkHarc);
        Assert.Equal("Tahsilat kırılımı", roundTrip.ImmutableAudit.BreakdownMesajTahsilat);
        Assert.Equal("Harç kırılımı", roundTrip.ImmutableAudit.BreakdownMesajHarc);
    }

    [Fact]
    public void VersionTwo_MinimumDetailsRoundTripWithExactAllowedRecordProperties()
    {
        var id = Guid.Parse("00000000-0000-0000-0000-000000000123");
        var details = new HesapKontrolImmutableAuditDetails(
            new[]
            {
                new HesapKontrolImmutableAuditRecord(
                    id, new DateOnly(2026, 7, 18), BankaHesapTuru.Tahsilat,
                    KayitYonu.Eksik, 123.456789m, KayitDurumu.Acik,
                    FarkSinifi.Bilinmeyen, "2026/123", "Birim A", "BILINMEYEN",
                    null, null, null)
            },
            new HesapKontrolImmutableAuditGroups(
                new[] { id }, Array.Empty<Guid>(), Array.Empty<Guid>(),
                Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>()));
        var payload = new KasaRaporData
        {
            PayloadVersion = 2,
            ImmutableAudit = new KasaImmutableAuditData { TakipteSayisi = 1 },
            ImmutableAuditDetails = JsonSerializer.SerializeToElement(details)
        };

        var roundTrip = JsonSerializer.Deserialize<KasaRaporData>(
            JsonSerializer.Serialize(payload));
        var restored = roundTrip!.ImmutableAuditDetails!.Value
            .Deserialize<HesapKontrolImmutableAuditDetails>();

        Assert.Equal(2, roundTrip.PayloadVersion);
        Assert.NotNull(restored);
        var record = Assert.Single(restored.Records);
        Assert.Equal(123.456789m, record.Tutar);
        Assert.Equal("2026/123", record.DosyaNo);
        Assert.True(HesapKontrolImmutableAuditDetailsValidator.TryValidate(
            restored, out var validationError), validationError);

        using var document = JsonDocument.Parse(
            roundTrip.ImmutableAuditDetails.Value.GetRawText());
        var propertyNames = document.RootElement
            .GetProperty("Records")[0]
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[]
        {
            "AnalizTarihi", "BirimAdi", "CozulmeTarihi", "DosyaNo", "HesapTuru",
            "KaydetmeAnindakiDurum", "KayitId", "OnayTarihi", "Sinif",
            "TakipBaslangicTarihi", "TespitEdilenTip", "Tutar", "Yon"
        }, propertyNames);
    }

    [Fact]
    public void VersionTwo_DetailsJsonExcludesSensitiveFreeTextActorAndPathData()
    {
        var id = Guid.NewGuid();
        var details = new HesapKontrolImmutableAuditDetails(
            new[]
            {
                new HesapKontrolImmutableAuditRecord(
                    id, new DateOnly(2026, 7, 18), BankaHesapTuru.Harc,
                    KayitYonu.Fazla, 10m, KayitDurumu.Acik,
                    FarkSinifi.Bilinmeyen, "SAFE-FILE", "SAFE-UNIT", null,
                    null, null, null)
            },
            new HesapKontrolImmutableAuditGroups(
                new[] { id }, Array.Empty<Guid>(), Array.Empty<Guid>(),
                Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>()));

        var json = JsonSerializer.Serialize(details);
        var forbidden = new[]
        {
            "Aciklama", "Notlar", "CreatedBy", "OnaylayanKullanici",
            "GeriAlanKullanici", "CreatedByUserId", "TrackingStartedByUserId",
            "ResolvedByUserId", "ApprovedByUserId", "CancelledByUserId",
            "CalculatedByUserId", "DeletedByUserId", "C:\\\\secret\\\\archive",
            "current-folder", "audit-user", "audit@example.test"
        };

        Assert.All(forbidden, value => Assert.DoesNotContain(value, json, StringComparison.Ordinal));
    }
}
