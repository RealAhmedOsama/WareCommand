using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Labels;
using Wms.ASP.Extensions;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/labels")]
[Authorize(Policy = WmsPermissions.InventoryRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class LabelsController(
    ILabelTemplateService templateService,
    ILabelPrintService printService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("templates")]
    [Authorize(Policy = WmsPermissions.SettingsManage)]
    public async Task<IActionResult> ListTemplates(
        [FromQuery] string? name,
        [FromQuery] WmsLabelDocumentType? documentType,
        [FromQuery] string? language,
        [FromQuery] int? warehouseId,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await templateService.ListAsync(
            new WmsLabelTemplateQuery(name, documentType, language, warehouseId, includeInactive),
            cancellationToken));

    [HttpPost("templates")]
    [Authorize(Policy = WmsPermissions.SettingsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTemplate(
        [FromBody] WmsLabelTemplateDefinition definition,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await templateService.SaveVersionAsync(
            new WmsLabelTemplateSaveRequest(
                definition,
                currentUser.RequireUserId(),
                User.Identity?.Name),
            cancellationToken));

    [HttpPost("templates/activate")]
    [Authorize(Policy = WmsPermissions.SettingsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ActivateTemplate(
        [FromBody] WmsLabelTemplateActivationRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await templateService.ActivateAsync(
            request.Name,
            request.Version,
            request.Language,
            request.WarehouseId,
            currentUser.RequireUserId(),
            User.Identity?.Name,
            cancellationToken));

    [HttpPost("templates/rollback")]
    [Authorize(Policy = WmsPermissions.SettingsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RollbackTemplate(
        [FromBody] WmsLabelTemplateActivationRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await templateService.RollbackAsync(
            request.Name,
            request.Version,
            request.Language,
            request.WarehouseId,
            currentUser.RequireUserId(),
            User.Identity?.Name,
            cancellationToken));

    [HttpPost("preview")]
    [Authorize(Policy = WmsPermissions.SettingsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(
        [FromBody] WmsLabelPreviewRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await printService.PreviewAsync(request, cancellationToken));

    [HttpPost("print")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Print(
        [FromBody] WmsLabelPrintRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await printService.PrintAsync(
            request with
            {
                ActorUserId = currentUser.RequireUserId(),
                ActorUserName = User.Identity?.Name
            },
            cancellationToken));

    [HttpPost("jobs/{jobId:long}/retry")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Retry(
        long jobId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await printService.RetryAsync(
            jobId,
            currentUser.RequireUserId(),
            User.Identity?.Name,
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

public sealed record WmsLabelTemplateActivationRequest(
    string Name,
    int Version,
    string Language,
    int? WarehouseId = null);
