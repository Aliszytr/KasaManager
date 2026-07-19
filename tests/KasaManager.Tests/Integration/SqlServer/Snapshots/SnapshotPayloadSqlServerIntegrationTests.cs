using System.Security.Claims;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Orchestration;
using KasaManager.Application.Services;
using KasaManager.Application.Services.ReadAdapter;
using KasaManager.Domain.Calculation.Data;
using KasaManager.Domain.FinancialExceptions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Domain.Settings;
using KasaManager.Domain.Validation;
using KasaManager.Infrastructure.Services;
using KasaManager.Tests.Integration.SqlServer.Support;
using KasaManager.Web.Controllers;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;

namespace KasaManager.Tests.Integration.SqlServer.Snapshots;

[Collection(SqlServerIntegrationCollection.Name)]
public sealed class SnapshotPayloadSqlServerIntegrationTests
{
    private static readonly DateOnly SaveDate = new(2060, 4, 21);
    private static readonly DateOnly ActorComparisonDate = new(2060, 4, 22);
    private static readonly DateOnly FullRoundTripDate = new(2060, 4, 26);
    private static readonly DateOnly JsonSecurityDate = new(2060, 4, 27);
    private static readonly DateOnly AuditFailureDate = new(2060, 4, 28);
    private static readonly DateOnly SqlRollbackDate = new(2060, 4, 29);
    private static readonly DateOnly HistoricalImmutabilityDate = new(2060, 5, 1);
    private static readonly DateOnly HistoricalIsolationDate = new(2060, 5, 2);
    private static readonly DateOnly HistoricalResaveDate = new(2026, 7, 14);
    private static readonly DateOnly LegacyCompatibilityDate = new(2026, 7, 15);
    private static readonly DateOnly LivePoolAlignmentDate = new(2072, 7, 19);
    private readonly SqlServerIntegrationFixture _fixture;

    public SnapshotPayloadSqlServerIntegrationTests(SqlServerIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [SqlServerFact]
    public async Task SaveReport_PayloadVersionTwo_PersistsCashierButNotCreatorInFinancialJson()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), $"kasa_c42c_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(webRoot, "Data", "Raporlar"));

        try
        {
            await using var saveContext = _fixture.CreateContext();
            var snapshotService = new CalculatedKasaSnapshotService(
                saveContext,
                NullLogger<CalculatedKasaSnapshotService>.Instance);
            var controller = CreateController(
                webRoot, snapshotService, SaveDate, 17, "server-actor-c4");

            var result = await controller.SaveReport(
                new KasaPreviewViewModel
                {
                    SelectedDate = SaveDate,
                    KasaType = "Aksam",
                    KasayiYapan = "client-actor-secret-c4"
                },
                CancellationToken.None);

            var response = Assert.IsType<JsonResult>(result);
            using (var responseJson = JsonDocument.Parse(JsonSerializer.Serialize(response.Value)))
                Assert.True(responseJson.RootElement.GetProperty("ok").GetBoolean());

            await using var readContext = _fixture.CreateContext();
            var saved = await readContext.CalculatedKasaSnapshots
                .AsNoTracking()
                .SingleAsync(snapshot =>
                    snapshot.RaporTarihi == SaveDate
                    && snapshot.KasaTuru == KasaRaporTuru.Aksam);

            Assert.Equal(17, saved.CalculatedByUserId);
            Assert.Equal("server-actor-c4", saved.CalculatedBy);
            Assert.False(string.IsNullOrWhiteSpace(saved.KasaRaporDataJson));

            using var payload = JsonDocument.Parse(saved.KasaRaporDataJson);
            Assert.Equal(2, payload.RootElement.GetProperty("PayloadVersion").GetInt32());
            Assert.Equal(JsonValueKind.Object, payload.RootElement.GetProperty("ImmutableAudit").ValueKind);
            Assert.Equal(JsonValueKind.Object, payload.RootElement.GetProperty("ImmutableAuditDetails").ValueKind);
            Assert.Equal(SaveDate.ToString("yyyy-MM-dd"), payload.RootElement.GetProperty("Tarih").GetString());
            Assert.Equal("Aksam", payload.RootElement.GetProperty("KasaTuru").GetString());
            Assert.Equal(1234.5678m, payload.RootElement.GetProperty("BankadanCekilen").GetDecimal());
            Assert.True(payload.RootElement.TryGetProperty("MuhabereNo", out _));
            Assert.Equal(
                "C4.2C actor isolation",
                payload.RootElement.GetProperty("KasayiYapan").GetString());

            var propertyNames = EnumeratePropertyNames(payload.RootElement).ToArray();
            var forbiddenActorProperties = new[]
            {
                "CreatedByUserId",
                "TrackingStartedByUserId",
                "ResolvedByUserId",
                "ApprovedByUserId",
                "CancelledByUserId",
                "CalculatedByUserId",
                "DeletedByUserId"
            };
            foreach (var forbidden in forbiddenActorProperties)
            {
                Assert.DoesNotContain(
                    propertyNames,
                    name => name.Equals(forbidden, StringComparison.OrdinalIgnoreCase));
            }

            var loadService = new CalculatedKasaSnapshotService(
                readContext,
                NullLogger<CalculatedKasaSnapshotService>.Instance);
            var loadController = CreateController(
                webRoot, loadService, SaveDate, 29, "empty-v2-reader");
            var loadResult = await loadController.LoadSnapshot(saved.Id, CancellationToken.None);
            var loadView = Assert.IsType<ViewResult>(loadResult);
            var loadedModel = Assert.IsType<KasaPreviewViewModel>(loadView.Model);
            Assert.Equal(2, loadedModel.LoadedAuditPayloadVersion);
            Assert.True(loadedModel.HasImmutableAuditData);
            Assert.True(loadedModel.HasImmutableAuditRecordDetails);
            Assert.Empty(loadedModel.ImmutableAuditRecords);
            Assert.Null(loadedModel.ImmutableAuditNotice);
            Assert.Null(loadedModel.ImmutableAuditRecordDetailsNotice);
        }
        finally
        {
            await using var cleanup = _fixture.CreateContext();
            await cleanup.CalculatedKasaSnapshots
                .Where(snapshot => snapshot.RaporTarihi == SaveDate)
                .ExecuteDeleteAsync();
            await cleanup.DailyCalculationResults
                .Where(result => result.ForDate == SaveDate)
                .ExecuteDeleteAsync();
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [SqlServerFact]
    public async Task SaveReport_FullVersionTwoPayload_RoundTripsAllAuditAndRelationalTypes()
    {
        var webRoot = CreateWebRoot();
        var auditSnapshot = CreateFullAuditSnapshot(FullRoundTripDate);
        var saveStartedUtc = DateTime.UtcNow;

        try
        {
            Guid savedId;
            DateTime calculatedAtUtc;
            await using (var saveContext = _fixture.CreateContext())
            {
                var snapshotService = new CalculatedKasaSnapshotService(
                    saveContext,
                    NullLogger<CalculatedKasaSnapshotService>.Instance);
                var controller = CreateController(
                    webRoot,
                    snapshotService,
                    FullRoundTripDate,
                    17,
                    "roundtrip-server-actor",
                    auditSnapshot);

                var result = await controller.SaveReport(
                    NewSaveModel(FullRoundTripDate, "roundtrip-client-spoof"),
                    CancellationToken.None);
                AssertSuccessful(result);

                var tracked = Assert.Single(saveContext.CalculatedKasaSnapshots.Local);
                savedId = tracked.Id;
                calculatedAtUtc = tracked.CalculatedAtUtc;
            }

            await using var readContext = _fixture.CreateContext();
            var saved = await readContext.CalculatedKasaSnapshots
                .AsNoTracking()
                .SingleAsync(snapshot => snapshot.Id == savedId);

            Assert.Equal(savedId, saved.Id);
            Assert.Equal(FullRoundTripDate, saved.RaporTarihi);
            Assert.Equal(KasaRaporTuru.Aksam, saved.KasaTuru);
            Assert.Equal(calculatedAtUtc, saved.CalculatedAtUtc);
            Assert.InRange(saved.CalculatedAtUtc, saveStartedUtc, DateTime.UtcNow);
            Assert.Equal(17, saved.CalculatedByUserId);
            Assert.Equal("roundtrip-server-actor", saved.CalculatedBy);
            Assert.Null(saved.FormulaSetId);
            Assert.Null(saved.FormulaSetName);
            Assert.Null(saved.Description);
            Assert.Equal(string.Empty, saved.Notes);
            Assert.Null(saved.DeletedAtUtc);
            Assert.Null(saved.DeletedBy);
            Assert.Null(saved.DeletedByUserId);

            var payload = Assert.IsType<KasaRaporData>(
                JsonSerializer.Deserialize<KasaRaporData>(saved.KasaRaporDataJson!));
            Assert.Equal(2, payload.PayloadVersion);
            Assert.Equal(FullRoundTripDate, payload.Tarih);
            Assert.Equal("Aksam", payload.KasaTuru);
            Assert.Equal(1234.5678m, payload.BankadanCekilen);
            Assert.NotNull(payload.ImmutableAudit);
            Assert.Equal(101.123456m, payload.ImmutableAudit.GuneAitEksikFazlaTahsilat);
            Assert.Equal(0m, payload.ImmutableAudit.GuneAitEksikFazlaHarc);
            Assert.Equal(-17.7654321m, payload.ImmutableAudit.TakipKasaEtkisiNet);
            Assert.Equal(3, payload.ImmutableAudit.TakipteSayisi);
            Assert.Null(payload.ImmutableAudit.BreakdownMesajTahsilat);
            Assert.Equal(string.Empty, payload.ImmutableAudit.BreakdownMesajHarc);

            Assert.True(payload.ImmutableAuditDetails.HasValue);
            var details = payload.ImmutableAuditDetails.Value
                .Deserialize<HesapKontrolImmutableAuditDetails>();
            Assert.NotNull(details);
            Assert.True(HesapKontrolImmutableAuditDetailsValidator.TryValidate(details, out var error), error);
            Assert.Equal(auditSnapshot.Details.Records, details.Records);
            Assert.Equal(auditSnapshot.Details.Groups.AktifKayitlar, details.Groups.AktifKayitlar);
            Assert.Equal(auditSnapshot.Details.Groups.OncekiAciklar, details.Groups.OncekiAciklar);
            Assert.Equal(auditSnapshot.Details.Groups.BugunCozulenler, details.Groups.BugunCozulenler);
            Assert.Equal(auditSnapshot.Details.Groups.ReconciliationKayitlar, details.Groups.ReconciliationKayitlar);
            Assert.Equal(auditSnapshot.Details.Groups.TakipteKayitlar, details.Groups.TakipteKayitlar);
            Assert.Equal(auditSnapshot.Details.Groups.BugunTakipCozulenler, details.Groups.BugunTakipCozulenler);

            Assert.Null(details.Records[0].DosyaNo);
            Assert.Equal(string.Empty, details.Records[0].BirimAdi);
            Assert.Null(details.Records[0].TespitEdilenTip);
            Assert.True(details.Records[0].TakipBaslangicTarihi.HasValue);
            Assert.Null(details.Records[0].CozulmeTarihi);
            Assert.Null(details.Records[0].OnayTarihi);
            Assert.Equal(0m, details.Records[1].Tutar);
            Assert.Equal(string.Empty, details.Records[1].DosyaNo);
            Assert.Null(details.Records[1].BirimAdi);
            Assert.True(details.Records[1].CozulmeTarihi.HasValue);
            Assert.Null(details.Records[1].OnayTarihi);
            Assert.Equal(
                new DateTime(2060, 4, 26, 12, 34, 56, 789, DateTimeKind.Utc).AddTicks(1234),
                details.Records[2].OnayTarihi);
        }
        finally
        {
            await CleanupDateAsync(FullRoundTripDate);
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [SqlServerFact]
    public async Task SaveReport_PersistedJsonTree_ContainsReportMetadataButNoAuditActorPathOrSourceProperties()
    {
        var webRoot = CreateWebRoot();

        try
        {
            await using var saveContext = _fixture.CreateContext();
            var snapshotService = new CalculatedKasaSnapshotService(
                saveContext,
                NullLogger<CalculatedKasaSnapshotService>.Instance);
            var controller = CreateController(
                webRoot,
                snapshotService,
                JsonSecurityDate,
                17,
                "json-security-server-actor",
                CreateFullAuditSnapshot(JsonSecurityDate));

            var result = await controller.SaveReport(
                NewSaveModel(JsonSecurityDate, "json-security-client-spoof"),
                CancellationToken.None);
            AssertSuccessful(result);

            await using var readContext = _fixture.CreateContext();
            var savedJson = await readContext.CalculatedKasaSnapshots
                .AsNoTracking()
                .Where(snapshot => snapshot.RaporTarihi == JsonSecurityDate)
                .Select(snapshot => snapshot.KasaRaporDataJson)
                .SingleAsync();
            using var payload = JsonDocument.Parse(savedJson!);
            var propertyNames = EnumeratePropertyNames(payload.RootElement).ToArray();
            Assert.Equal(
                "C4.2C actor isolation",
                payload.RootElement.GetProperty("KasayiYapan").GetString());
            Assert.True(payload.RootElement.TryGetProperty("Aciklama", out _));
            Assert.Equal(string.Empty, payload.RootElement.GetProperty("GunlukNot").GetString());
            var forbidden = new[]
            {
                "CreatedBy",
                "CreatedByUserId",
                "TrackingStartedBy",
                "TrackingStartedByUserId",
                "ResolvedBy",
                "ResolvedByUserId",
                "ApprovedBy",
                "ApprovedByUserId",
                "CancelledBy",
                "CancelledByUserId",
                "CalculatedBy",
                "CalculatedByUserId",
                "DeletedBy",
                "DeletedByUserId",
                "Notlar",
                "Description",
                "Notes",
                "PhysicalPath",
                "SourcePath",
                "ArchivePath",
                "CurrentPath",
                "SourceMetadata"
            };

            foreach (var propertyName in forbidden)
            {
                Assert.DoesNotContain(
                    propertyNames,
                    actual => actual.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
            }
            Assert.Contains(
                propertyNames,
                actual => actual.Equals("MuhabereNo", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await CleanupDateAsync(JsonSecurityDate);
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [SqlServerFact]
    public async Task SaveReport_WhenImmutableAuditFails_LeavesNoSnapshotVersionOrPartialPayload()
    {
        var webRoot = CreateWebRoot();

        try
        {
            await using (var seed = _fixture.CreateContext())
            {
                seed.DailyCalculationResults.Add(new DailyCalculationResult
                {
                    Id = Guid.NewGuid(),
                    ForDate = AuditFailureDate,
                    KasaTuru = "Aksam",
                    CalculatedVersion = 7,
                    ResultsJson = "{\"stable\":\"before-audit-failure\"}",
                    InputsFingerprint = "audit-failure-stable-fingerprint",
                    CalculatedAt = new DateTime(2060, 4, 28, 8, 30, 0, DateTimeKind.Utc)
                });
                await seed.SaveChangesAsync();
            }

            await using (var saveContext = _fixture.CreateContext())
            {
                var snapshotService = new CalculatedKasaSnapshotService(
                    saveContext,
                    NullLogger<CalculatedKasaSnapshotService>.Instance);
                var controller = CreateController(
                    webRoot,
                    snapshotService,
                    AuditFailureDate,
                    17,
                    "audit-failure-server-actor",
                    auditFailure: new InvalidOperationException("injected immutable audit failure"));

                var result = await controller.SaveReport(
                    NewSaveModel(AuditFailureDate, "audit-failure-client-spoof"),
                    CancellationToken.None);
                var response = Assert.IsType<JsonResult>(result);
                using var responseJson = JsonDocument.Parse(JsonSerializer.Serialize(response.Value));
                Assert.False(responseJson.RootElement.GetProperty("ok").GetBoolean());
            }

            await using var readContext = _fixture.CreateContext();
            Assert.False(await readContext.CalculatedKasaSnapshots
                .AsNoTracking()
                .AnyAsync(snapshot => snapshot.RaporTarihi == AuditFailureDate));
            var daily = await readContext.DailyCalculationResults
                .AsNoTracking()
                .SingleAsync(result =>
                    result.ForDate == AuditFailureDate
                    && result.KasaTuru == "Aksam");
            Assert.Equal(7, daily.CalculatedVersion);
            Assert.Equal("{\"stable\":\"before-audit-failure\"}", daily.ResultsJson);
            Assert.Equal("audit-failure-stable-fingerprint", daily.InputsFingerprint);
        }
        finally
        {
            await CleanupDateAsync(AuditFailureDate);
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [SqlServerFact]
    public async Task SaveAsync_WhenSqlConstraintFails_RollsBackVersionAndAllRelationalMutations()
    {
        var originalId = Guid.Parse("00000000-0000-0000-0000-000000000201");
        var failedId = Guid.Parse("00000000-0000-0000-0000-000000000202");

        try
        {
            await using (var seed = _fixture.CreateContext())
            {
                seed.CalculatedKasaSnapshots.Add(new CalculatedKasaSnapshot
                {
                    Id = originalId,
                    RaporTarihi = SqlRollbackDate,
                    KasaTuru = KasaRaporTuru.Aksam,
                    CalculatedAtUtc = new DateTime(2060, 4, 29, 8, 0, 0, DateTimeKind.Utc),
                    CalculatedByUserId = 17,
                    CalculatedBy = "original-actor",
                    Version = 4,
                    IsActive = true,
                    InputsJson = "{\"stable\":1}",
                    OutputsJson = "{\"stable\":2}",
                    KasaRaporDataJson = "{\"PayloadVersion\":2,\"stable\":true}"
                });
                seed.DailyCalculationResults.Add(new DailyCalculationResult
                {
                    Id = Guid.NewGuid(),
                    ForDate = SqlRollbackDate,
                    KasaTuru = "Aksam",
                    CalculatedVersion = 4,
                    ResultsJson = "{\"stable\":2}",
                    InputsFingerprint = "rollback-stable-fingerprint",
                    CalculatedAt = new DateTime(2060, 4, 29, 8, 0, 0, DateTimeKind.Utc)
                });
                await seed.SaveChangesAsync();
            }

            await using (var failingContext = _fixture.CreateContext())
            {
                var service = new CalculatedKasaSnapshotService(
                    failingContext,
                    NullLogger<CalculatedKasaSnapshotService>.Instance);
                var failedSnapshot = new CalculatedKasaSnapshot
                {
                    Id = failedId,
                    RaporTarihi = SqlRollbackDate,
                    KasaTuru = KasaRaporTuru.Aksam,
                    InputsJson = null!,
                    OutputsJson = "{\"failed\":true}",
                    KasaRaporDataJson = "{\"PayloadVersion\":2,\"partial\":true}"
                };

                await Assert.ThrowsAsync<DbUpdateException>(() =>
                    service.SaveAsync(failedSnapshot, 29, "failed-actor", CancellationToken.None));
            }

            await using var readContext = _fixture.CreateContext();
            var snapshots = await readContext.CalculatedKasaSnapshots
                .AsNoTracking()
                .Where(snapshot => snapshot.RaporTarihi == SqlRollbackDate)
                .ToArrayAsync();
            var original = Assert.Single(snapshots);
            Assert.Equal(originalId, original.Id);
            Assert.True(original.IsActive);
            Assert.Equal(4, original.Version);
            Assert.Equal("{\"PayloadVersion\":2,\"stable\":true}", original.KasaRaporDataJson);
            Assert.DoesNotContain(snapshots, snapshot => snapshot.Id == failedId);
            Assert.DoesNotContain(snapshots, snapshot =>
                snapshot.KasaRaporDataJson != null
                && snapshot.KasaRaporDataJson.Contains("partial", StringComparison.Ordinal));

            var daily = await readContext.DailyCalculationResults
                .AsNoTracking()
                .SingleAsync(result =>
                    result.ForDate == SqlRollbackDate
                    && result.KasaTuru == "Aksam");
            Assert.Equal(4, daily.CalculatedVersion);
            Assert.Equal("{\"stable\":2}", daily.ResultsJson);
            Assert.Equal("rollback-stable-fingerprint", daily.InputsFingerprint);
        }
        finally
        {
            await CleanupDateAsync(SqlRollbackDate);
        }
    }

    [SqlServerFact]
    public async Task SaveReport_ActorChanges_DoNotChangeFinancialJsonOrFingerprint()
    {
        var firstWebRoot = CreateWebRoot();
        var secondWebRoot = CreateWebRoot();

        try
        {
            await using (var seed = _fixture.CreateContext())
            {
                seed.DailyCalculationResults.Add(new DailyCalculationResult
                {
                    Id = Guid.NewGuid(),
                    ForDate = ActorComparisonDate,
                    KasaTuru = "Aksam",
                    CalculatedVersion = 0,
                    ResultsJson = "{}",
                    InputsFingerprint = "financial-fingerprint-c42c",
                    CalculatedAt = DateTime.UtcNow
                });
                await seed.SaveChangesAsync();
            }

            await using (var firstContext = _fixture.CreateContext())
            {
                var firstService = new CalculatedKasaSnapshotService(
                    firstContext,
                    NullLogger<CalculatedKasaSnapshotService>.Instance);
                var firstController = CreateController(
                    firstWebRoot, firstService, ActorComparisonDate, 17, "first-server-actor");
                var firstResult = await firstController.SaveReport(
                    NewSaveModel(ActorComparisonDate, "first-client-spoof"),
                    CancellationToken.None);
                AssertSuccessful(firstResult);
            }

            await using (var secondContext = _fixture.CreateContext())
            {
                var secondService = new CalculatedKasaSnapshotService(
                    secondContext,
                    NullLogger<CalculatedKasaSnapshotService>.Instance);
                var secondController = CreateController(
                    secondWebRoot, secondService, ActorComparisonDate, 29, "second-server-actor");
                var secondResult = await secondController.SaveReport(
                    NewSaveModel(ActorComparisonDate, "second-client-spoof"),
                    CancellationToken.None);
                AssertSuccessful(secondResult);
            }

            await using var readContext = _fixture.CreateContext();
            var versions = await readContext.CalculatedKasaSnapshots
                .AsNoTracking()
                .Where(snapshot =>
                    snapshot.RaporTarihi == ActorComparisonDate
                    && snapshot.KasaTuru == KasaRaporTuru.Aksam)
                .OrderBy(snapshot => snapshot.Version)
                .ToArrayAsync();
            var saved = Assert.Single(versions);
            Assert.Equal((1, 17, "first-server-actor"),
                (saved.Version, saved.CalculatedByUserId, saved.CalculatedBy));
            Assert.True(saved.IsActive);
            Assert.DoesNotContain("second-server-actor", saved.KasaRaporDataJson, StringComparison.Ordinal);

            using var payload = JsonDocument.Parse(saved.KasaRaporDataJson!);
            Assert.Equal(
                "C4.2C actor isolation",
                payload.RootElement.GetProperty("KasayiYapan").GetString());

            var daily = await readContext.DailyCalculationResults
                .AsNoTracking()
                .SingleAsync(result =>
                    result.ForDate == ActorComparisonDate
                    && result.KasaTuru == "Aksam");
            Assert.Equal("financial-fingerprint-c42c", daily.InputsFingerprint);
            Assert.Equal(1, daily.CalculatedVersion);
            Assert.Equal(saved.OutputsJson, daily.ResultsJson);
        }
        finally
        {
            await CleanupDateAsync(ActorComparisonDate);
            Directory.Delete(firstWebRoot, recursive: true);
            Directory.Delete(secondWebRoot, recursive: true);
        }
    }

    [SqlServerFact]
    public async Task LoadSnapshot_LegacyV0V1V2CashierProperty_RemainsReadable()
    {
        for (var payloadVersion = 0; payloadVersion <= 2; payloadVersion++)
        {
            var reportDate = new DateOnly(2060, 4, 23 + payloadVersion);
            var webRoot = CreateWebRoot();

            try
            {
                var legacyPayload = new KasaRaporData
                {
                    PayloadVersion = payloadVersion,
                    Tarih = reportDate,
                    KasaTuru = "Aksam",
                    KasayiYapan = "legacy-cashier",
                    Aciklama = "legacy-description",
                    GunlukNot = "legacy-daily-note",
                    MuhabereNo = "MUH/2060-0042",
                    BankadanCekilen = 9876.54321m,
                    KasadaKalacakHedef = 0m,
                    GuneAitEksikFazlaTahsilat = 88.99m,
                    ImmutableAudit = payloadVersion >= 1
                        ? new KasaImmutableAuditData()
                        : null,
                    ImmutableAuditDetails = payloadVersion == 2
                        ? JsonSerializer.SerializeToElement(EmptyDetails())
                        : null
                };
                var legacyJson = JsonSerializer.Serialize(legacyPayload);
                Assert.Contains("\"KasayiYapan\"", legacyJson, StringComparison.Ordinal);

                var snapshot = new CalculatedKasaSnapshot
                {
                    Id = Guid.NewGuid(),
                    RaporTarihi = reportDate,
                    KasaTuru = KasaRaporTuru.Aksam,
                    CalculatedByUserId = 71,
                    CalculatedBy = "relational-creator",
                    Version = 1,
                    IsActive = true,
                    InputsJson = "{}",
                    OutputsJson = "{}",
                    KasaRaporDataJson = legacyJson
                };

                await using var context = _fixture.CreateContext();
                context.CalculatedKasaSnapshots.Add(snapshot);
                await context.SaveChangesAsync();
                var snapshotService = new CalculatedKasaSnapshotService(
                    context,
                    NullLogger<CalculatedKasaSnapshotService>.Instance);
                var controller = CreateController(
                    webRoot, snapshotService, reportDate, 99, "current-reader");

                var result = await controller.LoadSnapshot(snapshot.Id, CancellationToken.None);

                var view = Assert.IsType<ViewResult>(result);
                var model = Assert.IsType<KasaPreviewViewModel>(view.Model);
                Assert.Equal(reportDate, model.SelectedDate);
                Assert.Equal("Aksam", model.KasaType);
                Assert.Equal(9876.54321m, model.BankadanCekilen);
                Assert.Equal(0m, model.KasadaKalacakHedef);
                Assert.Equal(
                    payloadVersion == 0 ? 88.99m : 0m,
                    model.GuneAitEksikFazlaTahsilat);
                Assert.Equal(payloadVersion, model.LoadedAuditPayloadVersion);
                Assert.Equal("legacy-cashier", model.KasayiYapan);
                Assert.Equal("legacy-description", model.Aciklama);
                Assert.Equal("legacy-daily-note", model.GunlukKasaNotu);
                Assert.Equal("MUH/2060-0042", model.MuhabereNo);
                if (payloadVersion == 0)
                {
                    Assert.False(model.HasImmutableAuditData);
                }
                else
                {
                    Assert.True(model.HasImmutableAuditData);
                    if (payloadVersion == 2)
                        Assert.True(model.HasImmutableAuditRecordDetails);
                }
            }
            finally
            {
                await CleanupDateAsync(reportDate);
                Directory.Delete(webRoot, recursive: true);
            }
        }
    }

    [SqlServerFact]
    public async Task LoadSnapshot_HistoricalPayloadRemainsImmutableAcrossLiveHkMutationsAndScopes()
    {
        var webRoot = CreateWebRoot();
        await using var hkScope = new SqlServerHesapKontrolScope(_fixture);
        var hkContext = hkScope.Context;
        var tracked = NewHistoricalRecord(
            HistoricalImmutabilityDate,
            BankaHesapTuru.Tahsilat,
            KayitYonu.Eksik,
            101.25m,
            KayitDurumu.Takipte,
            trackingDate: HistoricalImmutabilityDate);
        var open = NewHistoricalRecord(
            HistoricalImmutabilityDate,
            BankaHesapTuru.Harc,
            KayitYonu.Fazla,
            202.50m,
            KayitDurumu.Acik);
        var otherDate = NewHistoricalRecord(
            HistoricalIsolationDate,
            BankaHesapTuru.Tahsilat,
            KayitYonu.Fazla,
            303.75m,
            KayitDurumu.Acik);
        hkScope.AddRange(tracked, open, otherDate);

        try
        {
            await hkContext.SaveChangesAsync();
            var canonicalAnalysis = new BankaHesapKontrolService(
                hkContext,
                Mock.Of<IComparisonService>(),
                Mock.Of<IImportOrchestrator>(),
                Mock.Of<IHesapKontrolSourceResolver>(),
                NullLogger<BankaHesapKontrolService>.Instance);

            async Task<Guid> SaveSnapshotAsync(DateOnly date, string kasaType)
            {
                await using var saveContext = _fixture.CreateContext();
                var snapshotService = new CalculatedKasaSnapshotService(
                    saveContext,
                    NullLogger<CalculatedKasaSnapshotService>.Instance);
                var controller = CreateController(
                    webRoot,
                    snapshotService,
                    date,
                    17,
                    $"historical-{kasaType.ToLowerInvariant()}-actor",
                    analysisService: canonicalAnalysis);
                var model = NewSaveModel(date, "client-spoof");
                model.KasaType = kasaType;

                AssertSuccessful(await controller.SaveReport(model, CancellationToken.None));

                var kasaTuru = Enum.Parse<KasaRaporTuru>(kasaType, ignoreCase: true);
                return await saveContext.CalculatedKasaSnapshots
                    .Where(snapshot => snapshot.RaporTarihi == date && snapshot.KasaTuru == kasaTuru)
                    .Select(snapshot => snapshot.Id)
                    .SingleAsync();
            }

            var aksamId = await SaveSnapshotAsync(HistoricalImmutabilityDate, "Aksam");
            var sabahId = await SaveSnapshotAsync(HistoricalImmutabilityDate, "Sabah");
            var otherDateId = await SaveSnapshotAsync(HistoricalIsolationDate, "Aksam");

            await using var beforeContext = _fixture.CreateContext();
            var beforeSnapshots = (await beforeContext.CalculatedKasaSnapshots
                    .AsNoTracking()
                    .Where(snapshot => snapshot.RaporTarihi == HistoricalImmutabilityDate
                        || snapshot.RaporTarihi == HistoricalIsolationDate)
                    .ToListAsync())
                .ToDictionary(
                    snapshot => snapshot.Id,
                    snapshot => JsonSerializer.Serialize(new
                    {
                        snapshot.RaporTarihi,
                        snapshot.KasaTuru,
                        snapshot.Version,
                        snapshot.IsActive,
                        snapshot.KasaRaporDataJson,
                        snapshot.InputsJson,
                        snapshot.OutputsJson,
                        snapshot.CalculatedAtUtc
                    }));
            var beforeDaily = (await beforeContext.DailyCalculationResults
                    .AsNoTracking()
                    .Where(result => result.ForDate == HistoricalImmutabilityDate
                        || result.ForDate == HistoricalIsolationDate)
                    .ToListAsync())
                .ToDictionary(
                    result => (result.ForDate, result.KasaTuru),
                    result => JsonSerializer.Serialize(new
                    {
                        result.CalculatedVersion,
                        result.InputsFingerprint,
                        result.ResultsJson,
                        result.CalculatedAt
                    }));
            var savedPayload = JsonSerializer.Deserialize<KasaRaporData>(
                (await beforeContext.CalculatedKasaSnapshots
                    .AsNoTracking()
                    .SingleAsync(snapshot => snapshot.Id == aksamId)).KasaRaporDataJson!);
            Assert.NotNull(savedPayload?.ImmutableAuditDetails);
            var savedDetails = savedPayload.ImmutableAuditDetails.Value
                .Deserialize<HesapKontrolImmutableAuditDetails>();
            Assert.NotNull(savedDetails);
            Assert.Contains(savedDetails.Records, record =>
                record.KayitId == tracked.Id
                && record.KaydetmeAnindakiDurum == KayitDurumu.Takipte);
            Assert.Contains(savedDetails.Records, record =>
                record.KayitId == open.Id
                && record.KaydetmeAnindakiDurum == KayitDurumu.Acik);

            tracked.Durum = KayitDurumu.Cozuldu;
            tracked.CozulmeTarihi = HistoricalImmutabilityDate.AddDays(1);
            open.Durum = KayitDurumu.Iptal;
            open.KullaniciOnay = true;
            open.OnayTarihi = new DateTime(2060, 5, 1, 14, 30, 0, DateTimeKind.Utc);
            var addedLater = NewHistoricalRecord(
                HistoricalImmutabilityDate,
                BankaHesapTuru.Tahsilat,
                KayitYonu.Fazla,
                404m,
                KayitDurumu.Onaylandi);
            hkScope.AddRange(addedLater);
            await hkContext.SaveChangesAsync();

            await using var loadContext = _fixture.CreateContext();
            var loadService = new CalculatedKasaSnapshotService(
                loadContext,
                NullLogger<CalculatedKasaSnapshotService>.Instance);
            var noLiveAnalysis = new Mock<IBankaHesapKontrolService>(MockBehavior.Strict);

            async Task<KasaPreviewViewModel> LoadAsync(Guid id, DateOnly date)
            {
                var controller = CreateController(
                    webRoot,
                    loadService,
                    date,
                    99,
                    "historical-reader",
                    analysisService: noLiveAnalysis.Object);
                var view = Assert.IsType<ViewResult>(
                    await controller.LoadSnapshot(id, CancellationToken.None));
                return Assert.IsType<KasaPreviewViewModel>(view.Model);
            }

            var loadedAksam = await LoadAsync(aksamId, HistoricalImmutabilityDate);
            var loadedSabah = await LoadAsync(sabahId, HistoricalImmutabilityDate);
            var loadedOtherDate = await LoadAsync(otherDateId, HistoricalIsolationDate);
            noLiveAnalysis.VerifyNoOtherCalls();

            Assert.Equal((HistoricalImmutabilityDate, "Aksam"),
                (loadedAksam.SelectedDate, loadedAksam.KasaType));
            Assert.Equal((HistoricalImmutabilityDate, "Sabah"),
                (loadedSabah.SelectedDate, loadedSabah.KasaType));
            Assert.Equal((HistoricalIsolationDate, "Aksam"),
                (loadedOtherDate.SelectedDate, loadedOtherDate.KasaType));
            Assert.Contains(loadedAksam.ImmutableAuditRecords, record =>
                record.KayitId == tracked.Id
                && record.KaydetmeAnindakiDurum == KayitDurumu.Takipte);
            Assert.Contains(loadedAksam.ImmutableAuditRecords, record =>
                record.KayitId == open.Id
                && record.KaydetmeAnindakiDurum == KayitDurumu.Acik);
            Assert.DoesNotContain(loadedAksam.ImmutableAuditRecords, record =>
                record.KayitId == addedLater.Id);

            var afterSnapshots = (await loadContext.CalculatedKasaSnapshots
                    .AsNoTracking()
                    .Where(snapshot => snapshot.RaporTarihi == HistoricalImmutabilityDate
                        || snapshot.RaporTarihi == HistoricalIsolationDate)
                    .ToListAsync())
                .ToDictionary(
                    snapshot => snapshot.Id,
                    snapshot => JsonSerializer.Serialize(new
                    {
                        snapshot.RaporTarihi,
                        snapshot.KasaTuru,
                        snapshot.Version,
                        snapshot.IsActive,
                        snapshot.KasaRaporDataJson,
                        snapshot.InputsJson,
                        snapshot.OutputsJson,
                        snapshot.CalculatedAtUtc
                    }));
            var afterDaily = (await loadContext.DailyCalculationResults
                    .AsNoTracking()
                    .Where(result => result.ForDate == HistoricalImmutabilityDate
                        || result.ForDate == HistoricalIsolationDate)
                    .ToListAsync())
                .ToDictionary(
                    result => (result.ForDate, result.KasaTuru),
                    result => JsonSerializer.Serialize(new
                    {
                        result.CalculatedVersion,
                        result.InputsFingerprint,
                        result.ResultsJson,
                        result.CalculatedAt
                    }));

            Assert.Equal(3, beforeSnapshots.Count);
            Assert.Equal(3, beforeDaily.Count);
            Assert.Equal(beforeSnapshots.Keys.OrderBy(id => id), afterSnapshots.Keys.OrderBy(id => id));
            foreach (var before in beforeSnapshots)
                Assert.Equal(before.Value, afterSnapshots[before.Key]);
            Assert.Equal(
                beforeDaily.Keys.OrderBy(key => key.ForDate).ThenBy(key => key.KasaTuru),
                afterDaily.Keys.OrderBy(key => key.ForDate).ThenBy(key => key.KasaTuru));
            foreach (var before in beforeDaily)
                Assert.Equal(before.Value, afterDaily[before.Key]);
        }
        finally
        {
            await CleanupDateAsync(HistoricalImmutabilityDate);
            await CleanupDateAsync(HistoricalIsolationDate);
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [SqlServerFact]
    public async Task SaveReport_HistoricalSourceAfterHkMutation_PreservesAuditThroughSqlAndReload()
    {
        var webRoot = CreateWebRoot();
        await using var hkScope = new SqlServerHesapKontrolScope(_fixture);
        var hkContext = hkScope.Context;
        var tahsilat = NewHistoricalRecord(
            HistoricalResaveDate,
            BankaHesapTuru.Tahsilat,
            KayitYonu.Eksik,
            3000m,
            KayitDurumu.Takipte,
            trackingDate: HistoricalResaveDate);
        var harc = NewHistoricalRecord(
            HistoricalResaveDate,
            BankaHesapTuru.Harc,
            KayitYonu.Eksik,
            29873.80m,
            KayitDurumu.Takipte,
            trackingDate: HistoricalResaveDate);
        hkScope.AddRange(tahsilat, harc);

        try
        {
            await hkContext.SaveChangesAsync();
            var canonicalAnalysis = new BankaHesapKontrolService(
                hkContext,
                Mock.Of<IComparisonService>(),
                Mock.Of<IImportOrchestrator>(),
                Mock.Of<IHesapKontrolSourceResolver>(),
                NullLogger<BankaHesapKontrolService>.Instance);

            Guid firstId;
            await using (var firstContext = _fixture.CreateContext())
            {
                var firstController = CreateController(
                    webRoot,
                    new CalculatedKasaSnapshotService(
                        firstContext, NullLogger<CalculatedKasaSnapshotService>.Instance),
                    HistoricalResaveDate,
                    17,
                    "historical-v1-actor",
                    analysisService: canonicalAnalysis);
                SetSaveForm(
                    firstController,
                    "{\"bankadan_cekilen\":1234.5678}",
                    "{\"financial_output\":2469.1356}",
                    "1234.5678",
                    hiddenAuditValue: "0");
                var firstModel = NewSaveModel(HistoricalResaveDate, "client-v1-spoof");
                firstModel.KasaType = "Sabah";

                AssertSuccessful(await firstController.SaveReport(
                    firstModel, CancellationToken.None));
                firstId = await firstContext.CalculatedKasaSnapshots
                    .Where(snapshot => snapshot.RaporTarihi == HistoricalResaveDate
                        && snapshot.KasaTuru == KasaRaporTuru.Sabah)
                    .Select(snapshot => snapshot.Id)
                    .SingleAsync();
            }

            CalculatedKasaSnapshot firstBeforeMutation;
            await using (var beforeContext = _fixture.CreateContext())
            {
                firstBeforeMutation = await beforeContext.CalculatedKasaSnapshots
                    .AsNoTracking()
                    .SingleAsync(snapshot => snapshot.Id == firstId);
            }
            var firstPayload = JsonSerializer.Deserialize<KasaRaporData>(
                firstBeforeMutation.KasaRaporDataJson!);
            Assert.NotNull(firstPayload?.ImmutableAudit);
            Assert.Equal(1, firstBeforeMutation.Version);
            Assert.True(firstBeforeMutation.IsActive);
            Assert.Equal(-3000m, firstPayload.GuneAitEksikFazlaTahsilat);
            Assert.Equal(-29873.80m, firstPayload.GuneAitEksikFazlaHarc);
            Assert.Equal(-3000m, firstPayload.ImmutableAudit.GuneAitEksikFazlaTahsilat);
            Assert.Equal(-29873.80m, firstPayload.ImmutableAudit.GuneAitEksikFazlaHarc);

            tahsilat.Durum = KayitDurumu.Cozuldu;
            tahsilat.CozulmeTarihi = HistoricalResaveDate.AddDays(1);
            harc.Durum = KayitDurumu.Cozuldu;
            harc.CozulmeTarihi = HistoricalResaveDate.AddDays(1);
            await hkContext.SaveChangesAsync();

            var mutableLiveAudit = await canonicalAnalysis.GetImmutableAuditSnapshotAsync(
                HistoricalResaveDate, CancellationToken.None);
            Assert.Equal(0m, mutableLiveAudit.Summary.GuneAitEksikFazlaTahsilat);
            Assert.Equal(0m, mutableLiveAudit.Summary.GuneAitEksikFazlaHarc);

            KasaPreviewViewModel loadedFirst;
            await using (var loadFirstContext = _fixture.CreateContext())
            {
                var noLiveLoad = new Mock<IBankaHesapKontrolService>(MockBehavior.Strict);
                var loadFirstController = CreateController(
                    webRoot,
                    new CalculatedKasaSnapshotService(
                        loadFirstContext, NullLogger<CalculatedKasaSnapshotService>.Instance),
                    HistoricalResaveDate,
                    29,
                    "historical-v1-reader",
                    analysisService: noLiveLoad.Object);
                var view = Assert.IsType<ViewResult>(await loadFirstController.LoadSnapshot(
                    firstId, CancellationToken.None));
                loadedFirst = Assert.IsType<KasaPreviewViewModel>(view.Model);
                Assert.Equal(firstId, loadedFirst.LoadedSnapshotId);
                Assert.Equal(-3000m, loadedFirst.GuneAitEksikFazlaTahsilat);
                Assert.Equal(-29873.80m, loadedFirst.GuneAitEksikFazlaHarc);
                noLiveLoad.VerifyNoOtherCalls();
            }

            Guid secondId;
            await using (var secondContext = _fixture.CreateContext())
            {
                var secondController = CreateController(
                    webRoot,
                    new CalculatedKasaSnapshotService(
                        secondContext, NullLogger<CalculatedKasaSnapshotService>.Instance),
                    HistoricalResaveDate,
                    29,
                    "historical-v2-actor",
                    analysisService: canonicalAnalysis);
                SetSaveForm(
                    secondController,
                    "{\"bankadan_cekilen\":2234.5678}",
                    "{\"financial_output\":4469.1356}",
                    "2234.5678",
                    hiddenAuditValue: "999999");
                loadedFirst.GuneAitEksikFazlaTahsilat = 777777m;
                loadedFirst.GuneAitEksikFazlaHarc = 888888m;
                loadedFirst.TakipteEksikTahsilat = 999999m;

                AssertSuccessful(await secondController.SaveReport(
                    loadedFirst, CancellationToken.None));
                secondId = await secondContext.CalculatedKasaSnapshots
                    .Where(snapshot => snapshot.RaporTarihi == HistoricalResaveDate
                        && snapshot.KasaTuru == KasaRaporTuru.Sabah
                        && snapshot.IsActive)
                    .Select(snapshot => snapshot.Id)
                    .SingleAsync();
            }

            CalculatedKasaSnapshot[] versions;
            DailyCalculationResult daily;
            await using (var inspectContext = _fixture.CreateContext())
            {
                versions = await inspectContext.CalculatedKasaSnapshots
                    .AsNoTracking()
                    .Where(snapshot => snapshot.RaporTarihi == HistoricalResaveDate
                        && snapshot.KasaTuru == KasaRaporTuru.Sabah)
                    .OrderBy(snapshot => snapshot.Version)
                    .ToArrayAsync();
                daily = await inspectContext.DailyCalculationResults
                    .AsNoTracking()
                    .SingleAsync(result => result.ForDate == HistoricalResaveDate
                        && result.KasaTuru == "Sabah");
            }

            Assert.Equal(2, versions.Length);
            Assert.Equal(firstId, versions[0].Id);
            Assert.Equal(secondId, versions[1].Id);
            Assert.False(versions[0].IsActive);
            Assert.True(versions[1].IsActive);
            Assert.Equal("historical-v1-actor", versions[0].CalculatedBy);
            Assert.Equal("historical-v2-actor", versions[1].CalculatedBy);
            Assert.Equal(firstBeforeMutation.KasaRaporDataJson, versions[0].KasaRaporDataJson);
            Assert.NotEqual(versions[0].InputsJson, versions[1].InputsJson);
            Assert.NotEqual(versions[0].OutputsJson, versions[1].OutputsJson);

            var secondPayload = JsonSerializer.Deserialize<KasaRaporData>(
                versions[1].KasaRaporDataJson!);
            Assert.NotNull(secondPayload?.ImmutableAudit);
            Assert.Equal(-3000m, secondPayload.GuneAitEksikFazlaTahsilat);
            Assert.Equal(-29873.80m, secondPayload.GuneAitEksikFazlaHarc);
            Assert.Equal(-3000m, secondPayload.ImmutableAudit.GuneAitEksikFazlaTahsilat);
            Assert.Equal(-29873.80m, secondPayload.ImmutableAudit.GuneAitEksikFazlaHarc);
            Assert.Equal(2, daily.CalculatedVersion);
            Assert.Equal(versions[1].OutputsJson, daily.ResultsJson);

            KasaPreviewViewModel loadedSecond;
            await using (var loadSecondContext = _fixture.CreateContext())
            {
                var noLiveLoad = new Mock<IBankaHesapKontrolService>(MockBehavior.Strict);
                var loadSecondController = CreateController(
                    webRoot,
                    new CalculatedKasaSnapshotService(
                        loadSecondContext, NullLogger<CalculatedKasaSnapshotService>.Instance),
                    HistoricalResaveDate,
                    44,
                    "historical-v2-reader",
                    analysisService: noLiveLoad.Object);
                var view = Assert.IsType<ViewResult>(await loadSecondController.LoadSnapshot(
                    secondId, CancellationToken.None));
                loadedSecond = Assert.IsType<KasaPreviewViewModel>(view.Model);
                Assert.Equal(-3000m, loadedSecond.GuneAitEksikFazlaTahsilat);
                Assert.Equal(-29873.80m, loadedSecond.GuneAitEksikFazlaHarc);
                noLiveLoad.VerifyNoOtherCalls();
            }

            await using (var noOpContext = _fixture.CreateContext())
            {
                var noLiveSave = new Mock<IBankaHesapKontrolService>(MockBehavior.Strict);
                var noOpController = CreateController(
                    webRoot,
                    new CalculatedKasaSnapshotService(
                        noOpContext, NullLogger<CalculatedKasaSnapshotService>.Instance),
                    HistoricalResaveDate,
                    55,
                    "historical-noop-actor",
                    analysisService: noLiveSave.Object);
                SetSaveForm(
                    noOpController,
                    versions[1].InputsJson,
                    versions[1].OutputsJson,
                    "2234.5678",
                    hiddenAuditValue: "0");
                SetDynamicSnapshotUrl(noOpController);
                var noOpResult = Assert.IsType<JsonResult>(await noOpController.SaveReport(
                    loadedSecond, CancellationToken.None));
                using var noOpResponse = JsonDocument.Parse(JsonSerializer.Serialize(noOpResult.Value));
                Assert.True(noOpResponse.RootElement.GetProperty("ok").GetBoolean());
                Assert.True(noOpResponse.RootElement.GetProperty("isNoOp").GetBoolean());
                Assert.Contains(secondId.ToString(),
                    noOpResponse.RootElement.GetProperty("redirectUrl").GetString(),
                    StringComparison.OrdinalIgnoreCase);
                noLiveSave.VerifyNoOtherCalls();
            }

            await using var finalContext = _fixture.CreateContext();
            var finalVersions = await finalContext.CalculatedKasaSnapshots
                .AsNoTracking()
                .Where(snapshot => snapshot.RaporTarihi == HistoricalResaveDate
                    && snapshot.KasaTuru == KasaRaporTuru.Sabah)
                .OrderBy(snapshot => snapshot.Version)
                .ToArrayAsync();
            Assert.Equal(2, finalVersions.Length);
            Assert.Equal("historical-v2-actor", finalVersions[1].CalculatedBy);
        }
        finally
        {
            await CleanupDateAsync(HistoricalResaveDate);
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [SqlServerFact]
    public async Task LiveSave_WithoutTodayHkRecords_AlignsPoolAuditOutputsPayloadAndReload()
    {
        var webRoot = CreateWebRoot();
        await using var hkScope = new SqlServerHesapKontrolScope(_fixture);
        var hkContext = hkScope.Context;
        hkScope.AddRange(
            NewHistoricalRecord(
                LivePoolAlignmentDate.AddDays(-1),
                BankaHesapTuru.Tahsilat,
                KayitYonu.Eksik,
                3000m,
                KayitDurumu.Takipte,
                trackingDate: LivePoolAlignmentDate.AddDays(-1)),
            NewHistoricalRecord(
                LivePoolAlignmentDate.AddDays(-1),
                BankaHesapTuru.Harc,
                KayitYonu.Eksik,
                29873.80m,
                KayitDurumu.Takipte,
                trackingDate: LivePoolAlignmentDate.AddDays(-1)));

        try
        {
            await hkContext.SaveChangesAsync();
            Assert.False(await hkContext.HesapKontrolKayitlari.AnyAsync(record =>
                record.AnalizTarihi == LivePoolAlignmentDate));
            var canonicalAnalysis = new BankaHesapKontrolService(
                hkContext,
                Mock.Of<IComparisonService>(),
                Mock.Of<IImportOrchestrator>(),
                Mock.Of<IHesapKontrolSourceResolver>(),
                NullLogger<BankaHesapKontrolService>.Instance);
            var pool = await canonicalAnalysis.GetActiveFollowTotalsAsync(LivePoolAlignmentDate);
            Assert.Equal(-3000m, pool.TahsilatNet);
            Assert.Equal(-29873.80m, pool.HarcNet);

            await using var context = _fixture.CreateContext();
            var controller = CreateController(
                webRoot,
                new CalculatedKasaSnapshotService(
                    context, NullLogger<CalculatedKasaSnapshotService>.Instance),
                LivePoolAlignmentDate,
                71,
                "live-pool-alignment-actor",
                analysisService: canonicalAnalysis);
            SetSaveForm(
                controller,
                "{\"gune_ait_eksik_fazla_tahsilat\":-3000,\"gune_ait_eksik_fazla_harc\":-29873.80}",
                "{\"genel_kasa\":1,\"gune_ait_eksik_fazla_tahsilat\":0,\"gune_ait_eksik_fazla_harc\":0}",
                "0",
                "0");
            var model = NewSaveModel(LivePoolAlignmentDate, "client-value-ignored");
            model.KasaType = "Sabah";

            AssertSuccessful(await controller.SaveReport(model, CancellationToken.None));
            var saved = await context.CalculatedKasaSnapshots.SingleAsync(snapshot =>
                snapshot.RaporTarihi == LivePoolAlignmentDate
                && snapshot.KasaTuru == KasaRaporTuru.Sabah);
            using var outputs = JsonDocument.Parse(saved.OutputsJson);
            Assert.True(outputs.RootElement.EnumerateObject().Any());
            Assert.Equal(-3000m, decimal.Parse(outputs.RootElement
                .GetProperty("gune_ait_eksik_fazla_tahsilat").GetString()!,
                System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(-29873.80m, decimal.Parse(outputs.RootElement
                .GetProperty("gune_ait_eksik_fazla_harc").GetString()!,
                System.Globalization.CultureInfo.InvariantCulture));
            var payload = JsonSerializer.Deserialize<KasaRaporData>(saved.KasaRaporDataJson!);
            Assert.NotNull(payload?.ImmutableAudit);
            Assert.Equal(pool.TahsilatNet, payload.ImmutableAudit.GuneAitEksikFazlaTahsilat);
            Assert.Equal(pool.HarcNet, payload.ImmutableAudit.GuneAitEksikFazlaHarc);
            Assert.Equal(pool.TahsilatNet, payload.GuneAitEksikFazlaTahsilat);
            Assert.Equal(pool.HarcNet, payload.GuneAitEksikFazlaHarc);

            var view = Assert.IsType<ViewResult>(
                await controller.LoadSnapshot(saved.Id, CancellationToken.None));
            var loaded = Assert.IsType<KasaPreviewViewModel>(view.Model);
            Assert.True(loaded.HasResults);
            Assert.NotEmpty(loaded.FormulaRun!.Outputs);
            Assert.Equal(-3000m, loaded.GuneAitEksikFazlaTahsilat);
            Assert.Equal(-29873.80m, loaded.GuneAitEksikFazlaHarc);
        }
        finally
        {
            await CleanupDateAsync(LivePoolAlignmentDate);
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [SqlServerFact]
    public async Task SaveReport_LegacyV1BehindActiveZeroV2_CreatesV3AndThenNoOps()
    {
        var webRoot = CreateWebRoot();
        await using var hkScope = new SqlServerHesapKontrolScope(_fixture);
        var hkContext = hkScope.Context;
        var tahsilat = NewHistoricalRecord(
            LegacyCompatibilityDate, BankaHesapTuru.Tahsilat, KayitYonu.Eksik,
            3000m, KayitDurumu.Takipte, trackingDate: LegacyCompatibilityDate);
        var harc = NewHistoricalRecord(
            LegacyCompatibilityDate, BankaHesapTuru.Harc, KayitYonu.Eksik,
            29873.80m, KayitDurumu.Takipte, trackingDate: LegacyCompatibilityDate);
        hkScope.AddRange(tahsilat, harc);

        try
        {
            await hkContext.SaveChangesAsync();
            var canonicalAnalysis = new BankaHesapKontrolService(
                hkContext,
                Mock.Of<IComparisonService>(),
                Mock.Of<IImportOrchestrator>(),
                Mock.Of<IHesapKontrolSourceResolver>(),
                NullLogger<BankaHesapKontrolService>.Instance);

            Guid v1Id;
            await using (var context = _fixture.CreateContext())
            {
                var controller = CreateController(
                    webRoot,
                    new CalculatedKasaSnapshotService(
                        context, NullLogger<CalculatedKasaSnapshotService>.Instance),
                    LegacyCompatibilityDate, 17, "legacy-v1-actor",
                    analysisService: canonicalAnalysis);
                SetSaveForm(controller, "{\"amount\":1}", "{\"result\":1}", "1", "0");
                var model = NewSaveModel(LegacyCompatibilityDate, "client-v1");
                model.KasaType = "Sabah";

                AssertSuccessful(await controller.SaveReport(model, CancellationToken.None));
                var v1 = await context.CalculatedKasaSnapshots.SingleAsync(snapshot =>
                    snapshot.RaporTarihi == LegacyCompatibilityDate
                    && snapshot.KasaTuru == KasaRaporTuru.Sabah);
                v1Id = v1.Id;
                var payload = JsonSerializer.Deserialize<KasaRaporData>(v1.KasaRaporDataJson!);
                Assert.NotNull(payload?.ImmutableAudit);
                payload.PayloadVersion = 1;
                payload.ImmutableAuditDetails = null;
                v1.KasaRaporDataJson = JsonSerializer.Serialize(payload);
                await context.SaveChangesAsync();
            }

            tahsilat.Durum = KayitDurumu.Cozuldu;
            tahsilat.CozulmeTarihi = LegacyCompatibilityDate.AddDays(1);
            harc.Durum = KayitDurumu.Cozuldu;
            harc.CozulmeTarihi = LegacyCompatibilityDate.AddDays(1);
            await hkContext.SaveChangesAsync();

            await using (var context = _fixture.CreateContext())
            {
                var controller = CreateController(
                    webRoot,
                    new CalculatedKasaSnapshotService(
                        context, NullLogger<CalculatedKasaSnapshotService>.Instance),
                    LegacyCompatibilityDate, 29, "live-v2-actor",
                    analysisService: canonicalAnalysis);
                SetSaveForm(controller, "{\"amount\":2}", "{\"result\":2}", "2", "0");
                var model = NewSaveModel(LegacyCompatibilityDate, "client-v2");
                model.KasaType = "Sabah";

                AssertSuccessful(await controller.SaveReport(model, CancellationToken.None));
            }

            Guid v3Id;
            string v3Inputs;
            string v3Outputs;
            await using (var context = _fixture.CreateContext())
            {
                var noLiveAnalysis = new Mock<IBankaHesapKontrolService>(MockBehavior.Strict);
                var controller = CreateController(
                    webRoot,
                    new CalculatedKasaSnapshotService(
                        context, NullLogger<CalculatedKasaSnapshotService>.Instance),
                    LegacyCompatibilityDate, 44, "historical-v3-actor",
                    analysisService: noLiveAnalysis.Object);
                SetSaveForm(controller, "{\"amount\":3}", "{\"result\":3}", "3", "999999");
                var model = NewSaveModel(LegacyCompatibilityDate, "client-v3");
                model.KasaType = "Sabah";
                model.LoadedSnapshotId = v1Id;
                model.GuneAitEksikFazlaTahsilat = 777777m;
                model.GuneAitEksikFazlaHarc = 888888m;

                AssertSuccessful(await controller.SaveReport(model, CancellationToken.None));
                var v3 = await context.CalculatedKasaSnapshots.SingleAsync(snapshot =>
                    snapshot.RaporTarihi == LegacyCompatibilityDate
                    && snapshot.KasaTuru == KasaRaporTuru.Sabah
                    && snapshot.IsActive);
                v3Id = v3.Id;
                v3Inputs = v3.InputsJson;
                v3Outputs = v3.OutputsJson;
                noLiveAnalysis.VerifyNoOtherCalls();
            }

            await using (var inspectContext = _fixture.CreateContext())
            {
                var versions = await inspectContext.CalculatedKasaSnapshots
                    .AsNoTracking()
                    .Where(snapshot => snapshot.RaporTarihi == LegacyCompatibilityDate
                        && snapshot.KasaTuru == KasaRaporTuru.Sabah)
                    .OrderBy(snapshot => snapshot.Version)
                    .ToArrayAsync();
                Assert.Equal(3, versions.Length);
                Assert.Equal(new[] { 1, 2, 3 }, versions.Select(snapshot => snapshot.Version));
                Assert.False(versions[0].IsActive);
                Assert.False(versions[1].IsActive);
                Assert.True(versions[2].IsActive);
                Assert.Equal(v1Id, versions[0].Id);
                Assert.Equal(v3Id, versions[2].Id);

                var v1Payload = JsonSerializer.Deserialize<KasaRaporData>(versions[0].KasaRaporDataJson!);
                var v2Payload = JsonSerializer.Deserialize<KasaRaporData>(versions[1].KasaRaporDataJson!);
                var v3Payload = JsonSerializer.Deserialize<KasaRaporData>(versions[2].KasaRaporDataJson!);
                Assert.Equal(1, v1Payload!.PayloadVersion);
                Assert.Equal(0m, v2Payload!.ImmutableAudit!.GuneAitEksikFazlaTahsilat);
                Assert.Equal(0m, v2Payload.ImmutableAudit.GuneAitEksikFazlaHarc);
                Assert.Equal(1, v3Payload!.PayloadVersion);
                Assert.False(v3Payload.ImmutableAuditDetails.HasValue);
                Assert.Equal(-3000m, v3Payload.ImmutableAudit!.GuneAitEksikFazlaTahsilat);
                Assert.Equal(-29873.80m, v3Payload.ImmutableAudit.GuneAitEksikFazlaHarc);
            }

            await using (var context = _fixture.CreateContext())
            {
                var noLiveAnalysis = new Mock<IBankaHesapKontrolService>(MockBehavior.Strict);
                var controller = CreateController(
                    webRoot,
                    new CalculatedKasaSnapshotService(
                        context, NullLogger<CalculatedKasaSnapshotService>.Instance),
                    LegacyCompatibilityDate, 55, "noop-actor",
                    analysisService: noLiveAnalysis.Object);
                SetSaveForm(controller, v3Inputs, v3Outputs, "3", "0");
                SetDynamicSnapshotUrl(controller);
                var model = NewSaveModel(LegacyCompatibilityDate, "client-noop");
                model.KasaType = "Sabah";
                model.LoadedSnapshotId = v3Id;

                var result = Assert.IsType<JsonResult>(
                    await controller.SaveReport(model, CancellationToken.None));
                using var response = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
                Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
                Assert.True(response.RootElement.GetProperty("isNoOp").GetBoolean());
                Assert.Equal(3, await context.CalculatedKasaSnapshots.CountAsync(snapshot =>
                    snapshot.RaporTarihi == LegacyCompatibilityDate
                    && snapshot.KasaTuru == KasaRaporTuru.Sabah));
                noLiveAnalysis.VerifyNoOtherCalls();
            }
        }
        finally
        {
            await CleanupDateAsync(LegacyCompatibilityDate);
            Directory.Delete(webRoot, recursive: true);
        }
    }

    private static KasaPreviewController CreateController(
        string webRoot,
        ICalculatedKasaSnapshotService snapshotService,
        DateOnly auditDate,
        int actorUserId,
        string actorUsername,
        HesapKontrolImmutableAuditSnapshot? auditSnapshot = null,
        Exception? auditFailure = null,
        IBankaHesapKontrolService? analysisService = null)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.WebRootPath).Returns(webRoot);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Upload:SubFolder"] = @"Data\Raporlar"
            })
            .Build();
        var defaults = new Mock<IKasaGlobalDefaultsService>();
        defaults.Setup(service => service.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KasaGlobalDefaultsSettings());
        var analysis = analysisService;
        if (analysis is null)
        {
            var analysisMock = new Mock<IBankaHesapKontrolService>();
            var auditSetup = analysisMock.Setup(service => service.GetImmutableAuditSnapshotAsync(
                auditDate, It.IsAny<CancellationToken>()));
            if (auditFailure is null)
            {
                auditSetup.ReturnsAsync(auditSnapshot ?? new HesapKontrolImmutableAuditSnapshot(
                    new EksikFazlaAutoFill(0, 0, 0, 0, 0, 0, false, "empty"),
                    EmptyDetails()));
            }
            else
            {
                auditSetup.ThrowsAsync(auditFailure);
            }
            analysis = analysisMock.Object;
        }
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(value => value.IsAuthenticated).Returns(true);
        currentUser.SetupGet(value => value.UserId).Returns(actorUserId);
        currentUser.SetupGet(value => value.Username).Returns(actorUsername);
        var financialExceptions = new Mock<IFinansalIstisnaService>();
        financialExceptions.Setup(service => service.ListByDateAsync(
                auditDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FinansalIstisna>());

        var controller = new KasaPreviewController(
            Mock.Of<IKasaOrchestrator>(),
            environment.Object,
            configuration,
            Mock.Of<IImportOrchestrator>(),
            Mock.Of<IKasaReportDateRulesService>(),
            defaults.Object,
            analysis,
            currentUser.Object,
            Mock.Of<IHesapKontrolSourceResolver>(),
            Mock.Of<IReportDataBuilder>(),
            Mock.Of<IExportService>(),
            Mock.Of<IKasaValidationService>(),
            Mock.Of<IVergideBirikenLedgerService>(),
            Mock.Of<IDocumentTemplateService>(),
            financialExceptions.Object,
            Mock.Of<IFinansalIstisnaAnomaliService>(),
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<KasaPreviewController>>(),
            Mock.Of<IKasaReadModelService>(),
            snapshotService,
            Mock.Of<IKasaRaporSnapshotService>());

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, actorUsername) },
                "TestAuth"))
        };
        httpContext.Request.Form = new FormCollection(
            new Dictionary<string, StringValues>
            {
                ["SaveRaporAdi"] = "C4.2C actor isolation",
                ["SaveInputsJson"] = "{}",
                ["SaveOutputsJson"] = "{}",
                ["RptGunlukNot"] = "",
                ["RptBankadanCekilen"] = "1234.5678",
                ["ConfirmOverwrite"] = "true"
            });
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(
            httpContext,
            Mock.Of<ITempDataProvider>());
        var url = new Mock<IUrlHelper>();
        url.Setup(helper => helper.Action(It.IsAny<UrlActionContext>()))
            .Returns("/KasaPreview/LoadSnapshot/test");
        controller.Url = url.Object;
        return controller;
    }

    private static KasaPreviewViewModel NewSaveModel(DateOnly date, string clientActor) => new()
    {
        SelectedDate = date,
        KasaType = "Aksam",
        KasayiYapan = clientActor
    };

    private static void SetSaveForm(
        KasaPreviewController controller,
        string inputsJson,
        string outputsJson,
        string bankadanCekilen,
        string hiddenAuditValue)
    {
        controller.HttpContext.Request.Form = new FormCollection(
            new Dictionary<string, StringValues>
            {
                ["SaveRaporAdi"] = "Historical immutable audit",
                ["SaveInputsJson"] = inputsJson,
                ["SaveOutputsJson"] = outputsJson,
                ["RptGunlukNot"] = "",
                ["RptBankadanCekilen"] = bankadanCekilen,
                ["ConfirmOverwrite"] = "true",
                ["RptEfGuneT"] = hiddenAuditValue,
                ["RptEfGuneH"] = hiddenAuditValue,
                ["RptEfDundenT"] = hiddenAuditValue,
                ["RptEfDundenH"] = hiddenAuditValue,
                ["RptEfGelenT"] = hiddenAuditValue,
                ["RptEfGelenH"] = hiddenAuditValue
            });
    }

    private static void SetDynamicSnapshotUrl(KasaPreviewController controller)
    {
        var url = new Mock<IUrlHelper>();
        url.Setup(helper => helper.Action(It.IsAny<UrlActionContext>()))
            .Returns((UrlActionContext context) =>
            {
                var id = context.Values?.GetType().GetProperty("id")?.GetValue(context.Values);
                return $"/KasaPreview/LoadSnapshot/{id}";
            });
        controller.Url = url.Object;
    }

    private static HesapKontrolKaydi NewHistoricalRecord(
        DateOnly date,
        BankaHesapTuru account,
        KayitYonu direction,
        decimal amount,
        KayitDurumu status,
        DateOnly? trackingDate = null) => new()
    {
        Id = Guid.NewGuid(),
        AnalizTarihi = date,
        HesapTuru = account,
        Yon = direction,
        Tutar = amount,
        Durum = status,
        Sinif = FarkSinifi.Bilinmeyen,
        TespitEdilenTip = "C42E_HISTORICAL",
        TakipBaslangicTarihi = trackingDate,
        DosyaNo = $"C42E-{amount}",
        BirimAdi = "C42E historical"
    };

    private static void AssertSuccessful(IActionResult result)
    {
        var response = Assert.IsType<JsonResult>(result);
        using var responseJson = JsonDocument.Parse(JsonSerializer.Serialize(response.Value));
        Assert.True(responseJson.RootElement.GetProperty("ok").GetBoolean());
    }

    private static string CreateWebRoot()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), $"kasa_c42c_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(webRoot, "Data", "Raporlar"));
        return webRoot;
    }

    private async Task CleanupDateAsync(DateOnly date)
    {
        await using var cleanup = _fixture.CreateContext();
        await cleanup.CalculatedKasaSnapshots
            .Where(snapshot => snapshot.RaporTarihi == date)
            .ExecuteDeleteAsync();
        await cleanup.DailyCalculationResults
            .Where(result => result.ForDate == date)
            .ExecuteDeleteAsync();
    }

    private static IEnumerable<string> EnumeratePropertyNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                yield return property.Name;
                foreach (var nested in EnumeratePropertyNames(property.Value))
                    yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            foreach (var nested in EnumeratePropertyNames(item))
                yield return nested;
        }
    }

    private static HesapKontrolImmutableAuditDetails EmptyDetails() => new(
        Array.Empty<HesapKontrolImmutableAuditRecord>(),
        new HesapKontrolImmutableAuditGroups(
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>()));

    private static HesapKontrolImmutableAuditSnapshot CreateFullAuditSnapshot(DateOnly date)
    {
        var trackedId = Guid.Parse("00000000-0000-0000-0000-000000000101");
        var resolvedId = Guid.Parse("00000000-0000-0000-0000-000000000102");
        var approvedId = Guid.Parse("00000000-0000-0000-0000-000000000103");
        var records = new[]
        {
            new HesapKontrolImmutableAuditRecord(
                trackedId,
                date.AddDays(-2),
                BankaHesapTuru.Tahsilat,
                KayitYonu.Eksik,
                999999999.123456789m,
                KayitDurumu.Takipte,
                FarkSinifi.Bilinmeyen,
                null,
                string.Empty,
                null,
                date.AddDays(-1),
                null,
                null),
            new HesapKontrolImmutableAuditRecord(
                resolvedId,
                date.AddDays(-1),
                BankaHesapTuru.Harc,
                KayitYonu.Fazla,
                0m,
                KayitDurumu.Cozuldu,
                FarkSinifi.Askida,
                string.Empty,
                null,
                "ASKIDA",
                null,
                date,
                null),
            new HesapKontrolImmutableAuditRecord(
                approvedId,
                date,
                BankaHesapTuru.Tahsilat,
                KayitYonu.Fazla,
                42.0000001m,
                KayitDurumu.Onaylandi,
                FarkSinifi.Askida,
                "2060/42",
                "Birim C",
                string.Empty,
                date.AddDays(-3),
                date,
                new DateTime(2060, 4, 26, 12, 34, 56, 789, DateTimeKind.Utc).AddTicks(1234))
        };
        var details = new HesapKontrolImmutableAuditDetails(
            records,
            new HesapKontrolImmutableAuditGroups(
                new[] { trackedId },
                new[] { trackedId },
                new[] { resolvedId },
                new[] { resolvedId, approvedId },
                new[] { trackedId },
                new[] { approvedId }));
        var summary = new EksikFazlaAutoFill(
            101.123456m,
            0m,
            202.234567m,
            303.345678m,
            404.456789m,
            505.567891m,
            true,
            "source-only-info",
            TakipteEksikTahsilat: 606.678912m,
            TakipteEksikHarc: 707.789123m,
            TakipteFazlaTahsilat: 808.891234m,
            TakipteFazlaHarc: 909.912345m,
            TakipteSayisi: 3,
            BeklenenTahsilat: 111.111111m,
            OlaganDisiTahsilat: 222.222222m,
            BeklenenHarc: 333.333333m,
            OlaganDisiHarc: 444.444444m,
            ToplamFarkTahsilat: 555.555555m,
            ToplamFarkHarc: 666.666666m,
            TakipKasaEtkisiTahsilat: 777.777777m,
            TakipKasaEtkisiHarc: 795.5432091m,
            TakipKasaEtkisiNet: -17.7654321m,
            BreakdownMesajTahsilat: null,
            BreakdownMesajHarc: string.Empty);
        return new HesapKontrolImmutableAuditSnapshot(summary, details);
    }
}
