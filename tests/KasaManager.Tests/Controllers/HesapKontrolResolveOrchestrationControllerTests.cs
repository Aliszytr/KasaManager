using KasaManager.Application.Abstractions;
using KasaManager.Domain.FinancialExceptions;
using KasaManager.Web.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KasaManager.Tests.Controllers;

/// <summary>
/// KasaManager Revision 3 Manual Resolve Write-BusinessDate Surgical Closure — targeted tests for
/// HesapKontrolController.Resolve's server-side orchestration: it must classify the target via
/// GetResolveTargetKindAsync, route Stopaj to the date-free command, route Financial through the
/// WRITE BusinessDate resolver (never through the historical "tarih" query/redirect parameter or
/// the system clock), and perform no mutation when the WRITE resolver fails closed.
/// </summary>
public sealed class HesapKontrolResolveOrchestrationControllerTests
{
    private static readonly Guid RecordId = Guid.NewGuid();

    [Fact]
    public async Task Resolve_StopajTarget_CallsStopajCommand_NeverConsultsWriteBusinessDateResolver()
    {
        var service = new Mock<IBankaHesapKontrolService>();
        service.Setup(x => x.GetResolveTargetKindAsync(RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HesapKontrolResolveTargetKind.Stopaj);
        service.Setup(x => x.ResolveTrackedStopajAsync(
                RecordId, 17, "test-user", "note", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var writeResolver = new Mock<IManualResolveWriteBusinessDateResolver>();
        var controller = CreateController(service, writeResolver);

        var result = await controller.Resolve(RecordId, "note");

        Assert.IsType<RedirectToActionResult>(result);
        service.Verify(x => x.ResolveTrackedStopajAsync(
            RecordId, 17, "test-user", "note", It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.ResolveTrackedFinancialAsync(
            It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        writeResolver.Verify(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_FinancialTarget_WriteResolverSucceeds_UsesResolvedDate_NotTarihOrClock()
    {
        var resolvedBusinessDate = new DateOnly(2026, 8, 21);
        var service = new Mock<IBankaHesapKontrolService>();
        service.Setup(x => x.GetResolveTargetKindAsync(RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HesapKontrolResolveTargetKind.Financial);
        service.Setup(x => x.ResolveTrackedFinancialAsync(
                RecordId, resolvedBusinessDate, 17, "test-user", "note", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var writeResolver = new Mock<IManualResolveWriteBusinessDateResolver>();
        writeResolver.Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ManualResolveWriteBusinessDateResult.Ok(resolvedBusinessDate, "test"));

        var controller = CreateController(service, writeResolver);

        // A historical "tarih" navigation context is supplied — it must have zero influence on the
        // financial reversal date, which must come only from the WRITE resolver's result.
        var result = await controller.Resolve(RecordId, "note", tarih: "2020-01-01");

        Assert.IsType<RedirectToActionResult>(result);
        service.Verify(x => x.ResolveTrackedFinancialAsync(
            RecordId, resolvedBusinessDate, 17, "test-user", "note", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resolve_FinancialTarget_WriteResolverFailsClosed_PerformsNoMutation()
    {
        var service = new Mock<IBankaHesapKontrolService>();
        service.Setup(x => x.GetResolveTargetKindAsync(RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HesapKontrolResolveTargetKind.Financial);

        var writeResolver = new Mock<IManualResolveWriteBusinessDateResolver>();
        writeResolver.Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ManualResolveWriteBusinessDateResult.FailClosed(
                ManualResolveWriteBusinessDateFailureReason.NoAnalyzableExcelDate, "no date"));

        var controller = CreateController(service, writeResolver);

        var result = await controller.Resolve(RecordId, "note");

        Assert.IsType<RedirectToActionResult>(result);
        service.Verify(x => x.ResolveTrackedFinancialAsync(
            It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        service.Verify(x => x.ResolveTrackedStopajAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains("çözülemedi", controller.TempData["Error"]!.ToString());
    }

    [Fact]
    public async Task Resolve_NotFoundTarget_NeitherCommandNorWriteResolverCalled()
    {
        var service = new Mock<IBankaHesapKontrolService>();
        service.Setup(x => x.GetResolveTargetKindAsync(RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HesapKontrolResolveTargetKind.NotFound);
        var writeResolver = new Mock<IManualResolveWriteBusinessDateResolver>();
        var controller = CreateController(service, writeResolver);

        var result = await controller.Resolve(RecordId, "note");

        Assert.IsType<RedirectToActionResult>(result);
        service.Verify(x => x.ResolveTrackedStopajAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        service.Verify(x => x.ResolveTrackedFinancialAsync(
            It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        writeResolver.Verify(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static HesapKontrolController CreateController(
        Mock<IBankaHesapKontrolService> service,
        Mock<IManualResolveWriteBusinessDateResolver> writeResolver)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.WebRootPath).Returns(@"C:\FakeWebRoot");

        var controller = new HesapKontrolController(
            service.Object,
            Mock.Of<ICurrentUser>(u => u.IsAuthenticated && u.UserId == 17 && u.Username == "test-user"),
            Mock.Of<IHesapKontrolExportService>(),
            Mock.Of<IFinansalIstisnaService>(),
            Mock.Of<IHesapKontrolSourceResolver>(),
            NullLogger<HesapKontrolController>.Instance,
            env.Object,
            writeResolver.Object);

        controller.TempData = new TempDataDictionary(
            new DefaultHttpContext(), Mock.Of<ITempDataProvider>());
        return controller;
    }
}
