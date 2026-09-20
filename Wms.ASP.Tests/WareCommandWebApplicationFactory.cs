using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;

namespace Wms.ASP.Tests;

public sealed class WareCommandWebApplicationFactory : WebApplicationFactory<Wms.ASP.Program>
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "warecommand-web-tests-" + Guid.NewGuid().ToString("N"));

    public string DatabasePath => Path.Combine(_root, "web-tests.db");

    public string DataProtectionPath => Path.Combine(_root, "keys");

    public int? AuthenticationPermitLimitOverride { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(DataProtectionPath);

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={DatabasePath}");
        builder.UseSetting("Wms:DatabaseProvider", "Sqlite");
        builder.UseSetting("Wms:SeedProfile", "None");
        builder.ConfigureServices(services =>
        {
            services.AddControllersWithViews()
                .AddApplicationPart(typeof(ErrorProbeController).Assembly);
        });
        if (AuthenticationPermitLimitOverride.HasValue)
        {
            builder.UseSetting(
                "Security:RateLimiting:AuthenticationPermitLimit",
                AuthenticationPermitLimitOverride.Value.ToString(CultureInfo.InvariantCulture));
        }
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={DatabasePath}",
                ["Wms:DatabaseProvider"] = "Sqlite",
                ["Wms:SeedProfile"] = "None",
                ["Authentication:CookieSecure"] = "false",
                ["Authentication:CookieHours"] = "8",
                ["HttpsRedirection:Enabled"] = "false",
                ["DataProtection:KeyDirectory"] = DataProtectionPath,
                ["AllowedHosts"] = "*"
            };

            if (AuthenticationPermitLimitOverride.HasValue)
            {
                settings["Security:RateLimiting:AuthenticationPermitLimit"] =
                    AuthenticationPermitLimitOverride.Value.ToString(CultureInfo.InvariantCulture);
            }

            configuration.AddInMemoryCollection(settings);
        });
    }

    public async Task<WmsUser> CreateUserAsync(
        string? userName = null,
        string? email = null,
        string password = "ValidPassword123!",
        bool assignDefaultRole = true)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<WmsUser>>();
        var resolvedUserName = userName ?? "operator-" + Guid.NewGuid().ToString("N")[..8];
        var user = new WmsUser
        {
            UserName = resolvedUserName,
            Email = email ?? $"{resolvedUserName}@example.test",
            EmailConfirmed = true,
            DisplayName = "Web Operator",
            EmployeeCode = "EMP-" + Guid.NewGuid().ToString("N")[..8],
            Locale = "en-US",
            TimeZone = "UTC",
            IsActive = true,
            LockoutEnabled = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        if (assignDefaultRole)
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var role = await roleManager.FindByNameAsync(WmsRoleNames.WarehouseStaff) ??
                       throw new InvalidOperationException("The default warehouse staff role was not seeded.");
            var roleResult = await userManager.AddToRoleAsync(user, role.Name!);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    string.Join("; ", roleResult.Errors.Select(error => error.Description)));
            }
        }

        return user;
    }

    public async Task RemoveAllRolesAsync(string userId)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<WmsUser>>();
        var user = await userManager.FindByIdAsync(userId) ??
                   throw new InvalidOperationException($"User '{userId}' was not found.");
        var roles = await userManager.GetRolesAsync(user);
        var result = await userManager.RemoveFromRolesAsync(user, roles);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        await userManager.UpdateSecurityStampAsync(user);
    }

    public async Task AssignRoleAsync(string userId, string roleName)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<WmsUser>>();
        var user = await userManager.FindByIdAsync(userId) ??
                   throw new InvalidOperationException($"User '{userId}' was not found.");
        var result = await userManager.AddToRoleAsync(user, roleName);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(error => error.Description)));
        }

        await userManager.UpdateSecurityStampAsync(user);
    }

    public async Task<WarehouseTestData> CreateWarehouseScenarioAsync(string userId)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        var firstWarehouse = new Warehouse("WEB-MAIN-" + Guid.NewGuid().ToString("N")[..6], "Web Main");
        var secondWarehouse = new Warehouse("WEB-OTHER-" + Guid.NewGuid().ToString("N")[..6], "Web Other");
        var item = new Item("WEB-ITEM-" + Guid.NewGuid().ToString("N")[..6], "Web Item", "EA");
        context.Warehouses.AddRange(firstWarehouse, secondWarehouse);
        context.Items.Add(item);
        await context.SaveChangesAsync();

        var firstLocation = new Location("WEB-A-" + Guid.NewGuid().ToString("N")[..6], "Assigned location", firstWarehouse.Id);
        var secondLocation = new Location("WEB-B-" + Guid.NewGuid().ToString("N")[..6], "Unassigned location", secondWarehouse.Id);
        context.Locations.AddRange(firstLocation, secondLocation);
        await context.SaveChangesAsync();
        context.Stock.AddRange(
            new Stock(item.Id, firstLocation.Id, new Quantity(10)),
            new Stock(item.Id, secondLocation.Id, new Quantity(20)));
        context.UserWarehouseAssignments.Add(new WmsUserWarehouseAssignment
        {
            UserId = userId,
            WarehouseId = firstWarehouse.Id,
            IsDefault = true
        });
        await context.SaveChangesAsync();

        return new WarehouseTestData(
            firstLocation.Code,
            secondLocation.Code,
            item.Sku);
    }

    public async Task<WmsUser> GetUserAsync(string userId)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<WmsUser>>();
        return await userManager.FindByIdAsync(userId) ??
               throw new InvalidOperationException($"User '{userId}' was not found.");
    }

    public async Task DisableUserAsync(string userId)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<WmsUser>>();
        var user = await userManager.FindByIdAsync(userId) ??
                   throw new InvalidOperationException($"User '{userId}' was not found.");
        user.IsActive = false;
        var stampResult = await userManager.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
        {
            throw new InvalidOperationException("Could not rotate the disabled user's security stamp.");
        }

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(error => error.Description)));
        }
    }

    public async Task<string> GenerateResetTokenAsync(string userId)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<WmsUser>>();
        var user = await userManager.FindByIdAsync(userId) ??
                   throw new InvalidOperationException($"User '{userId}' was not found.");
        return await userManager.GeneratePasswordResetTokenAsync(user);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            for (var attempt = 0; attempt < 5 && Directory.Exists(_root); attempt++)
            {
                try
                {
                    Directory.Delete(_root, recursive: true);
                }
                catch (IOException)
                {
                    Thread.Sleep(100);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
}

public sealed record WarehouseTestData(
    string AssignedLocationCode,
    string UnassignedLocationCode,
    string ItemSku);
