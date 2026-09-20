using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Wms.ASP.Security;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class AuthenticationFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    private const string CurrentPassword = "ValidPassword123!";

    [Fact]
    public async Task AnonymousUsersAreRedirectedAwayFromWarehouseOperations()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/Items");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        Assert.Contains("ReturnUrl", response.Headers.Location?.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StateChangingLoginRequiresAnAntiforgeryToken()
    {
        using var client = CreateClient();

        using var response = await client.PostAsync(
            "/Account/Login",
            Form(new Dictionary<string, string>
            {
                ["UserName"] = "operator",
                ["Password"] = CurrentPassword
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SecurityHeadersAndAccountCachePolicyAreApplied()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        AssertHeaderContains(response, "Content-Security-Policy", "default-src 'self'");
        AssertHeaderContains(response, "Content-Security-Policy", "frame-ancestors 'none'");
        AssertHeaderContains(response, "Content-Security-Policy", "'nonce-");
        AssertHeaderContains(response, "X-Content-Type-Options", "nosniff");
        AssertHeaderContains(response, "X-Frame-Options", "DENY");
        AssertHeaderContains(response, "Referrer-Policy", "strict-origin-when-cross-origin");
        AssertHeaderContains(response, "Permissions-Policy", "camera=()");
        AssertHeaderContains(response, "Cache-Control", "no-store");
        AssertHeaderContains(response, "Pragma", "no-cache");
    }

    [Fact]
    public async Task UnexpectedApiErrorsReturnSafeProblemDetailsWithCorrelationReference()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ErrorProbe/Unexpected");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Correlation-ID", "error-test-correlation");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("sensitive provider detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(body);
        Assert.Equal("server.unexpected_error", document.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(
            "error-test-correlation",
            document.RootElement.GetProperty("errorReference").GetString());
        Assert.Equal(500, document.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task UnexpectedMvcErrorsRenderFriendlyPageWithoutExceptionDetails()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ErrorProbe/Unexpected");
        request.Headers.Add("X-Correlation-ID", "mvc-error-correlation");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("Error reference", body, StringComparison.Ordinal);
        Assert.Contains("mvc-error-correlation", body, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive provider detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrencyExceptionsReturnRetryableConflictProblemDetails()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ErrorProbe/Concurrency");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("data.concurrency_conflict", document.RootElement.GetProperty("errorCode").GetString());
        Assert.True(document.RootElement.GetProperty("retryable").GetBoolean());
        Assert.DoesNotContain("internal concurrency detail", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorrelationAndOperationHeadersPropagateThroughTheMvcBoundary()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ErrorProbe/Correlation");
        request.Headers.Add("X-Correlation-ID", "workflow-correlation");
        request.Headers.Add("X-Operation-ID", "workflow-operation");
        request.Headers.Add("X-Reference-ID", "shipment:123");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("workflow-correlation", response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal("workflow-operation", response.Headers.GetValues("X-Operation-ID").Single());
        Assert.Equal("shipment:123", response.Headers.GetValues("X-Reference-ID").Single());

        using var document = JsonDocument.Parse(body);
        Assert.Equal("workflow-correlation", document.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal("workflow-operation", document.RootElement.GetProperty("operation").GetString());
        Assert.Equal("http.request", document.RootElement.GetProperty("operationName").GetString());
        Assert.Equal("shipment:123", document.RootElement.GetProperty("reference").GetString());
        Assert.Equal("Web", document.RootElement.GetProperty("sourceClient").GetString());
    }

    [Fact]
    public async Task ExternalLoginReturnUrlFallsBackToDashboard()
    {
        var user = await factory.CreateUserAsync();
        using var client = CreateClient();

        using var login = await PostLoginAsync(
            client,
            user.UserName!,
            CurrentPassword,
            returnUrl: "https://evil.example/account");

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/", GetPath(login.Headers.Location));
    }

    [Fact]
    public async Task AuthenticationRateLimitReturnsRetryAfterWhenExhausted()
    {
        using var limitedFactory = new WareCommandWebApplicationFactory
        {
            AuthenticationPermitLimitOverride = 1
        };
        var user = await limitedFactory.CreateUserAsync();
        Assert.Equal(
            1,
            limitedFactory.Services.GetRequiredService<WmsSecurityOptions>()
                .RateLimiting.AuthenticationPermitLimit);
        using var client = limitedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        using var firstAttempt = await PostLoginAsync(client, user.UserName!, CurrentPassword);
        Assert.Equal(HttpStatusCode.Redirect, firstAttempt.StatusCode);

        using var secondClient = limitedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        using var secondAttempt = await PostLoginAsync(secondClient, user.UserName!, CurrentPassword);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondAttempt.StatusCode);
        Assert.True(secondAttempt.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task LoginFailureSuccessCookieFlagsAndLogoutAreEnforcedThroughMvc()
    {
        var user = await factory.CreateUserAsync();
        using var client = CreateClient();

        using var failedLogin = await PostLoginAsync(client, user.UserName!, "WrongPassword123!");
        Assert.Equal(HttpStatusCode.OK, failedLogin.StatusCode);
        Assert.Contains("Invalid username or password", await failedLogin.Content.ReadAsStringAsync());

        using var successfulLogin = await PostLoginAsync(client, user.UserName!, CurrentPassword, rememberMe: true);
        Assert.Equal(HttpStatusCode.Redirect, successfulLogin.StatusCode);
        Assert.Equal("/", GetPath(successfulLogin.Headers.Location));
        var setCookie = successfulLogin.Headers.TryGetValues("Set-Cookie", out var cookieValues)
            ? string.Join(";", cookieValues)
            : string.Empty;
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);

        using var protectedResponse = await client.GetAsync("/Items");
        Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);

        var token = await GetAntiForgeryTokenAsync(client, "/Items");
        using var logout = await client.PostAsync(
            "/Account/Logout",
            Form(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);

        using var afterLogout = await client.GetAsync("/Items");
        Assert.Equal(HttpStatusCode.Redirect, afterLogout.StatusCode);
    }

    [Fact]
    public async Task RepeatedMvcLoginFailuresLockTheAccount()
    {
        var user = await factory.CreateUserAsync();
        using var client = CreateClient();

        HttpResponseMessage? lastAttempt = null;
        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                lastAttempt?.Dispose();
                lastAttempt = await PostLoginAsync(client, user.UserName!, "WrongPassword123!");
            }

            lastAttempt!.Dispose();
            lastAttempt = await PostLoginAsync(client, user.UserName!, CurrentPassword);
            var body = await lastAttempt!.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, lastAttempt.StatusCode);
            Assert.Contains("temporarily locked", body, StringComparison.OrdinalIgnoreCase);
            var lockedUser = await factory.GetUserAsync(user.Id);
            Assert.True(lockedUser.LockoutEnd > DateTimeOffset.UtcNow);
        }
        finally
        {
            lastAttempt?.Dispose();
        }
    }

    [Fact]
    public async Task DisabledUserCannotSignInAndExistingCookieIsRejected()
    {
        var user = await factory.CreateUserAsync();
        using var client = CreateClient();
        using var login = await PostLoginAsync(client, user.UserName!, CurrentPassword);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        await factory.DisableUserAsync(user.Id);

        using var existingCookieResponse = await client.GetAsync("/Items");
        Assert.Equal(HttpStatusCode.Redirect, existingCookieResponse.StatusCode);

        using var disabledClient = CreateClient();
        using var disabledLogin = await PostLoginAsync(disabledClient, user.UserName!, CurrentPassword);
        var body = await disabledLogin.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, disabledLogin.StatusCode);
        Assert.Contains("Invalid username or password", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PasswordResetRouteAcceptsIdentityTokenAndNewCredentialWorks()
    {
        var user = await factory.CreateUserAsync();
        var token = await factory.GenerateResetTokenAsync(user.Id);
        using var client = CreateClient();

        using var resetPage = await client.GetAsync(
            $"/Account/ResetPassword?userId={Uri.EscapeDataString(user.Id)}&code={Uri.EscapeDataString(token)}");
        var antiforgeryToken = ExtractAntiForgeryToken(await resetPage.Content.ReadAsStringAsync());
        using var reset = await client.PostAsync(
            "/Account/ResetPassword",
            Form(new Dictionary<string, string>
            {
                ["UserId"] = user.Id,
                ["Code"] = token,
                ["Password"] = "ResetPassword123!",
                ["ConfirmPassword"] = "ResetPassword123!",
                ["__RequestVerificationToken"] = antiforgeryToken
            }));

        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        Assert.Equal("/Account/ResetPasswordConfirmation", reset.Headers.Location?.ToString());

        using var oldLogin = await PostLoginAsync(client, user.UserName!, CurrentPassword);
        Assert.Equal(HttpStatusCode.OK, oldLogin.StatusCode);
        using var newLogin = await PostLoginAsync(client, user.UserName!, "ResetPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, newLogin.StatusCode);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    internal static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client,
        string userName,
        string password,
        bool rememberMe = false,
        string? returnUrl = null)
    {
        var token = await GetAntiForgeryTokenAsync(client, "/Account/Login");
        var values = new Dictionary<string, string>
        {
            ["UserName"] = userName,
            ["Password"] = password,
            ["RememberMe"] = rememberMe ? "true" : "false",
            ["__RequestVerificationToken"] = token
        };

        if (returnUrl is not null)
        {
            values["ReturnUrl"] = returnUrl;
        }

        return await client.PostAsync(
            "/Account/Login",
            Form(values));
    }

    private static void AssertHeaderContains(
        HttpResponseMessage response,
        string headerName,
        string expectedValue)
    {
        Assert.True(response.Headers.TryGetValues(headerName, out var values), $"Missing {headerName} header.");
        Assert.Contains(expectedValue, string.Join(";", values!), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> GetAntiForgeryTokenAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ExtractAntiForgeryToken(await response.Content.ReadAsStringAsync());
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.True(match.Success, "Could not find an antiforgery token in the response.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static FormUrlEncodedContent Form(Dictionary<string, string> values) =>
        new(values);

    private static string? GetPath(Uri? location) =>
        location is null
            ? null
            : location.IsAbsoluteUri
                ? location.AbsolutePath
                : location.OriginalString.Split('?', 2)[0];
}
