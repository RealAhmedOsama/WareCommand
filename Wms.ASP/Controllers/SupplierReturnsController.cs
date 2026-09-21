using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.SupplierReturns;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/supplier-returns")]
[Authorize(Policy = WmsPermissions.SupplierReturnsRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class SupplierReturnsController(
    ISupplierReturnService supplierReturnService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? warehouseId,
        [FromQuery] int? supplierId,
        [FromQuery] SupplierReturnStatus? status,
        [FromQuery] string? searchTerm,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await supplierReturnService.ListAsync(
            new SupplierReturnQuery(warehouseId, supplierId, status, searchTerm, page, pageSize),
            cancellationToken));

    [HttpGet("{supplierReturnId:int}")]
    public async Task<IActionResult> Get(
        int supplierReturnId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await supplierReturnService.GetAsync(supplierReturnId, cancellationToken));

    [HttpPost]
    [Authorize(Policy = WmsPermissions.SupplierReturnsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] SupplierReturnCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await supplierReturnService.CreateAsync(
            new SupplierReturnCreateInput(
                request.ReturnNumber,
                request.WarehouseId,
                request.SupplierId,
                request.StagingLocationId,
                request.Lines.Select(line => new SupplierReturnLineInput(
                    line.ItemId,
                    line.RequestedBaseQuantity,
                    line.BaseUnitOfMeasure,
                    line.SourceLocationId,
                    line.InventoryStatusId,
                    line.Reason,
                    line.LotId,
                    line.SerialNumberId,
                    line.SerialNumber,
                    line.LicensePlateId,
                    line.PurchaseOrderLineId,
                    line.AdvanceShippingNoticeLineId,
                    line.ReceiptLineId,
                    line.QualityInspectionId,
                    line.QualityInspectionDispositionId,
                    line.SourceReference)).ToArray(),
                request.SourceType,
                request.Reason,
                request.PurchaseOrderId,
                request.AdvanceShippingNoticeId,
                request.ReceiptId,
                request.QualityInspectionId,
                request.SupplierAuthorizationReference,
                request.ExternalReference),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("{supplierReturnId:int}/approve")]
    [Authorize(Policy = WmsPermissions.SupplierReturnsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(
        int supplierReturnId,
        [FromBody] SupplierReturnCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await supplierReturnService.ApproveAsync(
            new SupplierReturnCommandInput(
                supplierReturnId,
                request.IdempotencyKey,
                request.Reason,
                request.SupervisorOverride,
                request.OverrideReason),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{supplierReturnId:int}/release")]
    [Authorize(Policy = WmsPermissions.SupplierReturnsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Release(
        int supplierReturnId,
        [FromBody] SupplierReturnCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await supplierReturnService.ReleaseAsync(
            new SupplierReturnCommandInput(supplierReturnId, request.IdempotencyKey, request.Reason),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{supplierReturnId:int}/pack")]
    [Authorize(Policy = WmsPermissions.SupplierReturnsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pack(
        int supplierReturnId,
        [FromBody] SupplierReturnCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await supplierReturnService.PackAsync(
            new SupplierReturnCommandInput(supplierReturnId, request.IdempotencyKey, request.Reason),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{supplierReturnId:int}/ship")]
    [Authorize(Policy = WmsPermissions.ShippingExecute)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ship(
        int supplierReturnId,
        [FromBody] SupplierReturnShipRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await supplierReturnService.ShipAsync(
            new SupplierReturnShipInput(
                supplierReturnId,
                request.CarrierCode,
                request.TrackingNumber,
                request.ShippingDocumentReference,
                request.IdempotencyKey),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{supplierReturnId:int}/acknowledge")]
    [Authorize(Policy = WmsPermissions.SupplierReturnsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Acknowledge(
        int supplierReturnId,
        [FromBody] SupplierReturnCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await supplierReturnService.AcknowledgeAsync(
            new SupplierReturnCommandInput(supplierReturnId, request.IdempotencyKey, request.Reason),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{supplierReturnId:int}/close")]
    [Authorize(Policy = WmsPermissions.SupplierReturnsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(
        int supplierReturnId,
        [FromBody] SupplierReturnCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await supplierReturnService.CloseAsync(
            new SupplierReturnCommandInput(supplierReturnId, request.IdempotencyKey, request.Reason),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{supplierReturnId:int}/exception")]
    [Authorize(Policy = WmsPermissions.SupplierReturnsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Exception(
        int supplierReturnId,
        [FromBody] SupplierReturnCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await supplierReturnService.ExceptionAsync(
            new SupplierReturnCommandInput(supplierReturnId, request.IdempotencyKey, request.Reason),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{supplierReturnId:int}/cancel")]
    [Authorize(Policy = WmsPermissions.SupplierReturnsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        int supplierReturnId,
        [FromBody] SupplierReturnCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await supplierReturnService.CancelAsync(
            new SupplierReturnCommandInput(supplierReturnId, request.IdempotencyKey, request.Reason),
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

public sealed class SupplierReturnCreateRequest
{
    [Required, StringLength(80)] public string ReturnNumber { get; init; } = string.Empty;
    [Range(1, int.MaxValue)] public int WarehouseId { get; init; }
    [Range(1, int.MaxValue)] public int SupplierId { get; init; }
    [Range(1, int.MaxValue)] public int StagingLocationId { get; init; }
    [Required, StringLength(40)] public string SourceType { get; init; } = "MANUAL";
    [Required, StringLength(1_000)] public string Reason { get; init; } = string.Empty;
    [Range(1, int.MaxValue)] public int? PurchaseOrderId { get; init; }
    [Range(1, int.MaxValue)] public int? AdvanceShippingNoticeId { get; init; }
    [Range(1, int.MaxValue)] public int? ReceiptId { get; init; }
    [Range(1, int.MaxValue)] public int? QualityInspectionId { get; init; }
    [StringLength(120)] public string? SupplierAuthorizationReference { get; init; }
    [StringLength(120)] public string? ExternalReference { get; init; }
    [Required, MinLength(1)] public IReadOnlyList<SupplierReturnLineRequest> Lines { get; init; } = [];
}

public sealed class SupplierReturnLineRequest
{
    [Range(1, int.MaxValue)] public int ItemId { get; init; }
    [Range(typeof(decimal), "0.000001", "79228162514264337593543950335")] public decimal RequestedBaseQuantity { get; init; }
    [Required, StringLength(20)] public string BaseUnitOfMeasure { get; init; } = string.Empty;
    [Range(1, int.MaxValue)] public int SourceLocationId { get; init; }
    [Range(1, int.MaxValue)] public int InventoryStatusId { get; init; }
    [Required, StringLength(1_000)] public string Reason { get; init; } = string.Empty;
    [Range(1, int.MaxValue)] public int? LotId { get; init; }
    [Range(1, int.MaxValue)] public int? SerialNumberId { get; init; }
    [StringLength(100)] public string? SerialNumber { get; init; }
    [Range(1, int.MaxValue)] public int? LicensePlateId { get; init; }
    [Range(1, int.MaxValue)] public int? PurchaseOrderLineId { get; init; }
    [Range(1, int.MaxValue)] public int? AdvanceShippingNoticeLineId { get; init; }
    [Range(1, int.MaxValue)] public int? ReceiptLineId { get; init; }
    [Range(1, int.MaxValue)] public int? QualityInspectionId { get; init; }
    [Range(1, int.MaxValue)] public int? QualityInspectionDispositionId { get; init; }
    [StringLength(200)] public string? SourceReference { get; init; }
}

public sealed class SupplierReturnCommandRequest
{
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
    [StringLength(1_000)] public string? Reason { get; init; }
    public bool SupervisorOverride { get; init; }
    [StringLength(1_000)] public string? OverrideReason { get; init; }
}

public sealed class SupplierReturnShipRequest
{
    [Required, StringLength(80)] public string CarrierCode { get; init; } = string.Empty;
    [StringLength(120)] public string? TrackingNumber { get; init; }
    [StringLength(120)] public string? ShippingDocumentReference { get; init; }
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
}
