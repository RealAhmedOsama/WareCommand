using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wms.Infrastructure.Identity;

namespace Wms.ASP.Tests;

public sealed class WareCommandWebApplicationFactory : WebApplicationFactory<Wms.ASP.Program>
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "warecommand-web-tests-" + Guid.NewGuid().ToString("N"));

    public string DatabasePath => Path.Combine(_root, "web-tests.db");

    public string DataProtectionPath => Path.Combine(_root, "keys");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(DataProtectionPath);

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={DatabasePath}");
        builder.UseSetting("Wms:DatabaseProvider", "Sqlite");
        builder.UseSetting("Wms:SeedProfile", "None");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={DatabasePath}",
                ["Wms:DatabaseProvider"] = "Sqlite",
                ["Wms:SeedProfile"] = "None",
                ["Authentication:CookieSecure"] = "false",
                ["Authentication:CookieHours"] = "8",
                ["HttpsRedirection:Enabled"] = "false",
                ["DataProtection:KeyDirectory"] = DataProtectionPath,
                ["AllowedHosts"] = "*"
            });
        });
    }

    public async Task<WmsUser> CreateUserAsync(
        string? userName = null,
        string? email = null,
        string password = "ValidPassword123!")
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

        return user;
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
