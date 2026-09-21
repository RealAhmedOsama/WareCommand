using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Transfers;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/transfers")]
[Authorize(Policy = WmsPermissions.InventoryRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class TransfersController(
    ITransferService transferService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? sourceWarehouseId,
        [FromQuery] int? destinationWarehouseId,
        [FromQuery] TransferOrderStatus? status,
        [FromQuery] string? searchTerm,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await transferService.ListAsync(
            new TransferQuery(sourceWarehouseId, destinationWarehouseId, status, searchTerm, page, pageSize),
            cancellationToken));

    [HttpGet("{transferOrderId:int}")]
    public async Task<IActionResult> Get(
        int transferOrderId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await transferService.GetAsync(transferOrderId, cancellationToken));

    [HttpPost]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] TransferOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await transferService.CreateAsync(
            new TransferOrderInput(
                request.TransferNumber,
                request.CreationIdempotencyKey,
                request.SourceWarehouseId,
                request.DestinationWarehouseId,
                request.TransitLocationId,
                request.Lines.Select(line => new TransferLineInput(
                    line.ItemId,
                    line.RequestedQuantity,
                    line.BaseUnitOfMeasure,
                    line.SourceLocationId,
                    line.DestinationLocationId,
                    line.LotId,
                    line.SerialNumberId,
                    line.SerialNumber,
                    line.LicensePlateId,
                    line.SourceInventoryStatusId,
                    line.DestinationInventoryStatusId,
                    line.Notes)).ToArray(),
                request.Priority,
                request.ExternalReference,
                request.Notes),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("{transferOrderId:int}/confirm")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Confirm(
        int transferOrderId,
        [FromBody] TransferCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteCommand(transferService.ConfirmAsync(
            new TransferCommandInput(transferOrderId, request.IdempotencyKey, request.Reason),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{transferOrderId:int}/release")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Release(
        int transferOrderId,
        [FromBody] TransferCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteCommand(transferService.ReleaseAsync(
            new TransferCommandInput(transferOrderId, request.IdempotencyKey, request.Reason),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{transferOrderId:int}/ship")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Ship(
        int transferOrderId,
        [FromBody] TransferQuantityCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteCommand(transferService.ShipAsync(
            new TransferQuantityCommandInput(
                transferOrderId,
                request.TransferLineId,
                request.Quantity,
                request.IdempotencyKey,
                request.Reason),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{transferOrderId:int}/receive")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Receive(
        int transferOrderId,
        [FromBody] TransferQuantityCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteCommand(transferService.ReceiveAsync(
            new TransferQuantityCommandInput(
                transferOrderId,
                request.TransferLineId,
                request.Quantity,
                request.IdempotencyKey,
                request.Reason),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{transferOrderId:int}/close")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Close(
        int transferOrderId,
        [FromBody] TransferCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteCommand(transferService.CloseAsync(
            new TransferCommandInput(transferOrderId, request.IdempotencyKey, request.Reason),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{transferOrderId:int}/cancel")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Cancel(
        int transferOrderId,
        [FromBody] TransferCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteCommand(transferService.CancelAsync(
            new TransferCommandInput(transferOrderId, request.IdempotencyKey, request.Reason),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("~/api/internal-movements")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(
        [FromBody] InternalMovementRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await transferService.MoveAsync(
            new InternalMovementInput(
                request.WarehouseId,
                request.ItemId,
                request.Quantity,
                request.BaseUnitOfMeasure,
                request.SourceLocationId,
                request.DestinationLocationId,
                request.IdempotencyKey,
                request.LotId,
                request.SerialNumberId,
                request.SerialNumber,
                request.LicensePlateId,
                request.InventoryStatusId,
                request.Reason),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    private async Task<IActionResult> ExecuteCommand(Task<Result<TransferOrderDto>> command) =>
        ToActionResult(await command);

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

public sealed class TransferOrderRequest
{
    [Required, StringLength(80)] public string TransferNumber { get; init; } = string.Empty;
    [Required, StringLength(250)] public string CreationIdempotencyKey { get; init; } = string.Empty;
    [Range(1, int.MaxValue)] public int SourceWarehouseId { get; init; }
    [Range(1, int.MaxValue)] public int DestinationWarehouseId { get; init; }
    [Range(1, int.MaxValue)] public int TransitLocationId { get; init; }
    [Range(0, int.MaxValue)] public int Priority { get; init; } = 50;
    [StringLength(100)] public string? ExternalReference { get; init; }
    [StringLength(2_000)] public string? Notes { get; init; }
    [Required, MinLength(1)] public IReadOnlyList<TransferLineRequest> Lines { get; init; } = [];
}

public sealed class TransferLineRequest
{
    [Range(1, int.MaxValue)] public int ItemId { get; init; }
    [Range(typeof(decimal), "0.000001", "79228162514264337593543950335")] public decimal RequestedQuantity { get; init; }
    [Required, StringLength(20)] public string BaseUnitOfMeasure { get; init; } = string.Empty;
    [Range(1, int.MaxValue)] public int SourceLocationId { get; init; }
    [Range(1, int.MaxValue)] public int DestinationLocationId { get; init; }
    [Range(1, int.MaxValue)] public int? LotId { get; init; }
    [Range(1, int.MaxValue)] public int? SerialNumberId { get; init; }
    [StringLength(100)] public string? SerialNumber { get; init; }
    [Range(1, int.MaxValue)] public int? LicensePlateId { get; init; }
    [Range(1, int.MaxValue)] public int SourceInventoryStatusId { get; init; } = InventoryStatusSystemIds.Available;
    [Range(1, int.MaxValue)] public int DestinationInventoryStatusId { get; init; } = InventoryStatusSystemIds.Available;
    [StringLength(1_000)] public string? Notes { get; init; }
}

public class TransferCommandRequest
{
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
    [StringLength(1_000)] public string? Reason { get; init; }
}

public sealed class TransferQuantityCommandRequest : TransferCommandRequest
{
    [Range(1, int.MaxValue)] public int TransferLineId { get; init; }
    [Range(typeof(decimal), "0.000001", "79228162514264337593543950335")] public decimal Quantity { get; init; }
}

public sealed class InternalMovementRequest
{
    [Range(1, int.MaxValue)] public int WarehouseId { get; init; }
    [Range(1, int.MaxValue)] public int ItemId { get; init; }
    [Range(typeof(decimal), "0.000001", "79228162514264337593543950335")] public decimal Quantity { get; init; }
    [Required, StringLength(20)] public string BaseUnitOfMeasure { get; init; } = string.Empty;
    [Range(1, int.MaxValue)] public int SourceLocationId { get; init; }
    [Range(1, int.MaxValue)] public int DestinationLocationId { get; init; }
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
    [Range(1, int.MaxValue)] public int? LotId { get; init; }
    [Range(1, int.MaxValue)] public int? SerialNumberId { get; init; }
    [StringLength(100)] public string? SerialNumber { get; init; }
    [Range(1, int.MaxValue)] public int? LicensePlateId { get; init; }
    [Range(1, int.MaxValue)] public int InventoryStatusId { get; init; } = InventoryStatusSystemIds.Available;
    [StringLength(1_000)] public string? Reason { get; init; }
}
