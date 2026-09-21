using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Outbound;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/outbound/waves")]
[Authorize(Policy = WmsPermissions.InventoryRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class WavesController(
    IWaveService waveService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? warehouseId,
        [FromQuery] WaveStatus? status,
        [FromQuery] int? templateId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await waveService.SearchAsync(
            new WaveQuery(warehouseId, status, templateId, page, pageSize),
            cancellationToken));

    [HttpGet("{waveId:int}")]
    public async Task<IActionResult> Get(
        int waveId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await waveService.GetAsync(waveId, cancellationToken));

    [HttpPost("simulate")]
    public async Task<IActionResult> Simulate(
        [FromBody] WaveCreateInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await waveService.SimulateAsync(
            input,
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.AllocationManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] WaveCreateInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await waveService.CreateAsync(
            input,
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("{waveId:int}/process")]
    [Authorize(Policy = WmsPermissions.AllocationManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Process(
        int waveId,
        [FromBody] WaveProcessInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await waveService.ProcessAsync(
            waveId,
            input,
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpDelete("{waveId:int}/lines/{salesOrderLineId:int}")]
    [Authorize(Policy = WmsPermissions.AllocationManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveLine(
        int waveId,
        int salesOrderLineId,
        [FromBody] WaveLineRemovalInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await waveService.RemoveLineAsync(
            waveId,
            salesOrderLineId,
            input,
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("{waveId:int}/cancel")]
    [Authorize(Policy = WmsPermissions.AllocationManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        int waveId,
        [FromBody] WaveCancellationInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await waveService.CancelAsync(
            waveId,
            input,
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpGet("templates")]
    public async Task<IActionResult> Templates(
        [FromQuery] int? warehouseId,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await waveService.SearchTemplatesAsync(
            new WaveTemplateQuery(warehouseId, includeInactive),
            cancellationToken));

    [HttpPost("templates")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTemplate(
        [FromBody] WaveTemplateInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await waveService.SaveTemplateAsync(
            null,
            input,
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPut("templates/{templateId:int}")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTemplate(
        int templateId,
        [FromBody] WaveTemplateInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await waveService.SaveTemplateAsync(
            templateId,
            input,
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
