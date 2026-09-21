using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.WarehouseWork;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/work")]
[Authorize(Policy = WmsPermissions.WorkRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class WarehouseWorkController(
    IWarehouseWorkService warehouseWorkService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(WarehouseWorkPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? warehouseId,
        [FromQuery] WarehouseWorkType? type,
        [FromQuery] WarehouseWorkStatus? status,
        [FromQuery] string? assignedUserId,
        [FromQuery] string? queueCode,
        [FromQuery] bool includeTerminal = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await warehouseWorkService.ListAsync(
            new WarehouseWorkQuery(
                warehouseId,
                type,
                status,
                assignedUserId,
                queueCode,
                includeTerminal,
                page,
                pageSize),
            cancellationToken));

    [HttpGet("{workId:int}")]
    public async Task<IActionResult> Get(
        int workId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await warehouseWorkService.GetAsync(workId, cancellationToken));

    [HttpPost]
    [Authorize(Policy = WmsPermissions.WorkManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] WarehouseWorkCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await warehouseWorkService.CreateAsync(
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("{workId:int}/assign")]
    [Authorize(Policy = WmsPermissions.WorkManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(
        int workId,
        [FromBody] WarehouseWorkAssignmentRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await warehouseWorkService.AssignAsync(
            workId,
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{workId:int}/release")]
    [Authorize(Policy = WmsPermissions.WorkManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Release(
        int workId,
        [FromBody] WarehouseWorkCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await warehouseWorkService.ReleaseAsync(
            workId,
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{workId:int}/start")]
    [Authorize(Policy = WmsPermissions.WorkExecute)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(
        int workId,
        [FromBody] WarehouseWorkCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await warehouseWorkService.StartAsync(
            workId,
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{workId:int}/pause")]
    [Authorize(Policy = WmsPermissions.WorkExecute)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pause(
        int workId,
        [FromBody] WarehouseWorkCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await warehouseWorkService.PauseAsync(
            workId,
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{workId:int}/resume")]
    [Authorize(Policy = WmsPermissions.WorkExecute)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resume(
        int workId,
        [FromBody] WarehouseWorkCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await warehouseWorkService.ResumeAsync(
            workId,
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{workId:int}/exception")]
    [Authorize(Policy = WmsPermissions.WorkExecute)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordException(
        int workId,
        [FromBody] WarehouseWorkExceptionRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await warehouseWorkService.RecordExceptionAsync(
            workId,
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{workId:int}/cancel")]
    [Authorize(Policy = WmsPermissions.WorkManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        int workId,
        [FromBody] WarehouseWorkCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await warehouseWorkService.CancelAsync(
            workId,
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{workId:int}/complete")]
    [Authorize(Policy = WmsPermissions.WorkExecute)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(
        int workId,
        [FromBody] WarehouseWorkCompletionRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await warehouseWorkService.CompleteAsync(
            workId,
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

public sealed class WarehouseWorkCreateRequest
{
    [Required, StringLength(250)]
    public string CreationKey { get; init; } = string.Empty;

    public WarehouseWorkType Type { get; init; }

    [Range(1, int.MaxValue)]
    public int WarehouseId { get; init; }

    [Required, StringLength(100)]
    public string SourceEntityType { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string SourceEntityId { get; init; } = string.Empty;

    [Range(0, 1000)]
    public int Priority { get; init; } = 50;

    [StringLength(100)]
    public string? SourceLineReference { get; init; }

    [StringLength(50)]
    public string? QueueCode { get; init; }

    public DateTime? DueAtUtc { get; init; }

    [StringLength(50)]
    public string? TeamCode { get; init; }

    [StringLength(2_000)]
    public string? Notes { get; init; }

    public bool MakeAvailable { get; init; } = true;

    public IReadOnlyList<WarehouseWorkLineRequest> Lines { get; init; } = [];

    public WarehouseWorkInput ToInput() => new(
        CreationKey,
        Type,
        WarehouseId,
        SourceEntityType,
        SourceEntityId,
        Priority,
        SourceLineReference,
        QueueCode,
        DueAtUtc,
        TeamCode,
        Notes,
        MakeAvailable,
        Lines.Select(line => line.ToInput()).ToArray());
}

public sealed class WarehouseWorkLineRequest
{
    [Range(1, int.MaxValue)]
    public int Sequence { get; init; }

    [Range(1, int.MaxValue)]
    public int WarehouseId { get; init; }

    [Range(1, int.MaxValue)]
    public int ItemId { get; init; }

    [Range(typeof(decimal), "0.000000000001", "79228162514264337593543950335")]
    public decimal PlannedQuantity { get; init; }

    [Required, StringLength(30)]
    public string BaseUnitOfMeasure { get; init; } = string.Empty;

    public int? SourceLocationId { get; init; }
    public int? DestinationLocationId { get; init; }
    public int? LotId { get; init; }
    public int? SerialNumberId { get; init; }

    [StringLength(100)]
    public string? SerialNumber { get; init; }

    public int? LicensePlateId { get; init; }
    public int? InventoryStatusId { get; init; }

    [StringLength(200)]
    public string? SourceReference { get; init; }

    [StringLength(4_000)]
    public string? DimensionsSnapshot { get; init; }

    public WarehouseWorkLineInput ToInput() => new(
        Sequence,
        WarehouseId,
        ItemId,
        PlannedQuantity,
        BaseUnitOfMeasure,
        SourceLocationId,
        DestinationLocationId,
        LotId,
        SerialNumberId,
        SerialNumber,
        LicensePlateId,
        InventoryStatusId,
        SourceReference,
        DimensionsSnapshot);
}

public sealed class WarehouseWorkAssignmentRequest
{
    [StringLength(450)]
    public string? UserId { get; init; }

    [StringLength(50)]
    public string? TeamCode { get; init; }

    [Required, StringLength(200)]
    public string IdempotencyKey { get; init; } = string.Empty;

    public bool SupervisorOverride { get; init; }

    [StringLength(1_000)]
    public string? OverrideReason { get; init; }

    public WarehouseWorkAssignmentInput ToInput() => new(
        UserId,
        TeamCode,
        IdempotencyKey,
        SupervisorOverride,
        OverrideReason);
}

public sealed class WarehouseWorkCommandRequest
{
    [Required, StringLength(200)]
    public string IdempotencyKey { get; init; } = string.Empty;

    [StringLength(1_000)]
    public string? Reason { get; init; }

    public WarehouseWorkCommandInput ToInput() => new(IdempotencyKey, Reason);
}

public sealed class WarehouseWorkExceptionRequest
{
    public WarehouseWorkExceptionType ExceptionType { get; init; }

    [Required, StringLength(1_000)]
    public string Reason { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string IdempotencyKey { get; init; } = string.Empty;

    public WarehouseWorkExceptionInput ToInput() => new(ExceptionType, Reason, IdempotencyKey);
}

public sealed class WarehouseWorkCompletionRequest
{
    [Required, StringLength(200)]
    public string IdempotencyKey { get; init; } = string.Empty;

    public IReadOnlyList<WarehouseWorkLineActualRequest>? Lines { get; init; }

    public IReadOnlyList<WarehouseWorkScanRequest>? Scans { get; init; }

    public bool SupervisorOverride { get; init; }

    [StringLength(1_000)]
    public string? OverrideReason { get; init; }

    [StringLength(200)]
    public string? CompletionReference { get; init; }

    public WarehouseWorkCompletionInput ToInput() => new(
        IdempotencyKey,
        Lines?.Select(line => line.ToInput()).ToArray(),
        SupervisorOverride,
        OverrideReason,
        CompletionReference,
        Scans?.Select(scan => scan.ToInput()).ToArray());
}

public sealed class WarehouseWorkLineActualRequest
{
    [Range(1, int.MaxValue)]
    public int LineId { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal ActualQuantity { get; init; }

    public WarehouseWorkLineActualInput ToInput() => new(LineId, ActualQuantity);
}

public sealed class WarehouseWorkScanRequest
{
    [Range(1, int.MaxValue)]
    public int LineId { get; init; }

    [Range(1, int.MaxValue)]
    public int ItemId { get; init; }

    [Range(1, int.MaxValue)]
    public int SourceLocationId { get; init; }

    [Range(1, int.MaxValue)]
    public int DestinationLocationId { get; init; }

    [Range(typeof(decimal), "0.000000000001", "79228162514264337593543950335")]
    public decimal ActualQuantity { get; init; }

    public int? LicensePlateId { get; init; }

    public bool DestinationOverride { get; init; }

    public WarehouseWorkScanInput ToInput() => new(
        LineId,
        ItemId,
        SourceLocationId,
        DestinationLocationId,
        ActualQuantity,
        LicensePlateId,
        DestinationOverride);
}
