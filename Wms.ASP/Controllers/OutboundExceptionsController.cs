using System.ComponentModel.DataAnnotations;
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
[Route("api/outbound-exceptions")]
[Authorize(Policy = WmsPermissions.SalesOrdersRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class OutboundExceptionsController(
    IOutboundExceptionService outboundExceptionService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? warehouseId,
        [FromQuery] OutboundExceptionCode? code,
        [FromQuery] OutboundExceptionSeverity? severity,
        [FromQuery] OutboundExceptionStatus? status,
        [FromQuery] string? queueCode,
        [FromQuery] string? ownerUserId,
        [FromQuery] int? salesOrderId,
        [FromQuery] int? shipmentId,
        [FromQuery] string? searchTerm,
        [FromQuery] bool includeClosed = false,
        [FromQuery] bool overdueOnly = false,
        [FromQuery] DateTime? asOfUtc = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await outboundExceptionService.ListAsync(
            new OutboundExceptionQuery(
                warehouseId,
                code,
                severity,
                status,
                queueCode,
                ownerUserId,
                salesOrderId,
                shipmentId,
                searchTerm,
                includeClosed,
                overdueOnly,
                asOfUtc,
                page,
                pageSize),
            cancellationToken));

    [HttpGet("{exceptionId:int}")]
    public async Task<IActionResult> Get(
        int exceptionId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await outboundExceptionService.GetAsync(exceptionId, cancellationToken));

    [HttpPost]
    [Authorize(Policy = WmsPermissions.SalesOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] OutboundExceptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await outboundExceptionService.CreateAsync(
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("{exceptionId:int}/assign")]
    [Authorize(Policy = WmsPermissions.SalesOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(
        int exceptionId,
        [FromBody] OutboundExceptionAssignmentRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await outboundExceptionService.AssignAsync(
            exceptionId,
            new OutboundExceptionAssignmentInput(request.OwnerUserId, request.TeamCode),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{exceptionId:int}/review")]
    [Authorize(Policy = WmsPermissions.SalesOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartReview(
        int exceptionId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await outboundExceptionService.StartReviewAsync(
            exceptionId,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{exceptionId:int}/resolve")]
    [Authorize(Policy = WmsPermissions.SalesOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(
        int exceptionId,
        [FromBody] OutboundExceptionResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await outboundExceptionService.ResolveAsync(
            exceptionId,
            request.IdempotencyKey,
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));
    }

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

public sealed class OutboundExceptionRequest
{
    [Range(1, int.MaxValue)] public int WarehouseId { get; init; }
    public OutboundExceptionCode Code { get; init; }
    public OutboundExceptionSeverity Severity { get; init; }
    [Required, StringLength(2_000)] public string Reason { get; init; } = string.Empty;
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
    [StringLength(50)] public string? QueueCode { get; init; }
    public DateTime? DueAtUtc { get; init; }
    [Range(1, int.MaxValue)] public int? SalesOrderId { get; init; }
    [Range(1, int.MaxValue)] public int? SalesOrderLineId { get; init; }
    [Range(1, int.MaxValue)] public int? InventoryReservationId { get; init; }
    [Range(1, int.MaxValue)] public int? WarehouseWorkId { get; init; }
    [Range(1, int.MaxValue)] public int? WarehouseWorkLineId { get; init; }
    [Range(1, int.MaxValue)] public int? ShipmentId { get; init; }
    [Range(1, int.MaxValue)] public int? ShipmentPackageId { get; init; }
    [Range(1, int.MaxValue)] public int? ShipmentLoadId { get; init; }
    [Range(1, int.MaxValue)] public int? ItemId { get; init; }
    [Range(1, int.MaxValue)] public int? LocationId { get; init; }
    [Range(1, int.MaxValue)] public int? LotId { get; init; }
    [Range(1, int.MaxValue)] public int? SerialNumberId { get; init; }
    [Range(1, int.MaxValue)] public int? LicensePlateId { get; init; }
    public decimal? ExpectedBaseQuantity { get; init; }
    public decimal? ActualBaseQuantity { get; init; }
    public decimal? VarianceBaseQuantity { get; init; }
    [StringLength(50)] public string? ItemSkuSnapshot { get; init; }
    [StringLength(2_000)] public string? Notes { get; init; }
    [StringLength(4_000)] public string? AttachmentReferences { get; init; }

    public OutboundExceptionInput ToInput() => new(
        WarehouseId,
        Code,
        Severity,
        Reason,
        IdempotencyKey,
        QueueCode,
        DueAtUtc,
        SalesOrderId,
        SalesOrderLineId,
        InventoryReservationId,
        WarehouseWorkId,
        WarehouseWorkLineId,
        ShipmentId,
        ShipmentPackageId,
        ShipmentLoadId,
        ItemId,
        LocationId,
        LotId,
        SerialNumberId,
        LicensePlateId,
        ExpectedBaseQuantity,
        ActualBaseQuantity,
        VarianceBaseQuantity,
        ItemSkuSnapshot,
        Notes,
        AttachmentReferences);
}

public sealed class OutboundExceptionAssignmentRequest
{
    [StringLength(450)] public string? OwnerUserId { get; init; }
    [StringLength(50)] public string? TeamCode { get; init; }
}

public sealed class OutboundExceptionResolutionRequest
{
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
    public OutboundExceptionResolution Resolution { get; init; }
    [Required, StringLength(2_000)] public string Reason { get; init; } = string.Empty;
    public decimal? Quantity { get; init; }
    [Range(1, int.MaxValue)] public int? SubstituteItemId { get; init; }
    [Range(1, int.MaxValue)] public int? NewCarrierId { get; init; }
    [Range(1, int.MaxValue)] public int? NewCarrierServiceId { get; init; }
    public bool SupervisorOverride { get; init; }
    [StringLength(1_000)] public string? OverrideReason { get; init; }

    public OutboundExceptionResolutionInput ToInput() => new(
        Resolution,
        Reason,
        Quantity,
        SubstituteItemId,
        NewCarrierId,
        NewCarrierServiceId,
        SupervisorOverride,
        OverrideReason);
}
