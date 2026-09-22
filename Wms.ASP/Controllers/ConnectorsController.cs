using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Connectors;
using Wms.Application.Identity;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/connectors")]
[Authorize(Policy = WmsPermissions.SettingsManage)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class ConnectorsController(IConnectorService connectorService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken = default) =>
        ToActionResult(await connectorService.ListAsync(cancellationToken));

    [HttpPost("mappings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveMapping(
        [FromBody] ConnectorMappingProfileRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await connectorService.SaveMappingProfileAsync(request, cancellationToken));

    [HttpPost("instances")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] ConnectorInstanceCreateRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await connectorService.CreateAsync(request, cancellationToken));

    [HttpPost("instances/{connectorId:long}/credential")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RotateCredential(
        long connectorId,
        [FromBody] ConnectorCredentialRotationRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await connectorService.RotateCredentialAsync(
            connectorId,
            request,
            cancellationToken));

    [HttpPost("instances/{connectorId:long}/test-connection")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestConnection(
        long connectorId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await connectorService.TestConnectionAsync(connectorId, cancellationToken));

    [HttpPost("runs")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(
        [FromBody] ConnectorRunRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await connectorService.RunAsync(request, cancellationToken));

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
