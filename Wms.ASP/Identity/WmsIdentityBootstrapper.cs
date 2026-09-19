using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wms.Infrastructure.Identity;

namespace Wms.ASP.Identity;

public static class WmsRoles
{
    public const string Administrator = "Administrator";
    public const string WarehouseStaff = "WarehouseStaff";
}

public sealed class WmsIdentityBootstrapper(
    UserManager<WmsUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IConfiguration configuration,
    IAuthenticationAuditService auditService,
    ILogger<WmsIdentityBootstrapper> logger)
{
    public async Task EnsureBootstrapAdministratorAsync(CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue("Authentication:Bootstrap:Enabled", false))
        {
            return;
        }

        if (await userManager.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var userName = configuration["Authentication:Bootstrap:UserName"];
        var email = configuration["Authentication:Bootstrap:Email"];
        var password = configuration["Authentication:Bootstrap:Password"];
        var passwordFile = configuration["Authentication:Bootstrap:PasswordFile"];
        if (string.IsNullOrWhiteSpace(password) && !string.IsNullOrWhiteSpace(passwordFile))
        {
            if (!File.Exists(passwordFile))
            {
                throw new InvalidOperationException(
                    $"Bootstrap authentication is enabled but the configured password file '{passwordFile}' does not exist.");
            }

            password = File.ReadAllText(passwordFile).TrimEnd('\r', '\n');
        }

        password ??= Environment.GetEnvironmentVariable("WARECOMMAND_ADMIN_BOOTSTRAP_PASSWORD");

        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException(
                "Bootstrap authentication is enabled but Authentication:Bootstrap:UserName and Email are not configured.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Bootstrap authentication is enabled but no bootstrap password was supplied. Provide WARECOMMAND_ADMIN_BOOTSTRAP_PASSWORD through a secret store or disable bootstrap.");
        }

        var administratorRole = await EnsureRoleAsync(WmsRoles.Administrator);
        await EnsureRoleAsync(WmsRoles.WarehouseStaff);

        var user = new WmsUser
        {
            UserName = userName.Trim(),
            Email = email.Trim(),
            EmailConfirmed = true,
            DisplayName = configuration["Authentication:Bootstrap:DisplayName"]?.Trim() ?? userName.Trim(),
            EmployeeCode = configuration["Authentication:Bootstrap:EmployeeCode"]?.Trim() ?? "BOOTSTRAP-ADMIN",
            Locale = configuration["Authentication:Bootstrap:Locale"]?.Trim() ?? "en-US",
            TimeZone = configuration["Authentication:Bootstrap:TimeZone"]?.Trim() ?? "UTC",
            IsActive = true,
            LockoutEnabled = true
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create the configured bootstrap administrator: {string.Join("; ", createResult.Errors.Select(error => error.Description))}");
        }

        var roleResult = await userManager.AddToRoleAsync(user, administratorRole.Name!);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not assign the bootstrap administrator role: {string.Join("; ", roleResult.Errors.Select(error => error.Description))}");
        }

        await auditService.RecordAsync(
            WmsAuthenticationEventTypes.BootstrapAdminCreated,
            succeeded: true,
            userId: user.Id,
            userName: user.UserName,
            details: "Administrator provisioned through explicitly enabled bootstrap configuration.",
            cancellationToken: cancellationToken);

        logger.LogWarning(
            "Bootstrap administrator {UserName} was created because Authentication:Bootstrap:Enabled is enabled. Disable bootstrap after first provisioning.",
            user.UserName);
    }

    private async Task<IdentityRole> EnsureRoleAsync(string roleName)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role is not null)
        {
            return role;
        }

        var newRole = new IdentityRole(roleName);
        var result = await roleManager.CreateAsync(newRole);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create the '{roleName}' role: {string.Join("; ", result.Errors.Select(error => error.Description))}");
        }

        return newRole;
    }
}
