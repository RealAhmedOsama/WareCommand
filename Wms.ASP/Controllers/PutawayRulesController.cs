using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Putaway;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/putaway-rules")]
[Authorize(Policy = WmsPermissions.LocationsRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class PutawayRulesController(
    IPutawayRuleService putawayRuleService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? warehouseId,
        [FromQuery] bool includeInactive = false,
        [FromQuery] bool includeSimulation = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await putawayRuleService.ListAsync(
            new PutawayRuleQuery(warehouseId, includeInactive, includeSimulation, page, pageSize),
            cancellationToken));

    [HttpGet("{ruleId:int}")]
    public async Task<IActionResult> Get(
        int ruleId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await putawayRuleService.GetAsync(ruleId, cancellationToken));

    [HttpPost]
    [Authorize(Policy = WmsPermissions.LocationsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] PutawayRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await putawayRuleService.CreateAsync(
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPut("{ruleId:int}")]
    [Authorize(Policy = WmsPermissions.LocationsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        int ruleId,
        [FromBody] PutawayRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await putawayRuleService.UpdateAsync(
            ruleId,
            request.ToInput(),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("{ruleId:int}/activate")]
    [Authorize(Policy = WmsPermissions.LocationsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(
        int ruleId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await putawayRuleService.SetActiveAsync(
            ruleId,
            true,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{ruleId:int}/deactivate")]
    [Authorize(Policy = WmsPermissions.LocationsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(
        int ruleId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await putawayRuleService.SetActiveAsync(
            ruleId,
            false,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("suggest")]
    public async Task<IActionResult> Suggest(
        [FromBody] PutawaySuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await putawayRuleService.SuggestAsync(
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

public sealed class PutawayRuleRequest
{
    [Range(1, int.MaxValue)]
    public int WarehouseId { get; init; }

    [Required, StringLength(80)]
    public string Code { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;

    public PutawayRuleStrategy Strategy { get; init; }

    [Range(0, 1_000_000)]
    public int Priority { get; init; }

    public DateTime EffectiveFromUtc { get; init; } = DateTime.UtcNow;
    public DateTime? EffectiveToUtc { get; init; }
    public int? ItemId { get; init; }

    [StringLength(100)]
    public string? ItemCategory { get; init; }

    public int? SupplierId { get; init; }

    [StringLength(50)]
    public string? PackageType { get; init; }

    public LicensePlateType? LicensePlateType { get; init; }
    public int? InventoryStatusId { get; init; }
    public LotStatus? LotStatus { get; init; }
    public decimal? MinimumTemperatureCelsius { get; init; }
    public decimal? MaximumTemperatureCelsius { get; init; }

    [StringLength(100)]
    public string? HazardClass { get; init; }

    [StringLength(100)]
    public string? StorageProfile { get; init; }

    [StringLength(50)]
    public string? SourceProcess { get; init; }

    public int? FixedLocationId { get; init; }
    public LocationType? TargetLocationType { get; init; }

    [StringLength(100)]
    public string? TargetStorageProfile { get; init; }

    public int? FallbackLocationId { get; init; }
    public bool IsSimulation { get; init; }
    public bool IsActive { get; init; } = true;

    [StringLength(2_000)]
    public string? Notes { get; init; }

    public PutawayRuleInput ToInput() => new(
        WarehouseId,
        Code,
        Name,
        Strategy,
        Priority,
        EffectiveFromUtc,
        EffectiveToUtc,
        ItemId,
        ItemCategory,
        SupplierId,
        PackageType,
        LicensePlateType,
        InventoryStatusId,
        LotStatus,
        MinimumTemperatureCelsius,
        MaximumTemperatureCelsius,
        HazardClass,
        StorageProfile,
        SourceProcess,
        FixedLocationId,
        TargetLocationType,
        TargetStorageProfile,
        FallbackLocationId,
        IsSimulation,
        IsActive,
        Notes);
}

public sealed class PutawaySuggestionRequest
{
    [Range(1, int.MaxValue)]
    public int WarehouseId { get; init; }

    [Range(1, int.MaxValue)]
    public int ItemId { get; init; }

    [Wms.ASP.Validation.InvariantDecimalRange("0.000000000001", "79228162514264337593543950335")]
    public decimal Quantity { get; init; }

    public int? LotId { get; init; }
    public int? InventoryStatusId { get; init; }
    public int? LicensePlateId { get; init; }
    public LicensePlateType? LicensePlateType { get; init; }
    public int? SupplierId { get; init; }

    [StringLength(50)]
    public string? PackageType { get; init; }

    [StringLength(50)]
    public string? SourceProcess { get; init; }

    public DateTime? AtUtc { get; init; }
    public bool Simulation { get; init; }

    public PutawaySuggestionInput ToInput() => new(
        WarehouseId,
        ItemId,
        Quantity,
        LotId,
        InventoryStatusId,
        LicensePlateId,
        LicensePlateType,
        SupplierId,
        PackageType,
        SourceProcess,
        AtUtc,
        Simulation);
}
