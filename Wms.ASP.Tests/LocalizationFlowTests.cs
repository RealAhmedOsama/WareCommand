using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class LocalizationFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task QueryCultureRendersArabicRtlAndEnglishLtrDocumentMetadata()
    {
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        using var arabic = await client.GetAsync("/Account/Login?culture=ar-SA&ui-culture=ar-SA");
        var arabicBody = await arabic.Content.ReadAsStringAsync();
        arabicBody = WebUtility.HtmlDecode(arabicBody);
        Assert.Equal(HttpStatusCode.OK, arabic.StatusCode);
        Assert.Contains("<html lang=\"ar-SA\" dir=\"rtl\">", arabicBody, StringComparison.Ordinal);
        Assert.Contains("تسجيل الدخول", arabicBody, StringComparison.Ordinal);
        Assert.Contains("fonts.googleapis.com/css2?family=Cairo", arabicBody, StringComparison.Ordinal);

        using var english = await client.GetAsync("/Account/Login?culture=en-US&ui-culture=en-US");
        var englishBody = await english.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, english.StatusCode);
        Assert.Contains("<html lang=\"en-US\" dir=\"ltr\">", englishBody, StringComparison.Ordinal);
        Assert.Contains("Sign in", englishBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnonymousLocaleSelectionPersistsThroughTheCultureCookie()
    {
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        using var loginPage = await client.GetAsync("/Account/Login");
        var token = ExtractAntiForgeryToken(await loginPage.Content.ReadAsStringAsync());
        using var selection = await client.PostAsync(
            "/Account/SetLocale",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Locale"] = "ar-SA",
                ["ReturnUrl"] = "/Account/Login",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, selection.StatusCode);
        Assert.Equal("/Account/Login", selection.Headers.Location?.OriginalString.Split('?', 2)[0]);

        using var persisted = await client.GetAsync("/Account/Login");
        var body = WebUtility.HtmlDecode(await persisted.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, persisted.StatusCode);
        Assert.Contains("<html lang=\"ar-SA\" dir=\"rtl\">", body, StringComparison.Ordinal);
        Assert.Contains("تسجيل الدخول", body, StringComparison.Ordinal);
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
}
