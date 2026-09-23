using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.SalesOrders;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/allocations")]
[Authorize(Policy = WmsPermissions.InventoryRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class SalesOrderAllocationsController(
    ISalesOrderAllocationService allocationService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("{salesOrderId:int}")]
    public async Task<IActionResult> Get(
        int salesOrderId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await allocationService.GetAsync(salesOrderId, cancellationToken));

    [HttpPost("{salesOrderId:int}/simulate")]
    [Authorize(Policy = WmsPermissions.AllocationManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Simulate(
        int salesOrderId,
        [FromBody] SalesOrderAllocationRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await allocationService.SimulateAsync(
            salesOrderId,
            request.ToCommand(simulation: true),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{salesOrderId:int}/allocate")]
    [Authorize(Policy = WmsPermissions.AllocationManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Allocate(
        int salesOrderId,
        [FromBody] SalesOrderAllocationRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await allocationService.AllocateAsync(
            salesOrderId,
            request.ToCommand(),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{salesOrderId:int}/release")]
    [Authorize(Policy = WmsPermissions.AllocationManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Release(
        int salesOrderId,
        [FromBody] SalesOrderAllocationRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await allocationService.ReleaseAsync(
            salesOrderId,
            request.ToCommand(),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{salesOrderId:int}/reallocate")]
    [Authorize(Policy = WmsPermissions.AllocationManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reallocate(
        int salesOrderId,
        [FromBody] SalesOrderAllocationRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await allocationService.ReallocateAsync(
            salesOrderId,
            request.ToCommand(),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{salesOrderId:int}/unrelease")]
    [Authorize(Policy = WmsPermissions.AllocationManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unrelease(
        int salesOrderId,
        [FromBody] SalesOrderAllocationRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await allocationService.UnreleaseAsync(
            salesOrderId,
            request.ToCommand(),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{salesOrderId:int}/cancel")]
    [Authorize(Policy = WmsPermissions.AllocationManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        int salesOrderId,
        [FromBody] SalesOrderAllocationRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await allocationService.CancelAsync(
            salesOrderId,
            request.ToCommand(),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("batch")]
    [Authorize(Policy = WmsPermissions.AllocationManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AllocateBatch(
        [FromBody] SalesOrderAllocationBatchRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await allocationService.AllocateBatchAsync(
            request.ToCommand(),
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

public sealed class SalesOrderAllocationRequest
{
    [Required]
    [StringLength(250)]
    public string IdempotencyKey { get; init; } = string.Empty;

    public IReadOnlyList<int>? LineIds { get; init; }
    public bool ReleaseToWarehouse { get; init; } = true;
    public bool Replan { get; init; }

    [StringLength(1_000)]
    public string? Reason { get; init; }

    public SalesOrderAllocationCommand ToCommand(bool simulation = false) =>
        new(
            LineIds,
            ReleaseToWarehouse,
            Replan,
            simulation,
            IdempotencyKey,
            Reason);
}

public sealed class SalesOrderAllocationBatchRequest
{
    [Required, MinLength(1)]
    public IReadOnlyList<int> SalesOrderIds { get; init; } = [];

    [Required]
    [StringLength(250)]
    public string IdempotencyKey { get; init; } = string.Empty;

    public bool ReleaseToWarehouse { get; init; } = true;
    public bool Replan { get; init; }

    [StringLength(1_000)]
    public string? Reason { get; init; }

    public SalesOrderAllocationBatchCommand ToCommand() =>
        new(SalesOrderIds, ReleaseToWarehouse, Replan, IdempotencyKey, Reason);
}
