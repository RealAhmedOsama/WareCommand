using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.InventoryStatuses;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/inventory-statuses")]
[Authorize(Policy = WmsPermissions.InventoryRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class InventoryStatusesController(
    IInventoryStatusService inventoryStatusService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<InventoryStatusDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery] int? warehouseId,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var result = await inventoryStatusService.GetStatusesAsync(
            warehouseId,
            includeInactive,
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.SettingsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] InventoryStatusDefinitionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await inventoryStatusService.CreateAsync(
            ToInput(request),
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPut("{statusId:int}")]
    [Authorize(Policy = WmsPermissions.SettingsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        int statusId,
        [FromBody] InventoryStatusDefinitionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await inventoryStatusService.UpdateAsync(
            statusId,
            ToInput(request),
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{statusId:int}/active")]
    [Authorize(Policy = WmsPermissions.SettingsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(
        int statusId,
        [FromBody] InventoryStatusActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await inventoryStatusService.SetActiveAsync(
            statusId,
            request.IsActive,
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("transitions")]
    [Authorize(Policy = WmsPermissions.SettingsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfigureTransition(
        [FromBody] InventoryStatusTransitionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await inventoryStatusService.ConfigureTransitionAsync(
            new InventoryStatusTransitionInput(
                request.FromStatusId,
                request.ToStatusId,
                request.RequiresReason),
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("stock/{stockId:int}/change")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStockStatus(
        int stockId,
        [FromBody] InventoryStatusChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await inventoryStatusService.ChangeStockStatusAsync(
            stockId,
            request.Quantity,
            request.TargetStatusId,
            request.Reason,
            request.ReferenceNumber,
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    private static InventoryStatusDefinitionInput ToInput(InventoryStatusDefinitionRequest request) =>
        new(
            request.Code,
            request.Name,
            request.LocalizedName,
            request.WarehouseId,
            request.IsAvailable,
            request.IsAllocatable,
            request.IsPickable,
            request.IsShippable,
            request.IsCountable,
            request.DefaultLocationType,
            request.ForceForLocationType);

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

public sealed class InventoryStatusDefinitionRequest
{
    [Required]
    [StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string LocalizedName { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int? WarehouseId { get; init; }

    public bool IsAvailable { get; init; }
    public bool IsAllocatable { get; init; }
    public bool IsPickable { get; init; }
    public bool IsShippable { get; init; }
    public bool IsCountable { get; init; }
    public LocationType? DefaultLocationType { get; init; }
    public bool ForceForLocationType { get; init; }
}

public sealed class InventoryStatusActivationRequest
{
    public bool IsActive { get; init; }
}

public sealed class InventoryStatusTransitionRequest
{
    [Range(1, int.MaxValue)]
    public int FromStatusId { get; init; }

    [Range(1, int.MaxValue)]
    public int ToStatusId { get; init; }

    public bool RequiresReason { get; init; } = true;
}

public sealed class InventoryStatusChangeRequest
{
    [Wms.ASP.Validation.InvariantDecimalRange("0.000000000001", "79228162514264337593543950335")]
    public decimal Quantity { get; init; }

    [Range(1, int.MaxValue)]
    public int TargetStatusId { get; init; }

    [Required]
    [StringLength(1_000)]
    public string Reason { get; init; } = string.Empty;

    [StringLength(100)]
    public string? ReferenceNumber { get; init; }
}
