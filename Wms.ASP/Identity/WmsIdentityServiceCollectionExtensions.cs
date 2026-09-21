using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Infrastructure.Identity;

namespace Wms.ASP.Identity;

public static class WmsIdentityServiceCollectionExtensions
{
    public static IServiceCollection AddWareCommandIdentity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<IAuthorizationHandler, WmsPermissionAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            foreach (var permission in WmsPermissions.Catalog)
            {
                options.AddPolicy(
                    permission,
                    policy =>
                    {
                        policy.RequireAuthenticatedUser();
                        policy.AddRequirements(new WmsPermissionRequirement(permission));
                    });
            }
        });

        services
            .AddIdentity<WmsUser, IdentityRole>(options =>
            {
                options.Password.RequiredLength = configuration.GetValue("Authentication:Password:RequiredLength", 12);
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = configuration.GetValue(
                    "Authentication:Lockout:MaxFailedAccessAttempts",
                    5);
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(configuration.GetValue(
                    "Authentication:Lockout:Minutes",
                    15));
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = configuration.GetValue(
                    "Authentication:RequireConfirmedEmail",
                    false);
            })
            .AddEntityFrameworkStores<Wms.Infrastructure.Data.WmsDbContext>()
            .AddDefaultTokenProviders();

        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, WmsApiClientAuthenticationHandler>(
                WmsApiClientAuthenticationDefaults.Scheme,
                _ => { });

        services.Configure<SecurityStampValidatorOptions>(options =>
        {
            options.ValidationInterval = TimeSpan.Zero;
        });

        services.AddScoped<
            IUserClaimsPrincipalFactory<WmsUser>,
            WmsClaimsPrincipalFactory>();
        services.AddScoped<WmsIdentityBootstrapper>();
        services.AddScoped<IAccountNotificationSender, SmtpAccountNotificationSender>();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = configuration["Authentication:CookieName"] ?? "WareCommand.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = configuration.GetValue(
                "Authentication:CookieSecure",
                !environment.IsDevelopment())
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(configuration.GetValue(
                "Authentication:CookieHours",
                8));
            options.SlidingExpiration = true;
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.ReturnUrlParameter = "returnUrl";
            options.Events.OnValidatePrincipal = async context =>
            {
                var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<WmsUser>>();
                var clock = context.HttpContext.RequestServices.GetRequiredService<IClock>();
                var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var user = string.IsNullOrWhiteSpace(userId)
                    ? null
                    : await userManager.FindByIdAsync(userId);

                if (user is null || !user.IsActive || (user.LockoutEnd.HasValue && user.LockoutEnd > clock.UtcNow))
                {
                    context.RejectPrincipal();
                }
            };
        });

        return services;
    }
}
