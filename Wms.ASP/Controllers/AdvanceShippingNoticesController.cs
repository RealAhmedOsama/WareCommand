using System.ComponentModel.DataAnnotations;
using System.Text;
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
[Route("api/advance-shipping-notices")]
[Authorize(Policy = WmsPermissions.AdvanceShippingNoticesRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class AdvanceShippingNoticesController(
    IAdvanceShippingNoticeService advanceShippingNoticeService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(AdvanceShippingNoticePageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? warehouseId,
        [FromQuery] int? supplierId,
        [FromQuery] AdvanceShippingNoticeStatus? status,
        [FromQuery] string? searchTerm,
        [FromQuery] DateTime? expectedArrivalFromUtc,
        [FromQuery] DateTime? expectedArrivalToUtc,
        [FromQuery] bool includeCancelled = true,
        [FromQuery] AdvanceShippingNoticeSortField sortBy = AdvanceShippingNoticeSortField.ExpectedArrivalFromUtc,
        [FromQuery] bool descending = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await advanceShippingNoticeService.ListAsync(
            new AdvanceShippingNoticeListQuery(
                warehouseId,
                supplierId,
                status,
                searchTerm,
                expectedArrivalFromUtc,
                expectedArrivalToUtc,
                includeCancelled,
                sortBy,
                descending,
                page,
                pageSize),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken = default) =>
        ToActionResult(await advanceShippingNoticeService.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] AdvanceShippingNoticeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await advanceShippingNoticeService.CreateAsync(
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] AdvanceShippingNoticeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await advanceShippingNoticeService.UpdateAsync(
            id,
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("{id:int}/submit")]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id, CancellationToken cancellationToken = default) =>
        ToActionResult(await advanceShippingNoticeService.SubmitAsync(
            id,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{id:int}/expected")]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkExpected(int id, CancellationToken cancellationToken = default) =>
        ToActionResult(await advanceShippingNoticeService.MarkExpectedAsync(
            id,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{id:int}/dock")]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignDock(
        int id,
        [FromBody] DockAssignmentRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await advanceShippingNoticeService.AssignDockAsync(
            id,
            request.DockLocationId,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{id:int}/arrive")]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Arrive(
        int id,
        [FromBody] DockAssignmentRequest? request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await advanceShippingNoticeService.ArriveAsync(
            id,
            request?.DockLocationId,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{id:int}/exception")]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkException(int id, CancellationToken cancellationToken = default) =>
        ToActionResult(await advanceShippingNoticeService.MarkExceptionAsync(
            id,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{id:int}/complete")]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int id, CancellationToken cancellationToken = default) =>
        ToActionResult(await advanceShippingNoticeService.CompleteAsync(
            id,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{id:int}/cancel")]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken = default) =>
        ToActionResult(await advanceShippingNoticeService.CancelAsync(
            id,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("import")]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [EnableRateLimiting(WmsRateLimitPolicies.Import)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(
        [FromBody] AdvanceShippingNoticeImportRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await advanceShippingNoticeService.ImportAsync(
            request.Csv,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpGet("export")]
    [EnableRateLimiting(WmsRateLimitPolicies.Import)]
    public async Task<IActionResult> Export(
        [FromQuery] int? warehouseId,
        [FromQuery] int? supplierId,
        [FromQuery] AdvanceShippingNoticeStatus? status,
        [FromQuery] string? searchTerm,
        CancellationToken cancellationToken = default)
    {
        var result = await advanceShippingNoticeService.ExportAsync(
            new AdvanceShippingNoticeListQuery(
                warehouseId,
                supplierId,
                status,
                searchTerm,
                PageSize: 200),
            cancellationToken);
        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(result.Value))
            .ToArray();
        return File(bytes, "text/csv", "advance-shipping-notices.csv");
    }

    [HttpPost("{id:int}/discrepancies")]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDiscrepancy(
        int id,
        [FromBody] AdvanceShippingNoticeDiscrepancyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await advanceShippingNoticeService.AddDiscrepancyAsync(
            id,
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

        return ProblemFor(result.FirstError!);
    }

    private ObjectResult ProblemFor(ResultError error)
    {
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

    private IActionResult ToActionResult(Result result)
    {
        if (result.IsSuccess)
        {
            return NoContent();
        }

        return ProblemFor(result.FirstError!);
    }
}

public sealed class AdvanceShippingNoticeRequest
{
    [Range(1, int.MaxValue)]
    public int WarehouseId { get; init; }

    [Range(1, int.MaxValue)]
    public int SupplierId { get; init; }

    [StringLength(200)]
    public string? CarrierName { get; init; }

    public DateTime? ExpectedArrivalFromUtc { get; init; }
    public DateTime? ExpectedArrivalToUtc { get; init; }

    [StringLength(100)]
    public string? VehicleNumber { get; init; }

    [StringLength(100)]
    public string? TrailerNumber { get; init; }

    [StringLength(100)]
    public string? ContainerNumber { get; init; }

    [StringLength(100)]
    public string? TrackingReference { get; init; }

    [StringLength(100)]
    public string? ExternalReference { get; init; }

    [StringLength(30)]
    public string SourceType { get; init; } = "MANUAL";

    [StringLength(200)]
    public string? SourceReference { get; init; }

    [StringLength(8_000)]
    public string? SourcePayload { get; init; }

    [Range(1, int.MaxValue)]
    public int? DockLocationId { get; init; }

    [StringLength(2_000)]
    public string? Notes { get; init; }

    public bool AllowMultiplePurchaseOrders { get; init; } = true;

    [Required]
    [MinLength(1)]
    public IReadOnlyList<AdvanceShippingNoticeLineRequest> Lines { get; init; } = [];

    public AdvanceShippingNoticeInput ToInput() => new(
        WarehouseId,
        SupplierId,
        CarrierName,
        ExpectedArrivalFromUtc,
        ExpectedArrivalToUtc,
        VehicleNumber,
        TrailerNumber,
        ContainerNumber,
        TrackingReference,
        ExternalReference,
        SourceType,
        SourceReference,
        SourcePayload,
        DockLocationId,
        Notes,
        AllowMultiplePurchaseOrders,
        Lines.Select(line => line.ToInput()).ToArray());
}

public sealed class AdvanceShippingNoticeLineRequest
{
    [Required]
    [StringLength(50)]
    public string ItemSku { get; init; } = string.Empty;

    [Range(typeof(decimal), "0.000000000001", "79228162514264337593543950335")]
    public decimal ExpectedQuantity { get; init; }

    [StringLength(20)]
    public string? UnitOfMeasure { get; init; }

    [StringLength(40)]
    public string? PackagingCode { get; init; }

    [Range(1, int.MaxValue)]
    public int? PurchaseOrderId { get; init; }

    [Range(1, int.MaxValue)]
    public int? PurchaseOrderLineId { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public decimal? OverDeliveryTolerancePercent { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public decimal? UnderDeliveryTolerancePercent { get; init; }

    [StringLength(100)]
    public string? PreAdvisedLotNumber { get; init; }

    public DateTime? PreAdvisedExpiryDate { get; init; }

    [StringLength(100)]
    public string? PreAdvisedSerialNumber { get; init; }

    [StringLength(100)]
    public string? ExpectedLicensePlateNumber { get; init; }

    public bool ExpectedLicensePlateIsSscc { get; init; }

    [StringLength(1_000)]
    public string? Notes { get; init; }

    public AdvanceShippingNoticeLineInput ToInput() => new(
        ItemSku,
        ExpectedQuantity,
        UnitOfMeasure,
        PackagingCode,
        PurchaseOrderId,
        PurchaseOrderLineId,
        OverDeliveryTolerancePercent,
        UnderDeliveryTolerancePercent,
        PreAdvisedLotNumber,
        PreAdvisedExpiryDate,
        PreAdvisedSerialNumber,
        ExpectedLicensePlateNumber,
        ExpectedLicensePlateIsSscc,
        Notes);
}

public sealed class AdvanceShippingNoticeImportRequest
{
    [Required]
    public string Csv { get; init; } = string.Empty;
}

public sealed class DockAssignmentRequest
{
    [Range(1, int.MaxValue)]
    public int? DockLocationId { get; init; }
}

public sealed class AdvanceShippingNoticeDiscrepancyRequest
{
    [Range(1, int.MaxValue)]
    public int? AdvanceShippingNoticeLineId { get; init; }

    [Required]
    public AdvanceShippingNoticeDiscrepancyKind Kind { get; init; }

    public decimal? ExpectedBaseQuantity { get; init; }
    public decimal? ReceivedBaseQuantity { get; init; }
    public decimal? VarianceBaseQuantity { get; init; }

    [Required]
    [StringLength(2_000)]
    public string Details { get; init; } = string.Empty;

    public AdvanceShippingNoticeDiscrepancyInput ToInput() => new(
        AdvanceShippingNoticeLineId,
        Kind,
        ExpectedBaseQuantity,
        ReceivedBaseQuantity,
        VarianceBaseQuantity,
        Details);
}
