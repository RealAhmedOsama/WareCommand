using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
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

    private static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client,
        string userName,
        string password,
        bool rememberMe = false)
    {
        var token = await GetAntiForgeryTokenAsync(client, "/Account/Login");
        return await client.PostAsync(
            "/Account/Login",
            Form(new Dictionary<string, string>
            {
                ["UserName"] = userName,
                ["Password"] = password,
                ["RememberMe"] = rememberMe ? "true" : "false",
                ["__RequestVerificationToken"] = token
            }));
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
