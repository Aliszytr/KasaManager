namespace KasaManager.Application.Abstractions;

/// <summary>
/// Exposes the authenticated actor without leaking ASP.NET types into application services.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    int? UserId { get; }
    string? Username { get; }
    bool IsInRole(string role);
}

