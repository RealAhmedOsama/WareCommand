using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Inbound;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/inbound-exceptions")]
[Authorize(Policy = WmsPermissions.AdvanceShippingNoticesRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class InboundExceptionsController(
    IInboundExceptionService inboundExceptionService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(InboundExceptionPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? warehouseId,
        [FromQuery] InboundExceptionCode? code,
        [FromQuery] InboundExceptionSeverity? severity,
        [FromQuery] InboundExceptionStatus? status,
        [FromQuery] string? queueCode,
        [FromQuery] string? ownerUserId,
        [FromQuery] int? receiptId,
        [FromQuery] int? receivingSessionId,
        [FromQuery] string? searchTerm,
        [FromQuery] bool includeClosed = false,
        [FromQuery] bool overdueOnly = false,
        [FromQuery] DateTime? asOfUtc = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await inboundExceptionService.ListAsync(
            new InboundExceptionQuery(
                warehouseId,
                code,
                severity,
                status,
                queueCode,
                ownerUserId,
                receiptId,
                receivingSessionId,
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
        ToActionResult(await inboundExceptionService.GetAsync(exceptionId, cancellationToken));

    [HttpPost]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] InboundExceptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await inboundExceptionService.CreateAsync(
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("{exceptionId:int}/assign")]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(
        int exceptionId,
        [FromBody] InboundExceptionAssignmentRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await inboundExceptionService.AssignAsync(
            exceptionId,
            new InboundExceptionAssignmentInput(request.OwnerUserId, request.TeamCode),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{exceptionId:int}/review")]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartReview(
        int exceptionId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await inboundExceptionService.StartReviewAsync(
            exceptionId,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{exceptionId:int}/resolve")]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(
        int exceptionId,
        [FromBody] InboundExceptionResolutionRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await inboundExceptionService.ResolveAsync(
            exceptionId,
            request.ToInput(),
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

public sealed class InboundExceptionRequest
{
    [Range(1, int.MaxValue)]
    public int WarehouseId { get; init; }

    public InboundExceptionCode Code { get; init; }
    public InboundExceptionSeverity Severity { get; init; }

    [Required, StringLength(2_000)]
    public string Reason { get; init; } = string.Empty;

    [Required, StringLength(250)]
    public string IdempotencyKey { get; init; } = string.Empty;

    [StringLength(50)]
    public string? QueueCode { get; init; }

    public DateTime? DueAtUtc { get; init; }
    public int? AdvanceShippingNoticeId { get; init; }
    public int? AdvanceShippingNoticeLineId { get; init; }
    public int? ReceiptId { get; init; }
    public int? ReceiptLineId { get; init; }
    public int? ReceivingSessionId { get; init; }
    public int? LicensePlateId { get; init; }
    public int? WarehouseWorkId { get; init; }
    public int? ItemId { get; init; }
    public int? StagingLocationId { get; init; }
    public decimal? ExpectedBaseQuantity { get; init; }
    public decimal? ActualBaseQuantity { get; init; }
    public decimal? VarianceBaseQuantity { get; init; }

    [StringLength(50)]
    public string? ItemSkuSnapshot { get; init; }

    [StringLength(2_000)]
    public string? Notes { get; init; }

    [StringLength(4_000)]
    public string? AttachmentReferences { get; init; }

    public InboundExceptionInput ToInput() => new(
        WarehouseId,
        Code,
        Severity,
        Reason,
        IdempotencyKey,
        QueueCode,
        DueAtUtc,
        AdvanceShippingNoticeId,
        AdvanceShippingNoticeLineId,
        ReceiptId,
        ReceiptLineId,
        ReceivingSessionId,
        LicensePlateId,
        WarehouseWorkId,
        ItemId,
        StagingLocationId,
        ExpectedBaseQuantity,
        ActualBaseQuantity,
        VarianceBaseQuantity,
        ItemSkuSnapshot,
        Notes,
        AttachmentReferences);
}

public sealed class InboundExceptionAssignmentRequest
{
    [StringLength(450)]
    public string? OwnerUserId { get; init; }

    [StringLength(50)]
    public string? TeamCode { get; init; }
}

public sealed class InboundExceptionResolutionRequest
{
    public InboundExceptionResolution Resolution { get; init; }

    [Required, StringLength(2_000)]
    public string Reason { get; init; } = string.Empty;

    public bool SupervisorOverride { get; init; }

    [StringLength(1_000)]
    public string? OverrideReason { get; init; }

    public InboundExceptionResolutionInput ToInput() => new(
        Resolution,
        Reason,
        SupervisorOverride,
        OverrideReason);
}
