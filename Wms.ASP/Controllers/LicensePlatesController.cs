using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.LicensePlates;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/license-plates")]
[Authorize(Policy = WmsPermissions.InventoryRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class LicensePlatesController(
    ILicensePlateService licensePlateService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<LicensePlateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] int? warehouseId,
        [FromQuery] string? number,
        [FromQuery] LicensePlateStatus? status,
        [FromQuery] bool includeVoided = false,
        [FromQuery, Range(1, 200)] int pageSize = 50,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        CancellationToken cancellationToken = default)
    {
        var result = await licensePlateService.SearchAsync(
            new LicensePlateSearchQuery(warehouseId, number, status, includeVoided, page, pageSize),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{licensePlateId:int}")]
    [ProducesResponseType(typeof(LicensePlateDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int licensePlateId, CancellationToken cancellationToken = default)
    {
        var result = await licensePlateService.GetAsync(licensePlateId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{licensePlateId:int}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<LicensePlateHistoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> History(
        int licensePlateId,
        [FromQuery, Range(1, 200)] int pageSize = 50,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        CancellationToken cancellationToken = default)
    {
        var result = await licensePlateService.GetHistoryAsync(
            licensePlateId,
            page,
            pageSize,
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] LicensePlateCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await licensePlateService.CreateAsync(
            new LicensePlateCreateInput(
                request.WarehouseId,
                request.CurrentLocationId,
                request.Number,
                request.IsSscc,
                request.Type,
                request.GrossWeightKg,
                request.LengthCm,
                request.WidthCm,
                request.HeightCm,
                request.SourceReference,
                request.Notes),
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{licensePlateId:int}/contents")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddContent(
        int licensePlateId,
        [FromBody] LicensePlateContentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await licensePlateService.AddContentAsync(
            licensePlateId,
            ToContentInput(request),
            currentUser.RequireUserId(),
            request.ReferenceNumber,
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("contents/move")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MoveContent(
        [FromBody] LicensePlateMoveContentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await licensePlateService.MoveContentAsync(
            request.SourceLicensePlateId,
            request.TargetLicensePlateId,
            ToContentInput(request),
            currentUser.RequireUserId(),
            request.ReferenceNumber,
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{licensePlateId:int}/contents/split")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SplitContent(
        int licensePlateId,
        [FromBody] LicensePlateSplitRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await licensePlateService.SplitContentAsync(
            licensePlateId,
            ToContentInput(request),
            ToCreateInput(request.Destination),
            currentUser.RequireUserId(),
            request.ReferenceNumber,
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{sourceLicensePlateId:int}/merge/{targetLicensePlateId:int}")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Merge(
        int sourceLicensePlateId,
        int targetLicensePlateId,
        [FromBody] LicensePlateReferenceRequest? request,
        CancellationToken cancellationToken = default)
    {
        var result = await licensePlateService.MergeAsync(
            sourceLicensePlateId,
            targetLicensePlateId,
            currentUser.RequireUserId(),
            request?.ReferenceNumber,
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{licensePlateId:int}/move")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(
        int licensePlateId,
        [FromBody] LicensePlateMoveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await licensePlateService.MoveAsync(
            licensePlateId,
            request.TargetLocationId,
            currentUser.RequireUserId(),
            request.ReferenceNumber,
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{childLicensePlateId:int}/nest/{parentLicensePlateId:int}")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Nest(
        int childLicensePlateId,
        int parentLicensePlateId,
        CancellationToken cancellationToken = default)
    {
        var result = await licensePlateService.NestAsync(
            childLicensePlateId,
            parentLicensePlateId,
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{childLicensePlateId:int}/unnest")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unnest(
        int childLicensePlateId,
        CancellationToken cancellationToken = default)
    {
        var result = await licensePlateService.UnnestAsync(
            childLicensePlateId,
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{licensePlateId:int}/pack")]
    [Authorize(Policy = WmsPermissions.PackingExecute)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pack(
        int licensePlateId,
        [FromBody] LicensePlateReferenceRequest? request,
        CancellationToken cancellationToken = default)
    {
        var result = await licensePlateService.PackAsync(
            licensePlateId,
            currentUser.RequireUserId(),
            request?.ReferenceNumber,
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{licensePlateId:int}/reopen")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(
        int licensePlateId,
        [FromBody] LicensePlateReasonRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await licensePlateService.ReopenAsync(
            licensePlateId,
            currentUser.RequireUserId(),
            request.Reason,
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{licensePlateId:int}/ship")]
    [Authorize(Policy = WmsPermissions.ShippingExecute)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ship(
        int licensePlateId,
        [FromBody] LicensePlateReferenceRequest? request,
        CancellationToken cancellationToken = default)
    {
        var result = await licensePlateService.ShipAsync(
            licensePlateId,
            currentUser.RequireUserId(),
            request?.ReferenceNumber,
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{licensePlateId:int}/return")]
    [Authorize(Policy = WmsPermissions.ReceivingExecute)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(
        int licensePlateId,
        [FromBody] LicensePlateReturnRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await licensePlateService.ReturnAsync(
            licensePlateId,
            request.TargetLocationId,
            currentUser.RequireUserId(),
            request.ReferenceNumber,
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{licensePlateId:int}/void")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Void(
        int licensePlateId,
        [FromBody] LicensePlateReasonRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await licensePlateService.VoidAsync(
            licensePlateId,
            currentUser.RequireUserId(),
            request.Reason,
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPut("numbering")]
    [Authorize(Policy = WmsPermissions.SettingsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfigureNumbering(
        [FromBody] LicensePlateNumberingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await licensePlateService.ConfigureNumberingAsync(
            new LicensePlateNumberingInput(
                request.WarehouseId,
                request.Prefix,
                request.Padding,
                request.NextNumber),
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    private static LicensePlateCreateInput ToCreateInput(LicensePlateCreateRequest request) =>
        new(
            request.WarehouseId,
            request.CurrentLocationId,
            request.Number,
            request.IsSscc,
            request.Type,
            request.GrossWeightKg,
            request.LengthCm,
            request.WidthCm,
            request.HeightCm,
            request.SourceReference,
            request.Notes);

    private static LicensePlateContentInput ToContentInput(LicensePlateContentRequest request) =>
        new(
            request.ItemId,
            request.Quantity,
            request.LotId,
            request.SerialNumberId,
            request.InventoryStatusId,
            request.ItemPackagingId,
            request.OwnerKind,
            request.InventoryOwnerId,
            request.OwnerCodeSnapshot);

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

public sealed class LicensePlateCreateRequest
{
    [Range(1, int.MaxValue)]
    public int WarehouseId { get; init; }

    [Range(1, int.MaxValue)]
    public int? CurrentLocationId { get; init; }

    [StringLength(100)]
    public string? Number { get; init; }

    public bool IsSscc { get; init; }

    public LicensePlateType Type { get; init; } = LicensePlateType.Pallet;

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? GrossWeightKg { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? LengthCm { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? WidthCm { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? HeightCm { get; init; }

    [StringLength(100)]
    public string? SourceReference { get; init; }

    [StringLength(1_000)]
    public string? Notes { get; init; }
}

public class LicensePlateContentRequest
{
    [Range(1, int.MaxValue)]
    public int ItemId { get; init; }

    [Wms.ASP.Validation.InvariantDecimalRange("0.000000000001", "79228162514264337593543950335")]
    public decimal Quantity { get; init; }

    [Range(1, int.MaxValue)]
    public int? LotId { get; init; }

    [Range(1, int.MaxValue)]
    public int? SerialNumberId { get; init; }

    [Range(1, int.MaxValue)]
    public int InventoryStatusId { get; init; } = InventoryStatusSystemIds.Available;

    [Range(1, int.MaxValue)]
    public int? ItemPackagingId { get; init; }

    public InventoryOwnerKind OwnerKind { get; init; } = InventoryOwnerKind.CompanyOwned;

    [Range(1, int.MaxValue)]
    public int? InventoryOwnerId { get; init; }

    [StringLength(80)]
    public string? OwnerCodeSnapshot { get; init; }

    [StringLength(100)]
    public string? ReferenceNumber { get; init; }
}

public sealed class LicensePlateMoveContentRequest : LicensePlateContentRequest
{
    [Range(1, int.MaxValue)]
    public int SourceLicensePlateId { get; init; }

    [Range(1, int.MaxValue)]
    public int TargetLicensePlateId { get; init; }
}

public sealed class LicensePlateSplitRequest : LicensePlateContentRequest
{
    [Required]
    public LicensePlateCreateRequest Destination { get; init; } = new();
}

public sealed class LicensePlateMoveRequest
{
    [Range(1, int.MaxValue)]
    public int TargetLocationId { get; init; }

    [StringLength(100)]
    public string? ReferenceNumber { get; init; }
}

public sealed class LicensePlateReturnRequest
{
    [Range(1, int.MaxValue)]
    public int TargetLocationId { get; init; }

    [StringLength(100)]
    public string? ReferenceNumber { get; init; }
}

public sealed class LicensePlateReasonRequest
{
    [Required]
    [StringLength(1_000)]
    public string Reason { get; init; } = string.Empty;
}

public sealed class LicensePlateReferenceRequest
{
    [StringLength(100)]
    public string? ReferenceNumber { get; init; }
}

public sealed class LicensePlateNumberingRequest
{
    [Range(1, int.MaxValue)]
    public int WarehouseId { get; init; }

    [Required]
    [StringLength(20)]
    public string Prefix { get; init; } = string.Empty;

    [Range(1, 20)]
    public int Padding { get; init; } = 8;

    [Range(1, long.MaxValue)]
    public long NextNumber { get; init; } = 1;
}
