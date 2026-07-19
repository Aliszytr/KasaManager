namespace KasaManager.Application.Abstractions;

public static class CurrentUserExtensions
{
    /// <summary>
    /// Resolves the server-side actor for an interactive write and fails closed when
    /// authentication or the stable UserId claim is unavailable.
    /// </summary>
    public static int RequireAuthenticatedUserId(this ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        if (!currentUser.IsAuthenticated || currentUser.UserId is not int userId || userId <= 0)
        {
            throw new UnauthorizedAccessException(
                "An authenticated user with a valid UserId claim is required.");
        }

        return userId;
    }
}

