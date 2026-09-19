using Microsoft.AspNetCore.Identity;

namespace Wms.Infrastructure.Identity;

public sealed class WmsUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    public string EmployeeCode { get; set; } = string.Empty;

    public string Locale { get; set; } = "en-US";

    public string TimeZone { get; set; } = "UTC";

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastLoginAtUtc { get; set; }
}
