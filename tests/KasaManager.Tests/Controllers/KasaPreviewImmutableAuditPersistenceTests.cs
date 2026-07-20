using System.Security.Claims;
using System.Text.Json;
using KasaManager.Application.Abstractions;
using KasaManager.Application.Orchestration;
using KasaManager.Application.Services;
using KasaManager.Application.Services.ReadAdapter;
using KasaManager.Domain.FinancialExceptions;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Domain.Settings;
using KasaManager.Domain.Validation;
using KasaManager.Web.Controllers;
using KasaManager.Web.Models;
using KasaManager.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;

namespace KasaManager.Tests.Controllers;

public sealed class KasaPreviewImmutableAuditPersistenceTests
{
    private static readonly DateOnly SaveDate = new(2026, 7, 14);

    [Theory]
    [InlineData("98.738,00", 98738.00)]
    [InlineData("1,234.56", 1234.56)]
    public async Task SaveLoad_CultureFormattedOutput_RoundTripsWithoutAmbiguousWarning(
        string posted,
        decimal expected)
    {
        using var fixture = CreateFixture();
        fixture.Analysis.Setup(service => service.GetImmutableAuditSnapshotAsync(
                SaveDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HesapKontrolImmutableAuditSnapshot(
                new EksikFazlaAutoFill(0, 0, 0, 0, 0, 0, false, "no data"),
                EmptyDetails()));
        fixture.Controller.HttpContext.Request.Form = new FormCollection(
            new Dictionary<string, StringValues>
            {
                ["SaveRaporAdi"] = "Culture round-trip",
                ["SaveInputsJson"] = "{}",
                ["SaveOutputsJson"] = $"{{\"genel_kasa\":\"{posted}\"}}",
                ["RptGenelKasa"] = posted,
                ["RptGunlukNot"] = ""
            });

        AssertSuccessful(await fixture.Controller.SaveReport(NewModel(), CancellationToken.None));
        fixture.Snapshots.Setup(service => service.GetByIdAsync(
                fixture.SavedSnapshot!.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.SavedSnapshot);
        var loadedResult = Assert.IsType<ViewResult>(await fixture.Controller.LoadSnapshot(
            fixture.SavedSnapshot!.Id, CancellationToken.None));
        var loaded = Assert.IsType<KasaPreviewViewModel>(loadedResult.Model);

        Assert.Equal(expected, loaded.FormulaRun!.Outputs["genel_kasa"]);
        fixture.Logger.Verify(logger => logger.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("AMOUNT-PARSE-AMBIGUOUS")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }

    [Fact]
    public async Task SaveReport_IgnoresModelBoundAuditValues_AndPersistsServerSideResult()
    {
        using var fixture = CreateFixture();
        var serverFill = new EksikFazlaAutoFill(
            10.11m, 20.22m, 30.33m, 40.44m, 50.55m, 60.66m, true, "server",
            TakipteEksikTahsilat: 3000.123456m,
            TakipteEksikHarc: 29873.809876m,
            TakipteFazlaTahsilat: 70.77m,
            TakipteFazlaHarc: 80.88m,
            TakipteSayisi: 15,
            BeklenenTahsilat: 90.99m,
            OlaganDisiTahsilat: 100.10m,
            BeklenenHarc: 110.11m,
            OlaganDisiHarc: 120.12m,
            ToplamFarkTahsilat: -130.13m,
            ToplamFarkHarc: -140.14m,
            TakipKasaEtkisiTahsilat: 150.15m,
            TakipKasaEtkisiHarc: 160.16m,
            TakipKasaEtkisiNet: -10.01m,
            BreakdownMesajTahsilat: "server tahsilat",
            BreakdownMesajHarc: "server harç");
        fixture.Analysis
            .Setup(service => service.GetImmutableAuditSnapshotAsync(
                SaveDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HesapKontrolImmutableAuditSnapshot(
                serverFill, CreateServerDetails()));

        var model = NewModel();
        model.KasayiYapan = "client-spoof";
        model.TakipteEksikTahsilat = 999999m;
        model.TakipteEksikHarc = 999999m;
        model.TakipteFazlaTahsilat = 999999m;
        model.TakipteFazlaHarc = 999999m;
        model.TakipteSayisi = 999;
        model.ImmutableAuditRecords = new[]
        {
            new ImmutableAuditRecordViewModel(
                Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                SaveDate,
                BankaHesapTuru.Stopaj,
                KayitYonu.Fazla,
                999999m,
                KayitDurumu.Iptal,
                FarkSinifi.Bilinmeyen,
                "client", "client", "client", null, null, null)
        };
        model.ImmutableAuditRecordGroups = new ImmutableAuditRecordGroupsViewModel(
            new[] { Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff") },
            Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>(),
            Array.Empty<Guid>(), Array.Empty<Guid>());

        var result = await fixture.Controller.SaveReport(model, CancellationToken.None);

        AssertSuccessful(result);
        var payload = SavedPayload(fixture);
        Assert.Equal(2, payload.PayloadVersion);
        Assert.NotNull(payload.ImmutableAudit);
        Assert.True(payload.ImmutableAuditDetails.HasValue);
        Assert.Equal(3000.123456m, payload.ImmutableAudit.TakipteEksikTahsilat);
        Assert.Equal(29873.809876m, payload.ImmutableAudit.TakipteEksikHarc);
        Assert.Equal(15, payload.ImmutableAudit.TakipteSayisi);
        Assert.Equal(-10.01m, payload.ImmutableAudit.TakipKasaEtkisiNet);
        Assert.Equal("server tahsilat", payload.ImmutableAudit.BreakdownMesajTahsilat);
        Assert.Equal("server harç", payload.ImmutableAudit.BreakdownMesajHarc);
        Assert.NotEqual(model.TakipteEksikTahsilat, payload.ImmutableAudit.TakipteEksikTahsilat);
        Assert.Equal(17, fixture.SavedSnapshot!.CalculatedByUserId);
        Assert.Equal("audit-user", fixture.SavedSnapshot.CalculatedBy);
        var details = payload.ImmutableAuditDetails.Value
            .Deserialize<HesapKontrolImmutableAuditDetails>();
        Assert.NotNull(details);
        Assert.Single(details.Records);
        Assert.DoesNotContain(details.Records, record =>
            record.KayitId == Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        Assert.DoesNotContain(
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            details.Groups.AktifKayitlar);
        fixture.Analysis.Verify(service => service.GetImmutableAuditSnapshotAsync(
            SaveDate, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsCashierDescriptionAndDailyNote_SeparatelyFromCreator()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(value => value.IsAuthenticated).Returns(true);
        currentUser.SetupGet(value => value.UserId).Returns(17);
        currentUser.SetupGet(value => value.Username).Returns("admin");
        using var fixture = CreateFixture(currentUser.Object);
        fixture.Analysis
            .Setup(service => service.GetImmutableAuditSnapshotAsync(
                SaveDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HesapKontrolImmutableAuditSnapshot(
                new EksikFazlaAutoFill(0, 0, 0, 0, 0, 0, false, "no data"),
                EmptyDetails()));
        fixture.Controller.HttpContext.Request.Form = new FormCollection(
            new Dictionary<string, StringValues>
            {
                ["SaveRaporAdi"] = "ESRA DAĞAŞAN",
                ["SaveInputsJson"] = "{}",
                ["SaveOutputsJson"] = "{}",
                ["RptGunlukNot"] = "Günlük kasa notu"
            });
        var saveModel = NewModel();
        saveModel.Aciklama = "Kasa açıklaması";

        AssertSuccessful(await fixture.Controller.SaveReport(
            saveModel, CancellationToken.None));

        var payload = SavedPayload(fixture);
        Assert.Equal("ESRA DAĞAŞAN", payload.KasayiYapan);
        Assert.Equal("Kasa açıklaması", payload.Aciklama);
        Assert.Equal("Günlük kasa notu", payload.GunlukNot);
        Assert.Equal("admin", fixture.SavedSnapshot!.CalculatedBy);
        fixture.Snapshots.Setup(service => service.GetByIdAsync(
                fixture.SavedSnapshot.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.SavedSnapshot);

        var loadedResult = Assert.IsType<ViewResult>(await fixture.Controller.LoadSnapshot(
            fixture.SavedSnapshot.Id, CancellationToken.None));
        var loaded = Assert.IsType<KasaPreviewViewModel>(loadedResult.Model);
        Assert.Equal("ESRA DAĞAŞAN", loaded.KasayiYapan);
        Assert.Equal("Kasa açıklaması", loaded.Aciklama);
        Assert.Equal("Günlük kasa notu", loaded.GunlukKasaNotu);
        Assert.Equal("admin", fixture.SavedSnapshot.CalculatedBy);
    }

    [Fact]
    public async Task SaveReport_WithNoTrackingData_PersistsNonNullRealZeroAudit()
    {
        using var fixture = CreateFixture();
        fixture.Analysis
            .Setup(service => service.GetImmutableAuditSnapshotAsync(
                SaveDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HesapKontrolImmutableAuditSnapshot(
                new EksikFazlaAutoFill(0, 0, 0, 0, 0, 0, false, "no data"),
                EmptyDetails()));

        var result = await fixture.Controller.SaveReport(
            NewModel(), CancellationToken.None);

        AssertSuccessful(result);
        var payload = SavedPayload(fixture);
        Assert.Equal(2, payload.PayloadVersion);
        Assert.NotNull(payload.ImmutableAudit);
        var details = payload.ImmutableAuditDetails!.Value
            .Deserialize<HesapKontrolImmutableAuditDetails>();
        Assert.NotNull(details);
        Assert.Empty(details.Records);

        var numericProperties = typeof(KasaImmutableAuditData).GetProperties()
            .Where(property => property.PropertyType == typeof(decimal)
                            || property.PropertyType == typeof(int));
        foreach (var property in numericProperties)
            Assert.Equal(0m, Convert.ToDecimal(property.GetValue(payload.ImmutableAudit)));
    }

    [Fact]
    public async Task CalculateWinner_ToLiveSave_PostsPersistsAndReloadsPoolAuditValues()
    {
        using var fixture = CreateFixture();
        var serverFill = new EksikFazlaAutoFill(
            -3000m, -29873.80m, 0m, 0m, 0m, 0m, true, "server authoritative");
        fixture.Analysis.Setup(service => service.GetImmutableAuditSnapshotAsync(
                SaveDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HesapKontrolImmutableAuditSnapshot(serverFill, CreateServerDetails()));
        var model = NewModel();
        model.GuneAitEksikFazlaTahsilat = 0m;
        model.GuneAitEksikFazlaHarc = 0m;
        model.PoolEntries = new()
        {
            new() { CanonicalKey = "gune_ait_eksik_fazla_tahsilat", Value = "-3000" },
            new() { CanonicalKey = "gune_ait_eksik_fazla_harc", Value = "-29873.80" }
        };
        fixture.Controller.ModelState.SetModelValue(
            nameof(KasaPreviewViewModel.GuneAitEksikFazlaTahsilat), "0", "0");
        fixture.Controller.ModelState.SetModelValue(
            nameof(KasaPreviewViewModel.GuneAitEksikFazlaHarc), "0", "0");
        var winnerMethod = typeof(KasaPreviewController).GetMethod(
            "LogValueSourceDiagnostics",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(winnerMethod);
        winnerMethod.Invoke(fixture.Controller, new object[] { model, "Calculate" });
        Assert.False(fixture.Controller.ModelState.ContainsKey(
            nameof(KasaPreviewViewModel.GuneAitEksikFazlaTahsilat)));
        Assert.False(fixture.Controller.ModelState.ContainsKey(
            nameof(KasaPreviewViewModel.GuneAitEksikFazlaHarc)));

        fixture.Controller.HttpContext.Request.Form = new FormCollection(
            new Dictionary<string, StringValues>
            {
                ["SaveRaporAdi"] = "Calculate winner live save",
                ["SaveInputsJson"] = "{}",
                ["SaveOutputsJson"] = "{\"genel_kasa\":0}",
                ["RptGunlukNot"] = "",
                ["RptEfGuneT"] = model.GuneAitEksikFazlaTahsilat!.Value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                ["RptEfGuneH"] = model.GuneAitEksikFazlaHarc!.Value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
            });

        Assert.Null(model.LoadedSnapshotId);
        Assert.Equal("-3000", fixture.Controller.Request.Form["RptEfGuneT"]);
        Assert.Equal("-29873.80", fixture.Controller.Request.Form["RptEfGuneH"]);
        AssertSuccessful(await fixture.Controller.SaveReport(model, CancellationToken.None));

        var payload = SavedPayload(fixture);
        Assert.Equal(-3000m, payload.GuneAitEksikFazlaTahsilat);
        Assert.Equal(-29873.80m, payload.GuneAitEksikFazlaHarc);
        using (var outputs = JsonDocument.Parse(fixture.SavedSnapshot!.OutputsJson))
        {
            Assert.Equal(-3000m, decimal.Parse(outputs.RootElement
                .GetProperty("gune_ait_eksik_fazla_tahsilat").GetString()!,
                System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(-29873.80m, decimal.Parse(outputs.RootElement
                .GetProperty("gune_ait_eksik_fazla_harc").GetString()!,
                System.Globalization.CultureInfo.InvariantCulture));
        }

        fixture.Snapshots.Setup(service => service.GetByIdAsync(
                fixture.SavedSnapshot!.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.SavedSnapshot);

        var loadedResult = Assert.IsType<ViewResult>(await fixture.Controller.LoadSnapshot(
            fixture.SavedSnapshot!.Id, CancellationToken.None));
        var loaded = Assert.IsType<KasaPreviewViewModel>(loadedResult.Model);
        Assert.Equal(-3000m, loaded.GuneAitEksikFazlaTahsilat);
        Assert.Equal(-29873.80m, loaded.GuneAitEksikFazlaHarc);
    }

    [Fact]
    public async Task CalculateSaveLoad_ImmutableAuditWinsWhenLegacyOutputsContainZero()
    {
        using var fixture = CreateFixture();
        var serverFill = new EksikFazlaAutoFill(
            -3000m, -29873.80m, 101m, 202m, 303m, 404m, true, "server authoritative");
        fixture.Analysis.Setup(service => service.GetImmutableAuditSnapshotAsync(
                SaveDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HesapKontrolImmutableAuditSnapshot(serverFill, CreateServerDetails()));
        var model = NewModel();
        model.PoolEntries = new()
        {
            new() { CanonicalKey = "gune_ait_eksik_fazla_tahsilat", Value = "-3000" },
            new() { CanonicalKey = "gune_ait_eksik_fazla_harc", Value = "-29873.80" }
        };
        var winnerMethod = typeof(KasaPreviewController).GetMethod(
            "LogValueSourceDiagnostics",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(winnerMethod);
        winnerMethod.Invoke(fixture.Controller, new object[] { model, "Calculate" });
        fixture.Controller.HttpContext.Request.Form = new FormCollection(
            new Dictionary<string, StringValues>
            {
                ["SaveRaporAdi"] = "Legacy zero outputs restore",
                ["SaveInputsJson"] = "{}",
                ["SaveOutputsJson"] = "{\"gune_ait_eksik_fazla_tahsilat\":0,\"gune_ait_eksik_fazla_harc\":0}",
                ["RptGunlukNot"] = "",
                ["RptEfGuneT"] = "-3000",
                ["RptEfGuneH"] = "-29873.80"
            });

        AssertSuccessful(await fixture.Controller.SaveReport(model, CancellationToken.None));
        Assert.False(string.IsNullOrWhiteSpace(fixture.SavedSnapshot!.OutputsJson));
        var persistedOutputs = JsonDocument.Parse(fixture.SavedSnapshot!.OutputsJson);
        Assert.True(persistedOutputs.RootElement.EnumerateObject().Any());
        Assert.Equal(-3000m, decimal.Parse(persistedOutputs.RootElement
            .GetProperty("gune_ait_eksik_fazla_tahsilat").GetString()!,
            System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(-29873.80m, decimal.Parse(persistedOutputs.RootElement
            .GetProperty("gune_ait_eksik_fazla_harc").GetString()!,
            System.Globalization.CultureInfo.InvariantCulture));

        fixture.SavedSnapshot.OutputsJson = """
            {"genel_kasa":"1.00","gune_ait_eksik_fazla_tahsilat":0,"gune_ait_eksik_fazla_harc":0}
            """;
        fixture.Snapshots.Setup(service => service.GetByIdAsync(
                fixture.SavedSnapshot.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.SavedSnapshot);

        var loadedResult = Assert.IsType<ViewResult>(await fixture.Controller.LoadSnapshot(
            fixture.SavedSnapshot.Id, CancellationToken.None));
        var loaded = Assert.IsType<KasaPreviewViewModel>(loadedResult.Model);
        Assert.True(loaded.HasResults);
        Assert.NotNull(loaded.FormulaRun);
        Assert.NotEmpty(loaded.FormulaRun.Outputs);
        Assert.Equal(1m, loaded.FormulaRun.Outputs["genel_kasa"]);
        Assert.Equal(-3000m, loaded.GuneAitEksikFazlaTahsilat);
        Assert.Equal(-29873.80m, loaded.GuneAitEksikFazlaHarc);
    }

    [Theory]
    [InlineData("-3000.00")]
    [InlineData("-3.000,00")]
    [InlineData("-29873.80")]
    [InlineData("-29.873,80")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData(null)]
    public async Task SaveReport_ServerAuditIsAuthoritativeForSixRootFields_RegardlessOfHiddenText(
        string? hiddenText)
    {
        using var fixture = CreateFixture();
        var serverFill = new EksikFazlaAutoFill(
            -3000.00m,
            -29873.80m,
            101.01m,
            202.02m,
            303.03m,
            404.04m,
            true,
            "server authoritative");
        fixture.Analysis
            .Setup(service => service.GetImmutableAuditSnapshotAsync(
                SaveDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HesapKontrolImmutableAuditSnapshot(
                serverFill, CreateServerDetails()));

        var form = new Dictionary<string, StringValues>
        {
            ["SaveRaporAdi"] = "Audit root test",
            ["SaveInputsJson"] = "{}",
            ["SaveOutputsJson"] = "{}",
            ["RptGunlukNot"] = ""
        };
        if (hiddenText is not null)
        {
            form["RptEfGuneT"] = hiddenText;
            form["RptEfGuneH"] = hiddenText;
            form["RptEfDundenT"] = hiddenText;
            form["RptEfDundenH"] = hiddenText;
            form["RptEfGelenT"] = hiddenText;
            form["RptEfGelenH"] = hiddenText;
        }
        fixture.Controller.HttpContext.Request.Form = new FormCollection(form);

        AssertSuccessful(await fixture.Controller.SaveReport(NewModel(), CancellationToken.None));

        var payload = SavedPayload(fixture);
        Assert.NotNull(payload.ImmutableAudit);
        Assert.Equal(-3000.00m, payload.GuneAitEksikFazlaTahsilat);
        Assert.Equal(-29873.80m, payload.GuneAitEksikFazlaHarc);
        Assert.Equal(101.01m, payload.DundenEksikFazlaTahsilat);
        Assert.Equal(202.02m, payload.DundenEksikFazlaHarc);
        Assert.Equal(303.03m, payload.DundenEksikFazlaGelenTahsilat);
        Assert.Equal(404.04m, payload.DundenEksikFazlaGelenHarc);
        Assert.Equal(-3000.00m, payload.ImmutableAudit.GuneAitEksikFazlaTahsilat);
        Assert.Equal(-29873.80m, payload.ImmutableAudit.GuneAitEksikFazlaHarc);
    }

    [Fact]
    public async Task SaveReport_HistoricalSource_UsesValidatedPersistedAuditAndSkipsLiveReanalysis()
    {
        using var fixture = CreateFixture();
        var source = NewSourceSnapshot(SaveDate, KasaRaporTuru.Aksam);
        fixture.Snapshots.Setup(service => service.GetByIdAsync(
                source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);
        fixture.Controller.HttpContext.Request.Form = SaveForm(confirmOverwrite: true, hiddenValue: "999999");
        var model = NewModel();
        model.LoadedSnapshotId = source.Id;

        AssertSuccessful(await fixture.Controller.SaveReport(model, CancellationToken.None));

        var payload = SavedPayload(fixture);
        Assert.NotNull(payload.ImmutableAudit);
        Assert.Equal(-3000m, payload.ImmutableAudit.GuneAitEksikFazlaTahsilat);
        Assert.Equal(-29873.80m, payload.ImmutableAudit.GuneAitEksikFazlaHarc);
        Assert.Equal(-3000m, payload.GuneAitEksikFazlaTahsilat);
        Assert.Equal(-29873.80m, payload.GuneAitEksikFazlaHarc);
        Assert.NotEqual(999999m, payload.GuneAitEksikFazlaTahsilat);
        fixture.Snapshots.Verify(service => service.GetByIdAsync(
            source.Id, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Analysis.Verify(service => service.GetImmutableAuditSnapshotAsync(
            It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveReport_HistoricalVersionOne_UsesPersistedScalarAuditWithoutInventingDetails()
    {
        using var fixture = CreateFixture();
        var source = NewSourceSnapshot(SaveDate, KasaRaporTuru.Aksam);
        source.KasaRaporDataJson = JsonSerializer.Serialize(new KasaRaporData
        {
            PayloadVersion = 1,
            ImmutableAudit = new KasaImmutableAuditData
            {
                GuneAitEksikFazlaTahsilat = -3000m,
                GuneAitEksikFazlaHarc = -29873.80m
            }
        });
        fixture.Snapshots.Setup(service => service.GetByIdAsync(
                source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);
        fixture.Controller.HttpContext.Request.Form = SaveForm(
            confirmOverwrite: true, hiddenValue: "999999");
        var model = NewModel();
        model.LoadedSnapshotId = source.Id;
        model.GuneAitEksikFazlaTahsilat = 777777m;
        model.GuneAitEksikFazlaHarc = 888888m;

        AssertSuccessful(await fixture.Controller.SaveReport(model, CancellationToken.None));

        var payload = SavedPayload(fixture);
        Assert.Equal(1, payload.PayloadVersion);
        Assert.NotNull(payload.ImmutableAudit);
        Assert.Equal(-3000m, payload.ImmutableAudit.GuneAitEksikFazlaTahsilat);
        Assert.Equal(-29873.80m, payload.ImmutableAudit.GuneAitEksikFazlaHarc);
        Assert.Equal(-3000m, payload.GuneAitEksikFazlaTahsilat);
        Assert.Equal(-29873.80m, payload.GuneAitEksikFazlaHarc);
        Assert.False(payload.ImmutableAuditDetails.HasValue);
        fixture.Snapshots.Verify(service => service.SaveAsync(
            It.IsAny<CalculatedKasaSnapshot>(), It.IsAny<int>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Analysis.Verify(service => service.GetImmutableAuditSnapshotAsync(
            It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveReport_HistoricalLegacyWithoutImmutableAudit_RemainsFailClosed()
    {
        using var fixture = CreateFixture();
        var source = NewSourceSnapshot(SaveDate, KasaRaporTuru.Aksam);
        source.KasaRaporDataJson = JsonSerializer.Serialize(new KasaRaporData
        {
            PayloadVersion = 0,
            GuneAitEksikFazlaTahsilat = -3000m,
            GuneAitEksikFazlaHarc = -29873.80m
        });
        fixture.Snapshots.Setup(service => service.GetByIdAsync(
                source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);
        fixture.Controller.HttpContext.Request.Form = SaveForm(
            confirmOverwrite: true, hiddenValue: "-3000");
        var model = NewModel();
        model.LoadedSnapshotId = source.Id;

        var result = await fixture.Controller.SaveReport(model, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(json.Value));
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        fixture.Snapshots.Verify(service => service.SaveAsync(
            It.IsAny<CalculatedKasaSnapshot>(), It.IsAny<int>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.Analysis.Verify(service => service.GetImmutableAuditSnapshotAsync(
            It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SaveReport_HistoricalSource_CrossChainReferenceFailsClosed(
        bool differentDate,
        bool differentKasaType)
    {
        using var fixture = CreateFixture();
        var source = NewSourceSnapshot(
            differentDate ? SaveDate.AddDays(-1) : SaveDate,
            differentKasaType ? KasaRaporTuru.Sabah : KasaRaporTuru.Aksam);
        fixture.Snapshots.Setup(service => service.GetByIdAsync(
                source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);
        fixture.Analysis.Setup(service => service.GetImmutableAuditSnapshotAsync(
                SaveDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HesapKontrolImmutableAuditSnapshot(
                new EksikFazlaAutoFill(0, 0, 0, 0, 0, 0, false, "live"),
                EmptyDetails()));
        fixture.Controller.HttpContext.Request.Form = SaveForm(confirmOverwrite: true, hiddenValue: "-777");
        var model = NewModel();
        model.LoadedSnapshotId = source.Id;

        var result = await fixture.Controller.SaveReport(model, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(json.Value));
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("kaynak", document.RootElement.GetProperty("message").GetString(),
            StringComparison.OrdinalIgnoreCase);
        fixture.Snapshots.Verify(service => service.SaveAsync(
            It.IsAny<CalculatedKasaSnapshot>(), It.IsAny<int>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.Analysis.Verify(service => service.GetImmutableAuditSnapshotAsync(
            It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveReport_HistoricalSource_CorruptVersionTwoFailsClosedWithoutLiveFallback()
    {
        using var fixture = CreateFixture();
        var source = NewSourceSnapshot(SaveDate, KasaRaporTuru.Aksam);
        source.KasaRaporDataJson = JsonSerializer.Serialize(new KasaRaporData
        {
            PayloadVersion = 2,
            ImmutableAudit = new KasaImmutableAuditData
            {
                GuneAitEksikFazlaTahsilat = -3000m
            },
            ImmutableAuditDetails = JsonSerializer.SerializeToElement(new { Records = Array.Empty<object>() })
        });
        fixture.Snapshots.Setup(service => service.GetByIdAsync(
                source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);
        fixture.Analysis.Setup(service => service.GetImmutableAuditSnapshotAsync(
                SaveDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HesapKontrolImmutableAuditSnapshot(
                new EksikFazlaAutoFill(0, 0, 0, 0, 0, 0, false, "live"),
                EmptyDetails()));
        fixture.Controller.HttpContext.Request.Form = SaveForm(confirmOverwrite: true, hiddenValue: "0");
        var model = NewModel();
        model.LoadedSnapshotId = source.Id;

        var result = await fixture.Controller.SaveReport(model, CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(json.Value));
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Contains("audit", document.RootElement.GetProperty("message").GetString(),
            StringComparison.OrdinalIgnoreCase);
        fixture.Snapshots.Verify(service => service.SaveAsync(
            It.IsAny<CalculatedKasaSnapshot>(), It.IsAny<int>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.Analysis.Verify(service => service.GetImmutableAuditSnapshotAsync(
            It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Razor_SaveReportFormPostsLoadedSnapshotIdOnlyAsSourceReference()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(
            root, "src", "KasaManager.Web", "Views", "KasaPreview", "Index.cshtml"));
        var formStart = source.IndexOf("id=\"saveReportForm\"", StringComparison.Ordinal);
        var formEnd = source.IndexOf("</form>", formStart, StringComparison.Ordinal);

        Assert.True(formStart >= 0 && formEnd > formStart);
        Assert.Contains("asp-for=\"LoadedSnapshotId\"", source[formStart..formEnd]);
    }

    [Fact]
    public void Razor_SubmitSaveFormGuaranteesLoadedSnapshotIdInFormDataBeforeHttpPost()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(
            root, "src", "KasaManager.Web", "Views", "KasaPreview", "Index.cshtml"));

        const string sourceElement = "id=\"loadedSnapshotId\"";
        Assert.Equal(1, source.Split(sourceElement, StringSplitOptions.None).Length - 1);

        var submitStart = source.IndexOf(
            "function submitSaveForm(withOverwrite)", StringComparison.Ordinal);
        var formDataStart = source.IndexOf(
            "var fd = new FormData(form);", submitStart, StringComparison.Ordinal);
        var fetchStart = source.IndexOf(
            "fetch(form.action", formDataStart, StringComparison.Ordinal);

        Assert.True(submitStart >= 0 && formDataStart > submitStart && fetchStart > formDataStart);
        var formDataBoundary = source[formDataStart..fetchStart];
        Assert.Contains(
            "document.getElementById('loadedSnapshotId')", formDataBoundary,
            StringComparison.Ordinal);
        Assert.Contains(
            "fd.set('LoadedSnapshotId', loadedIdEl.value)", formDataBoundary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveReport_WhenServerAuditQueryFails_DoesNotPersistPartialSnapshot()
    {
        using var fixture = CreateFixture();
        fixture.Analysis
            .Setup(service => service.GetImmutableAuditSnapshotAsync(
                SaveDate, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit query failed"));

        var result = await fixture.Controller.SaveReport(
            NewModel(), CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(json.Value));
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Null(fixture.SavedSnapshot);
        fixture.Snapshots.Verify(service => service.SaveAsync(
            It.IsAny<CalculatedKasaSnapshot>(), It.IsAny<int>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveReport_WhenServerDetailsValidationFails_DoesNotPersistPartialVersionTwo()
    {
        using var fixture = CreateFixture();
        var missingId = Guid.NewGuid();
        var invalid = new HesapKontrolImmutableAuditDetails(
            Array.Empty<HesapKontrolImmutableAuditRecord>(),
            new HesapKontrolImmutableAuditGroups(
                new[] { missingId }, Array.Empty<Guid>(), Array.Empty<Guid>(),
                Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>()));
        fixture.Analysis
            .Setup(service => service.GetImmutableAuditSnapshotAsync(
                SaveDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HesapKontrolImmutableAuditSnapshot(
                new EksikFazlaAutoFill(0, 0, 0, 0, 0, 0, false, null), invalid));

        var result = await fixture.Controller.SaveReport(NewModel(), CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(json.Value));
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Null(fixture.SavedSnapshot);
        fixture.Snapshots.Verify(service => service.SaveAsync(
            It.IsAny<CalculatedKasaSnapshot>(), It.IsAny<int>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-an-integer")]
    public async Task SaveReport_MissingOrInvalidUserId_FailsClosedBeforeAuditAndSnapshot(
        string? userIdClaim)
    {
        using var fixture = CreateFixture(CurrentUserWithClaim(userIdClaim));

        var result = await fixture.Controller.SaveReport(
            NewModel(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(fixture.Analysis.Invocations);
        Assert.Empty(fixture.Snapshots.Invocations);
        Assert.Null(fixture.SavedSnapshot);
    }

    [Fact]
    public async Task SaveReport_NoOp_UsesPersistedSnapshotReturnedByServiceForRedirectAndVersion()
    {
        using var fixture = CreateFixture();
        fixture.Analysis
            .Setup(service => service.GetImmutableAuditSnapshotAsync(
                SaveDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HesapKontrolImmutableAuditSnapshot(
                new EksikFazlaAutoFill(0, 0, 0, 0, 0, 0, false, "no data"),
                EmptyDetails()));

        var persisted = new CalculatedKasaSnapshot
        {
            Id = Guid.NewGuid(),
            RaporTarihi = SaveDate,
            KasaTuru = KasaRaporTuru.Aksam,
            Version = 7,
            IsActive = true
        };
        fixture.Snapshots
            .Setup(service => service.GetActiveAsync(
                SaveDate, KasaRaporTuru.Aksam, It.IsAny<CancellationToken>()))
            .ReturnsAsync(persisted);

        CalculatedKasaSnapshot? transientCandidate = null;
        fixture.Snapshots
            .Setup(service => service.SaveAsync(
                It.IsAny<CalculatedKasaSnapshot>(), 17, "audit-user",
                It.IsAny<CancellationToken>()))
            .Callback<CalculatedKasaSnapshot, int, string?, CancellationToken>(
                (candidate, _, _, _) => transientCandidate = candidate)
            .ReturnsAsync(persisted);

        fixture.Controller.HttpContext.Request.Form = new FormCollection(
            new Dictionary<string, StringValues>
            {
                ["SaveRaporAdi"] = "Audit test",
                ["SaveInputsJson"] = "{}",
                ["SaveOutputsJson"] = "{}",
                ["RptGunlukNot"] = "",
                ["ConfirmOverwrite"] = "true"
            });
        var url = new Mock<IUrlHelper>();
        url.Setup(helper => helper.Action(It.IsAny<UrlActionContext>()))
            .Returns((UrlActionContext context) =>
            {
                var id = context.Values?.GetType().GetProperty("id")?.GetValue(context.Values);
                return $"/KasaPreview/LoadSnapshot/{id}";
            });
        fixture.Controller.Url = url.Object;

        var result = await fixture.Controller.SaveReport(NewModel(), CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(json.Value));
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.NotNull(transientCandidate);
        Assert.NotEqual(persisted.Id, transientCandidate.Id);
        var redirectUrl = document.RootElement.GetProperty("redirectUrl").GetString();
        Assert.Contains(persisted.Id.ToString(), redirectUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(transientCandidate.Id.ToString(), redirectUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(7, document.RootElement.GetProperty("version").GetInt32());
        Assert.True(document.RootElement.GetProperty("isNoOp").GetBoolean());
        Assert.False(document.RootElement.GetProperty("isUpdate").GetBoolean());
        Assert.False(document.RootElement.GetProperty("createdNewVersion").GetBoolean());
    }

    [Fact]
    public async Task SaveReport_NewVersion_UsesNewPersistedSnapshotForRedirectAndState()
    {
        using var fixture = CreateFixture();
        fixture.Analysis
            .Setup(service => service.GetImmutableAuditSnapshotAsync(
                SaveDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HesapKontrolImmutableAuditSnapshot(
                new EksikFazlaAutoFill(0, 0, 0, 0, 0, 0, false, "no data"),
                EmptyDetails()));
        var url = new Mock<IUrlHelper>();
        url.Setup(helper => helper.Action(It.IsAny<UrlActionContext>()))
            .Returns((UrlActionContext context) =>
            {
                var id = context.Values?.GetType().GetProperty("id")?.GetValue(context.Values);
                return $"/KasaPreview/LoadSnapshot/{id}";
            });
        fixture.Controller.Url = url.Object;

        var result = await fixture.Controller.SaveReport(NewModel(), CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(json.Value));
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.False(document.RootElement.GetProperty("isNoOp").GetBoolean());
        Assert.False(document.RootElement.GetProperty("isUpdate").GetBoolean());
        Assert.True(document.RootElement.GetProperty("createdNewVersion").GetBoolean());
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        Assert.NotNull(fixture.SavedSnapshot);
        Assert.Contains(
            fixture.SavedSnapshot.Id.ToString(),
            document.RootElement.GetProperty("redirectUrl").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static HesapKontrolImmutableAuditDetails CreateServerDetails()
    {
        var id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var record = new HesapKontrolImmutableAuditRecord(
            id,
            SaveDate,
            BankaHesapTuru.Tahsilat,
            KayitYonu.Eksik,
            12.34m,
            KayitDurumu.Acik,
            FarkSinifi.Bilinmeyen,
            "2026/1",
            "Birim A",
            "BILINMEYEN",
            null,
            null,
            null);
        return new HesapKontrolImmutableAuditDetails(
            new[] { record },
            new HesapKontrolImmutableAuditGroups(
                new[] { id },
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                Array.Empty<Guid>()));
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

    private static CalculatedKasaSnapshot NewSourceSnapshot(
        DateOnly date,
        KasaRaporTuru kasaTuru)
    {
        var audit = new KasaImmutableAuditData
        {
            GuneAitEksikFazlaTahsilat = -3000m,
            GuneAitEksikFazlaHarc = -29873.80m,
            OncekiGunAcikTahsilat = 101m,
            OncekiGunAcikHarc = 202m,
            BugunCozulenTahsilat = 303m,
            BugunCozulenHarc = 404m
        };
        return new CalculatedKasaSnapshot
        {
            Id = Guid.NewGuid(),
            RaporTarihi = date,
            KasaTuru = kasaTuru,
            Version = 1,
            IsActive = true,
            InputsJson = "{}",
            OutputsJson = "{}",
            KasaRaporDataJson = JsonSerializer.Serialize(new KasaRaporData
            {
                PayloadVersion = 2,
                ImmutableAudit = audit,
                ImmutableAuditDetails = JsonSerializer.SerializeToElement(CreateServerDetails()),
                GuneAitEksikFazlaTahsilat = 999999m,
                GuneAitEksikFazlaHarc = 999999m
            })
        };
    }

    private static FormCollection SaveForm(bool confirmOverwrite, string hiddenValue) => new(
        new Dictionary<string, StringValues>
        {
            ["SaveRaporAdi"] = "Historical source test",
            ["SaveInputsJson"] = "{\"financialInput\":2}",
            ["SaveOutputsJson"] = "{\"financialOutput\":4}",
            ["RptGunlukNot"] = "",
            ["ConfirmOverwrite"] = confirmOverwrite ? "true" : "false",
            ["RptEfGuneT"] = hiddenValue,
            ["RptEfGuneH"] = hiddenValue,
            ["RptEfDundenT"] = hiddenValue,
            ["RptEfDundenH"] = hiddenValue,
            ["RptEfGelenT"] = hiddenValue,
            ["RptEfGelenH"] = hiddenValue
        });

    private static TestFixture CreateFixture(ICurrentUser? suppliedCurrentUser = null)
    {
        var webRoot = Path.Combine(Path.GetTempPath(), $"kasa_audit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(webRoot, "Data", "Raporlar"));

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
        var analysis = new Mock<IBankaHesapKontrolService>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(value => value.IsAuthenticated).Returns(true);
        currentUser.SetupGet(value => value.UserId).Returns(17);
        currentUser.SetupGet(value => value.Username).Returns("audit-user");
        var resolvedCurrentUser = suppliedCurrentUser ?? currentUser.Object;
        var snapshots = new Mock<ICalculatedKasaSnapshotService>();
        snapshots.Setup(service => service.GetActiveAsync(
                SaveDate, KasaRaporTuru.Aksam, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalculatedKasaSnapshot?)null);
        var financialExceptions = new Mock<IFinansalIstisnaService>();
        financialExceptions.Setup(service => service.ListByDateAsync(
                SaveDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FinansalIstisna>());

        var logger = new Mock<ILogger<KasaPreviewController>>();
        var controller = new KasaPreviewController(
            Mock.Of<IKasaOrchestrator>(),
            environment.Object,
            configuration,
            Mock.Of<IImportOrchestrator>(),
            Mock.Of<IKasaReportDateRulesService>(),
            defaults.Object,
            analysis.Object,
            resolvedCurrentUser,
            Mock.Of<IHesapKontrolSourceResolver>(),
            Mock.Of<IReportDataBuilder>(),
            Mock.Of<IExportService>(),
            Mock.Of<IKasaValidationService>(),
            Mock.Of<IVergideBirikenLedgerService>(),
            Mock.Of<IDocumentTemplateService>(),
            financialExceptions.Object,
            Mock.Of<IFinansalIstisnaAnomaliService>(),
            Mock.Of<IDistributedCache>(),
            logger.Object,
            Mock.Of<IKasaReadModelService>(),
            snapshots.Object,
            Mock.Of<IKasaRaporSnapshotService>());

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "audit-user") }, "TestAuth"))
        };
        httpContext.Request.Form = new FormCollection(
            new Dictionary<string, StringValues>
            {
                ["SaveRaporAdi"] = "Audit test",
                ["SaveInputsJson"] = "{}",
                ["SaveOutputsJson"] = "{}",
                ["RptGunlukNot"] = ""
            });
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(
            httpContext, Mock.Of<ITempDataProvider>());
        var url = new Mock<IUrlHelper>();
        url.Setup(helper => helper.Action(It.IsAny<UrlActionContext>()))
            .Returns("/KasaPreview/LoadSnapshot/test");
        controller.Url = url.Object;

        var fixture = new TestFixture(webRoot, controller, analysis, snapshots, logger);
        snapshots.Setup(service => service.SaveAsync(
                It.IsAny<CalculatedKasaSnapshot>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<CalculatedKasaSnapshot, int, string?, CancellationToken>((snapshot, actorUserId, actorUsername, _) =>
            {
                snapshot.Version = 1;
                snapshot.CalculatedByUserId = actorUserId;
                snapshot.CalculatedBy = actorUsername;
                fixture.SavedSnapshot = snapshot;
            })
            .ReturnsAsync((CalculatedKasaSnapshot snapshot, int _, string? _, CancellationToken _) => snapshot);

        return fixture;
    }

    private static ICurrentUser CurrentUserWithClaim(string? userIdClaim)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "audit-user") };
        if (userIdClaim is not null)
            claims.Add(new Claim("UserId", userIdClaim));
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
        };
        return new HttpContextCurrentUser(
            new HttpContextAccessor { HttpContext = context });
    }

    private static KasaPreviewViewModel NewModel() => new()
    {
        SelectedDate = SaveDate,
        KasaType = "Aksam",
        KasayiYapan = "audit-user"
    };

    private static void AssertSuccessful(IActionResult result)
    {
        var json = Assert.IsType<JsonResult>(result);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(json.Value));
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
    }

    private static KasaRaporData SavedPayload(TestFixture fixture)
    {
        Assert.NotNull(fixture.SavedSnapshot?.KasaRaporDataJson);
        return Assert.IsType<KasaRaporData>(
            JsonSerializer.Deserialize<KasaRaporData>(fixture.SavedSnapshot.KasaRaporDataJson));
    }

    private sealed class TestFixture : IDisposable
    {
        public TestFixture(
            string webRoot,
            KasaPreviewController controller,
            Mock<IBankaHesapKontrolService> analysis,
            Mock<ICalculatedKasaSnapshotService> snapshots,
            Mock<ILogger<KasaPreviewController>> logger)
        {
            WebRoot = webRoot;
            Controller = controller;
            Analysis = analysis;
            Snapshots = snapshots;
            Logger = logger;
        }

        public string WebRoot { get; }
        public KasaPreviewController Controller { get; }
        public Mock<IBankaHesapKontrolService> Analysis { get; }
        public Mock<ICalculatedKasaSnapshotService> Snapshots { get; }
        public Mock<ILogger<KasaPreviewController>> Logger { get; }
        public CalculatedKasaSnapshot? SavedSnapshot { get; set; }

        public void Dispose()
        {
            if (Directory.Exists(WebRoot))
                Directory.Delete(WebRoot, recursive: true);
        }
    }
}
