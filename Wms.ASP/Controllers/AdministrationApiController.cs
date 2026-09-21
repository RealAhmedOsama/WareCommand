using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.ApiClients;
using Wms.Application.Administration;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/administration")]
[Authorize(Policy = WmsPermissions.AccessManage)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class AdministrationApiController(
    IAdministrationService administrationService,
    IApiClientCredentialService apiClientCredentialService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(CancellationToken cancellationToken = default) =>
        ToActionResult(await administrationService.GetCatalogAsync(cancellationToken));

    [HttpGet("readiness")]
    public async Task<IActionResult> Readiness(
        [FromQuery, Range(1, int.MaxValue)] int? warehouseId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await administrationService.GetReadinessAsync(warehouseId, cancellationToken));

    [HttpGet("history")]
    public async Task<IActionResult> History(
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery, StringLength(450)] string? userId,
        [FromQuery, Range(1, int.MaxValue)] int? warehouseId,
        [FromQuery, StringLength(100)] string? action,
        [FromQuery, StringLength(100)] string? entityType,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 200)] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await administrationService.GetHistoryAsync(
            new AdministrationHistoryQuery(
                fromUtc,
                toUtc,
                userId,
                warehouseId,
                action,
                entityType,
                page,
                pageSize),
            cancellationToken));

    [HttpGet("clients")]
    public async Task<IActionResult> Clients(CancellationToken cancellationToken = default) =>
        ToActionResult(await apiClientCredentialService.ListAsync(
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("clients")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateClient(
        [FromBody] ApiClientCreateRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await apiClientCredentialService.CreateAsync(
            request,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("clients/{clientId}/rotate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RotateClient(
        string clientId,
        [FromBody] ApiClientRotateRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await apiClientCredentialService.RotateAsync(
            clientId,
            request,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("clients/{clientId}/revoke")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeClient(
        string clientId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await apiClientCredentialService.RevokeAsync(
            clientId,
            currentUser.RequireUserId(),
            cancellationToken));

    private IActionResult ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        var error = result.FirstError!;
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict or ErrorType.Concurrency or ErrorType.BusinessRule => StatusCodes.Status409Conflict,
            ErrorType.Dependency => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        return Problem(
            statusCode: statusCode,
            title: error.Type.ToString(),
            detail: error.Message,
            type: $"https://warecommand.local/problems/{error.Code}",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = error.Code,
                ["retryable"] = error.IsRetryable
            });
    }
}
