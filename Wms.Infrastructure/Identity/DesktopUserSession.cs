using Wms.Application.Identity;

namespace Wms.Infrastructure.Identity;

public sealed class DesktopUserSession : ICurrentUser
{
    public bool IsAuthenticated => UserId is not null;

    public string? UserId { get; private set; }

    public string? UserName { get; private set; }

    public string? DisplayName { get; private set; }

    public void SignIn(WmsUser user)
    {
        UserId = user.Id;
        UserName = user.UserName;
        DisplayName = user.DisplayName;
    }

    public void SignOut()
    {
        UserId = null;
        UserName = null;
        DisplayName = null;
    }
}
