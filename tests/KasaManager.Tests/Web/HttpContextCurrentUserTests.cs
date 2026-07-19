using System.Security.Claims;
using KasaManager.Application.Abstractions;
using KasaManager.Web.DependencyInjection;
using KasaManager.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace KasaManager.Tests.Web;

public sealed class HttpContextCurrentUserTests
{
    [Fact]
    public void AuthenticatedPrincipal_WithValidUserId_ReturnsStableUserId()
    {
        var currentUser = CreateAuthenticated(userId: "42");

        Assert.True(currentUser.IsAuthenticated);
        Assert.Equal(42, currentUser.UserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-7")]
    public void AuthenticatedPrincipal_WithMissingOrInvalidUserId_ReturnsNull(string? userId)
    {
        var currentUser = CreateAuthenticated(userId);

        Assert.True(currentUser.IsAuthenticated);
        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void UnauthenticatedPrincipal_ReturnsFalseAndNullUserId()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("UserId", "42") }));
        var currentUser = Create(principal);

        Assert.False(currentUser.IsAuthenticated);
        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void AuthenticatedPrincipal_ReturnsUsernameFromIdentity()
    {
        var currentUser = CreateAuthenticated("42", username: "alice");

        Assert.Equal("alice", currentUser.Username);
    }

    [Fact]
    public void AdminRoleClaim_IsResolvedByIsInRole()
    {
        var currentUser = CreateAuthenticated("42", role: "Admin");

        Assert.True(currentUser.IsInRole("Admin"));
    }

    [Fact]
    public void UserRoleClaim_DoesNotResolveAsAdmin()
    {
        var currentUser = CreateAuthenticated("42", role: "User");

        Assert.False(currentUser.IsInRole("Admin"));
    }

    [Fact]
    public void MissingHttpContext_ReturnsSafeAnonymousValues()
    {
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor());

        Assert.False(currentUser.IsAuthenticated);
        Assert.Null(currentUser.UserId);
        Assert.Null(currentUser.Username);
        Assert.False(currentUser.IsInRole("Admin"));
    }

    [Fact]
    public void AddCurrentUser_RegistersResolvableScopedService()
    {
        var services = new ServiceCollection();
        services.AddCurrentUser();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<HttpContextCurrentUser>(
            scope.ServiceProvider.GetRequiredService<ICurrentUser>());
    }

    [Fact]
    public void DifferentPrincipals_ReturnTheirOwnStableUserIds()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = ContextFor(AuthenticatedPrincipal(new Claim("UserId", "17")))
        };
        var currentUser = new HttpContextCurrentUser(accessor);

        Assert.Equal(17, currentUser.UserId);

        accessor.HttpContext = ContextFor(AuthenticatedPrincipal(new Claim("UserId", "29")));

        Assert.Equal(29, currentUser.UserId);
    }

    [Fact]
    public void NameIdentifierWithoutCustomUserId_DoesNotResolveUserId()
    {
        var principal = AuthenticatedPrincipal(
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim(ClaimTypes.Name, "alice"));
        var currentUser = Create(principal);

        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void RequireAuthenticatedUserId_WithValidActor_ReturnsUserId()
    {
        var currentUser = CreateAuthenticated("42");

        Assert.Equal(42, currentUser.RequireAuthenticatedUserId());
    }

    [Fact]
    public void RequireAuthenticatedUserId_WhenUnauthenticated_FailsClosed()
    {
        var currentUser = Create(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Throws<UnauthorizedAccessException>(
            () => currentUser.RequireAuthenticatedUserId());
    }

    [Fact]
    public void RequireAuthenticatedUserId_WhenClaimIsMissing_FailsClosed()
    {
        var currentUser = CreateAuthenticated(userId: null);

        Assert.Throws<UnauthorizedAccessException>(
            () => currentUser.RequireAuthenticatedUserId());
    }

    private static HttpContextCurrentUser CreateAuthenticated(
        string? userId,
        string username = "test-user",
        string role = "User")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, role)
        };

        if (userId is not null)
        {
            claims.Add(new Claim("UserId", userId));
        }

        return Create(AuthenticatedPrincipal(claims.ToArray()));
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "TestAuth"));

    private static HttpContextCurrentUser Create(ClaimsPrincipal principal)
    {
        return new HttpContextCurrentUser(new HttpContextAccessor
        {
            HttpContext = ContextFor(principal)
        });
    }

    private static DefaultHttpContext ContextFor(ClaimsPrincipal principal) =>
        new() { User = principal };
}
