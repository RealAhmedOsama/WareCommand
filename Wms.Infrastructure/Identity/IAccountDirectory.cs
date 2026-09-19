namespace Wms.Infrastructure.Identity;

public interface IAccountDirectory
{
    Task<IReadOnlyList<WmsAccountSummary>> ListAsync(CancellationToken cancellationToken = default);
}

public sealed record WmsAccountSummary(
    string Id,
    string UserName,
    string Email,
    string DisplayName,
    string EmployeeCode,
    bool IsActive,
    DateTimeOffset? LockoutEnd,
    DateTimeOffset? LastLoginAtUtc);
