using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.Identity;
using Wms.ASP.Identity;
using Wms.ASP.Models;
using Wms.Infrastructure.Identity;

namespace Wms.ASP.Controllers;

public sealed class AccountController(
    UserManager<WmsUser> userManager,
    SignInManager<WmsUser> signInManager,
    RoleManager<IdentityRole> roleManager,
    IAccountDirectory accountDirectory,
    IUserAccessDirectory userAccessDirectory,
    ICurrentUser currentUser,
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
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByNameAsync(model.UserName) ??
                   await userManager.FindByEmailAsync(model.UserName);
        if (user is null)
        {
            await AuditAsync(WmsAuthenticationEventTypes.LoginFailed, false, userName: model.UserName);
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return View(model);
        }

        if (!user.IsActive)
        {
            await AuditAsync(
                WmsAuthenticationEventTypes.AccountDisabled,
                false,
                user,
                details: "Sign-in rejected for a disabled account.");
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return View(model);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            await AuditAsync(
                WmsAuthenticationEventTypes.AccountLockedOut,
                false,
                user,
                details: "Sign-in rejected while the account was locked.");
            ModelState.AddModelError(string.Empty, "This account is temporarily locked. Try again later.");
            return View(model);
        }

        var signInResult = await signInManager.PasswordSignInAsync(
            user,
            model.Password,
            model.RememberMe,
            lockoutOnFailure: true);

        if (signInResult.Succeeded)
        {
            user.LastLoginAtUtc = DateTimeOffset.UtcNow;
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
                    details: "Sign-in was rolled back because account metadata could not be persisted.");
                ModelState.AddModelError(string.Empty, "Sign-in could not be completed. Please try again.");
                return View(model);
            }

            await AuditAsync(WmsAuthenticationEventTypes.LoginSucceeded, true, user);
            return RedirectToLocal(model.ReturnUrl);
        }

        if (signInResult.IsLockedOut)
        {
            await AuditAsync(WmsAuthenticationEventTypes.AccountLockedOut, false, user);
            ModelState.AddModelError(string.Empty, "This account is temporarily locked. Try again later.");
        }
        else
        {
            await AuditAsync(WmsAuthenticationEventTypes.LoginFailed, false, user);
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
        }

        return View(model);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var user = await userManager.GetUserAsync(User);
        await AuditAsync(WmsAuthenticationEventTypes.Logout, true, user);
        await signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
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

                await notificationSender.SendPasswordResetAsync(user, resetUrl);
                await AuditAsync(
                    WmsAuthenticationEventTypes.PasswordResetRequested,
                    true,
                    user);
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
                    details: "Reset link could not be delivered.");
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
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
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
                details: "Reset requested for an unknown or disabled account.");
            return RedirectToAction(nameof(ResetPasswordConfirmation));
        }

        var resetResult = await userManager.ResetPasswordAsync(user, model.Code, model.Password);
        if (resetResult.Succeeded)
        {
            await AuditAsync(WmsAuthenticationEventTypes.PasswordResetSucceeded, true, user);
            return RedirectToAction(nameof(ResetPasswordConfirmation));
        }

        await AuditAsync(
            WmsAuthenticationEventTypes.PasswordResetFailed,
            false,
            user,
            details: "The supplied reset token was invalid or expired.");
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
    public IActionResult Manage() => View(new ChangePasswordViewModel());

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Manage(ChangePasswordViewModel model)
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
        await AuditAsync(WmsAuthenticationEventTypes.PasswordChanged, true, user);
        TempData["SuccessMessage"] = "Your password was changed successfully.";
        return RedirectToAction(nameof(Manage));
    }

    [Authorize(Roles = WmsRoles.Administrator)]
    [Authorize(Policy = WmsPermissions.AccessManage)]
    [HttpGet]
    public async Task<IActionResult> Users()
    {
        var users = (await accountDirectory.ListAsync())
            .Select(user => new AccountUserViewModel
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                DisplayName = user.DisplayName,
                EmployeeCode = user.EmployeeCode,
                IsActive = user.IsActive,
                IsLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow,
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
    public async Task<IActionResult> CreateUser(CreateUserViewModel model)
    {
        if (!ModelState.IsValid)
        {
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

        await AuditAsync(WmsAuthenticationEventTypes.AccountCreated, true, user);
        TempData["SuccessMessage"] = $"Account '{user.UserName}' was created.";
        return RedirectToAction(nameof(Users));
    }

    [Authorize(Policy = WmsPermissions.AccessManage)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(string id)
    {
        var user = await userManager.FindByIdAsync(id);
        var currentUser = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        if (currentUser?.Id == user.Id && user.IsActive)
        {
            TempData["ErrorMessage"] = "You cannot disable the account currently in use.";
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
            user);
        TempData["SuccessMessage"] = user.IsActive
            ? $"Account '{user.UserName}' was enabled."
            : $"Account '{user.UserName}' was disabled.";
        return RedirectToAction(nameof(Users));
    }

    [Authorize(Policy = WmsPermissions.AccessManage)]
    [HttpGet]
    public async Task<IActionResult> Access(string id)
    {
        var profile = await userAccessDirectory.GetProfileAsync(id);
        return profile is null
            ? NotFound()
            : View(ToManageAccessViewModel(profile));
    }

    [Authorize(Policy = WmsPermissions.AccessManage)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Access(ManageAccessViewModel model)
    {
        var profile = await userAccessDirectory.GetProfileAsync(model.UserId);
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
            model.DefaultWarehouseId);
        if (result.IsFailure)
        {
            ModelState.AddModelError(string.Empty, result.Error);
            ApplyAccessOptions(model, profile);
            return View(model);
        }

        TempData["SuccessMessage"] = $"Access assignments for '{profile.UserName}' were updated.";
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
        string? details = null)
    {
        await auditService.RecordAsync(
            eventType,
            succeeded,
            user?.Id,
            user?.UserName ?? userName,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            details);
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
