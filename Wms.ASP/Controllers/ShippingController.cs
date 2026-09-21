using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Shipping;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/shipping")]
[Authorize(Policy = WmsPermissions.ShippingExecute)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class ShippingController(
    IShipmentService shipmentService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpPost("carriers")]
    [Authorize(Policy = WmsPermissions.SettingsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCarrier(
        [FromBody] CarrierRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await shipmentService.CreateCarrierAsync(
            new CarrierInput(request.Code, request.Name, request.TrackingUrlTemplate),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("carriers/{carrierId:int}/services")]
    [Authorize(Policy = WmsPermissions.SettingsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCarrierService(
        int carrierId,
        [FromBody] CarrierServiceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await shipmentService.CreateCarrierServiceAsync(
            new CarrierServiceInput(
                carrierId,
                request.Code,
                request.Name,
                request.SupportsTracking,
                request.SupportsLabel),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("shipments")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateShipment(
        [FromBody] ShipmentCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await shipmentService.CreateShipmentAsync(
            new ShipmentCreateInput(
                request.ShipmentNumber,
                request.WarehouseId,
                request.ShipmentPackageIds,
                request.CarrierId,
                request.CarrierServiceId,
                request.PlannedShipAtUtc,
                request.ExternalReference,
                request.IdempotencyKey),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpGet("shipments/{shipmentId:int}")]
    public async Task<IActionResult> GetShipment(
        int shipmentId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await shipmentService.GetShipmentAsync(shipmentId, cancellationToken));

    [HttpPost("shipments/{shipmentId:int}/loads")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OpenLoad(
        int shipmentId,
        [FromBody] ShipmentLoadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await shipmentService.OpenLoadAsync(
            new ShipmentLoadInput(
                shipmentId,
                request.DockLocationId,
                request.TrailerNumber,
                request.RouteReference,
                request.IdempotencyKey),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("shipments/{shipmentId:int}/loads/{loadId:int}/packages/{packageId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoadPackage(
        int shipmentId,
        int loadId,
        int packageId,
        [FromBody] ShipmentPackageScanRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await shipmentService.LoadPackageAsync(
            new ShipmentPackageScanInput(
                shipmentId,
                packageId,
                loadId,
                request.IdempotencyKey),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("shipments/{shipmentId:int}/loads/{loadId:int}/packages/{packageId:int}/unload")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnloadPackage(
        int shipmentId,
        int loadId,
        int packageId,
        [FromBody] ShipmentPackageScanRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await shipmentService.UnloadPackageAsync(
            new ShipmentPackageScanInput(
                shipmentId,
                packageId,
                loadId,
                request.IdempotencyKey),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("shipments/{shipmentId:int}/confirm")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(
        int shipmentId,
        [FromBody] ShipmentCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await shipmentService.ConfirmShipmentAsync(
            new ShipmentCommandInput(shipmentId, request.IdempotencyKey, request.Reason),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("shipments/{shipmentId:int}/cancel")]
    [Authorize(Policy = WmsPermissions.ShippingExecute)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        int shipmentId,
        [FromBody] ShipmentCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await shipmentService.CancelShipmentAsync(
            new ShipmentCommandInput(shipmentId, request.IdempotencyKey, request.Reason),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("shipments/{shipmentId:int}/tracking")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Tracking(
        int shipmentId,
        [FromBody] ShipmentTrackingRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await shipmentService.UpdateTrackingAsync(
            new ShipmentTrackingUpdateInput(
                shipmentId,
                request.TrackingStatus,
                request.TrackingNumber,
                request.Source,
                request.ProviderReference,
                request.OccurredAtUtc,
                request.PayloadReference,
                request.IdempotencyKey),
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

public sealed class CarrierRequest
{
    [Required, StringLength(50)] public string Code { get; init; } = string.Empty;
    [Required, StringLength(200)] public string Name { get; init; } = string.Empty;
    [StringLength(500)] public string? TrackingUrlTemplate { get; init; }
}

public sealed class CarrierServiceRequest
{
    [Required, StringLength(80)] public string Code { get; init; } = string.Empty;
    [Required, StringLength(200)] public string Name { get; init; } = string.Empty;
    public bool SupportsTracking { get; init; } = true;
    public bool SupportsLabel { get; init; }
}

public sealed class ShipmentCreateRequest
{
    [Required, StringLength(80)] public string ShipmentNumber { get; init; } = string.Empty;
    [Range(1, int.MaxValue)] public int WarehouseId { get; init; }
    [Required, MinLength(1)] public IReadOnlyList<int> ShipmentPackageIds { get; init; } = [];
    [Range(1, int.MaxValue)] public int CarrierId { get; init; }
    [Range(1, int.MaxValue)] public int CarrierServiceId { get; init; }
    public DateTime? PlannedShipAtUtc { get; init; }
    [StringLength(150)] public string? ExternalReference { get; init; }
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed class ShipmentLoadRequest
{
    [Range(1, int.MaxValue)] public int? DockLocationId { get; init; }
    [StringLength(100)] public string? TrailerNumber { get; init; }
    [StringLength(150)] public string? RouteReference { get; init; }
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed class ShipmentPackageScanRequest
{
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed class ShipmentCommandRequest
{
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
    [StringLength(1_000)] public string? Reason { get; init; }
}

public sealed class ShipmentTrackingRequest
{
    [Required, StringLength(100)] public string TrackingStatus { get; init; } = string.Empty;
    [StringLength(250)] public string? TrackingNumber { get; init; }
    [Required, StringLength(50)] public string Source { get; init; } = string.Empty;
    [StringLength(250)] public string? ProviderReference { get; init; }
    public DateTime? OccurredAtUtc { get; init; }
    [StringLength(500)] public string? PayloadReference { get; init; }
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
}
