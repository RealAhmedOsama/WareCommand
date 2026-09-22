using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Purchasing;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/purchase-orders")]
[Authorize(Policy = WmsPermissions.PurchaseOrdersRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class PurchaseOrdersController(
    IPurchaseOrderService purchaseOrderService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PurchaseOrderPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? warehouseId,
        [FromQuery] int? supplierId,
        [FromQuery] PurchaseOrderStatus? status,
        [FromQuery] string? searchTerm,
        [FromQuery] DateOnly? orderDateFrom,
        [FromQuery] DateOnly? orderDateTo,
        [FromQuery] bool includeCancelled = true,
        [FromQuery] PurchaseOrderSortField sortBy = PurchaseOrderSortField.DocumentNumber,
        [FromQuery] bool descending = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await purchaseOrderService.ListAsync(
            new PurchaseOrderListQuery(
                warehouseId,
                supplierId,
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
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken = default) =>
        ToActionResult(await purchaseOrderService.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = WmsPermissions.PurchaseOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] PurchaseOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await purchaseOrderService.CreateAsync(
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = WmsPermissions.PurchaseOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] PurchaseOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await purchaseOrderService.UpdateAsync(
            id,
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("{id:int}/confirm")]
    [Authorize(Policy = WmsPermissions.PurchaseOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(int id, CancellationToken cancellationToken = default) =>
        ToActionResult(await purchaseOrderService.ConfirmAsync(
            id,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{id:int}/cancel")]
    [Authorize(Policy = WmsPermissions.PurchaseOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken = default) =>
        ToActionResult(await purchaseOrderService.CancelAsync(
            id,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{id:int}/close")]
    [Authorize(Policy = WmsPermissions.PurchaseOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(int id, CancellationToken cancellationToken = default) =>
        ToActionResult(await purchaseOrderService.CloseAsync(
            id,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{id:int}/reopen")]
    [Authorize(Policy = WmsPermissions.PurchaseOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(int id, CancellationToken cancellationToken = default) =>
        ToActionResult(await purchaseOrderService.ReopenAsync(
            id,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("import")]
    [Authorize(Policy = WmsPermissions.PurchaseOrdersManage)]
    [EnableRateLimiting(WmsRateLimitPolicies.Import)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(
        [FromBody] PurchaseOrderImportRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await purchaseOrderService.ImportAsync(
            request.Csv,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpGet("export")]
    [EnableRateLimiting(WmsRateLimitPolicies.Import)]
    public async Task<IActionResult> Export(
        [FromQuery] int? warehouseId,
        [FromQuery] int? supplierId,
        [FromQuery] PurchaseOrderStatus? status,
        [FromQuery] string? searchTerm,
        CancellationToken cancellationToken = default)
    {
        var result = await purchaseOrderService.ExportAsync(
            new PurchaseOrderListQuery(
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
        return File(bytes, "text/csv", "purchase-orders.csv");
    }

    private IActionResult ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return ProblemFor(result.FirstError!);
    }

    private IActionResult ToActionResult(Result result)
    {
        if (result.IsSuccess)
        {
            return NoContent();
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
}

public sealed class PurchaseOrderRequest
{
    [Range(1, int.MaxValue)]
    public int WarehouseId { get; init; }

    [Range(1, int.MaxValue)]
    public int SupplierId { get; init; }

    public DateOnly OrderDate { get; init; }
    public DateOnly? ExpectedReceiptDate { get; init; }

    [StringLength(100)]
    public string? ExternalReference { get; init; }

    [StringLength(30)]
    public string SourceType { get; init; } = "MANUAL";

    [StringLength(200)]
    public string? SourceReference { get; init; }

    [StringLength(3)]
    public string? CurrencyCode { get; init; }

    [StringLength(2_000)]
    public string? Notes { get; init; }

    [Required]
    [MinLength(1)]
    public IReadOnlyList<PurchaseOrderLineRequest> Lines { get; init; } = [];

    public PurchaseOrderInput ToInput() => new(
        WarehouseId,
        SupplierId,
        OrderDate,
        ExpectedReceiptDate,
        ExternalReference,
        SourceType,
        SourceReference,
        CurrencyCode,
        Notes,
        Lines.Select(line => line.ToInput()).ToArray());
}

public sealed class PurchaseOrderLineRequest
{
    [Required]
    [StringLength(50)]
    public string ItemSku { get; init; } = string.Empty;

    [Wms.ASP.Validation.InvariantDecimalRange("0.000000000001", "79228162514264337593543950335")]
    public decimal OrderedQuantity { get; init; }

    [StringLength(20)]
    public string? UnitOfMeasure { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public decimal? OverDeliveryTolerancePercent { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public decimal? UnderDeliveryTolerancePercent { get; init; }

    [StringLength(100)]
    public string? SupplierItemReference { get; init; }

    [StringLength(1_000)]
    public string? Notes { get; init; }

    public PurchaseOrderLineInput ToInput() => new(
        ItemSku,
        OrderedQuantity,
        UnitOfMeasure,
        OverDeliveryTolerancePercent,
        UnderDeliveryTolerancePercent,
        SupplierItemReference,
        Notes);
}

public sealed class PurchaseOrderImportRequest
{
    [Required]
    public string Csv { get; init; } = string.Empty;
}
