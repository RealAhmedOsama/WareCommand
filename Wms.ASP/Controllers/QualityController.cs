using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Quality;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/quality")]
[Authorize(Policy = WmsPermissions.QualityRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class QualityController(
    IQualityInspectionService qualityInspectionService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("profiles")]
    [ProducesResponseType(typeof(QualityProfilePageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListProfiles(
        [FromQuery] bool includeInactive = false,
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await qualityInspectionService.ListProfilesAsync(
            includeInactive,
            warehouseId,
            cancellationToken));

    [HttpPost("profiles")]
    [Authorize(Policy = WmsPermissions.QualityManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProfile(
        [FromBody] QualityProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await qualityInspectionService.CreateProfileAsync(
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("profiles/{profileId:int}/active")]
    [Authorize(Policy = WmsPermissions.QualityManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetProfileActive(
        int profileId,
        [FromBody] QualityProfileActivationRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await qualityInspectionService.SetProfileActiveAsync(
            profileId,
            request.IsActive,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpGet("inspections")]
    [ProducesResponseType(typeof(QualityInspectionPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListInspections(
        [FromQuery] int? warehouseId,
        [FromQuery] QualityInspectionStatus? status,
        [FromQuery] int? receiptId,
        [FromQuery] int? itemId,
        [FromQuery] int? supplierId,
        [FromQuery] bool includeClosed = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await qualityInspectionService.ListInspectionsAsync(
            new QualityInspectionQuery(
                warehouseId,
                status,
                receiptId,
                itemId,
                supplierId,
                includeClosed,
                page,
                pageSize),
            cancellationToken));

    [HttpGet("inspections/{inspectionId:int}")]
    public async Task<IActionResult> GetInspection(
        int inspectionId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await qualityInspectionService.GetInspectionAsync(
            inspectionId,
            cancellationToken));

    [HttpPost("inspections/{inspectionId:int}/results")]
    [Authorize(Policy = WmsPermissions.QualityInspect)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordResult(
        int inspectionId,
        [FromBody] QualityTestResultRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await qualityInspectionService.RecordTestResultAsync(
            inspectionId,
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("inspections/{inspectionId:int}/dispositions")]
    [Authorize(Policy = WmsPermissions.QualityInspect)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDisposition(
        int inspectionId,
        [FromBody] QualityDispositionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await qualityInspectionService.AddDispositionAsync(
            inspectionId,
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("inspections/{inspectionId:int}/close")]
    [Authorize(Policy = WmsPermissions.QualityInspect)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(
        int inspectionId,
        [FromBody] QualityCloseRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await qualityInspectionService.CloseAsync(
            inspectionId,
            currentUser.RequireUserId(),
            request.Reason,
            request.SupervisorOverride,
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

public sealed class QualityProfileRequest
{
    [Required, StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string LocalizedName { get; init; } = string.Empty;

    public QualityRiskLevel RiskLevel { get; init; } = QualityRiskLevel.Medium;
    public QualitySamplingMethod SamplingMethod { get; init; } = QualitySamplingMethod.FullInspection;

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal SamplingValue { get; init; }

    public bool IsRequired { get; init; } = true;

    [Range(1, int.MaxValue)]
    public int? WarehouseId { get; init; }

    [Range(1, int.MaxValue)]
    public int? SupplierId { get; init; }

    [Range(1, int.MaxValue)]
    public int? ItemId { get; init; }

    [StringLength(100)]
    public string? ItemCategory { get; init; }

    [StringLength(30)]
    public string? ReceiptSourceType { get; init; }

    public IReadOnlyList<QualityProfileTestRequest> Tests { get; init; } = [];

    public QualityProfileInput ToInput() => new(
        Code,
        Name,
        LocalizedName,
        RiskLevel,
        SamplingMethod,
        SamplingValue,
        IsRequired,
        WarehouseId,
        SupplierId,
        ItemId,
        ItemCategory,
        ReceiptSourceType,
        Tests.Select(test => test.ToInput()).ToArray());
}

public sealed class QualityProfileTestRequest
{
    [Range(1, int.MaxValue)]
    public int Sequence { get; init; }

    [Required, StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string LocalizedName { get; init; } = string.Empty;

    public QualityMeasurementType MeasurementType { get; init; }
    public bool IsRequired { get; init; } = true;
    public decimal? MinimumValue { get; init; }
    public decimal? MaximumValue { get; init; }

    [StringLength(2_000)]
    public string? AllowedValues { get; init; }

    public QualityProfileTestInput ToInput() => new(
        Sequence,
        Code,
        Name,
        LocalizedName,
        MeasurementType,
        IsRequired,
        MinimumValue,
        MaximumValue,
        AllowedValues);
}

public sealed class QualityProfileActivationRequest
{
    public bool IsActive { get; init; }
}

public sealed class QualityTestResultRequest
{
    [Range(1, int.MaxValue)]
    public int QualityProfileTestId { get; init; }

    [Wms.ASP.Validation.InvariantDecimalRange("0.000000000001", "79228162514264337593543950335")]
    public decimal InspectedQuantity { get; init; }

    [StringLength(500)]
    public string? RecordedValue { get; init; }

    public decimal? NumericValue { get; init; }
    public bool? BooleanValue { get; init; }
    public bool Passed { get; init; }

    [StringLength(1_000)]
    public string? Notes { get; init; }

    [StringLength(2_000)]
    public string? AttachmentReferences { get; init; }

    public QualityInspectionTestResultInput ToInput() => new(
        QualityProfileTestId,
        InspectedQuantity,
        RecordedValue,
        NumericValue,
        BooleanValue,
        Passed,
        Notes,
        AttachmentReferences);
}

public sealed class QualityDispositionRequest
{
    public QualityDispositionType Type { get; init; }

    [Wms.ASP.Validation.InvariantDecimalRange("0.000000000001", "79228162514264337593543950335")]
    public decimal Quantity { get; init; }

    [Required, StringLength(1_000)]
    public string Reason { get; init; } = string.Empty;

    [StringLength(100)]
    public string? ReferenceNumber { get; init; }

    [Range(1, int.MaxValue)]
    public int? TargetLocationId { get; init; }

    public bool SupervisorOverride { get; init; }

    [StringLength(1_000)]
    public string? OverrideReason { get; init; }

    public QualityDispositionInput ToInput() => new(
        Type,
        Quantity,
        Reason,
        ReferenceNumber,
        TargetLocationId,
        SupervisorOverride,
        OverrideReason);
}

public sealed class QualityCloseRequest
{
    [StringLength(1_000)]
    public string? Reason { get; init; }

    public bool SupervisorOverride { get; init; }
}
