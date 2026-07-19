using System.Reflection;
using KasaManager.Application.Abstractions;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KasaManager.Tests.Controllers;

public sealed class SnapshotAuthorizationControllerTests
{
    [Fact]
    public async Task Update_MissingUserId_Returns401WithoutServiceCall()
    {
        var service = new Mock<ICalculatedKasaSnapshotService>();
        var controller = CreateController(service, CurrentUser(userId: null));

        var result = await controller.Update(
            Guid.NewGuid(), "name", "description", "notes", CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(service.Invocations);
    }

    [Fact]
    public async Task Update_ServiceForbidden_Returns403Not500()
    {
        var id = Guid.NewGuid();
        var service = new Mock<ICalculatedKasaSnapshotService>();
        service.Setup(value => value.UpdateAsync(
                id, "name", null, "notes", 29, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SnapshotMutationResult.Forbidden);
        var controller = CreateController(service, CurrentUser(29));

        var result = await controller.Update(
            id, "name", null, "notes", CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Update_PassesOnlyServerSideActorAndRole()
    {
        var id = Guid.NewGuid();
        var service = new Mock<ICalculatedKasaSnapshotService>();
        service.Setup(value => value.UpdateAsync(
                id, "name", "description", "notes", 17, false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SnapshotMutationResult.Success);
        var controller = CreateController(service, CurrentUser(17));

        var result = await controller.Update(
            id, "name", "description", "notes", CancellationToken.None);

        Assert.IsType<JsonResult>(result);
        service.VerifyAll();
    }

    [Fact]
    public async Task Delete_NormalUserIsForbiddenBeforeService()
    {
        var service = new Mock<ICalculatedKasaSnapshotService>();
        var controller = CreateController(service, CurrentUser(17, isAdmin: false));

        var result = await controller.Sil(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(service.Invocations);
    }

    [Fact]
    public async Task AdminDeleteAndRestorePassServerSidePolicyContext()
    {
        var id = Guid.NewGuid();
        var service = new Mock<ICalculatedKasaSnapshotService>();
        service.Setup(value => value.DeleteAsync(
                id, 44, true, "admin-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SnapshotMutationResult.Success);
        service.Setup(value => value.RestoreAsync(
                id, 44, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SnapshotMutationResult.Success);
        var controller = CreateController(service, CurrentUser(44, isAdmin: true));

        Assert.IsType<JsonResult>(await controller.Sil(id, CancellationToken.None));
        Assert.IsType<JsonResult>(await controller.GeriYukle(id, CancellationToken.None));
        service.VerifyAll();
    }

    [Fact]
    public void SnapshotWriteActionsRetainHttpAndAuthorizationContracts()
    {
        AssertActionContract(nameof(KasaRaporlarController.Sil), adminOnly: true);
        AssertActionContract(nameof(KasaRaporlarController.GeriYukle), adminOnly: true);
        AssertActionContract(nameof(KasaRaporlarController.VersionuAktifYap), adminOnly: true);
        AssertActionContract(nameof(KasaRaporlarController.Update), adminOnly: false);

        var previewDelete = typeof(KasaPreviewController)
            .GetMethod(nameof(KasaPreviewController.DeleteSnapshot))!;
        Assert.NotNull(previewDelete.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(previewDelete.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.Equal("Admin",
            previewDelete.GetCustomAttribute<AuthorizeAttribute>()?.Roles);

        foreach (var method in new[]
                 {
                     typeof(KasaRaporlarController).GetMethod(nameof(KasaRaporlarController.Update))!,
                     typeof(KasaRaporlarController).GetMethod(nameof(KasaRaporlarController.Sil))!,
                     typeof(KasaRaporlarController).GetMethod(nameof(KasaRaporlarController.GeriYukle))!,
                     previewDelete
                 })
        {
            Assert.DoesNotContain(method.GetParameters(), parameter =>
                parameter.Name is "actorUserId" or "isAdmin" or "deletedBy");
        }
    }

    private static void AssertActionContract(string actionName, bool adminOnly)
    {
        var method = typeof(KasaRaporlarController).GetMethod(actionName)!;
        Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();
        if (adminOnly)
            Assert.Equal("Admin", authorize?.Roles);
        else
            Assert.Null(authorize);
    }

    private static KasaRaporlarController CreateController(
        Mock<ICalculatedKasaSnapshotService> service,
        ICurrentUser currentUser) => new(
            service.Object,
            currentUser,
            NullLogger<KasaRaporlarController>.Instance);

    private static ICurrentUser CurrentUser(int? userId, bool isAdmin = false)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(value => value.IsAuthenticated).Returns(true);
        currentUser.SetupGet(value => value.UserId).Returns(userId);
        currentUser.SetupGet(value => value.Username)
            .Returns(isAdmin ? "admin-user" : "normal-user");
        currentUser.Setup(value => value.IsInRole("Admin")).Returns(isAdmin);
        return currentUser.Object;
    }
}
