using System.Reflection;
using System.Security.Claims;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.FinancialExceptions;
using KasaManager.Web.Controllers;
using KasaManager.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KasaManager.Tests.Controllers;

public sealed class HesapKontrolActorAuditControllerTests
{
    [Fact]
    public async Task RunAnalysis_MissingUserIdClaim_FailsClosedBeforeServiceOrSource()
    {
        var service = new Mock<IBankaHesapKontrolService>();
        var source = new Mock<IHesapKontrolSourceResolver>();
        var controller = CreateController(service, source, currentUser: CreateCurrentUser());

        var result = await controller.RunAnalysis("2026-07-18", CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(service.Invocations);
        Assert.Empty(source.Invocations);
        Assert.Empty(controller.TempData);
    }

    [Fact]
    public async Task RunAnalysis_InvalidUserIdClaim_FailsClosedWithout500()
    {
        var service = new Mock<IBankaHesapKontrolService>();
        var source = new Mock<IHesapKontrolSourceResolver>();
        var controller = CreateController(
            service, source, currentUser: CreateCurrentUser("not-an-integer"));

        var result = await controller.RunAnalysis("2026-07-18", CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(service.Invocations);
        Assert.Empty(source.Invocations);
    }

    [Fact]
    public async Task Confirm_MissingUserId_FailsClosedBeforeCommand()
    {
        await AssertUnauthorizedAsync(controller =>
            controller.Confirm(Guid.NewGuid(), "note"));
    }

    [Fact]
    public async Task StartTracking_MissingUserId_FailsClosedBeforeCommand()
    {
        await AssertUnauthorizedAsync(controller =>
            controller.StartTracking(Guid.NewGuid(), "note"));
    }

    [Fact]
    public async Task Resolve_MissingUserId_FailsClosedBeforeCommand()
    {
        await AssertUnauthorizedAsync(controller =>
            controller.Resolve(Guid.NewGuid(), "note"));
    }

    [Fact]
    public async Task CancelAndBulkCancel_MissingUserId_FailClosedBeforeCommands()
    {
        var service = new Mock<IBankaHesapKontrolService>();
        var source = new Mock<IHesapKontrolSourceResolver>();
        var controller = CreateController(service, source, currentUser: CreateCurrentUser());

        var cancelResult = await controller.Cancel(Guid.NewGuid(), "reason");
        var bulkResult = await controller.BulkCancel(new List<Guid> { Guid.NewGuid() });

        Assert.IsType<UnauthorizedResult>(cancelResult);
        Assert.IsType<UnauthorizedResult>(bulkResult);
        Assert.Empty(service.Invocations);
    }

    [Fact]
    public async Task CreateFinancialException_MissingUserId_FailsClosedBeforeWriteService()
    {
        var service = new Mock<IBankaHesapKontrolService>();
        var source = new Mock<IHesapKontrolSourceResolver>();
        var financialExceptions = new Mock<IFinansalIstisnaService>();
        var controller = CreateController(
            service, source, CreateCurrentUser(), financialExceptions);

        var result = await controller.CreateFinansalIstisnaFromHK(
            Guid.NewGuid(), 0, 0, 10m, "test", "2026-07-18", CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(financialExceptions.Invocations);
        Assert.Empty(controller.TempData);
    }

    [Fact]
    public void InteractiveActions_RetainAntiForgeryAndDoNotBindActorParameters()
    {
        var actionNames = new[]
        {
            nameof(HesapKontrolController.RunAnalysis),
            nameof(HesapKontrolController.Confirm),
            nameof(HesapKontrolController.Cancel),
            nameof(HesapKontrolController.BulkCancel),
            nameof(HesapKontrolController.BulkDismissStale),
            nameof(HesapKontrolController.StartTracking),
            nameof(HesapKontrolController.Resolve),
            nameof(HesapKontrolController.Revert),
            nameof(HesapKontrolController.ApprovePotentialMatch),
            nameof(HesapKontrolController.RejectPotentialMatch),
            nameof(HesapKontrolController.CreateFinansalIstisnaFromHK)
        };

        foreach (var actionName in actionNames)
        {
            var method = typeof(HesapKontrolController).GetMethod(actionName)!;
            Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
            Assert.DoesNotContain(method.GetParameters(), parameter =>
                parameter.Name is "actorUserId" or "actorUsername" or "userId");
        }
    }

    private static async Task AssertUnauthorizedAsync(
        Func<HesapKontrolController, Task<IActionResult>> action)
    {
        var service = new Mock<IBankaHesapKontrolService>();
        var source = new Mock<IHesapKontrolSourceResolver>();
        var controller = CreateController(service, source, currentUser: CreateCurrentUser());

        var result = await action(controller);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(service.Invocations);
        Assert.Empty(controller.TempData);
    }

    private static HesapKontrolController CreateController(
        Mock<IBankaHesapKontrolService> service,
        Mock<IHesapKontrolSourceResolver> source,
        ICurrentUser currentUser,
        Mock<IFinansalIstisnaService>? financialExceptions = null)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.WebRootPath).Returns(@"C:\FakeWebRoot");
        var controller = new HesapKontrolController(
            service.Object,
            currentUser,
            Mock.Of<IHesapKontrolExportService>(),
            (financialExceptions ?? new Mock<IFinansalIstisnaService>()).Object,
            source.Object,
            NullLogger<HesapKontrolController>.Instance,
            environment.Object);
        controller.TempData = new TempDataDictionary(
            new DefaultHttpContext(), Mock.Of<ITempDataProvider>());
        return controller;
    }

    private static ICurrentUser CreateCurrentUser(string? userIdClaim = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "server-user") };
        if (userIdClaim is not null)
            claims.Add(new Claim("UserId", userIdClaim));

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
        };
        return new HttpContextCurrentUser(
            new HttpContextAccessor { HttpContext = context });
    }
}
