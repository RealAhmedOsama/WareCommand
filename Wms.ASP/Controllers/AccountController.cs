using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Localization;
using Wms.ASP.Extensions;
using Wms.ASP.Identity;
using Wms.ASP.Models;
using Wms.ASP.Security;
using Wms.Infrastructure.Identity;

namespace Wms.ASP.Controllers;

public sealed class AccountController(
    UserManager<WmsUser> userManager,
    SignInManager<WmsUser> signInManager,
    RoleManager<IdentityRole> roleManager,
    IAccountDirectory accountDirectory,
    IUserAccessDirectory userAccessDirectory,
    ICurrentUser currentUser,
    IClock clock,
    IAuthenticationAuditService auditService,
    IAccountNotificationSender notificationSender,
    ILogger<AccountController> logger) : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToLocal(returnUrl);
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(WmsRateLimitPolicies.Authentication)]
    public async Task<IActionResult> Login(
        [Bind("UserName,Password,RememberMe,ReturnUrl")] LoginViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByNameAsync(model.UserName) ??
                   await userManager.FindByEmailAsync(model.UserName);
        if (user is null)
        {
            await AuditAsync(
                WmsAuthenticationEventTypes.LoginFailed,
                false,
                userName: model.UserName,
                cancellationToken: cancellationToken);
            ModelState.AddModelError(string.Empty, this.Localize("Account.InvalidCredentials"));
            return View(model);
        }

        if (!user.IsActive)
        {
            await AuditAsync(
                WmsAuthenticationEventTypes.AccountDisabled,
                false,
                user,
                details: "Sign-in rejected for a disabled account.",
                cancellationToken: cancellationToken);
            ModelState.AddModelError(string.Empty, this.Localize("Account.InvalidCredentials"));
            return View(model);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            await AuditAsync(
                WmsAuthenticationEventTypes.AccountLockedOut,
                false,
                user,
                details: "Sign-in rejected while the account was locked.",
                cancellationToken: cancellationToken);
            ModelState.AddModelError(string.Empty, this.Localize("Account.Locked"));
            return View(model);
        }

        var signInResult = await signInManager.PasswordSignInAsync(
            user,
            model.Password,
            model.RememberMe,
            lockoutOnFailure: true);

        if (signInResult.Succeeded)
        {
            user.LastLoginAtUtc = clock.UtcNow;
            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                await signInManager.SignOutAsync();
                logger.LogError(
                    "Could not persist the last-login timestamp for user {UserId}: {Errors}",
                    user.Id,
                    string.Join("; ", updateResult.Errors.Select(error => error.Code)));
                await AuditAsync(
                    WmsAuthenticationEventTypes.LoginFailed,
                    false,
                    user,
                    details: "Sign-in was rolled back because account metadata could not be persisted.",
                    cancellationToken: cancellationToken);
                ModelState.AddModelError(string.Empty, this.Localize("Account.SignInFailed"));
                return View(model);
            }

            await AuditAsync(
                WmsAuthenticationEventTypes.LoginSucceeded,
                true,
                user,
                cancellationToken: cancellationToken);
            return RedirectToLocal(model.ReturnUrl);
        }

        if (signInResult.IsLockedOut)
        {
            await AuditAsync(
                WmsAuthenticationEventTypes.AccountLockedOut,
                false,
                user,
                cancellationToken: cancellationToken);
            ModelState.AddModelError(string.Empty, this.Localize("Account.Locked"));
        }
        else
        {
            await AuditAsync(
                WmsAuthenticationEventTypes.LoginFailed,
                false,
                user,
                cancellationToken: cancellationToken);
            ModelState.AddModelError(string.Empty, this.Localize("Account.InvalidCredentials"));
        }

        return View(model);
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(User);
        await AuditAsync(
            WmsAuthenticationEventTypes.Logout,
            true,
            user,
            cancellationToken: cancellationToken);
        await signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(WmsRateLimitPolicies.PasswordReset)]
    public async Task<IActionResult> ForgotPassword(
        [Bind("Email")] ForgotPasswordViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email);
        if (user is not null && user.IsActive && !string.IsNullOrWhiteSpace(user.Email))
        {
            try
            {
                var code = await userManager.GeneratePasswordResetTokenAsync(user);
                var resetUrl = Url.Action(
                    nameof(ResetPassword),
                    "Account",
                    new { userId = user.Id, code },
                    Request.Scheme,
                    Request.Host.ToString());

                if (string.IsNullOrWhiteSpace(resetUrl))
                {
                    throw new InvalidOperationException("Could not build the password reset URL.");
                }

                await notificationSender.SendPasswordResetAsync(user, resetUrl, cancellationToken);
                await AuditAsync(
                    WmsAuthenticationEventTypes.PasswordResetRequested,
                    true,
                    user,
                    cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // The response remains generic so that account existence and delivery
                // configuration are never disclosed to an unauthenticated caller.
                logger.LogError(
                    exception,
                    "Password reset delivery failed for user {UserId}",
                    user.Id);
                await AuditAsync(
                    WmsAuthenticationEventTypes.PasswordResetFailed,
                    false,
                    user,
                    details: "Reset link could not be delivered.",
                    cancellationToken: cancellationToken);
            }
        }

        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ForgotPasswordConfirmation() => View();

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ResetPassword(string? userId, string? code)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        {
            return RedirectToAction(nameof(ResetPasswordConfirmation));
        }

        return View(new ResetPasswordViewModel { UserId = userId, Code = code });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(WmsRateLimitPolicies.PasswordReset)]
    public async Task<IActionResult> ResetPassword(
        [Bind("UserId,Code,Password,ConfirmPassword")] ResetPasswordViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByIdAsync(model.UserId);
        if (user is null || !user.IsActive)
        {
            await AuditAsync(
                WmsAuthenticationEventTypes.PasswordResetFailed,
                false,
                user,
                details: "Reset requested for an unknown or disabled account.",
                cancellationToken: cancellationToken);
            return RedirectToAction(nameof(ResetPasswordConfirmation));
        }

        var resetResult = await userManager.ResetPasswordAsync(user, model.Code, model.Password);
        if (resetResult.Succeeded)
        {
            await AuditAsync(
                WmsAuthenticationEventTypes.PasswordResetSucceeded,
                true,
                user,
                cancellationToken: cancellationToken);
            return RedirectToAction(nameof(ResetPasswordConfirmation));
        }

        await AuditAsync(
            WmsAuthenticationEventTypes.PasswordResetFailed,
            false,
            user,
            details: "The supplied reset token was invalid or expired.",
            cancellationToken: cancellationToken);
        AddIdentityErrors(resetResult);
        return View(model);
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ResetPasswordConfirmation() => View();

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Manage()
    {
        var user = await userManager.GetUserAsync(User);
        return View(new ChangePasswordViewModel
        {
            Locale = WmsLocaleCatalog.Normalize(user?.Locale)
        });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Manage(
        [Bind("CurrentPassword,NewPassword,ConfirmPassword")] ChangePasswordViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            await signInManager.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }

        var result = await userManager.ChangePasswordAsync(
            user,
            model.CurrentPassword,
            model.NewPassword);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return View(model);
        }

        await signInManager.RefreshSignInAsync(user);
        await AuditAsync(
            WmsAuthenticationEventTypes.PasswordChanged,
            true,
            user,
            cancellationToken: cancellationToken);
        TempData["SuccessMessage"] = this.Localize("Account.PasswordChanged");
        return RedirectToAction(nameof(Manage));
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetLocale(
        [Bind("Locale,ReturnUrl")] LanguagePreferenceViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!WmsLocaleCatalog.IsSupported(model.Locale))
        {
            ModelState.AddModelError(nameof(model.Locale), this.Localize("Language.Invalid"));
            return View(nameof(Manage), new ChangePasswordViewModel
            {
                Locale = WmsLocaleCatalog.Normalize(model.Locale)
            });
        }

        var user = await userManager.GetUserAsync(User);
        if (user is not null)
        {
            user.Locale = WmsLocaleCatalog.Normalize(model.Locale);
            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                AddIdentityErrors(updateResult);
                return View(nameof(Manage), new ChangePasswordViewModel { Locale = user.Locale });
            }

            await signInManager.RefreshSignInAsync(user);
        }
        var locale = WmsLocaleCatalog.Normalize(model.Locale);
        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(locale)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps
            });
        if (user is not null)
        {
            await AuditAsync(
                WmsAuthenticationEventTypes.ProfileChanged,
                true,
                user,
                details: "User language preference changed.",
                cancellationToken: cancellationToken);
        }
        TempData["SuccessMessage"] = this.Localize("Language.Saved");
        return RedirectToLocal(model.ReturnUrl);
    }

    [Authorize(Roles = WmsRoles.Administrator)]
    [Authorize(Policy = WmsPermissions.AccessManage)]
    [HttpGet]
    public async Task<IActionResult> Users(CancellationToken cancellationToken = default)
    {
        var users = (await accountDirectory.ListAsync(cancellationToken))
            .Select(user => new AccountUserViewModel
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                DisplayName = user.DisplayName,
                EmployeeCode = user.EmployeeCode,
                IsActive = user.IsActive,
                IsLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd > clock.UtcNow,
                LastLoginAtUtc = user.LastLoginAtUtc
            })
            .ToList();

        return View(users);
    }

    [Authorize(Policy = WmsPermissions.AccessManage)]
    [HttpGet]
    public IActionResult CreateUser() => View(new CreateUserViewModel());

    [Authorize(Policy = WmsPermissions.AccessManage)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(
        [Bind("UserName,Email,DisplayName,EmployeeCode,Locale,TimeZone,Password,ConfirmPassword")]
        CreateUserViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (!WmsLocaleCatalog.IsSupported(model.Locale))
        {
            ModelState.AddModelError(nameof(model.Locale), this.Localize("Language.Invalid"));
            return View(model);
        }

        var user = new WmsUser
        {
            UserName = model.UserName.Trim(),
            Email = model.Email.Trim(),
            EmailConfirmed = true,
            DisplayName = model.DisplayName.Trim(),
            EmployeeCode = model.EmployeeCode.Trim(),
            Locale = model.Locale.Trim(),
            TimeZone = model.TimeZone.Trim(),
            IsActive = true,
            LockoutEnabled = true
        };

        var createResult = await userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            AddIdentityErrors(createResult);
            return View(model);
        }

        await EnsureRoleAsync(WmsRoles.WarehouseStaff);
        var roleResult = await userManager.AddToRoleAsync(user, WmsRoles.WarehouseStaff);
        if (!roleResult.Succeeded)
        {
            AddIdentityErrors(roleResult);
            await userManager.DeleteAsync(user);
            return View(model);
        }

        await AuditAsync(
            WmsAuthenticationEventTypes.AccountCreated,
            true,
            user,
            cancellationToken: cancellationToken);
        TempData["SuccessMessage"] = this.Localize("Account.Created", user.UserName ?? string.Empty);
        return RedirectToAction(nameof(Users));
    }

    [Authorize(Policy = WmsPermissions.AccessManage)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(string id, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id);
        var currentUser = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        if (currentUser?.Id == user.Id && user.IsActive)
        {
            TempData["ErrorMessage"] = this.Localize("Account.CannotDisable");
            return RedirectToAction(nameof(Users));
        }

        user.IsActive = !user.IsActive;
        await userManager.UpdateSecurityStampAsync(user);
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return RedirectToAction(nameof(Users));
        }

        await AuditAsync(
            user.IsActive
                ? WmsAuthenticationEventTypes.AccountEnabledByAdmin
                : WmsAuthenticationEventTypes.AccountDisabledByAdmin,
            true,
            user,
            cancellationToken: cancellationToken);
        TempData["SuccessMessage"] = user.IsActive
            ? this.Localize("Account.Enabled", user.UserName ?? string.Empty)
            : this.Localize("Account.DisabledByAdmin", user.UserName ?? string.Empty);
        return RedirectToAction(nameof(Users));
    }

    [Authorize(Policy = WmsPermissions.AccessManage)]
    [HttpGet]
    public async Task<IActionResult> Access(string id, CancellationToken cancellationToken = default)
    {
        var profile = await userAccessDirectory.GetProfileAsync(id, cancellationToken);
        return profile is null
            ? NotFound()
            : View(ToManageAccessViewModel(profile));
    }

    [Authorize(Policy = WmsPermissions.AccessManage)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Access(
        [Bind("UserId,RoleNames,PermissionNames,WarehouseIds,DefaultWarehouseId")]
        ManageAccessViewModel model,
        CancellationToken cancellationToken = default)
    {
        var profile = await userAccessDirectory.GetProfileAsync(model.UserId, cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            ApplyAccessOptions(model, profile);
            return View(model);
        }

        var result = await userAccessDirectory.UpdateAsync(
            currentUser.RequireUserId(),
            model.UserId,
            model.RoleNames,
            model.PermissionNames,
            model.WarehouseIds,
            model.DefaultWarehouseId,
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            ApplyAccessOptions(model, profile);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Account.AccessUpdated", profile.UserName);
        return RedirectToAction(nameof(Users));
    }

    private async Task EnsureRoleAsync(string roleName)
    {
        if (await roleManager.RoleExistsAsync(roleName))
        {
            return;
        }

        var result = await roleManager.CreateAsync(new IdentityRole(roleName));
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create the '{roleName}' role: {string.Join("; ", result.Errors.Select(error => error.Description))}");
        }
    }

    private static ManageAccessViewModel ToManageAccessViewModel(WmsUserAccessProfile profile)
    {
        var model = new ManageAccessViewModel
        {
            UserId = profile.UserId,
            UserName = profile.UserName,
            RoleNames = profile.Roles
                .Where(role => role.IsSelected)
                .Select(role => role.Name)
                .ToList(),
            PermissionNames = profile.Permissions
                .Where(permission => permission.IsDirectGrant)
                .Select(permission => permission.Name)
                .ToList(),
            WarehouseIds = profile.Warehouses
                .Where(warehouse => warehouse.IsSelected)
                .Select(warehouse => warehouse.Id)
                .ToList(),
            DefaultWarehouseId = profile.Warehouses
                .Where(warehouse => warehouse.IsDefault)
                .Select(warehouse => (int?)warehouse.Id)
                .FirstOrDefault()
        };

        ApplyAccessOptions(model, profile);
        return model;
    }

    private static void ApplyAccessOptions(
        ManageAccessViewModel model,
        WmsUserAccessProfile profile)
    {
        model.UserName = profile.UserName;
        model.AvailableRoles = profile.Roles
            .Select(role => new AccessRoleOptionViewModel(
                role.Name,
                model.RoleNames.Contains(role.Name, StringComparer.Ordinal)))
            .ToList();
        model.AvailablePermissions = profile.Permissions
            .Select(permission => new AccessPermissionOptionViewModel(
                permission.Name,
                permission.Description,
                model.PermissionNames.Contains(permission.Name, StringComparer.Ordinal),
                permission.IsGrantedByRole))
            .ToList();
        model.AvailableWarehouses = profile.Warehouses
            .Select(warehouse => new AccessWarehouseOptionViewModel(
                warehouse.Id,
                warehouse.Code,
                warehouse.Name,
                model.WarehouseIds.Contains(warehouse.Id),
                model.DefaultWarehouseId == warehouse.Id))
            .ToList();
    }

    private async Task AuditAsync(
        string eventType,
        bool succeeded,
        WmsUser? user = null,
        string? userName = null,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        await auditService.RecordAsync(
            eventType,
            succeeded,
            user?.Id,
            user?.UserName ?? userName,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            details,
            cancellationToken == default ? HttpContext.RequestAborted : cancellationToken);
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
    }

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction("Index", "Dashboard");
    }
}
