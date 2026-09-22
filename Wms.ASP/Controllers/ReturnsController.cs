using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Returns;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/returns")]
[Authorize(Policy = WmsPermissions.ReceivingExecute)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class ReturnsController(
    IReturnService returnService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpPost("authorizations")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] ReturnAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await returnService.CreateAsync(
            new ReturnAuthorizationInput(
                request.RmaNumber,
                request.WarehouseId,
                request.Lines.Select(line => new ReturnLineInput(
                    line.ItemId,
                    line.ExpectedQuantity,
                    line.SalesOrderLineId,
                    line.ShipmentLineId,
                    line.ExpectedLotId,
                    line.ExpectedSerialNumberId)).ToArray(),
                request.CustomerId,
                request.SalesOrderId,
                request.ShipmentId,
                request.PackageId,
                request.ReturnLocationId,
                request.Unplanned,
                request.Reason,
                request.IdempotencyKey),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpGet("authorizations/{returnAuthorizationId:int}")]
    public async Task<IActionResult> Get(
        int returnAuthorizationId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await returnService.GetAsync(returnAuthorizationId, cancellationToken));

    [HttpPost("authorizations/{returnAuthorizationId:int}/authorize")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Authorize(
        int returnAuthorizationId,
        [FromBody] ReturnCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await returnService.AuthorizeAsync(
            new ReturnCommandInput(returnAuthorizationId, request.IdempotencyKey, request.Reason),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("authorizations/{returnAuthorizationId:int}/inspect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Inspect(
        int returnAuthorizationId,
        [FromBody] ReturnCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await returnService.InspectAsync(
            new ReturnCommandInput(returnAuthorizationId, request.IdempotencyKey, request.Reason),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("authorizations/{returnAuthorizationId:int}/receipts")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receive(
        int returnAuthorizationId,
        [FromBody] ReturnReceiptRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await returnService.ReceiveAsync(
            new ReturnReceiptInput(
                returnAuthorizationId,
                request.ReturnLineId,
                request.Quantity,
                request.InventoryStatusId,
                request.LotId,
                request.SerialNumberId,
                request.SerialNumber,
                request.LicensePlateId,
                request.IdempotencyKey),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("authorizations/{returnAuthorizationId:int}/dispositions")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dispose(
        int returnAuthorizationId,
        [FromBody] ReturnDispositionRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await returnService.DisposeAsync(
            new ReturnDispositionInput(
                returnAuthorizationId,
                request.ReturnReceiptId,
                request.Kind,
                request.Quantity,
                request.DestinationLocationId,
                request.DestinationInventoryStatusId,
                request.Reason,
                request.IdempotencyKey),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("authorizations/{returnAuthorizationId:int}/close")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(
        int returnAuthorizationId,
        [FromBody] ReturnCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await returnService.CloseAsync(
            new ReturnCommandInput(returnAuthorizationId, request.IdempotencyKey, request.Reason),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("authorizations/{returnAuthorizationId:int}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        int returnAuthorizationId,
        [FromBody] ReturnCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await returnService.CancelAsync(
            new ReturnCommandInput(returnAuthorizationId, request.IdempotencyKey, request.Reason),
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

public sealed class ReturnAuthorizationRequest
{
    [Required, StringLength(80)] public string RmaNumber { get; init; } = string.Empty;
    [Range(1, int.MaxValue)] public int WarehouseId { get; init; }
    [Required, MinLength(1)] public IReadOnlyList<ReturnLineRequest> Lines { get; init; } = [];
    [Range(1, int.MaxValue)] public int? CustomerId { get; init; }
    [Range(1, int.MaxValue)] public int? SalesOrderId { get; init; }
    [Range(1, int.MaxValue)] public int? ShipmentId { get; init; }
    [Range(1, int.MaxValue)] public int? PackageId { get; init; }
    [Range(1, int.MaxValue)] public int ReturnLocationId { get; init; }
    public bool Unplanned { get; init; }
    [Required, StringLength(1_000)] public string Reason { get; init; } = string.Empty;
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed class ReturnLineRequest
{
    [Range(1, int.MaxValue)] public int ItemId { get; init; }
    [Wms.ASP.Validation.InvariantDecimalRange("0.000001", "79228162514264337593543950335")] public decimal ExpectedQuantity { get; init; }
    [Range(1, int.MaxValue)] public int? SalesOrderLineId { get; init; }
    [Range(1, int.MaxValue)] public int? ShipmentLineId { get; init; }
    [Range(1, int.MaxValue)] public int? ExpectedLotId { get; init; }
    [Range(1, int.MaxValue)] public int? ExpectedSerialNumberId { get; init; }
}

public sealed class ReturnCommandRequest
{
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
    [StringLength(1_000)] public string? Reason { get; init; }
}

public sealed class ReturnReceiptRequest
{
    [Range(1, int.MaxValue)] public int ReturnLineId { get; init; }
    [Wms.ASP.Validation.InvariantDecimalRange("0.000001", "79228162514264337593543950335")] public decimal Quantity { get; init; }
    [Range(1, int.MaxValue)] public int InventoryStatusId { get; init; } = InventoryStatusSystemIds.ReturnPending;
    [Range(1, int.MaxValue)] public int? LotId { get; init; }
    [Range(1, int.MaxValue)] public int? SerialNumberId { get; init; }
    [StringLength(250)] public string? SerialNumber { get; init; }
    [Range(1, int.MaxValue)] public int? LicensePlateId { get; init; }
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed class ReturnDispositionRequest
{
    [Range(1, int.MaxValue)] public int ReturnReceiptId { get; init; }
    [EnumDataType(typeof(ReturnDispositionKind))] public ReturnDispositionKind Kind { get; init; }
    [Wms.ASP.Validation.InvariantDecimalRange("0.000001", "79228162514264337593543950335")] public decimal Quantity { get; init; }
    [Range(1, int.MaxValue)] public int? DestinationLocationId { get; init; }
    [Range(1, int.MaxValue)] public int? DestinationInventoryStatusId { get; init; }
    [Required, StringLength(1_000)] public string Reason { get; init; } = string.Empty;
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
}
