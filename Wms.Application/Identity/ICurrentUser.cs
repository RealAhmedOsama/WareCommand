namespace Wms.Application.Identity;

/// <summary>
/// The authenticated principal responsible for the current business operation.
/// Presentation adapters provide the implementation for their host.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    string? UserId { get; }

    string? UserName { get; }

    string? DisplayName { get; }
}

public static class CurrentUserExtensions
{
    public static string RequireUserId(this ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new InvalidOperationException("An authenticated user is required for this operation.");
        }

        return currentUser.UserId;
    }
}
