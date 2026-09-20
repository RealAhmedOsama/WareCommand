using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Receiving;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/receiving/sessions")]
[Authorize(Policy = WmsPermissions.ReceivingExecute)]
[EnableRateLimiting(WmsRateLimitPolicies.Scanning)]
public sealed class ReceivingSessionsController(
    IReceivingExecutionService receivingExecutionService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(
        [FromBody] ReceivingSessionStartRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await receivingExecutionService.StartAsync(
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpGet("{sessionId:int}")]
    public async Task<IActionResult> Get(int sessionId, CancellationToken cancellationToken = default) =>
        ToActionResult(await receivingExecutionService.GetAsync(sessionId, cancellationToken));

    [HttpPost("{sessionId:int}/pause")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pause(int sessionId, CancellationToken cancellationToken = default) =>
        ToActionResult(await receivingExecutionService.PauseAsync(
            sessionId,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{sessionId:int}/resume")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resume(int sessionId, CancellationToken cancellationToken = default) =>
        ToActionResult(await receivingExecutionService.ResumeAsync(
            sessionId,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{sessionId:int}/scans")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Scan(
        int sessionId,
        [FromBody] ReceivingScanRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await receivingExecutionService.ScanAsync(
            sessionId,
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("{sessionId:int}/complete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(
        int sessionId,
        [FromBody] ReceivingSessionCompletionRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await receivingExecutionService.CompleteAsync(
            sessionId,
            new ReceivingSessionCompletionInput(request.AllowPartial, request.SupervisorOverrideReason),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{sessionId:int}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        int sessionId,
        [FromBody] ReceivingSessionCancelRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await receivingExecutionService.CancelAsync(
            sessionId,
            request.Reason,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{sessionId:int}/scans/{scanId:int}/correct")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CorrectScan(
        int sessionId,
        int scanId,
        [FromBody] ReceivingSessionCorrectionRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await receivingExecutionService.CorrectScanAsync(
            sessionId,
            scanId,
            new ReceivingSessionCorrectionInput(request.Reason, request.Notes),
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
            ErrorType.Conflict or ErrorType.Concurrency => StatusCodes.Status409Conflict,
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

public sealed class ReceivingSessionStartRequest
{
    [Range(1, int.MaxValue)]
    public int WarehouseId { get; init; }

    [Range(1, int.MaxValue)]
    public int ReceivingLocationId { get; init; }

    [Required]
    public ReceivingSessionSourceType SourceType { get; init; }

    [Range(1, int.MaxValue)]
    public int? PurchaseOrderId { get; init; }

    [Range(1, int.MaxValue)]
    public int? AdvanceShippingNoticeId { get; init; }

    [Range(1, int.MaxValue)]
    public int? DockLocationId { get; init; }

    [StringLength(100)]
    public string? ExternalReference { get; init; }

    [StringLength(100)]
    public string? SessionReference { get; init; }

    [StringLength(2_000)]
    public string? Notes { get; init; }

    public bool SupervisorOverride { get; init; }

    [StringLength(1_000)]
    public string? SupervisorOverrideReason { get; init; }

    public ReceivingSessionStartInput ToInput() => new(
        WarehouseId,
        ReceivingLocationId,
        SourceType,
        PurchaseOrderId,
        AdvanceShippingNoticeId,
        DockLocationId,
        ExternalReference,
        SessionReference,
        Notes,
        SupervisorOverride,
        SupervisorOverrideReason);
}

public sealed class ReceivingScanRequest
{
    [Required, StringLength(100)]
    public string ClientOperationId { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string RawScanValue { get; init; } = string.Empty;

    [StringLength(50)]
    public string? ItemSku { get; init; }

    [Range(typeof(decimal), "0.000000000001", "79228162514264337593543950335")]
    public decimal? Quantity { get; init; }

    [StringLength(20)]
    public string? UnitOfMeasure { get; init; }

    [StringLength(40)]
    public string? PackagingCode { get; init; }

    [StringLength(100)]
    public string? LotNumber { get; init; }

    public DateTime? ExpiryDate { get; init; }
    public DateTime? ManufacturedDate { get; init; }

    [StringLength(100)]
    public string? SerialNumber { get; init; }

    [Range(1, int.MaxValue)]
    public int? LicensePlateId { get; init; }

    [StringLength(100)]
    public string? LicensePlateNumber { get; init; }

    public bool CreateLicensePlate { get; init; }

    [StringLength(50)]
    public string? DestinationLocationCode { get; init; }

    [StringLength(100)]
    public string? ReferenceNumber { get; init; }

    [StringLength(1_000)]
    public string? Notes { get; init; }

    [StringLength(1_000)]
    public string? SupervisorOverrideReason { get; init; }

    [Range(1, int.MaxValue)]
    public int? PurchaseOrderLineId { get; init; }

    [Range(1, int.MaxValue)]
    public int? AdvanceShippingNoticeLineId { get; init; }

    public ReceivingScanInput ToInput() => new(
        ClientOperationId,
        RawScanValue,
        ItemSku,
        Quantity,
        UnitOfMeasure,
        PackagingCode,
        LotNumber,
        ExpiryDate,
        ManufacturedDate,
        SerialNumber,
        LicensePlateId,
        LicensePlateNumber,
        CreateLicensePlate,
        DestinationLocationCode,
        ReferenceNumber,
        Notes,
        SupervisorOverrideReason,
        PurchaseOrderLineId,
        AdvanceShippingNoticeLineId);
}

public sealed class ReceivingSessionCompletionRequest
{
    public bool AllowPartial { get; init; }

    [StringLength(1_000)]
    public string? SupervisorOverrideReason { get; init; }
}

public sealed class ReceivingSessionCancelRequest
{
    [Required, StringLength(1_000)]
    public string Reason { get; init; } = string.Empty;
}

public sealed class ReceivingSessionCorrectionRequest
{
    [Required, StringLength(1_000)]
    public string Reason { get; init; } = string.Empty;

    [StringLength(1_000)]
    public string? Notes { get; init; }
}
