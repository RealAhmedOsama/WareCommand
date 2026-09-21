using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.ValueAddedServices;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/value-added-services")]
[Authorize(Policy = WmsPermissions.ValueAddedServiceRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class ValueAddedServicesController(
    IValueAddedService valueAddedService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("kits/{definitionId:int}")]
    public async Task<IActionResult> GetKit(
        int definitionId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await valueAddedService.GetKitDefinitionAsync(definitionId, cancellationToken));

    [HttpPost("kits")]
    [Authorize(Policy = WmsPermissions.ValueAddedServiceManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateKit(
        [FromBody] CreateKitRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await valueAddedService.CreateKitDefinitionAsync(
            new KitDefinitionInput(
                request.Code,
                request.Version,
                request.OutputItemId,
                request.OutputUnitOfMeasure,
                request.EffectiveFromUtc,
                request.EffectiveToUtc,
                request.Instructions,
                request.LocalizedInstructions,
                request.Lines?.Select(line => new KitDefinitionLineInput(
                    line.Sequence,
                    line.ComponentItemId,
                    line.QuantityPerOutput,
                    line.ComponentUnitOfMeasure,
                    line.SubstitutionPolicy,
                    line.ApprovedSubstitutionItemIdsJson,
                    line.Notes)).ToArray()),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpGet("orders")]
    public async Task<IActionResult> ListOrders(
        [FromQuery] int? warehouseId,
        [FromQuery] ValueAddedServiceOrderStatus? status,
        [FromQuery] ValueAddedServiceType? type,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await valueAddedService.ListOrdersAsync(
            new ValueAddedServiceOrderQuery(warehouseId, status, type, page, pageSize),
            cancellationToken));

    [HttpPost("orders")]
    [Authorize(Policy = WmsPermissions.ValueAddedServiceManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await valueAddedService.CreateOrderAsync(
            new ValueAddedServiceOrderInput(
                request.IdempotencyKey,
                request.Type,
                request.WarehouseId,
                request.SourceLocationId,
                request.DestinationLocationId,
                request.OutputItemId,
                request.RequestedOutputQuantity,
                request.KitDefinitionId,
                request.InputItemId,
                request.InputSelector is null
                    ? null
                    : new ValueAddedServiceInputSelector(
                        request.InputSelector.LocationId,
                        request.InputSelector.LotId,
                        request.InputSelector.SerialNumberId,
                        request.InputSelector.SerialNumber,
                        request.InputSelector.LicensePlateId,
                        request.InputSelector.InventoryStatusId,
                        request.InputSelector.OwnerKind,
                        request.InputSelector.InventoryOwnerId,
                        request.InputSelector.OwnerCodeSnapshot),
                request.StationCode,
                request.LabelTemplateCode,
                request.QualityProfileId,
                request.OutputOwnerKind,
                request.OutputInventoryOwnerId,
                request.OutputOwnerCodeSnapshot),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("orders/{orderId:int}/release")]
    [Authorize(Policy = WmsPermissions.ValueAddedServiceManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Release(
        int orderId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await valueAddedService.ReleaseAsync(
            orderId,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("orders/{orderId:int}/complete")]
    [Authorize(Policy = WmsPermissions.ValueAddedServiceManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(
        int orderId,
        [FromBody] CompleteOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await valueAddedService.CompleteAsync(
            orderId,
            new ValueAddedServiceCompletionInput(
                request.IdempotencyKey,
                request.OutputQuantity,
                request.Outputs.Select(output => new ValueAddedServiceOutputInput(
                    output.OrderLineId,
                    output.Quantity,
                    output.LotId,
                    output.SerialNumberId,
                    output.SerialNumber,
                    output.LicensePlateId,
                    output.InventoryStatusId,
                    output.OwnerKind,
                    output.InventoryOwnerId,
                    output.OwnerCodeSnapshot)).ToArray(),
                request.Components?.Select(component => new ValueAddedServiceComponentCompletionInput(
                    component.OrderLineId,
                    component.ConsumedQuantity,
                    component.ScrapQuantity,
                    component.Reason)).ToArray(),
                request.CompletionReference,
                request.Reason),
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Policy = WmsPermissions.ValueAddedServiceManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        int orderId,
        [FromBody] CancelOrderRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await valueAddedService.CancelAsync(
            orderId,
            request.Reason,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("orders/{orderId:int}/reverse")]
    [Authorize(Policy = WmsPermissions.ValueAddedServiceManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reverse(
        int orderId,
        [FromBody] ReverseOrderRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await valueAddedService.ReverseAsync(
            orderId,
            new ValueAddedServiceReversalInput(
                request.IdempotencyKey,
                request.Reason,
                request.ReversalReference),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpGet("orders/{orderId:int}/genealogy")]
    public async Task<IActionResult> Genealogy(
        int orderId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await valueAddedService.GetGenealogyAsync(orderId, cancellationToken));

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

    public sealed class CreateKitRequest
    {
        [Required, StringLength(80)]
        public string Code { get; init; } = string.Empty;

        [Range(1, int.MaxValue)]
        public int Version { get; init; }

        [Range(1, int.MaxValue)]
        public int OutputItemId { get; init; }

        [Required, StringLength(20)]
        public string OutputUnitOfMeasure { get; init; } = string.Empty;

        public DateTime EffectiveFromUtc { get; init; }
        public DateTime? EffectiveToUtc { get; init; }
        [StringLength(8_000)] public string? Instructions { get; init; }
        [StringLength(8_000)] public string? LocalizedInstructions { get; init; }
        public IReadOnlyList<KitLineRequest>? Lines { get; init; }
    }

    public sealed class KitLineRequest
    {
        [Range(1, int.MaxValue)] public int Sequence { get; init; }
        [Range(1, int.MaxValue)] public int ComponentItemId { get; init; }
        [Range(typeof(decimal), "0.000000000001", "79228162514264337593543950335")]
        public decimal QuantityPerOutput { get; init; }
        [Required, StringLength(20)] public string ComponentUnitOfMeasure { get; init; } = string.Empty;
        public KitSubstitutionPolicy SubstitutionPolicy { get; init; }
        [StringLength(2_000)] public string? ApprovedSubstitutionItemIdsJson { get; init; }
        [StringLength(1_000)] public string? Notes { get; init; }
    }

    public sealed class CreateOrderRequest
    {
        [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
        public ValueAddedServiceType Type { get; init; }
        [Range(1, int.MaxValue)] public int WarehouseId { get; init; }
        [Range(1, int.MaxValue)] public int SourceLocationId { get; init; }
        [Range(1, int.MaxValue)] public int DestinationLocationId { get; init; }
        [Range(1, int.MaxValue)] public int OutputItemId { get; init; }
        [Range(typeof(decimal), "0.000000000001", "79228162514264337593543950335")]
        public decimal RequestedOutputQuantity { get; init; }
        [Range(1, int.MaxValue)] public int? KitDefinitionId { get; init; }
        [Range(1, int.MaxValue)] public int? InputItemId { get; init; }
        public InputSelectorRequest? InputSelector { get; init; }
        [StringLength(50)] public string? StationCode { get; init; }
        [StringLength(120)] public string? LabelTemplateCode { get; init; }
        [Range(1, int.MaxValue)] public int? QualityProfileId { get; init; }
        public InventoryOwnerKind OutputOwnerKind { get; init; }
        [Range(1, int.MaxValue)] public int? OutputInventoryOwnerId { get; init; }
        [StringLength(80)] public string? OutputOwnerCodeSnapshot { get; init; }
    }

    public sealed class InputSelectorRequest
    {
        [Range(1, int.MaxValue)] public int? LocationId { get; init; }
        [Range(1, int.MaxValue)] public int? LotId { get; init; }
        [Range(1, int.MaxValue)] public int? SerialNumberId { get; init; }
        [StringLength(100)] public string? SerialNumber { get; init; }
        [Range(1, int.MaxValue)] public int? LicensePlateId { get; init; }
        [Range(1, int.MaxValue)] public int? InventoryStatusId { get; init; }
        public InventoryOwnerKind OwnerKind { get; init; }
        [Range(1, int.MaxValue)] public int? InventoryOwnerId { get; init; }
        [StringLength(80)] public string? OwnerCodeSnapshot { get; init; }
    }

    public sealed class CompleteOrderRequest
    {
        [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
        [Range(typeof(decimal), "0", "79228162514264337593543950335")]
        public decimal OutputQuantity { get; init; }
        public IReadOnlyList<OutputRequest> Outputs { get; init; } = [];
        public IReadOnlyList<ComponentRequest>? Components { get; init; }
        [StringLength(200)] public string? CompletionReference { get; init; }
        [StringLength(1_000)] public string? Reason { get; init; }
    }

    public sealed class OutputRequest
    {
        [Range(1, int.MaxValue)] public int OrderLineId { get; init; }
        [Range(typeof(decimal), "0", "79228162514264337593543950335")]
        public decimal Quantity { get; init; }
        [Range(1, int.MaxValue)] public int? LotId { get; init; }
        [Range(1, int.MaxValue)] public int? SerialNumberId { get; init; }
        [StringLength(100)] public string? SerialNumber { get; init; }
        [Range(1, int.MaxValue)] public int? LicensePlateId { get; init; }
        [Range(1, int.MaxValue)] public int? InventoryStatusId { get; init; }
        public InventoryOwnerKind? OwnerKind { get; init; }
        [Range(1, int.MaxValue)] public int? InventoryOwnerId { get; init; }
        [StringLength(80)] public string? OwnerCodeSnapshot { get; init; }
    }

    public sealed class ComponentRequest
    {
        [Range(1, int.MaxValue)] public int OrderLineId { get; init; }
        [Range(typeof(decimal), "0", "79228162514264337593543950335")]
        public decimal ConsumedQuantity { get; init; }
        [Range(typeof(decimal), "0", "79228162514264337593543950335")]
        public decimal ScrapQuantity { get; init; }
        [StringLength(1_000)] public string? Reason { get; init; }
    }

    public sealed class CancelOrderRequest
    {
        [Required, StringLength(1_000)] public string Reason { get; init; } = string.Empty;
    }

    public sealed class ReverseOrderRequest
    {
        [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
        [Required, StringLength(1_000)] public string Reason { get; init; } = string.Empty;
        [StringLength(200)] public string? ReversalReference { get; init; }
    }
}
