using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Wms.Infrastructure.Identity;

public sealed class DesktopAuthenticationService(
    UserManager<WmsUser> userManager,
    DesktopUserSession session,
    IAuthenticationAuditService auditService,
    ILogger<DesktopAuthenticationService> logger) : IDesktopAuthenticationService
{
    public async Task<DesktopAuthenticationResult> AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByNameAsync(userName) ??
                   await userManager.FindByEmailAsync(userName);
        if (user is null)
        {
            await auditService.RecordAsync(
                WmsAuthenticationEventTypes.LoginFailed,
                succeeded: false,
                userName: userName,
                details: "Desktop sign-in failed for an unknown account.",
                cancellationToken: cancellationToken);
            return DesktopAuthenticationResult.Failure("Invalid username or password.");
        }

        if (!user.IsActive)
        {
            await auditService.RecordAsync(
                WmsAuthenticationEventTypes.AccountDisabled,
                succeeded: false,
                userId: user.Id,
                userName: user.UserName,
                details: "Desktop sign-in rejected for a disabled account.",
                cancellationToken: cancellationToken);
            return DesktopAuthenticationResult.Failure("This account is disabled.");
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            await auditService.RecordAsync(
                WmsAuthenticationEventTypes.AccountLockedOut,
                succeeded: false,
                userId: user.Id,
                userName: user.UserName,
                details: "Desktop sign-in rejected while the account was locked.",
                cancellationToken: cancellationToken);
            return DesktopAuthenticationResult.Failure("This account is temporarily locked.");
        }

        var passwordMatches = await userManager.CheckPasswordAsync(user, password);
        if (!passwordMatches)
        {
            await userManager.AccessFailedAsync(user);
            var lockedOut = await userManager.IsLockedOutAsync(user);
            await auditService.RecordAsync(
                lockedOut
                    ? WmsAuthenticationEventTypes.AccountLockedOut
                    : WmsAuthenticationEventTypes.LoginFailed,
                succeeded: false,
                userId: user.Id,
                userName: user.UserName,
                details: lockedOut
                    ? "Desktop sign-in failure reached the lockout threshold."
                    : "Desktop sign-in failed.",
                cancellationToken: cancellationToken);
            return DesktopAuthenticationResult.Failure(
                lockedOut
                    ? "This account is temporarily locked."
                    : "Invalid username or password.");
        }

        await userManager.ResetAccessFailedCountAsync(user);
        user.LastLoginAtUtc = DateTimeOffset.UtcNow;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            logger.LogError(
                "Could not persist the last-login timestamp for desktop user {UserId}: {Errors}",
                user.Id,
                string.Join("; ", updateResult.Errors.Select(error => error.Code)));
            return DesktopAuthenticationResult.Failure("Sign-in could not be completed. Please try again.");
        }

        session.SignIn(user);
        await auditService.RecordAsync(
            WmsAuthenticationEventTypes.LoginSucceeded,
            succeeded: true,
            userId: user.Id,
            userName: user.UserName,
            details: "Desktop sign-in succeeded.",
            cancellationToken: cancellationToken);
        return DesktopAuthenticationResult.Success;
    }
}
