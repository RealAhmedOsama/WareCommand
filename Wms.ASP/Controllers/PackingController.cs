using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Packing;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/packing")]
[Authorize(Policy = WmsPermissions.PackingExecute)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class PackingController(
    IPackingService packingService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpPost("stations")]
    [Authorize(Policy = WmsPermissions.SettingsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateStation(
        [FromBody] PackingStationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await packingService.CreateStationAsync(
            new PackingStationInput(
                request.Code,
                request.Name,
                request.WarehouseId,
                request.LocationId,
                request.SupportedDevices,
                request.PrinterProfile,
                request.ScaleProfile,
                request.AllowedUserIds,
                request.PermissionProfile),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("stations/{stationId:int}/status")]
    [Authorize(Policy = WmsPermissions.SettingsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStationStatus(
        int stationId,
        [FromBody] PackingStationStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await packingService.SetStationStatusAsync(
            stationId,
            request.Status,
            request.IdempotencyKey,
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("sessions")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartSession(
        [FromBody] PackingSessionStartRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await packingService.StartSessionAsync(
            new PackingSessionStartInput(
                request.SessionNumber,
                request.WarehouseId,
                request.PackingStationId,
                request.SourceType,
                request.SourceReference,
                request.SalesOrderId,
                request.StagingLicensePlateId,
                request.IdempotencyKey),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpGet("sessions/{sessionId:int}")]
    public async Task<IActionResult> GetSession(
        int sessionId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await packingService.GetSessionAsync(sessionId, cancellationToken));

    [HttpPost("packages")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePackage(
        [FromBody] PackingPackageCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await packingService.CreatePackageAsync(
            new PackingPackageCreateInput(
                request.PackingSessionId,
                request.PackageNumber,
                request.TargetLicensePlateId,
                request.PackageType,
                request.SalesOrderId,
                request.AllowConsolidated,
                request.ExpectedWeightKg,
                request.WeightToleranceKg,
                request.WeightTolerancePercent,
                request.LabelReference,
                request.IdempotencyKey),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpGet("packages/{packageId:int}")]
    public async Task<IActionResult> GetPackage(
        int packageId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await packingService.GetPackageAsync(packageId, cancellationToken));

    [HttpPost("packages/{packageId:int}/pack")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pack(
        int packageId,
        [FromBody] PackingScanRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await packingService.PackAsync(
            request.ToInput(packageId),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("packages/{packageId:int}/close")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(
        int packageId,
        [FromBody] PackingPackageMeasureRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await packingService.ClosePackageAsync(
            request.ToInput(packageId),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("packages/{packageId:int}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(
        int packageId,
        [FromBody] PackingScanRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await packingService.RemoveContentAsync(
            request.ToInput(packageId),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("packages/{packageId:int}/reopen")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(
        int packageId,
        [FromBody] PackingPackageCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await packingService.ReopenPackageAsync(
            request.ToInput(packageId),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("packages/{packageId:int}/void")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Void(
        int packageId,
        [FromBody] PackingPackageCommandRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await packingService.VoidPackageAsync(
            request.ToInput(packageId),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("sessions/{sessionId:int}/complete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteSession(
        int sessionId,
        [FromBody] PackingCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await packingService.CompleteSessionAsync(
            sessionId,
            request.IdempotencyKey,
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
}

public sealed class PackingStationRequest
{
    [Required, StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [Required, StringLength(150)]
    public string Name { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int WarehouseId { get; init; }

    [Range(1, int.MaxValue)]
    public int LocationId { get; init; }

    [StringLength(500)] public string? SupportedDevices { get; init; }
    [StringLength(150)] public string? PrinterProfile { get; init; }
    [StringLength(150)] public string? ScaleProfile { get; init; }
    [StringLength(4_000)] public string? AllowedUserIds { get; init; }
    [StringLength(150)] public string? PermissionProfile { get; init; }
}

public sealed class PackingStationStatusRequest
{
    public PackingStationStatus Status { get; init; }

    [Required, StringLength(250)]
    public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed class PackingSessionStartRequest
{
    [Required, StringLength(50)] public string SessionNumber { get; init; } = string.Empty;
    [Range(1, int.MaxValue)] public int WarehouseId { get; init; }
    [Range(1, int.MaxValue)] public int PackingStationId { get; init; }
    public PackingSourceType SourceType { get; init; }
    [Required, StringLength(200)] public string SourceReference { get; init; } = string.Empty;
    [Range(1, int.MaxValue)] public int? SalesOrderId { get; init; }
    [Range(1, int.MaxValue)] public int? StagingLicensePlateId { get; init; }
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed class PackingPackageCreateRequest
{
    [Range(1, int.MaxValue)] public int PackingSessionId { get; init; }
    [Required, StringLength(80)] public string PackageNumber { get; init; } = string.Empty;
    [Range(1, int.MaxValue)] public int TargetLicensePlateId { get; init; }
    public PackingPackageType PackageType { get; init; }
    [Range(1, int.MaxValue)] public int? SalesOrderId { get; init; }
    public bool AllowConsolidated { get; init; }
    [Range(typeof(decimal), "0", "100000000")] public decimal? ExpectedWeightKg { get; init; }
    [Range(typeof(decimal), "0", "100000000")] public decimal WeightToleranceKg { get; init; }
    [Range(typeof(decimal), "0", "100")] public decimal WeightTolerancePercent { get; init; }
    [StringLength(250)] public string? LabelReference { get; init; }
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed class PackingScanRequest
{
    [Range(1, int.MaxValue)] public int SalesOrderLineId { get; init; }
    [Range(1, int.MaxValue)] public int ItemId { get; init; }
    [Range(1, int.MaxValue)] public int SourceLocationId { get; init; }
    [Range(typeof(decimal), "0.000001", "100000000")] public decimal Quantity { get; init; }
    [Range(1, int.MaxValue)] public int InventoryStatusId { get; init; } = 1;
    [Range(1, int.MaxValue)] public int? SourceLicensePlateId { get; init; }
    [Range(1, int.MaxValue)] public int? LotId { get; init; }
    [Range(1, int.MaxValue)] public int? SerialNumberId { get; init; }
    [StringLength(250)] public string? SerialNumber { get; init; }
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
    [StringLength(250)] public string? ScanReference { get; init; }
    public InventoryOwnerKind OwnerKind { get; init; } = InventoryOwnerKind.CompanyOwned;
    [Range(1, int.MaxValue)] public int? InventoryOwnerId { get; init; }
    [StringLength(80)] public string? OwnerCodeSnapshot { get; init; }

    public PackingScanInput ToInput(int packageId) => new(
        packageId,
        SalesOrderLineId,
        ItemId,
        SourceLocationId,
        Quantity,
        InventoryStatusId,
        SourceLicensePlateId,
        LotId,
        SerialNumberId,
        SerialNumber,
        IdempotencyKey,
        ScanReference,
        OwnerKind,
        InventoryOwnerId,
        OwnerCodeSnapshot);
}

public sealed class PackingPackageMeasureRequest
{
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
    [Range(typeof(decimal), "0", "100000000")] public decimal? ActualWeightKg { get; init; }
    [Range(typeof(decimal), "0", "100000")] public decimal? LengthCm { get; init; }
    [Range(typeof(decimal), "0", "100000")] public decimal? WidthCm { get; init; }
    [Range(typeof(decimal), "0", "100000")] public decimal? HeightCm { get; init; }

    public PackingPackageMeasureInput ToInput(int packageId) => new(
        packageId,
        IdempotencyKey,
        ActualWeightKg,
        LengthCm,
        WidthCm,
        HeightCm);
}

public sealed class PackingPackageCommandRequest
{
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
    [StringLength(1_000)] public string? Reason { get; init; }

    public PackingPackageCommandInput ToInput(int packageId) =>
        new(packageId, IdempotencyKey, Reason);
}

public sealed class PackingCompletionRequest
{
    [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
}
