using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Identification;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/scanning")]
[Authorize(Policy = WmsPermissions.ItemsRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Scanning)]
public sealed class ScanningController(IIdentificationService identificationService) : ControllerBase
{
    [HttpPost("resolve")]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(typeof(IdentificationResolutionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Resolve(
        [FromBody] ScanResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await identificationService.ResolveAsync(
            new IdentificationLookupRequest(request.Value, request.WarehouseId, request.IncludeInactive),
            cancellationToken);
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
            ErrorType.Conflict => StatusCodes.Status409Conflict,
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

public sealed class ScanResolutionRequest
{
    [Required]
    [StringLength(200)]
    public string Value { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int? WarehouseId { get; init; }

    public bool IncludeInactive { get; init; }
}
