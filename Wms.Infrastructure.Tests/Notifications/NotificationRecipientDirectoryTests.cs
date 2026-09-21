using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Identity;
using Wms.Application.Notifications;
using Wms.Domain.Entities;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;
using Wms.Infrastructure.Notifications;

namespace Wms.Infrastructure.Tests.Notifications;

public sealed class NotificationRecipientDirectoryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;

    public NotificationRecipientDirectoryTests()
    {
        _connection.Open();
        _context = new WmsDbContext(
            new DbContextOptionsBuilder<WmsDbContext>()
                .UseSqlite(_connection)
                .Options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task Role_and_warehouse_targeting_requires_active_assignment_and_permission()
    {
        var warehouse = new Warehouse("NOTIFY-01", "Notification warehouse");
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();
        _context.Users.AddRange(
            User("assigned", "assigned@example.test"),
            User("unassigned", "unassigned@example.test"));
        _context.Roles.Add(new IdentityRole
        {
            Id = "role-manager",
            Name = WmsRoleNames.WarehouseManager,
            NormalizedName = WmsRoleNames.WarehouseManager.ToUpperInvariant(),
            ConcurrencyStamp = "role-manager-stamp"
        });
        _context.UserRoles.AddRange(
            new IdentityUserRole<string> { UserId = "assigned", RoleId = "role-manager" },
            new IdentityUserRole<string> { UserId = "unassigned", RoleId = "role-manager" });
        _context.RoleClaims.Add(new IdentityRoleClaim<string>
        {
            RoleId = "role-manager",
            ClaimType = WmsAuthorizationClaimTypes.Permission,
            ClaimValue = WmsPermissions.NotificationsRead
        });
        await _context.SaveChangesAsync();
        _context.UserWarehouseAssignments.Add(new WmsUserWarehouseAssignment
        {
            UserId = "assigned",
            WarehouseId = warehouse.Id,
            IsDefault = true,
            AssignedAtUtc = DateTimeOffset.UtcNow
        });
        await _context.SaveChangesAsync();

        var directory = new NotificationRecipientDirectory(_context);
        var recipients = await directory.ResolveAsync(
            new NotificationAudience(
                Roles: [WmsRoleNames.WarehouseManager],
                WarehouseId: warehouse.Id),
            WmsPermissions.NotificationsRead);

        recipients.Select(recipient => recipient.UserId)
            .Should().Equal("assigned");
        recipients.Single().Roles.Should().Contain(WmsRoleNames.WarehouseManager);
    }

    private static WmsUser User(string id, string email) =>
        new()
        {
            Id = id,
            UserName = id,
            NormalizedUserName = id.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = id,
            EmployeeCode = id,
            Locale = "en-US",
            TimeZone = "UTC",
            IsActive = true
        };

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
