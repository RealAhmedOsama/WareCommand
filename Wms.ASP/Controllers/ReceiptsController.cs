using System.ComponentModel.DataAnnotations;
using System.Text;
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
[Route("api/receipts")]
[Authorize(Policy = WmsPermissions.ReceiptsRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class ReceiptsController(
    IReceiptService receiptService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ReceiptPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int? warehouseId,
        [FromQuery] int? supplierId,
        [FromQuery] ReceiptStatus? status,
        [FromQuery] string? searchTerm,
        [FromQuery] DateTime? receivedFromUtc,
        [FromQuery] DateTime? receivedToUtc,
        [FromQuery] bool includeCancelled = true,
        [FromQuery] ReceiptSortField sortBy = ReceiptSortField.ReceivedAtUtc,
        [FromQuery] bool descending = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        return ToActionResult(await receiptService.ListAsync(
            new ReceiptListQuery(
                warehouseId,
                supplierId,
                status,
                searchTerm,
                receivedFromUtc,
                receivedToUtc,
                includeCancelled,
                sortBy,
                descending,
                page,
                pageSize),
            cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken = default) =>
        ToActionResult(await receiptService.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = WmsPermissions.ReceiptsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] ReceiptRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await receiptService.CreateAsync(
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("{id:int}/complete")]
    [Authorize(Policy = WmsPermissions.ReceiptsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int id, CancellationToken cancellationToken = default) =>
        ToActionResult(await receiptService.CompleteAsync(id, currentUser.RequireUserId(), cancellationToken));

    [HttpPost("{id:int}/cancel")]
    [Authorize(Policy = WmsPermissions.ReceiptsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken = default) =>
        ToActionResult(await receiptService.CancelAsync(id, currentUser.RequireUserId(), cancellationToken));

    [HttpPost("{id:int}/reverse")]
    [Authorize(Policy = WmsPermissions.ReceiptsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reverse(
        int id,
        [FromBody] ReceiptReasonRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await receiptService.ReverseAsync(
            id,
            currentUser.RequireUserId(),
            request.Reason,
            cancellationToken));

    [HttpPost("{id:int}/correct")]
    [Authorize(Policy = WmsPermissions.ReceiptsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Correct(
        int id,
        [FromBody] ReceiptCorrectionRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await receiptService.CorrectAsync(
            id,
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("lines/{receiptLineId:int}/links")]
    [Authorize(Policy = WmsPermissions.ReceiptsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddLink(
        int receiptLineId,
        [FromBody] ReceiptLineLinkRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await receiptService.AddLinkAsync(
            receiptLineId,
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpGet("export")]
    [EnableRateLimiting(WmsRateLimitPolicies.Import)]
    public async Task<IActionResult> Export(
        [FromQuery] int? warehouseId,
        [FromQuery] int? supplierId,
        [FromQuery] ReceiptStatus? status,
        [FromQuery] string? searchTerm,
        CancellationToken cancellationToken = default)
    {
        var result = await receiptService.ExportAsync(
            new ReceiptListQuery(warehouseId, supplierId, status, searchTerm, PageSize: 200),
            cancellationToken);
        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(result.Value))
            .ToArray();
        return File(bytes, "text/csv", "receipts.csv");
    }

    [HttpGet("{id:int}/print")]
    public async Task<IActionResult> Print(int id, CancellationToken cancellationToken = default)
    {
        var result = await receiptService.GetAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        var receipt = result.Value;
        var lines = receipt.Lines.Select(line =>
            $"{line.ItemSku}\t{line.ReceivedBaseQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture)}\t{line.AcceptedBaseQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        return Content(
            $"Receipt {receipt.DocumentNumber}\nStatus: {receipt.Status}\nWarehouse: {receipt.WarehouseCode}\n\nItem\tReceived\tAccepted\n{string.Join('\n', lines)}",
            "text/plain",
            Encoding.UTF8);
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

public sealed class ReceiptRequest
{
    [Range(1, int.MaxValue)]
    public int WarehouseId { get; init; }

    [Range(1, int.MaxValue)]
    public int ReceivingLocationId { get; init; }

    [Range(1, int.MaxValue)]
    public int? SupplierId { get; init; }

    [Range(1, int.MaxValue)]
    public int? PurchaseOrderId { get; init; }

    [Range(1, int.MaxValue)]
    public int? AdvanceShippingNoticeId { get; init; }

    [Range(1, int.MaxValue)]
    public int? DockLocationId { get; init; }

    [StringLength(30)]
    public string SourceType { get; init; } = "MANUAL";

    [StringLength(200)]
    public string? SourceReference { get; init; }

    [StringLength(100)]
    public string? ExternalReference { get; init; }

    [StringLength(100)]
    public string? SessionReference { get; init; }

    [StringLength(2_000)]
    public string? Notes { get; init; }

    [Required, MinLength(1)]
    public IReadOnlyList<ReceiptLineRequest> Lines { get; init; } = [];

    public ReceiptInput ToInput() => new(
        WarehouseId,
        ReceivingLocationId,
        SupplierId,
        PurchaseOrderId,
        AdvanceShippingNoticeId,
        DockLocationId,
        SourceType,
        SourceReference,
        ExternalReference,
        SessionReference,
        Notes,
        Lines.Select(line => line.ToInput()).ToArray());
}

public sealed class ReceiptLineRequest
{
    [Required, StringLength(50)]
    public string ItemSku { get; init; } = string.Empty;

    [Range(typeof(decimal), "0.000000000001", "79228162514264337593543950335")]
    public decimal Quantity { get; init; }

    [StringLength(20)]
    public string? UnitOfMeasure { get; init; }

    [StringLength(40)]
    public string? PackagingCode { get; init; }

    [Range(1, int.MaxValue)]
    public int? PurchaseOrderId { get; init; }

    [Range(1, int.MaxValue)]
    public int? PurchaseOrderLineId { get; init; }

    [Range(1, int.MaxValue)]
    public int? AdvanceShippingNoticeId { get; init; }

    [Range(1, int.MaxValue)]
    public int? AdvanceShippingNoticeLineId { get; init; }

    [StringLength(100)]
    public string? LotNumber { get; init; }

    public DateTime? ExpiryDate { get; init; }

    [StringLength(100)]
    public string? SerialNumber { get; init; }

    [Range(1, int.MaxValue)]
    public int? LicensePlateId { get; init; }

    public decimal? AcceptedQuantity { get; init; }
    public decimal? RejectedQuantity { get; init; }
    public decimal? DamagedQuantity { get; init; }
    public decimal? QuarantinedQuantity { get; init; }

    [StringLength(1_000)]
    public string? Notes { get; init; }

    public ReceiptLineInput ToInput() => new(
        ItemSku,
        Quantity,
        UnitOfMeasure,
        PackagingCode,
        PurchaseOrderId,
        PurchaseOrderLineId,
        AdvanceShippingNoticeId,
        AdvanceShippingNoticeLineId,
        LotNumber,
        ExpiryDate,
        SerialNumber,
        LicensePlateId,
        AcceptedQuantity,
        RejectedQuantity,
        DamagedQuantity,
        QuarantinedQuantity,
        Notes);
}

public sealed class ReceiptReasonRequest
{
    [Required, StringLength(2_000)]
    public string Reason { get; init; } = string.Empty;
}

public sealed class ReceiptCorrectionRequest
{
    [StringLength(2_000)]
    public string? Reason { get; init; }

    [StringLength(2_000)]
    public string? Notes { get; init; }

    public ReceiptCorrectionInput ToInput() => new(Reason, Notes);
}

public sealed class ReceiptLineLinkRequest
{
    [Required]
    public ReceiptLineLinkType Type { get; init; }

    [Required, StringLength(200)]
    public string Reference { get; init; } = string.Empty;

    [StringLength(1_000)]
    public string? Notes { get; init; }

    public ReceiptLineLinkInput ToInput() => new(Type, Reference, Notes);
}
