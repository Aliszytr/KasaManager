using System.Globalization;
using KasaManager.Application.Abstractions;

namespace KasaManager.Web.Services;

public sealed class HttpContextCurrentUser : ICurrentUser
{
    private const string UserIdClaimType = "UserId";
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private System.Security.Claims.ClaimsPrincipal? Principal =>
        _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public int? UserId
    {
        get
        {
            if (!IsAuthenticated)
            {
                return null;
            }

            var value = Principal?.FindFirst(UserIdClaimType)?.Value;
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var userId)
                && userId > 0
                    ? userId
                    : null;
        }
    }

    public string? Username => IsAuthenticated ? Principal?.Identity?.Name : null;

    public bool IsInRole(string role) =>
        IsAuthenticated
        && !string.IsNullOrWhiteSpace(role)
        && Principal?.IsInRole(role) == true;
}

