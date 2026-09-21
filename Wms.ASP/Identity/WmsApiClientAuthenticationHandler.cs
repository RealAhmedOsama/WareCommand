using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Wms.Application.ApiClients;

namespace Wms.ASP.Identity;

public static class WmsApiClientAuthenticationDefaults
{
    public const string Scheme = "WareCommand.ApiClient";
}

public sealed class WmsApiClientAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiClientCredentialService credentialService,
    IApiClientContextAccessor apiClientContext) : AuthenticationHandler<AuthenticationSchemeOptions>(
        options,
        logger,
        encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        apiClientContext.Current = null;
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authorization["Bearer ".Length..].Trim();
        var separator = token.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0 || separator == token.Length - 1)
        {
            return AuthenticateResult.Fail("The API client credential format is invalid.");
        }

        var clientId = token[..separator];
        var secret = token[(separator + 1)..];
        var result = await credentialService.VerifyAsync(
            clientId,
            secret,
            Context.Connection.RemoteIpAddress?.ToString(),
            Context.RequestAborted);
        if (result.IsFailure)
        {
            Logger.LogWarning(
                "API client authentication failed for client {ApiClientId}; error {ErrorCode}",
                clientId,
                result.ErrorCode);
            return AuthenticateResult.Fail("The API client credential is invalid.");
        }

        apiClientContext.Current = result.Value.Context;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "api-client:" + result.Value.Client.ClientId),
            new(ClaimTypes.Name, result.Value.Client.Name),
            new(ApiClientClaimTypes.ClientId, result.Value.Client.ClientId)
        };
        claims.AddRange(result.Value.Context.Scopes.Select(scope =>
            new Claim(ApiClientClaimTypes.Scope, scope)));
        claims.AddRange(result.Value.Context.WarehouseIds.Select(warehouseId =>
            new Claim(ApiClientClaimTypes.WarehouseId, warehouseId.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        if (result.Value.Context.HasGlobalWarehouseAccess)
        {
            claims.Add(new Claim(ApiClientClaimTypes.GlobalWarehouseAccess, bool.TrueString));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
        return AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
