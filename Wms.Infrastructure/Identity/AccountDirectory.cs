using Microsoft.EntityFrameworkCore;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Identity;

public sealed class AccountDirectory(WmsDbContext context) : IAccountDirectory
{
    public async Task<IReadOnlyList<WmsAccountSummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        return await context.Users
            .AsNoTracking()
            .OrderBy(user => user.UserName)
            .Select(user => new WmsAccountSummary(
                user.Id,
                user.UserName ?? string.Empty,
                user.Email ?? string.Empty,
                user.DisplayName,
                user.EmployeeCode,
                user.IsActive,
                user.LockoutEnd,
                user.LastLoginAtUtc))
            .ToListAsync(cancellationToken);
    }
}
