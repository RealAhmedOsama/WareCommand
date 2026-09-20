using System.Security.Cryptography;

namespace Wms.ASP.Security;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public const string ContentSecurityPolicyNonceItem = "Wms.ContentSecurityPolicyNonce";

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        httpContext.Items[ContentSecurityPolicyNonceItem] = nonce;

        httpContext.Response.OnStarting(() =>
        {
            var headers = httpContext.Response.Headers;
            headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "base-uri 'self'; " +
                "object-src 'none'; " +
                "frame-ancestors 'none'; " +
                "frame-src 'none'; " +
                "form-action 'self'; " +
                $"script-src 'self' 'nonce-{nonce}'; " +
                "style-src 'self' https://cdn.jsdelivr.net https://fonts.googleapis.com; " +
                "style-src-attr 'none'; " +
                "font-src 'self' https://cdn.jsdelivr.net https://fonts.gstatic.com; " +
                "img-src 'self' data:; " +
                "connect-src 'self'; " +
                "media-src 'self'";
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] =
                "camera=(), geolocation=(), microphone=(), payment=(), usb=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";

            if (httpContext.Request.Path.StartsWithSegments("/Account", StringComparison.OrdinalIgnoreCase))
            {
                headers["Cache-Control"] = "no-store, max-age=0";
                headers["Pragma"] = "no-cache";
            }

            return Task.CompletedTask;
        });

        await next(httpContext);
    }
}
