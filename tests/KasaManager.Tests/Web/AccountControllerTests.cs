using KasaManager.Domain.Identity;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace KasaManager.Tests.Web;

/// <summary>
/// AccountController birim testleri.
/// Login GET/POST guard koşulları ve credential doğrulama testleri.
/// </summary>
public sealed class AccountControllerTests : IDisposable
{
    private readonly KasaManagerDbContext _db;

    public AccountControllerTests()
    {
        var options = new DbContextOptionsBuilder<KasaManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new KasaManagerDbContext(options);

        // Admin seed
        _db.KasaUsers.Add(new KasaUser
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
            DisplayName = "Test Admin",
            Role = "Admin",
            IsActive = true
        });

        // Inactive user
        _db.KasaUsers.Add(new KasaUser
        {
            Username = "disabled",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Pass123!"),
            DisplayName = "Disabled User",
            Role = "User",
            IsActive = false
        });

        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private AccountController CreateController()
    {
        var logger = new Mock<ILogger<AccountController>>();
        var controller = new AccountController(_db, logger.Object);

        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

        return controller;
    }

    // ───────────────────────────────────────────
    // Login GET
    // ───────────────────────────────────────────

    [Fact]
    public void Login_GET_ReturnsViewResult()
    {
        var controller = CreateController();
        var result = controller.Login();
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public void Login_GET_WithReturnUrl_SetsViewBag()
    {
        var controller = CreateController();
        var result = controller.Login("/Home/Index") as ViewResult;

        Assert.NotNull(result);
        Assert.Equal("/Home/Index", controller.ViewBag.ReturnUrl);
    }

    // ───────────────────────────────────────────
    // Login POST — Guard conditions
    // ───────────────────────────────────────────

    [Theory]
    [InlineData("", "somepass")]
    [InlineData("   ", "somepass")]
    [InlineData(null, "somepass")]
    public async Task Login_POST_EmptyUsername_ReturnsViewWithError(string? username, string password)
    {
        var controller = CreateController();
        var result = await controller.Login(username!, password) as ViewResult;

        Assert.NotNull(result);
        string error = controller.ViewBag.Error;
        Assert.Contains("gerekli", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("admin", "")]
    [InlineData("admin", "   ")]
    [InlineData("admin", null)]
    public async Task Login_POST_EmptyPassword_ReturnsViewWithError(string username, string? password)
    {
        var controller = CreateController();
        var result = await controller.Login(username, password!) as ViewResult;

        Assert.NotNull(result);
        string error = controller.ViewBag.Error;
        Assert.Contains("gerekli", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_POST_WrongPassword_ReturnsViewWithError()
    {
        var controller = CreateController();
        var result = await controller.Login("admin", "WrongPass!") as ViewResult;

        Assert.NotNull(result);
        string error = controller.ViewBag.Error;
        Assert.Contains("hatalı", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_POST_NonexistentUser_ReturnsViewWithError()
    {
        var controller = CreateController();
        var result = await controller.Login("nosuchuser", "Pass123!") as ViewResult;

        Assert.NotNull(result);
        string error = controller.ViewBag.Error;
        Assert.Contains("hatalı", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_POST_InactiveUser_ReturnsViewWithError()
    {
        var controller = CreateController();
        // disabled user exists but IsActive=false → filtered out by query
        var result = await controller.Login("disabled", "Pass123!") as ViewResult;

        Assert.NotNull(result);
        string error = controller.ViewBag.Error;
        Assert.Contains("hatalı", error, StringComparison.OrdinalIgnoreCase);
    }

    // ───────────────────────────────────────────
    // AccessDenied
    // ───────────────────────────────────────────

    [Fact]
    public void AccessDenied_ReturnsViewResult()
    {
        var controller = CreateController();
        var result = controller.AccessDenied();
        Assert.IsType<ViewResult>(result);
    }
}
