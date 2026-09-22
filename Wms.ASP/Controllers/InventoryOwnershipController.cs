using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/inventory-ownership")]
[Authorize(Policy = WmsPermissions.InventoryOwnershipRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class InventoryOwnershipController(
    IInventoryOwnershipService ownershipService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("owners")]
    public async Task<IActionResult> ListOwners(
        [FromQuery] InventoryOwnerKind? kind,
        [FromQuery] bool includeInactive = false,
        [FromQuery] string? searchTerm = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await ownershipService.ListOwnersAsync(
            new InventoryOwnerQuery(kind, includeInactive, searchTerm, page, pageSize),
            cancellationToken));

    [HttpGet("owners/{ownerId:int}")]
    public async Task<IActionResult> GetOwner(
        int ownerId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await ownershipService.GetOwnerAsync(ownerId, cancellationToken));

    [HttpPost("owners")]
    [Authorize(Policy = WmsPermissions.InventoryOwnershipManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOwner(
        [FromBody] InventoryOwnerRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await ownershipService.CreateOwnerAsync(
            new InventoryOwnerInput(
                request.OwnerCode,
                request.Kind,
                request.DisplayName,
                request.LocalizedName,
                request.SupplierId,
                request.CustomerId,
                request.ExternalOwnerReference),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("owners/{ownerId:int}/deactivate")]
    [Authorize(Policy = WmsPermissions.InventoryOwnershipManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateOwner(
        int ownerId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await ownershipService.DeactivateOwnerAsync(
            ownerId,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpGet("transfers")]
    public async Task<IActionResult> ListTransfers(
        [FromQuery] int? warehouseId,
        [FromQuery] int? itemId,
        [FromQuery] InventoryOwnershipTransferStatus? status,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await ownershipService.ListTransfersAsync(
            warehouseId,
            itemId,
            status,
            cancellationToken));

    [HttpPost("transfers")]
    [Authorize(Policy = WmsPermissions.InventoryOwnershipManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Transfer(
        [FromBody] InventoryOwnershipTransferRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await ownershipService.TransferAsync(
            new InventoryOwnershipTransferInput(
                request.IdempotencyKey,
                request.WarehouseId,
                request.ItemId,
                request.Quantity,
                request.BaseUnitOfMeasure,
                request.LocationId,
                request.InventoryStatusId,
                request.SourceOwnerKind,
                request.SourceInventoryOwnerId,
                request.SourceOwnerCodeSnapshot,
                request.DestinationOwnerKind,
                request.DestinationInventoryOwnerId,
                request.DestinationOwnerCodeSnapshot,
                request.LotId,
                request.SerialNumberId,
                request.SerialNumber,
                request.LicensePlateId,
                request.Reason),
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

    public sealed record InventoryOwnerRequest(
        [property: Required, StringLength(80)] string OwnerCode,
        InventoryOwnerKind Kind,
        [property: Required, StringLength(200)] string DisplayName,
        [property: StringLength(200)] string? LocalizedName,
        [property: Range(1, int.MaxValue)] int? SupplierId,
        [property: Range(1, int.MaxValue)] int? CustomerId,
        [property: StringLength(120)] string? ExternalOwnerReference);

    public sealed record InventoryOwnershipTransferRequest(
        [property: Required, StringLength(250)] string IdempotencyKey,
        [property: Range(1, int.MaxValue)] int WarehouseId,
        [property: Range(1, int.MaxValue)] int ItemId,
        [property: Wms.ASP.Validation.InvariantDecimalRange("0.000000000001", "79228162514264337593543950335")] decimal Quantity,
        [property: Required, StringLength(20)] string BaseUnitOfMeasure,
        [property: Range(1, int.MaxValue)] int LocationId,
        [property: Range(1, int.MaxValue)] int InventoryStatusId,
        InventoryOwnerKind SourceOwnerKind,
        [property: Range(1, int.MaxValue)] int? SourceInventoryOwnerId,
        [property: StringLength(80)] string? SourceOwnerCodeSnapshot,
        InventoryOwnerKind DestinationOwnerKind,
        [property: Range(1, int.MaxValue)] int? DestinationInventoryOwnerId,
        [property: StringLength(80)] string? DestinationOwnerCodeSnapshot,
        [property: Range(1, int.MaxValue)] int? LotId,
        [property: Range(1, int.MaxValue)] int? SerialNumberId,
        [property: StringLength(100)] string? SerialNumber,
        [property: Range(1, int.MaxValue)] int? LicensePlateId,
        [property: StringLength(1_000)] string? Reason);
}
