using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;

namespace Wms.ASP.Security;

public static class WmsRateLimitPolicies
{
    public const string Authentication = "wms-authentication";
    public const string PasswordReset = "wms-password-reset";
    public const string Report = "wms-report";
    public const string Api = "wms-api";
    public const string Scanning = "wms-scanning";
    public const string Import = "wms-import";
}

public sealed class WmsSecurityOptions
{
    public const string SectionName = "Security";

    public int MaxRequestBodyBytes { get; set; } = 1_048_576;

    public int MultipartBodyLengthLimitBytes { get; set; } = 10_485_760;

    public HstsOptions Hsts { get; set; } = new();

    public AntiforgeryOptions Antiforgery { get; set; } = new();

    public WmsRateLimitingOptions RateLimiting { get; set; } = new();

    public void Validate()
    {
        if (MaxRequestBodyBytes is < 16_384 or > 100_000_000)
        {
            throw new InvalidOperationException(
                "Security:MaxRequestBodyBytes must be between 16384 and 100000000.");
        }

        if (MultipartBodyLengthLimitBytes < MaxRequestBodyBytes ||
            MultipartBodyLengthLimitBytes > 500_000_000)
        {
            throw new InvalidOperationException(
                "Security:MultipartBodyLengthLimitBytes must be at least MaxRequestBodyBytes and no more than 500000000.");
        }

        RateLimiting.Validate();
    }
}

public sealed class HstsOptions
{
    public int MaxAgeDays { get; set; } = 180;

    public bool IncludeSubDomains { get; set; } = true;

    public bool Preload { get; set; }
}

public sealed class AntiforgeryOptions
{
    public bool SecureCookie { get; set; } = true;

    public string CookieName { get; set; } = "__Host-WareCommand-Antiforgery";
}

public sealed class WmsRateLimitingOptions
{
    public int GlobalPermitLimit { get; set; } = 600;

    public int AuthenticationPermitLimit { get; set; } = 30;

    public int PasswordResetPermitLimit { get; set; } = 10;

    public int ReportPermitLimit { get; set; } = 60;

    public int ApiPermitLimit { get; set; } = 120;

    public int ScanningPermitLimit { get; set; } = 120;

    public int ImportPermitLimit { get; set; } = 20;

    public int WindowSeconds { get; set; } = 60;

    public int PasswordResetWindowSeconds { get; set; } = 600;

    public void Validate()
    {
        var limits = new Dictionary<string, int>
        {
            [nameof(GlobalPermitLimit)] = GlobalPermitLimit,
            [nameof(AuthenticationPermitLimit)] = AuthenticationPermitLimit,
            [nameof(PasswordResetPermitLimit)] = PasswordResetPermitLimit,
            [nameof(ReportPermitLimit)] = ReportPermitLimit,
            [nameof(ApiPermitLimit)] = ApiPermitLimit,
            [nameof(ScanningPermitLimit)] = ScanningPermitLimit,
            [nameof(ImportPermitLimit)] = ImportPermitLimit
        };

        if (limits.Any(pair => pair.Value is < 1 or > 100_000))
        {
            throw new InvalidOperationException(
                "All Security:RateLimiting permit limits must be between 1 and 100000.");
        }

        if (WindowSeconds is < 1 or > 86_400 || PasswordResetWindowSeconds is < 1 or > 86_400)
        {
            throw new InvalidOperationException(
                "Security:RateLimiting window values must be between 1 and 86400 seconds.");
        }
    }
}

public static class WmsSecurityRegistration
{
    public static void AddWmsSecurity(this WebApplicationBuilder builder)
    {
        var securityOptions = builder.Configuration
            .GetSection(WmsSecurityOptions.SectionName)
            .Get<WmsSecurityOptions>() ?? new WmsSecurityOptions();

        if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
        {
            securityOptions.Antiforgery.SecureCookie = false;
            if (securityOptions.Antiforgery.CookieName.StartsWith("__Host-", StringComparison.Ordinal))
            {
                securityOptions.Antiforgery.CookieName = "WareCommand-Antiforgery";
            }
        }

        securityOptions.Validate();
        builder.Services.AddSingleton(securityOptions);
        var attachmentRequestBodyBytes = Math.Max(
            securityOptions.MaxRequestBodyBytes,
            builder.Configuration.GetValue<long?>(
                "Wms:Attachments:MaximumRequestBodyBytes") ?? 0);

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = attachmentRequestBodyBytes;
        });

        builder.Services.Configure<FormOptions>(options =>
        {
            options.ValueCountLimit = 1_000;
            options.KeyLengthLimit = 256;
            options.ValueLengthLimit = Math.Min(securityOptions.MaxRequestBodyBytes, 65_536);
            options.MultipartBodyLengthLimit = Math.Max(
                securityOptions.MultipartBodyLengthLimitBytes,
                attachmentRequestBodyBytes);
            options.MultipartHeadersLengthLimit = 16_384;
        });

        builder.Services.AddAntiforgery(options =>
        {
            options.Cookie.Name = securityOptions.Antiforgery.CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = securityOptions.Antiforgery.SecureCookie
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
        });

        builder.Services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(securityOptions.Hsts.MaxAgeDays);
            options.IncludeSubDomains = securityOptions.Hsts.IncludeSubDomains;
            options.Preload = securityOptions.Hsts.Preload;
        });

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientKey(httpContext, "global"),
                    _ => CreateFixedWindowOptions(
                        securityOptions.RateLimiting.GlobalPermitLimit,
                        securityOptions.RateLimiting.WindowSeconds)));

            AddPolicy(
                options,
                WmsRateLimitPolicies.Authentication,
                securityOptions.RateLimiting.AuthenticationPermitLimit,
                securityOptions.RateLimiting.WindowSeconds,
                "authentication");
            AddPolicy(
                options,
                WmsRateLimitPolicies.PasswordReset,
                securityOptions.RateLimiting.PasswordResetPermitLimit,
                securityOptions.RateLimiting.PasswordResetWindowSeconds,
                "password-reset");
            AddPolicy(
                options,
                WmsRateLimitPolicies.Report,
                securityOptions.RateLimiting.ReportPermitLimit,
                securityOptions.RateLimiting.WindowSeconds,
                "report");
            AddPolicy(
                options,
                WmsRateLimitPolicies.Api,
                securityOptions.RateLimiting.ApiPermitLimit,
                securityOptions.RateLimiting.WindowSeconds,
                "api");
            AddPolicy(
                options,
                WmsRateLimitPolicies.Scanning,
                securityOptions.RateLimiting.ScanningPermitLimit,
                securityOptions.RateLimiting.WindowSeconds,
                "scanning");
            AddPolicy(
                options,
                WmsRateLimitPolicies.Import,
                securityOptions.RateLimiting.ImportPermitLimit,
                securityOptions.RateLimiting.WindowSeconds,
                "import");

            options.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfterSeconds = securityOptions.RateLimiting.WindowSeconds;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                }

                context.HttpContext.Response.Headers.RetryAfter =
                    retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                await context.HttpContext.Response.WriteAsync(
                    "Too many requests. Please retry later.",
                    cancellationToken);
            };
        });
    }

    private static void AddPolicy(
        RateLimiterOptions options,
        string policyName,
        int permitLimit,
        int windowSeconds,
        string partitionName)
    {
        options.AddPolicy(
            policyName,
            httpContext => RateLimitPartition.GetFixedWindowLimiter(
                GetClientKey(httpContext, partitionName),
                _ => CreateFixedWindowOptions(permitLimit, windowSeconds)));
    }

    private static FixedWindowRateLimiterOptions CreateFixedWindowOptions(
        int permitLimit,
        int windowSeconds) =>
        new()
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true
        };

    private static string GetClientKey(HttpContext httpContext, string policyName)
    {
        var authorization = httpContext.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authorization["Bearer ".Length..].Trim();
            var separator = token.IndexOf('.', StringComparison.Ordinal);
            if (separator > 0 && separator <= 100)
            {
                var clientId = token[..separator];
                return string.Concat(policyName, ":api-client:", clientId);
            }
        }

        var remoteAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return string.Concat(policyName, ":", remoteAddress);
    }
}
