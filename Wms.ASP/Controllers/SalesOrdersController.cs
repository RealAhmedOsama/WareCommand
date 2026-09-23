using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.SalesOrders;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/sales-orders")]
[Authorize(Policy = WmsPermissions.SalesOrdersRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class SalesOrdersController(
    ISalesOrderService salesOrderService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? warehouseId = null,
        [FromQuery] int? customerId = null,
        [FromQuery] SalesOrderStatus? status = null,
        [FromQuery] string? searchTerm = null,
        [FromQuery] DateOnly? orderDateFrom = null,
        [FromQuery] DateOnly? orderDateTo = null,
        [FromQuery] bool includeCancelled = true,
        [FromQuery] SalesOrderSortField sortBy = SalesOrderSortField.DocumentNumber,
        [FromQuery] bool descending = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await salesOrderService.ListAsync(
            new SalesOrderListQuery(
                warehouseId,
                customerId,
                status,
                searchTerm,
                orderDateFrom,
                orderDateTo,
                includeCancelled,
                sortBy,
                descending,
                page,
                pageSize),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken = default)
    {
        var result = await salesOrderService.GetAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.SalesOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] SalesOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await salesOrderService.CreateAsync(
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = WmsPermissions.SalesOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] SalesOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await salesOrderService.UpdateAsync(
            id,
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:int}/confirm")]
    [Authorize(Policy = WmsPermissions.SalesOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(int id, CancellationToken cancellationToken = default) =>
        ToActionResult(await salesOrderService.ConfirmAsync(id, currentUser.RequireUserId(), cancellationToken));

    [HttpPost("{id:int}/hold")]
    [Authorize(Policy = WmsPermissions.SalesOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Hold(
        int id,
        [FromBody] SalesOrderHoldRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await salesOrderService.HoldAsync(
            id,
            request.Reason,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{id:int}/release-hold")]
    [Authorize(Policy = WmsPermissions.SalesOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReleaseHold(int id, CancellationToken cancellationToken = default) =>
        ToActionResult(await salesOrderService.ReleaseHoldAsync(id, currentUser.RequireUserId(), cancellationToken));

    [HttpPost("{id:int}/cancel")]
    [Authorize(Policy = WmsPermissions.SalesOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken = default) =>
        ToActionResult(await salesOrderService.CancelAsync(id, currentUser.RequireUserId(), cancellationToken));

    [HttpPost("{id:int}/close")]
    [Authorize(Policy = WmsPermissions.SalesOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(int id, CancellationToken cancellationToken = default) =>
        ToActionResult(await salesOrderService.CloseAsync(id, currentUser.RequireUserId(), cancellationToken));

    [HttpPost("import")]
    [Authorize(Policy = WmsPermissions.SalesOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(
        [FromBody] SalesOrderImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await salesOrderService.ImportAsync(
            request.Csv,
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] int? warehouseId = null,
        [FromQuery] int? customerId = null,
        [FromQuery] SalesOrderStatus? status = null,
        [FromQuery] string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        var result = await salesOrderService.ExportAsync(
            new SalesOrderListQuery(warehouseId, customerId, status, searchTerm),
            cancellationToken);
        return result.IsSuccess
            ? File(System.Text.Encoding.UTF8.GetBytes(result.Value), "text/csv", "sales-orders.csv")
            : ProblemFor(result.FirstError!);
    }

    private IActionResult ToActionResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : ProblemFor(result.FirstError!);

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
}

public sealed class SalesOrderRequest
{
    [Range(1, int.MaxValue)]
    public int WarehouseId { get; init; }
    [Range(1, int.MaxValue)]
    public int CustomerId { get; init; }
    [Range(1, int.MaxValue)]
    public int? ShipToAddressId { get; init; }
    [StringLength(50)]
    public string? ShipToCode { get; init; }
    public DateOnly? OrderDate { get; init; }
    public DateOnly? RequestedShipDate { get; init; }
    [StringLength(100)]
    public string? ExternalReference { get; init; }
    [StringLength(30)]
    public string SourceType { get; init; } = "MANUAL";
    [StringLength(200)]
    public string? SourceReference { get; init; }
    [Range(0, 999)]
    public int? Priority { get; init; }
    [StringLength(50)]
    public string? DefaultCarrierCode { get; init; }
    [StringLength(80)]
    public string? DefaultCarrierServiceCode { get; init; }
    public bool? AllowPartialShipment { get; init; }
    [StringLength(100)]
    public string? PackagingProfile { get; init; }
    [StringLength(100)]
    public string? LabelProfile { get; init; }
    [StringLength(2_000)]
    public string? Notes { get; init; }
    public IReadOnlyList<SalesOrderLineRequest>? Lines { get; init; }

    public SalesOrderInput ToInput() =>
        new(
            WarehouseId,
            CustomerId,
            ShipToAddressId,
            ShipToCode,
            OrderDate,
            RequestedShipDate,
            ExternalReference,
            SourceType,
            SourceReference,
            Priority,
            DefaultCarrierCode,
            DefaultCarrierServiceCode,
            AllowPartialShipment,
            PackagingProfile,
            LabelProfile,
            Notes,
            Lines?.Select(line => line.ToInput()).ToArray());
}

public sealed class SalesOrderLineRequest
{
    [Required]
    [StringLength(50)]
    public string ItemSku { get; init; } = string.Empty;
    [Wms.ASP.Validation.InvariantDecimalRange("0.000000000001", "79228162514264337593543950335")]
    public decimal OrderedQuantity { get; init; }
    [StringLength(20)]
    public string? UnitOfMeasure { get; init; }
    [StringLength(100)]
    public string? PackagingCode { get; init; }
    [StringLength(100)]
    public string? CustomerItemSku { get; init; }
    [StringLength(1_000)]
    public string? Notes { get; init; }

    public SalesOrderLineInput ToInput() =>
        new(ItemSku, OrderedQuantity, UnitOfMeasure, PackagingCode, CustomerItemSku, Notes);
}

public sealed class SalesOrderHoldRequest
{
    [Required]
    [StringLength(1_000)]
    public string Reason { get; init; } = string.Empty;
}

public sealed class SalesOrderImportRequest
{
    [Required]
    public string Csv { get; init; } = string.Empty;
}
