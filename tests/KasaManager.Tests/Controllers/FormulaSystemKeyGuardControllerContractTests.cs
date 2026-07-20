using KasaManager.Application.Abstractions;
using KasaManager.Application.Orchestration;
using KasaManager.Application.Orchestration.Dtos;
using KasaManager.Application.Services.ReadAdapter;
using KasaManager.Domain.Abstractions;
using KasaManager.Web.Controllers;
using KasaManager.Web.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;

namespace KasaManager.Tests.Controllers;

public sealed class FormulaSystemKeyGuardControllerContractTests
{
    [Fact]
    public async Task RunFormulaEngine_DbCreate_EmptyPostedPoolUsesHydratingCreateAndShowsTurkishError()
    {
        var orchestrator = CreateOrchestratorContractMock();
        var controller = CreatePreviewController(orchestrator.Object);
        controller.HttpContext.Request.Form = new FormCollection(
            new Dictionary<string, StringValues> { ["uiAction"] = "dbCreate" });
        var model = new KasaPreviewViewModel
        {
            SelectedDate = new DateOnly(2070, 9, 1),
            KasaType = "Sabah",
            Mappings =
            {
                new KasaManager.Web.Models.KasaPreviewMappingRow
                {
                    TargetKey = "takip_kasa_etkisi_tahsilat", Mode = "Formula", Expression = "1"
                }
            }
        };

        var result = await controller.RunFormulaEngine(model, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var returned = Assert.IsType<KasaPreviewViewModel>(view.Model);
        Assert.Contains(returned.Errors, error => error.Contains("sistem anahtarı", StringComparison.OrdinalIgnoreCase));
        orchestrator.Verify(service => service.CreateDbFormulaSetAsync(
            It.Is<KasaPreviewDto>(dto => dto.PoolEntries.Count == 0),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FormulaDesigner_UpdateTemplate_ValidationErrorShowsNoSuccessMessage()
    {
        var orchestrator = CreateOrchestratorContractMock();
        orchestrator.Setup(service => service.SaveDbFormulaSetAsync(
                It.IsAny<KasaPreviewDto>(), true, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<KasaPreviewDto, bool, string, CancellationToken>((dto, _, _, _) =>
                dto.Errors.Add("Sistem anahtarı formül hedefi olarak kullanılamaz: normal_tahsilat."))
            .Returns(Task.CompletedTask);
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.WebRootPath).Returns(@"C:\FakeWebRoot");
        var controller = new FormulaDesignerController(orchestrator.Object, environment.Object, CreateConfiguration());
        var model = new FormulaDesignerViewModel
        {
            SelectedTemplateId = Guid.NewGuid().ToString(),
            TemplateName = "Seed Sabah",
            ScopeType = "Sabah",
            Rows = { new FormulaDesignerRow { TargetKey = "normal_tahsilat", Expression = "normal_tahsilat + 1", Mode = "Formula" } }
        };

        var result = await controller.UpdateTemplate(model, CancellationToken.None);

        var returned = Assert.IsType<FormulaDesignerViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Contains(returned.Errors, error => error.Contains("sistem anahtarı", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(returned.Infos, info => info.Contains("güncellendi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FormulaDesigner_UpdateTemplate_BuiltInSabah61RowsWithIdentityTargets_Succeeds()
    {
        var orchestrator = CreateOrchestratorContractMock();
        orchestrator.Setup(service => service.SaveDbFormulaSetAsync(
                It.IsAny<KasaPreviewDto>(), true, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<KasaPreviewDto, bool, string, CancellationToken>((dto, _, _, _) =>
            {
                Assert.Equal(61, dto.Mappings.Count);
                Assert.Contains(dto.Mappings, row => row.TargetKey == "normal_tahsilat" && row.Expression == "normal_tahsilat");
                Assert.Contains(dto.Mappings, row => row.TargetKey == "takip_kasa_etkisi_tahsilat" && row.Expression == "takip_kasa_etkisi_tahsilat");
            })
            .Returns(Task.CompletedTask);
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.WebRootPath).Returns(@"C:\FakeWebRoot");
        var controller = new FormulaDesignerController(orchestrator.Object, environment.Object, CreateConfiguration());
        var rows = Enumerable.Range(0, 59)
            .Select(index => new FormulaDesignerRow
            {
                TargetKey = $"seed_output_{index}", Expression = "0", Mode = "Formula"
            })
            .Prepend(new FormulaDesignerRow
            {
                TargetKey = "takip_kasa_etkisi_tahsilat", Expression = "takip_kasa_etkisi_tahsilat", Mode = "Formula"
            })
            .Prepend(new FormulaDesignerRow
            {
                TargetKey = "normal_tahsilat", Expression = "normal_tahsilat", Mode = "Formula"
            })
            .ToList();
        var model = new FormulaDesignerViewModel
        {
            SelectedTemplateId = Guid.NewGuid().ToString(), TemplateName = "Sabah Kasa", ScopeType = "Sabah", Rows = rows
        };

        var result = await controller.UpdateTemplate(model, CancellationToken.None);

        var returned = Assert.IsType<FormulaDesignerViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Empty(returned.Errors);
        Assert.Contains(returned.Infos, info => info.Contains("güncellendi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FormulaDesigner_SaveTemplate_EmptyBuiltDtoUsesHydratingCreateAndShowsTurkishError()
    {
        var orchestrator = CreateOrchestratorContractMock();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.WebRootPath).Returns(@"C:\FakeWebRoot");
        var controller = new FormulaDesignerController(
            orchestrator.Object, environment.Object, CreateConfiguration());
        var model = new FormulaDesignerViewModel
        {
            TemplateName = "Invalid system target",
            ScopeType = "Sabah",
            Rows =
            {
                new FormulaDesignerRow
                {
                    TargetKey = "takip_kasa_etkisi_tahsilat", Mode = "Formula", Expression = "1"
                }
            }
        };

        var result = await controller.SaveTemplate(model, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var returned = Assert.IsType<FormulaDesignerViewModel>(view.Model);
        Assert.Contains(returned.Errors, error => error.Contains("sistem anahtarı", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(returned.Infos, info => info.Contains("oluşturuldu", StringComparison.OrdinalIgnoreCase));
        Assert.Null(returned.SelectedTemplateId);
        orchestrator.Verify(service => service.CreateDbFormulaSetAsync(
            It.Is<KasaPreviewDto>(dto => dto.PoolEntries.Count == 0),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<IKasaOrchestrator> CreateOrchestratorContractMock()
    {
        var mock = new Mock<IKasaOrchestrator>();
        mock.Setup(service => service.CreateDbFormulaSetAsync(
                It.IsAny<KasaPreviewDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<KasaPreviewDto, string, CancellationToken>((dto, _, _) =>
            {
                Assert.Empty(dto.PoolEntries);
                dto.Errors.Add("Sistem anahtarı formül hedefi olarak kullanılamaz: takip_kasa_etkisi_tahsilat.");
            })
            .Returns(Task.CompletedTask);
        mock.Setup(service => service.HydrateDbFormulaSetsAsync(
                It.IsAny<KasaPreviewDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static KasaPreviewController CreatePreviewController(IKasaOrchestrator orchestrator)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.WebRootPath).Returns(@"C:\FakeWebRoot");
        var controller = new KasaPreviewController(
            orchestrator, environment.Object, CreateConfiguration(),
            Mock.Of<IImportOrchestrator>(), Mock.Of<IKasaReportDateRulesService>(),
            Mock.Of<IKasaGlobalDefaultsService>(), Mock.Of<IBankaHesapKontrolService>(),
            Mock.Of<ICurrentUser>(), Mock.Of<IHesapKontrolSourceResolver>(),
            Mock.Of<IReportDataBuilder>(), Mock.Of<IExportService>(),
            Mock.Of<IKasaValidationService>(), Mock.Of<IVergideBirikenLedgerService>(),
            Mock.Of<IDocumentTemplateService>(), Mock.Of<IFinansalIstisnaService>(),
            Mock.Of<IFinansalIstisnaAnomaliService>(), Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<KasaPreviewController>>(), Mock.Of<IKasaReadModelService>(),
            Mock.Of<ICalculatedKasaSnapshotService>(), Mock.Of<IKasaRaporSnapshotService>());
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static IConfiguration CreateConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Upload:SubFolder"] = @"Data\Raporlar" })
        .Build();
}
