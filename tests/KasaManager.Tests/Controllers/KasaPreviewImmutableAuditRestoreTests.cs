using System.Reflection;
using System.Globalization;
using System.Security.Claims;
using System.Text;
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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace KasaManager.Tests.Controllers;

public sealed class KasaPreviewImmutableAuditRestoreTests
{
    private static readonly DateOnly SnapshotDate = new(2026, 7, 14);

    [Theory]
    [InlineData("98.738,00", 98738.00)]
    [InlineData("98738.00", 98738.00)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData(null, 0)]
    public async Task BuildKasaRaporData_PostedAmount_ParsesTurkishAndInvariantFormats(
        string? raw,
        decimal expected)
    {
        using var fixture = CreateFixture(new KasaRaporData());
        fixture.Controller.HttpContext.Request.Form = new FormCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["RptGenelKasa"] = raw
            });
        var method = typeof(KasaPreviewController).GetMethod(
            "BuildKasaRaporDataAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var invocation = Assert.IsType<Task<KasaRaporData>>(method!.Invoke(
            fixture.Controller,
            new object[] { new KasaPreviewViewModel(), false, CancellationToken.None }));
        var data = await invocation;

        Assert.Equal(expected, data.GenelKasa);
    }

    [Fact]
    public async Task DownloadGenelRapor_HistoricalInputFields_ProducesPdfWithPostedAmounts()
    {
        using var fixture = CreateFixture(new KasaRaporData());
        fixture.Controller.HttpContext.Request.Form = new FormCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["RptOnlineReddiyat"] = "2345.67",
                ["RptBankadanCikan"] = "3456.78",
                ["RptToplamStopaj"] = "4567.89"
            });
        var model = new KasaPreviewViewModel
        {
            SelectedDate = SnapshotDate,
            KasaType = "Aksam",
            KasayiYapan = "historical-user"
        };
        var buildMethod = typeof(KasaPreviewController).GetMethod(
            "BuildKasaRaporDataAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(buildMethod);
        var invocation = Assert.IsType<Task<KasaRaporData>>(buildMethod!.Invoke(
            fixture.Controller,
            new object[] { model, false, CancellationToken.None }));

        var data = await invocation;
        Assert.Equal(2345.67m, data.OnlineReddiyat);
        Assert.Equal(3456.78m, data.BankadanCikan);
        Assert.Equal(4567.89m, data.ToplamStopaj);

        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var result = await fixture.Controller.DownloadGenelRapor(model, CancellationToken.None);
        var pdf = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", pdf.ContentType);
        Assert.True(pdf.FileContents.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf.FileContents, 0, 4));
    }

    [Fact]
    public void TryParseAmount_AmbiguousSingleSeparator_UsesInvariantAndLogsWarning()
    {
        using var fixture = CreateFixture(new KasaRaporData());
        var method = typeof(KasaPreviewController).GetMethod(
            "TryParseAmount",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var arguments = new object?[] { "98.738", "test", 0m };

        var parsed = Assert.IsType<bool>(method!.Invoke(fixture.Controller, arguments));

        Assert.True(parsed);
        Assert.Equal(98.738m, Assert.IsType<decimal>(arguments[2]));
        fixture.Logger.Verify(logger => logger.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("AMOUNT-PARSE-AMBIGUOUS")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Theory]
    [InlineData("98738.00", 98738.00)]
    [InlineData("0.5", 0.5)]
    [InlineData("12.3456", 12.3456)]
    public void TryParseAmount_InvariantDecimalPattern_DoesNotLogAmbiguousWarning(
        string raw,
        decimal expected)
    {
        using var fixture = CreateFixture(new KasaRaporData());
        var method = typeof(KasaPreviewController).GetMethod(
            "TryParseAmount",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var arguments = new object?[] { raw, "test", 0m };

        var parsed = Assert.IsType<bool>(method!.Invoke(fixture.Controller, arguments));

        Assert.True(parsed);
        Assert.Equal(expected, Assert.IsType<decimal>(arguments[2]));
        fixture.Logger.Verify(logger => logger.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("AMOUNT-PARSE-AMBIGUOUS")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }

    [Theory]
    [InlineData("98.738,00", 98738.00)]
    [InlineData("1,234.56", 1234.56)]
    public void TryParseAmount_SaveRestoreRoundTrip_DoesNotLogAmbiguousWarning(
        string posted,
        decimal expected)
    {
        using var fixture = CreateFixture(new KasaRaporData());
        var method = typeof(KasaPreviewController).GetMethod(
            "TryParseAmount",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var saveArguments = new object?[] { posted, "Save.Outputs[test]", 0m };
        Assert.True(Assert.IsType<bool>(method.Invoke(fixture.Controller, saveArguments)));
        var savedValue = Assert.IsType<decimal>(saveArguments[2]);
        var invariantPersisted = savedValue.ToString(CultureInfo.InvariantCulture);
        var restoreArguments = new object?[] { invariantPersisted, "Snapshot.Outputs[test]", 0m };

        Assert.True(Assert.IsType<bool>(method.Invoke(fixture.Controller, restoreArguments)));
        Assert.Equal(expected, Assert.IsType<decimal>(restoreArguments[2]));
        fixture.Logger.Verify(logger => logger.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("AMOUNT-PARSE-AMBIGUOUS")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }

    [Theory]
    [InlineData("1234.56", 1234.56)]
    [InlineData(null, 0)]
    public void DownloadBankaFisi_ParseContract_PreservesInvariantAndEmptyValues(
        string? raw,
        decimal expected)
    {
        using var fixture = CreateFixture(new KasaRaporData());
        var method = typeof(KasaPreviewController).GetMethod(
            "TryParseAmount",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var arguments = new object?[] { raw, "Request.Form[PdfStopaj]", 0m };

        Assert.True(Assert.IsType<bool>(method.Invoke(fixture.Controller, arguments)));
        Assert.Equal(expected, Assert.IsType<decimal>(arguments[2]));

        var exportSource = File.ReadAllText(GetRepositoryPath(
            "src", "KasaManager.Web", "Controllers", "KasaPreviewController.Export.cs"));
        Assert.Contains("TryParseAmount(Request.Form[\"PdfStopaj\"].ToString()", exportSource);
        Assert.DoesNotContain("decimal ParseForm", exportSource);
        Assert.DoesNotContain("decimal.TryParse(Request.Form[name]", exportSource);
    }

    [Fact]
    public void TryParseAmount_InvalidNonEmptyInput_ReturnsZeroAndLogsWarning()
    {
        using var fixture = CreateFixture(new KasaRaporData());
        var method = typeof(KasaPreviewController).GetMethod(
            "TryParseAmount",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var arguments = new object?[] { "not-an-amount", "test", 99m };

        var parsed = Assert.IsType<bool>(method!.Invoke(fixture.Controller, arguments));

        Assert.False(parsed);
        Assert.Equal(0m, Assert.IsType<decimal>(arguments[2]));
        fixture.Logger.Verify(logger => logger.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("AMOUNT-PARSE-FAILED")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task LoadSnapshot_StringOutput_ParsesTurkishAmount()
    {
        using var fixture = CreateFixture(new KasaRaporData());
        fixture.Snapshot.OutputsJson = "{\"genel_kasa\":\"98.738,00\"}";

        var result = await fixture.Controller.LoadSnapshot(fixture.Snapshot.Id, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<KasaPreviewViewModel>(view.Model);
        Assert.Equal(98_738m, model.FormulaRun!.Outputs["genel_kasa"]);
    }

    [Fact]
    public async Task LoadSnapshot_LegacyPayload_RestoresNormalFieldsAndShowsLegacyNotice()
    {
        var payload = new KasaRaporData
        {
            VergiKasa = 123.45m,
            MuhabereNo = "MUH/2026-0042",
            Aciklama = "legacy-description",
            GunlukNot = "legacy-daily-note",
            KasayiYapan = "legacy-untrusted-actor",
            GuneAitEksikFazlaTahsilat = 11.11m,
            GuneAitEksikFazlaHarc = 22.22m,
            DundenEksikFazlaTahsilat = 33.33m,
            DundenEksikFazlaHarc = 44.44m,
            DundenEksikFazlaGelenTahsilat = 55.55m,
            DundenEksikFazlaGelenHarc = 66.66m
        };
        using var fixture = CreateFixture(payload);

        var model = await LoadModelAsync(fixture);

        Assert.Equal(123.45m, model.VergiKasaBakiyeToplam);
        Assert.Equal(11.11m, model.GuneAitEksikFazlaTahsilat);
        Assert.Equal(22.22m, model.GuneAitEksikFazlaHarc);
        Assert.Equal(33.33m, model.DundenEksikFazlaTahsilat);
        Assert.Equal(44.44m, model.DundenEksikFazlaHarc);
        Assert.Equal(55.55m, model.DundenEksikFazlaGelenTahsilat);
        Assert.Equal(66.66m, model.DundenEksikFazlaGelenHarc);
        Assert.Equal("legacy-description", model.Aciklama);
        Assert.Equal("legacy-daily-note", model.GunlukKasaNotu);
        Assert.Equal("MUH/2026-0042", model.MuhabereNo);
        Assert.Equal("legacy-untrusted-actor", model.KasayiYapan);
        Assert.Contains("\"MuhabereNo\":\"MUH/2026-0042\"", fixture.Snapshot.KasaRaporDataJson);
        Assert.False(model.HasImmutableAuditData);
        Assert.Equal(0, model.LoadedAuditPayloadVersion);
        Assert.Contains("eski raporda", model.ImmutableAuditNotice);
        Assert.Equal(0m, model.TakipteEksikTahsilat);
        AssertNoLiveHesapKontrolCalls(fixture);
    }

    [Fact]
    public async Task LoadSnapshot_MissingId_PreservesRealReportNotFoundBehavior()
    {
        using var fixture = CreateFixture(new KasaRaporData());
        var missingId = Guid.NewGuid();
        fixture.Snapshots.Setup(service => service.GetByIdAsync(
                missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalculatedKasaSnapshot?)null);

        var result = await fixture.Controller.LoadSnapshot(missingId, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Contains("Rapor bulunamadı", fixture.Controller.TempData["ErrorMessage"]?.ToString());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("legacy-description", "MUH/2026-0042")]
    public async Task LoadSnapshot_LegacyDescriptionAndMuhabereNo_PreserveNullEmptyAndValue(
        string? aciklama,
        string? muhabereNo)
    {
        using var fixture = CreateFixture(new KasaRaporData
        {
            Aciklama = aciklama,
            MuhabereNo = muhabereNo
        });

        var model = await LoadModelAsync(fixture);

        Assert.Equal(aciklama, model.Aciklama);
        Assert.Equal(muhabereNo, model.MuhabereNo);
        AssertNoLiveHesapKontrolCalls(fixture);
    }

    [Fact]
    public async Task LoadSnapshot_PersistedCashier_TakesPriorityOverRelationalCreator()
    {
        using var fixture = CreateFixture(
            new KasaRaporData { KasayiYapan = "persisted-cashier" },
            snapshotCalculatedBy: "real-creator");

        var model = await LoadModelAsync(fixture);

        Assert.Equal("persisted-cashier", model.KasayiYapan);
        AssertNoLiveHesapKontrolCalls(fixture);
    }

    [Fact]
    public async Task LoadSnapshot_MissingCashier_FallsBackToRelationalCreator()
    {
        using var fixture = CreateFixture(
            new KasaRaporData(),
            snapshotCalculatedBy: "legacy-creator");

        var model = await LoadModelAsync(fixture);

        Assert.Equal("legacy-creator", model.KasayiYapan);
        AssertNoLiveHesapKontrolCalls(fixture);
    }

    [Fact]
    public async Task LoadSnapshot_LegacyPayload_LeavesImmutableOnlyFieldsUnavailable()
    {
        using var fixture = CreateFixture(new KasaRaporData
        {
            GuneAitEksikFazlaTahsilat = 12.34m
        });

        var model = await LoadModelAsync(fixture);

        Assert.Equal(12.34m, model.GuneAitEksikFazlaTahsilat);
        Assert.False(model.HasImmutableAuditData);
        Assert.Contains("eski raporda", model.ImmutableAuditNotice);
        Assert.Equal(0m, model.TakipteEksikTahsilat);
        Assert.Equal(0, model.TakipteSayisi);
        Assert.Null(model.TakipKasaEtkisiNet);
        Assert.Null(model.BreakdownMesajTahsilat);
        Assert.False(model.HasOnlyZeroLoadedImmutableAuditData);
        fixture.Analysis.Verify(service => service.GetAutoFillDataAsync(
            It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoadSnapshot_VersionOneNonZero_RestoresEveryAuditScalar()
    {
        var audit = CreateNonZeroAudit();
        using var fixture = CreateFixture(new KasaRaporData
        {
            PayloadVersion = 1,
            ImmutableAudit = audit
        });

        var model = await LoadModelAsync(fixture);

        Assert.True(model.HasImmutableAuditData);
        Assert.Null(model.ImmutableAuditNotice);
        Assert.Equal(1, model.LoadedAuditPayloadVersion);
        Assert.Equal(audit.GuneAitEksikFazlaTahsilat, model.GuneAitEksikFazlaTahsilat);
        Assert.Equal(audit.GuneAitEksikFazlaHarc, model.GuneAitEksikFazlaHarc);
        Assert.Equal(audit.OncekiGunAcikTahsilat, model.DundenEksikFazlaTahsilat);
        Assert.Equal(audit.OncekiGunAcikHarc, model.DundenEksikFazlaHarc);
        Assert.Equal(audit.BugunCozulenTahsilat, model.DundenEksikFazlaGelenTahsilat);
        Assert.Equal(audit.BugunCozulenHarc, model.DundenEksikFazlaGelenHarc);
        Assert.Equal(audit.TakipteEksikTahsilat, model.TakipteEksikTahsilat);
        Assert.Equal(audit.TakipteEksikHarc, model.TakipteEksikHarc);
        Assert.Equal(audit.TakipteFazlaTahsilat, model.TakipteFazlaTahsilat);
        Assert.Equal(audit.TakipteFazlaHarc, model.TakipteFazlaHarc);
        Assert.Equal(audit.TakipteSayisi, model.TakipteSayisi);
        Assert.Equal(audit.ToplamFarkTahsilat, model.ToplamFarkTahsilat);
        Assert.Equal(audit.ToplamFarkHarc, model.ToplamFarkHarc);
        Assert.Equal(audit.BeklenenTahsilat, model.BeklenenTahsilat);
        Assert.Equal(audit.BeklenenHarc, model.BeklenenHarc);
        Assert.Equal(audit.OlaganDisiTahsilat, model.OlaganDisiTahsilat);
        Assert.Equal(audit.OlaganDisiHarc, model.OlaganDisiHarc);
        Assert.Equal(audit.TakipKasaEtkisiTahsilat, model.TakipKasaEtkisiTahsilat);
        Assert.Equal(audit.TakipKasaEtkisiHarc, model.TakipKasaEtkisiHarc);
        Assert.Equal(audit.TakipKasaEtkisiNet, model.TakipKasaEtkisiNet);
        Assert.Equal(audit.BreakdownMesajTahsilat, model.BreakdownMesajTahsilat);
        Assert.Equal(audit.BreakdownMesajHarc, model.BreakdownMesajHarc);
        Assert.False(model.HasOnlyZeroLoadedImmutableAuditData);
        Assert.False(model.HasImmutableAuditRecordDetails);
        Assert.Contains("bu sürümde bulunmuyor", model.ImmutableAuditRecordDetailsNotice);
        AssertNoLiveHesapKontrolCalls(fixture);
    }

    [Fact]
    public async Task LoadSnapshot_VersionOneRealZero_IsDistinctFromMissingAudit()
    {
        using var fixture = CreateFixture(new KasaRaporData
        {
            PayloadVersion = 1,
            ImmutableAudit = new KasaImmutableAuditData()
        });

        var model = await LoadModelAsync(fixture);

        Assert.True(model.HasImmutableAuditData);
        Assert.Null(model.ImmutableAuditNotice);
        Assert.Equal(1, model.LoadedAuditPayloadVersion);
        Assert.True(model.HasOnlyZeroLoadedImmutableAuditData);
        Assert.False(model.HasImmutableAuditRecordDetails);
        Assert.Contains("bu sürümde bulunmuyor", model.ImmutableAuditRecordDetailsNotice);
        AssertNoLiveHesapKontrolCalls(fixture);
    }

    [Fact]
    public async Task LoadSnapshot_VersionOneMissingAudit_ShowsCorruptPayloadNoticeWithoutLiveFallback()
    {
        using var fixture = CreateFixture(new KasaRaporData
        {
            PayloadVersion = 1,
            ImmutableAudit = null
        });

        var model = await LoadModelAsync(fixture);

        Assert.False(model.HasImmutableAuditData);
        Assert.Equal(1, model.LoadedAuditPayloadVersion);
        Assert.Contains("eksik veya okunamadı", model.ImmutableAuditNotice);
        AssertNoLiveHesapKontrolCalls(fixture);
    }

    [Fact]
    public async Task LoadSnapshot_NewerPayload_RestoresNormalFieldsButDoesNotTrustAudit()
    {
        using var fixture = CreateFixture(new KasaRaporData
        {
            PayloadVersion = 3,
            ImmutableAudit = CreateNonZeroAudit(),
            VergiKasa = 456.78m
        });

        var model = await LoadModelAsync(fixture);

        Assert.Equal(456.78m, model.VergiKasaBakiyeToplam);
        Assert.False(model.HasImmutableAuditData);
        Assert.Equal(3, model.LoadedAuditPayloadVersion);
        Assert.Contains("daha yeni", model.ImmutableAuditNotice);
        Assert.Equal(0m, model.TakipteEksikTahsilat);
        Assert.False(model.HasImmutableAuditRecordDetails);
        AssertNoLiveHesapKontrolCalls(fixture);
    }

    [Fact]
    public async Task LoadSnapshot_VersionTwoValidNonEmpty_RestoresScalarAndMappedDetails()
    {
        var details = CreateValidDetails();
        using var fixture = CreateFixture(new KasaRaporData
        {
            PayloadVersion = 2,
            ImmutableAudit = CreateNonZeroAudit(),
            ImmutableAuditDetails = JsonSerializer.SerializeToElement(details)
        });

        var model = await LoadModelAsync(fixture);

        Assert.True(model.HasImmutableAuditData);
        Assert.True(model.HasImmutableAuditRecordDetails);
        Assert.Null(model.ImmutableAuditNotice);
        Assert.Null(model.ImmutableAuditRecordDetailsNotice);
        var record = Assert.Single(model.ImmutableAuditRecords);
        Assert.Equal(details.Records[0].KayitId, record.KayitId);
        Assert.Equal(details.Records[0].AnalizTarihi, record.AnalizTarihi);
        Assert.Equal(details.Records[0].HesapTuru, record.HesapTuru);
        Assert.Equal(details.Records[0].Yon, record.Yon);
        Assert.Equal(details.Records[0].Tutar, record.Tutar);
        Assert.Equal(details.Records[0].KaydetmeAnindakiDurum, record.KaydetmeAnindakiDurum);
        Assert.Equal(details.Records[0].Sinif, record.Sinif);
        Assert.Equal("2026/42", record.DosyaNo);
        Assert.Equal(details.Records[0].BirimAdi, record.BirimAdi);
        Assert.Equal(details.Records[0].TespitEdilenTip, record.TespitEdilenTip);
        Assert.Equal(details.Records[0].TakipBaslangicTarihi, record.TakipBaslangicTarihi);
        Assert.Equal(details.Records[0].CozulmeTarihi, record.CozulmeTarihi);
        Assert.Equal(details.Records[0].OnayTarihi, record.OnayTarihi);
        Assert.Equal(details.Groups.AktifKayitlar, model.ImmutableAuditRecordGroups.AktifKayitlar);
        AssertNoLiveHesapKontrolCalls(fixture);
    }

    [Fact]
    public async Task LoadSnapshot_VersionTwoValidEmpty_IsAvailableAndNotAnError()
    {
        using var fixture = CreateFixture(new KasaRaporData
        {
            PayloadVersion = 2,
            ImmutableAudit = new KasaImmutableAuditData(),
            ImmutableAuditDetails = JsonSerializer.SerializeToElement(EmptyDetails())
        });

        var model = await LoadModelAsync(fixture);

        Assert.True(model.HasImmutableAuditData);
        Assert.True(model.HasImmutableAuditRecordDetails);
        Assert.Empty(model.ImmutableAuditRecords);
        Assert.Null(model.ImmutableAuditRecordDetailsNotice);
        AssertNoLiveHesapKontrolCalls(fixture);
    }

    [Fact]
    public async Task LoadSnapshot_VersionTwo_RestoresNegativeAuditRootsAndRelationalCreator()
    {
        var audit = CreateNonZeroAudit();
        audit.GuneAitEksikFazlaTahsilat = -3000.00m;
        audit.GuneAitEksikFazlaHarc = -29873.80m;
        using var fixture = CreateFixture(
            new KasaRaporData
            {
                PayloadVersion = 2,
                GuneAitEksikFazlaTahsilat = 0m,
                GuneAitEksikFazlaHarc = 0m,
                ImmutableAudit = audit,
                ImmutableAuditDetails = JsonSerializer.SerializeToElement(CreateValidDetails())
            },
            snapshotCalculatedBy: "ESRA DAĞAŞAN");

        var model = await LoadModelAsync(fixture);

        Assert.Equal(-3000.00m, model.GuneAitEksikFazlaTahsilat);
        Assert.Equal(-29873.80m, model.GuneAitEksikFazlaHarc);
        Assert.Equal("ESRA DAĞAŞAN", model.KasayiYapan);
        Assert.True(model.HasImmutableAuditData);
        Assert.True(model.HasImmutableAuditRecordDetails);
        Assert.Equal(fixture.Snapshot.Id, model.LoadedSnapshotId);
        AssertNoLiveHesapKontrolCalls(fixture);
    }

    [Fact]
    public async Task LoadSnapshot_VersionTwoNonZeroScalarWithEmptyDetails_IsRejectedAsCorruptDetails()
    {
        using var fixture = CreateFixture(new KasaRaporData
        {
            PayloadVersion = 2,
            ImmutableAudit = CreateNonZeroAudit(),
            ImmutableAuditDetails = JsonSerializer.SerializeToElement(EmptyDetails())
        });

        var model = await LoadModelAsync(fixture);

        Assert.True(model.HasImmutableAuditData);
        Assert.False(model.HasImmutableAuditRecordDetails);
        Assert.Contains("bozuk veya doğrulanamadı", model.ImmutableAuditRecordDetailsNotice);
        AssertNoLiveHesapKontrolCalls(fixture);
    }

    [Fact]
    public async Task LoadSnapshot_VersionTwoDetailsNull_KeepsValidScalarAndMarksDetailsCorrupt()
    {
        using var fixture = CreateFixture(new KasaRaporData
        {
            PayloadVersion = 2,
            ImmutableAudit = CreateNonZeroAudit(),
            ImmutableAuditDetails = null
        });

        var model = await LoadModelAsync(fixture);

        Assert.True(model.HasImmutableAuditData);
        Assert.Equal(7.07m, model.TakipteEksikTahsilat);
        Assert.False(model.HasImmutableAuditRecordDetails);
        Assert.Contains("bozuk veya doğrulanamadı", model.ImmutableAuditRecordDetailsNotice);
        AssertNoLiveHesapKontrolCalls(fixture);
    }

    [Theory]
    [MemberData(nameof(InvalidVersionTwoDetails))]
    public async Task LoadSnapshot_VersionTwoStructurallyInvalidDetails_AreUnavailableWithoutLosingScalar(
        string _,
        JsonElement invalidDetails)
    {
        using var fixture = CreateFixture(new KasaRaporData
        {
            PayloadVersion = 2,
            ImmutableAudit = CreateNonZeroAudit(),
            ImmutableAuditDetails = invalidDetails
        });

        var model = await LoadModelAsync(fixture);

        Assert.True(model.HasImmutableAuditData);
        Assert.False(model.HasImmutableAuditRecordDetails);
        Assert.Contains("bozuk veya doğrulanamadı", model.ImmutableAuditRecordDetailsNotice);
        Assert.Empty(model.ImmutableAuditRecords);
        AssertNoLiveHesapKontrolCalls(fixture);
    }

    [Fact]
    public async Task LoadSnapshot_VersionTwoInvalidScalar_DoesNotTrustDetailsAlone()
    {
        using var fixture = CreateFixture(new KasaRaporData
        {
            PayloadVersion = 2,
            ImmutableAudit = null,
            ImmutableAuditDetails = JsonSerializer.SerializeToElement(CreateValidDetails())
        });

        var model = await LoadModelAsync(fixture);

        Assert.False(model.HasImmutableAuditData);
        Assert.False(model.HasImmutableAuditRecordDetails);
        Assert.Contains("eksik veya okunamadı", model.ImmutableAuditNotice);
        Assert.Empty(model.ImmutableAuditRecords);
        AssertNoLiveHesapKontrolCalls(fixture);
    }

    [Fact]
    public async Task LoadSnapshot_Get_RemainsReadOnlyAndDoesNotRunLiveHesapKontrol()
    {
        using var fixture = CreateFixture(new KasaRaporData
        {
            PayloadVersion = 1,
            ImmutableAudit = CreateNonZeroAudit()
        });

        await LoadModelAsync(fixture);

        fixture.Analysis.Verify(service => service.GetAutoFillDataAsync(
            It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Analysis.Verify(service => service.GetImmutableAuditSnapshotAsync(
            It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Analysis.Verify(service => service.AnalyzeFromComparisonAsync(
            It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Snapshots.Verify(service => service.SaveAsync(
            It.IsAny<CalculatedKasaSnapshot>(), It.IsAny<int>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.ReportSnapshots.Verify(service => service.SaveAsync(
            It.IsAny<KasaRaporSnapshot>(), It.IsAny<CancellationToken>()), Times.Never);

        var source = File.ReadAllText(GetRepositoryPath(
            "src", "KasaManager.Web", "Controllers", "KasaPreviewController.cs"));
        var start = source.IndexOf(
            "public async Task<IActionResult> LoadSnapshot", StringComparison.Ordinal);
        var end = source.IndexOf(
            "public async Task<IActionResult> DeleteSnapshot", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var block = source[start..end];
        Assert.DoesNotContain("GetAutoFillDataAsync", block);
        Assert.DoesNotContain("TryAutoFillEksikFazlaAsync", block);
        Assert.DoesNotContain("SaveAsync", block);
        Assert.DoesNotContain("SaveChanges", block);
    }

    [Fact]
    public async Task LoadSnapshot_DifferentAuthenticatedUserCanOpenSharedSnapshot()
    {
        using var fixture = CreateFixture(
            new KasaRaporData
            {
                PayloadVersion = 2,
                ImmutableAudit = CreateNonZeroAudit(),
                ImmutableAuditDetails = JsonSerializer.SerializeToElement(CreateValidDetails())
            },
            currentUserId: 29,
            currentUsername: "user-b",
            snapshotCreatorUserId: 17);

        var model = await LoadModelAsync(fixture);

        Assert.Equal(fixture.Snapshot.Id, model.LoadedSnapshotId);
        Assert.Equal(17, fixture.Snapshot.CalculatedByUserId);
        Assert.True(model.HasImmutableAuditRecordDetails);
        fixture.Snapshots.Verify(service => service.SaveAsync(
            It.IsAny<CalculatedKasaSnapshot>(), It.IsAny<int>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoadSnapshot_LegacyCreatorNullRemainsReadable()
    {
        using var fixture = CreateFixture(
            new KasaRaporData { PayloadVersion = 0 },
            currentUserId: 29,
            currentUsername: "user-b",
            snapshotCreatorUserId: null);

        var model = await LoadModelAsync(fixture);

        Assert.Equal(fixture.Snapshot.Id, model.LoadedSnapshotId);
        Assert.Null(fixture.Snapshot.CalculatedByUserId);
    }

    [Fact]
    public async Task LiveAutoFill_SuccessMarksAvailabilityWithoutLegacyNotice()
    {
        using var fixture = CreateFixture(new KasaRaporData());
        var fill = new EksikFazlaAutoFill(
            1m, 2m, 3m, 4m, 5m, 6m, true, "live",
            TakipteEksikTahsilat: 7m,
            TakipteSayisi: 1,
            ToplamFarkTahsilat: 8m);
        fixture.Analysis.Setup(service => service.GetAutoFillDataAsync(
                SnapshotDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fill);
        var model = new KasaPreviewViewModel { SelectedDate = SnapshotDate };
        var method = typeof(KasaPreviewController).GetMethod(
            "TryAutoFillEksikFazlaAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var invocation = Assert.IsAssignableFrom<Task>(method.Invoke(
            fixture.Controller, new object[] { model, CancellationToken.None }));
        await invocation;

        Assert.True(model.HasImmutableAuditData);
        Assert.Null(model.ImmutableAuditNotice);
        Assert.Equal(0, model.LoadedAuditPayloadVersion);
        Assert.Equal(7m, model.TakipteEksikTahsilat);
        Assert.Equal(8m, model.ToplamFarkTahsilat);
        Assert.False(model.HasImmutableAuditRecordDetails);
        Assert.Null(model.ImmutableAuditRecordDetailsNotice);
    }

    [Fact]
    public void Calculate_WinnerPoolValues_AreCopiedToPostedModelFields()
    {
        using var fixture = CreateFixture(new KasaRaporData());
        var model = new KasaPreviewViewModel
        {
            GuneAitEksikFazlaTahsilat = 0m,
            GuneAitEksikFazlaHarc = 0m,
            DundenEksikFazlaTahsilat = 0m,
            DundenEksikFazlaHarc = 0m,
            PoolEntries = new()
            {
                new() { CanonicalKey = "gune_ait_eksik_fazla_tahsilat", Value = "-3000" },
                new() { CanonicalKey = "gune_ait_eksik_fazla_harc", Value = "-29873.80" },
                new() { CanonicalKey = "dunden_eksik_fazla_tahsilat", Value = "101.01" },
                new() { CanonicalKey = "dunden_eksik_fazla_harc", Value = "202.02" }
            }
        };
        var method = typeof(KasaPreviewController).GetMethod(
            "LogValueSourceDiagnostics",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        method.Invoke(fixture.Controller, new object[] { model, "Calculate" });

        Assert.Equal(-3000m, model.GuneAitEksikFazlaTahsilat);
        Assert.Equal(-29873.80m, model.GuneAitEksikFazlaHarc);
        Assert.Equal(101.01m, model.DundenEksikFazlaTahsilat);
        Assert.Equal(202.02m, model.DundenEksikFazlaHarc);
    }

    [Theory]
    [InlineData("gune_ait_eksik_fazla_harc", "GuneAitEksikFazlaHarc")]
    [InlineData("gune_ait_eksik_fazla_tahsilat", "GuneAitEksikFazlaTahsilat")]
    [InlineData("dunden_eksik_fazla_harc", "DundenEksikFazlaHarc")]
    [InlineData("dunden_eksik_fazla_tahsilat", "DundenEksikFazlaTahsilat")]
    [InlineData("dunden_eksik_fazla_gelen_harc", "DundenEksikFazlaGelenHarc")]
    [InlineData("dunden_eksik_fazla_gelen_tahsilat", "DundenEksikFazlaGelenTahsilat")]
    public void Razor_ResultValRaw_ImmutableAuditBranchPrecedesPoolInputOutput(
        string key,
        string modelProperty)
    {
        var source = File.ReadAllText(GetRepositoryPath(
            "src", "KasaManager.Web", "Views", "KasaPreview", "Index.cshtml"));
        var resultValRawStart = source.IndexOf(
            "decimal ResultValRaw(string key)", StringComparison.Ordinal);
        var poolPriorityStart = source.IndexOf(
            "// PoolVal ile aynı kaynak önceliği: Pool > Input > Output.",
            resultValRawStart,
            StringComparison.Ordinal);
        Assert.True(resultValRawStart >= 0 && poolPriorityStart > resultValRawStart);
        var immutableBranch = source[resultValRawStart..poolPriorityStart];

        Assert.Contains($"\"{key}\"", immutableBranch, StringComparison.Ordinal);
        Assert.Contains($"=> Model.{modelProperty}", immutableBranch, StringComparison.Ordinal);
        Assert.Contains(
            "if (immutableAuditValue.HasValue) return immutableAuditValue.Value;",
            immutableBranch,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RazorAndInitialModel_DistinguishNoticeZeroAuditAndLiveState()
    {
        var initial = new KasaPreviewViewModel();
        Assert.False(initial.HasImmutableAuditData);
        Assert.Null(initial.ImmutableAuditNotice);
        Assert.False(initial.HasOnlyZeroLoadedImmutableAuditData);
        Assert.False(initial.HasImmutableAuditRecordDetails);
        Assert.Null(initial.ImmutableAuditRecordDetailsNotice);

        var source = File.ReadAllText(GetRepositoryPath(
            "src", "KasaManager.Web", "Views", "KasaPreview", "Index.cshtml"));
        Assert.Contains("data-testid=\"immutable-audit-notice\"", source);
        Assert.Contains("data-testid=\"immutable-audit-zero-result\"", source);
        Assert.Contains("data-testid=\"immutable-audit-loaded\"", source);
        Assert.Contains("Kaydetme anında takipte eksik veya fazla kayıt bulunmuyordu.", source);
        Assert.Contains("Model.HasOnlyZeroLoadedImmutableAuditData", source);
        Assert.Contains("string.IsNullOrWhiteSpace(Model.ImmutableAuditNotice)", source);
        Assert.Contains("Model.TakipteSayisi > 0", source);
        Assert.Contains("Model.ToplamFarkTahsilat != 0m", source);
        Assert.Contains("Model.HesapKontrolAutoFillMessage ??", source);
        Assert.Contains("immutable-audit-details-notice", source);
        Assert.Contains("immutable-audit-details-empty", source);
        Assert.Contains("immutable-audit-details-table", source);
        Assert.Contains("role=\"@(detailsCorrupt ? \"alert\" : \"status\")\"", source);
    }

    [Fact]
    public void Razor_GroupedDetailsExposeOnlyApprovedColumnsAndUseEncodedExpressions()
    {
        var source = File.ReadAllText(GetRepositoryPath(
            "src", "KasaManager.Web", "Views", "KasaPreview", "Index.cshtml"));
        var start = source.IndexOf(
            "data-testid=\"immutable-audit-details\"", StringComparison.Ordinal);
        var end = source.IndexOf(
            "@if (Model.TakipteSayisi > 0)", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var block = source[start..end];

        Assert.Contains("Analiz tarihi", block);
        Assert.Contains("Dosya no", block);
        Assert.Contains("Birim", block);
        Assert.Contains("Tespit tipi", block);
        Assert.DoesNotContain("Actor", block, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UserId", block, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Username", block, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Aciklama", block, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Notlar", block, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Html.Raw", block, StringComparison.Ordinal);
    }

    [Fact]
    public void Razor_LegacyNotice_DoesNotHideTopLevelHistoricalFields()
    {
        var source = File.ReadAllText(GetRepositoryPath(
            "src", "KasaManager.Web", "Views", "KasaPreview", "Index.cshtml"));

        Assert.DoesNotContain(
            "@if (string.IsNullOrWhiteSpace(Model.ImmutableAuditNotice))", source);
        Assert.Contains("asp-for=\"GuneAitEksikFazlaTahsilat\"", source);
        Assert.Contains("asp-for=\"DundenEksikFazlaTahsilat\"", source);
        Assert.Contains("asp-for=\"DundenEksikFazlaGelenTahsilat\"", source);
        Assert.Contains("asp-for=\"GuneAitEksikFazlaHarc\"", source);
        Assert.Contains("asp-for=\"DundenEksikFazlaHarc\"", source);
        Assert.Contains("asp-for=\"DundenEksikFazlaGelenHarc\"", source);
    }

    [Fact]
    public void Razor_LoadedSnapshotDescriptionAndMuhabereNo_AreReadOnlyEncodedExpressions()
    {
        var source = File.ReadAllText(GetRepositoryPath(
            "src", "KasaManager.Web", "Views", "KasaPreview", "Index.cshtml"));
        var start = source.IndexOf("id=\"loadedReportBar\"", StringComparison.Ordinal);
        var end = source.IndexOf("id=\"reportsPanel\"", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var block = source[start..end];

        Assert.Contains("@Model.Aciklama", block, StringComparison.Ordinal);
        Assert.Contains("@Model.MuhabereNo", block, StringComparison.Ordinal);
        Assert.DoesNotContain("Html.Raw", block, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"MuhabereNo\"", block, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-for=\"MuhabereNo\"", block, StringComparison.Ordinal);
    }

    [Fact]
    public void Razor_CashierCardAndSaveForm_BindBusinessMetadataFields()
    {
        var source = File.ReadAllText(GetRepositoryPath(
            "src", "KasaManager.Web", "Views", "KasaPreview", "Index.cshtml"));
        var cardStart = source.IndexOf("Kasayı Yapan", StringComparison.Ordinal);
        var cardEnd = source.IndexOf("Günlük Kasa Notu", cardStart, StringComparison.Ordinal);
        Assert.True(cardStart >= 0 && cardEnd > cardStart);
        var card = source[cardStart..cardEnd];

        Assert.Contains("Model.KasayiYapan", card, StringComparison.Ordinal);
        Assert.DoesNotContain("CalculatedBy", card, StringComparison.Ordinal);
        Assert.Contains("id=\"saveReportForm\"", source, StringComparison.Ordinal);
        Assert.Contains("asp-for=\"Aciklama\"", source, StringComparison.Ordinal);
    }

    private static async Task<KasaPreviewViewModel> LoadModelAsync(TestFixture fixture)
    {
        var result = await fixture.Controller.LoadSnapshot(
            fixture.Snapshot.Id, CancellationToken.None);
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        return Assert.IsType<KasaPreviewViewModel>(view.Model);
    }

    private static void AssertNoLiveHesapKontrolCalls(TestFixture fixture)
    {
        fixture.Analysis.Verify(service => service.GetAutoFillDataAsync(
            It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Analysis.Verify(service => service.GetImmutableAuditSnapshotAsync(
            It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Analysis.Verify(service => service.AnalyzeFromComparisonAsync(
            It.IsAny<DateOnly>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static TestFixture CreateFixture(
        KasaRaporData payload,
        int currentUserId = 17,
        string currentUsername = "audit-user",
        int? snapshotCreatorUserId = 17,
        string snapshotCalculatedBy = "audit-user")
    {
        var webRoot = Path.Combine(Path.GetTempPath(), $"kasa_audit_restore_{Guid.NewGuid():N}");
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
        var snapshots = new Mock<ICalculatedKasaSnapshotService>();
        var reportSnapshots = new Mock<IKasaRaporSnapshotService>();
        var financialExceptions = new Mock<IFinansalIstisnaService>();
        financialExceptions.Setup(service => service.ListByDateAsync(
                SnapshotDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FinansalIstisna>());

        var snapshot = new CalculatedKasaSnapshot
        {
            Id = Guid.NewGuid(),
            RaporTarihi = SnapshotDate,
            KasaTuru = KasaRaporTuru.Aksam,
            Name = "Audit restore",
            CalculatedBy = snapshotCalculatedBy,
            CalculatedByUserId = snapshotCreatorUserId,
            InputsJson = "{}",
            OutputsJson = "{}",
            KasaRaporDataJson = JsonSerializer.Serialize(payload)
        };
        snapshots.Setup(service => service.GetByIdAsync(
                snapshot.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var logger = new Mock<ILogger<KasaPreviewController>>();
        var controller = new KasaPreviewController(
            Mock.Of<IKasaOrchestrator>(),
            environment.Object,
            configuration,
            Mock.Of<IImportOrchestrator>(),
            Mock.Of<IKasaReportDateRulesService>(),
            defaults.Object,
            analysis.Object,
            Mock.Of<ICurrentUser>(user =>
                user.IsAuthenticated &&
                user.UserId == currentUserId &&
                user.Username == currentUsername),
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
            reportSnapshots.Object,
            Mock.Of<IEffectiveAnalysisDateResolver>());
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "audit-user") }, "TestAuth"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(
            httpContext, Mock.Of<ITempDataProvider>());

        return new TestFixture(
            webRoot, controller, snapshot, analysis, snapshots, reportSnapshots, logger);
    }

    public static IEnumerable<object[]> InvalidVersionTwoDetails()
    {
        var record = CreateValidRecord();
        var id = record.KayitId;

        yield return new object[]
        {
            "records-null",
            JsonSerializer.SerializeToElement(new HesapKontrolImmutableAuditDetails(
                null!, EmptyGroups()))
        };
        yield return new object[]
        {
            "group-null",
            JsonSerializer.SerializeToElement(new HesapKontrolImmutableAuditDetails(
                new[] { record },
                new HesapKontrolImmutableAuditGroups(
                    null!, Array.Empty<Guid>(), Array.Empty<Guid>(),
                    Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>())))
        };
        yield return new object[]
        {
            "duplicate-record",
            JsonSerializer.SerializeToElement(new HesapKontrolImmutableAuditDetails(
                new[] { record, record },
                new HesapKontrolImmutableAuditGroups(
                    new[] { id }, Array.Empty<Guid>(), Array.Empty<Guid>(),
                    Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>())))
        };
        yield return new object[]
        {
            "duplicate-group-reference",
            JsonSerializer.SerializeToElement(new HesapKontrolImmutableAuditDetails(
                new[] { record },
                new HesapKontrolImmutableAuditGroups(
                    new[] { id, id }, Array.Empty<Guid>(), Array.Empty<Guid>(),
                    Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>())))
        };
        yield return new object[]
        {
            "missing-record-reference",
            JsonSerializer.SerializeToElement(new HesapKontrolImmutableAuditDetails(
                Array.Empty<HesapKontrolImmutableAuditRecord>(),
                new HesapKontrolImmutableAuditGroups(
                    new[] { id }, Array.Empty<Guid>(), Array.Empty<Guid>(),
                    Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>())))
        };
        yield return new object[]
        {
            "unreferenced-record",
            JsonSerializer.SerializeToElement(new HesapKontrolImmutableAuditDetails(
                new[] { record }, EmptyGroups()))
        };
        yield return new object[]
        {
            "unknown-enum",
            JsonSerializer.SerializeToElement(new HesapKontrolImmutableAuditDetails(
                new[] { record with { HesapTuru = (BankaHesapTuru)999 } },
                new HesapKontrolImmutableAuditGroups(
                    new[] { id }, Array.Empty<Guid>(), Array.Empty<Guid>(),
                    Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>())))
        };
        var high = record with
        {
            KayitId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")
        };
        var low = record with
        {
            KayitId = Guid.Parse("00000000-0000-0000-0000-000000000001")
        };
        yield return new object[]
        {
            "record-order",
            JsonSerializer.SerializeToElement(new HesapKontrolImmutableAuditDetails(
                new[] { high, low },
                new HesapKontrolImmutableAuditGroups(
                    new[] { low.KayitId, high.KayitId }, Array.Empty<Guid>(), Array.Empty<Guid>(),
                    Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>())))
        };
        yield return new object[]
        {
            "group-order",
            JsonSerializer.SerializeToElement(new HesapKontrolImmutableAuditDetails(
                new[] { low, high },
                new HesapKontrolImmutableAuditGroups(
                    new[] { high.KayitId, low.KayitId }, Array.Empty<Guid>(), Array.Empty<Guid>(),
                    Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>())))
        };
        var approvedWithoutDate = record with
        {
            KaydetmeAnindakiDurum = KayitDurumu.Onaylandi,
            Sinif = FarkSinifi.Askida,
            OnayTarihi = null
        };
        yield return new object[]
        {
            "approved-date-null",
            JsonSerializer.SerializeToElement(new HesapKontrolImmutableAuditDetails(
                new[] { approvedWithoutDate },
                new HesapKontrolImmutableAuditGroups(
                    Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>(),
                    new[] { id }, Array.Empty<Guid>(), Array.Empty<Guid>())))
        };
        var trackedResolvedWithoutResolutionDate = record with
        {
            KaydetmeAnindakiDurum = KayitDurumu.Cozuldu,
            TakipBaslangicTarihi = SnapshotDate.AddDays(-1),
            CozulmeTarihi = null
        };
        yield return new object[]
        {
            "tracking-resolution-date-null",
            JsonSerializer.SerializeToElement(new HesapKontrolImmutableAuditDetails(
                new[] { trackedResolvedWithoutResolutionDate },
                new HesapKontrolImmutableAuditGroups(
                    Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<Guid>(),
                    Array.Empty<Guid>(), Array.Empty<Guid>(), new[] { id })))
        };
    }

    private static HesapKontrolImmutableAuditDetails CreateValidDetails()
    {
        var record = CreateValidRecord();
        return new HesapKontrolImmutableAuditDetails(
            new[] { record },
            new HesapKontrolImmutableAuditGroups(
                new[] { record.KayitId },
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                Array.Empty<Guid>(),
                Array.Empty<Guid>()));
    }

    private static HesapKontrolImmutableAuditRecord CreateValidRecord() => new(
        Guid.Parse("00000000-0000-0000-0000-000000000042"),
        SnapshotDate,
        BankaHesapTuru.Tahsilat,
        KayitYonu.Eksik,
        42.42m,
        KayitDurumu.Acik,
        FarkSinifi.Bilinmeyen,
        "2026/42",
        "Birim 42",
        "BILINMEYEN",
        null,
        null,
        null);

    private static HesapKontrolImmutableAuditDetails EmptyDetails() => new(
        Array.Empty<HesapKontrolImmutableAuditRecord>(), EmptyGroups());

    private static HesapKontrolImmutableAuditGroups EmptyGroups() => new(
        Array.Empty<Guid>(),
        Array.Empty<Guid>(),
        Array.Empty<Guid>(),
        Array.Empty<Guid>(),
        Array.Empty<Guid>(),
        Array.Empty<Guid>());

    private static KasaImmutableAuditData CreateNonZeroAudit() => new()
    {
        GuneAitEksikFazlaTahsilat = 1.01m,
        GuneAitEksikFazlaHarc = 2.02m,
        OncekiGunAcikTahsilat = 3.03m,
        OncekiGunAcikHarc = 4.04m,
        BugunCozulenTahsilat = 5.05m,
        BugunCozulenHarc = 6.06m,
        TakipteEksikTahsilat = 7.07m,
        TakipteEksikHarc = 8.08m,
        TakipteFazlaTahsilat = 9.09m,
        TakipteFazlaHarc = 10.10m,
        TakipteSayisi = 11,
        ToplamFarkTahsilat = 12.12m,
        ToplamFarkHarc = 13.13m,
        BeklenenTahsilat = 14.14m,
        BeklenenHarc = 15.15m,
        OlaganDisiTahsilat = 16.16m,
        OlaganDisiHarc = 17.17m,
        TakipKasaEtkisiTahsilat = 18.18m,
        TakipKasaEtkisiHarc = 19.19m,
        TakipKasaEtkisiNet = -1.01m,
        BreakdownMesajTahsilat = "Tahsilat audit",
        BreakdownMesajHarc = "Harç audit"
    };

    private static string GetRepositoryPath(params string[] segments)
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(new[] { root }.Concat(segments).ToArray());
    }

    private sealed class TestFixture : IDisposable
    {
        public TestFixture(
            string webRoot,
            KasaPreviewController controller,
            CalculatedKasaSnapshot snapshot,
            Mock<IBankaHesapKontrolService> analysis,
            Mock<ICalculatedKasaSnapshotService> snapshots,
            Mock<IKasaRaporSnapshotService> reportSnapshots,
            Mock<ILogger<KasaPreviewController>> logger)
        {
            WebRoot = webRoot;
            Controller = controller;
            Snapshot = snapshot;
            Analysis = analysis;
            Snapshots = snapshots;
            ReportSnapshots = reportSnapshots;
            Logger = logger;
        }

        public string WebRoot { get; }
        public KasaPreviewController Controller { get; }
        public CalculatedKasaSnapshot Snapshot { get; }
        public Mock<IBankaHesapKontrolService> Analysis { get; }
        public Mock<ICalculatedKasaSnapshotService> Snapshots { get; }
        public Mock<IKasaRaporSnapshotService> ReportSnapshots { get; }
        public Mock<ILogger<KasaPreviewController>> Logger { get; }

        public void Dispose()
        {
            if (Directory.Exists(WebRoot))
                Directory.Delete(WebRoot, recursive: true);
        }
    }
}
