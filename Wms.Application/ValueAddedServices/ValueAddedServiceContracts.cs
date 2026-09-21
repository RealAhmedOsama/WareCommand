using Wms.Application.Common;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;

namespace Wms.Application.ValueAddedServices;

public sealed record KitDefinitionLineInput(
    int Sequence,
    int ComponentItemId,
    decimal QuantityPerOutput,
    string ComponentUnitOfMeasure,
    KitSubstitutionPolicy SubstitutionPolicy = KitSubstitutionPolicy.None,
    string? ApprovedSubstitutionItemIdsJson = null,
    string? Notes = null);

public sealed record KitDefinitionInput(
    string Code,
    int Version,
    int OutputItemId,
    string OutputUnitOfMeasure,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc = null,
    string? Instructions = null,
    string? LocalizedInstructions = null,
    IReadOnlyList<KitDefinitionLineInput>? Lines = null);

public sealed record KitDefinitionLineDto(
    int Id,
    int Sequence,
    int ComponentItemId,
    string ComponentItemSku,
    string ComponentItemName,
    decimal QuantityPerOutput,
    string ComponentUnitOfMeasure,
    KitSubstitutionPolicy SubstitutionPolicy,
    string? ApprovedSubstitutionItemIdsJson,
    string? Notes,
    long Revision);

public sealed record KitDefinitionDto(
    int Id,
    string Code,
    int Version,
    int OutputItemId,
    string OutputItemSku,
    string OutputItemName,
    string OutputUnitOfMeasure,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    string? Instructions,
    string? LocalizedInstructions,
    bool IsActive,
    long Revision,
    IReadOnlyList<KitDefinitionLineDto> Lines);

public sealed record ValueAddedServiceInputSelector(
    int? LocationId = null,
    int? LotId = null,
    int? SerialNumberId = null,
    string? SerialNumber = null,
    int? LicensePlateId = null,
    int? InventoryStatusId = null,
    InventoryOwnerKind OwnerKind = InventoryOwnerKind.CompanyOwned,
    int? InventoryOwnerId = null,
    string? OwnerCodeSnapshot = null);

public sealed record ValueAddedServiceOrderInput(
    string IdempotencyKey,
    ValueAddedServiceType Type,
    int WarehouseId,
    int SourceLocationId,
    int DestinationLocationId,
    int OutputItemId,
    decimal RequestedOutputQuantity,
    int? KitDefinitionId = null,
    int? InputItemId = null,
    ValueAddedServiceInputSelector? InputSelector = null,
    string? StationCode = null,
    string? LabelTemplateCode = null,
    int? QualityProfileId = null,
    InventoryOwnerKind OutputOwnerKind = InventoryOwnerKind.CompanyOwned,
    int? OutputInventoryOwnerId = null,
    string? OutputOwnerCodeSnapshot = null);

public sealed record ValueAddedServiceComponentCompletionInput(
    int OrderLineId,
    decimal ConsumedQuantity,
    decimal ScrapQuantity = 0m,
    string? Reason = null);

public sealed record ValueAddedServiceOutputInput(
    int OrderLineId,
    decimal Quantity,
    int? LotId = null,
    int? SerialNumberId = null,
    string? SerialNumber = null,
    int? LicensePlateId = null,
    int? InventoryStatusId = null,
    InventoryOwnerKind? OwnerKind = null,
    int? InventoryOwnerId = null,
    string? OwnerCodeSnapshot = null);

public sealed record ValueAddedServiceCompletionInput(
    string IdempotencyKey,
    decimal OutputQuantity,
    IReadOnlyList<ValueAddedServiceOutputInput> Outputs,
    IReadOnlyList<ValueAddedServiceComponentCompletionInput>? Components = null,
    string? CompletionReference = null,
    string? Reason = null);

public sealed record ValueAddedServiceReversalInput(
    string IdempotencyKey,
    string Reason,
    string? ReversalReference = null);

public sealed record ValueAddedServiceOrderQuery(
    int? WarehouseId = null,
    ValueAddedServiceOrderStatus? Status = null,
    ValueAddedServiceType? Type = null,
    int Page = 1,
    int PageSize = 50);

public sealed record ValueAddedServiceOrderLineDto(
    int Id,
    int LineNumber,
    ValueAddedServiceLineKind Kind,
    int ItemId,
    string ItemSku,
    string ItemName,
    decimal PlannedQuantity,
    decimal ConsumedQuantity,
    decimal ProducedQuantity,
    decimal ScrapQuantity,
    decimal ReversedQuantity,
    decimal RemainingQuantity,
    string BaseUnitOfMeasure,
    int InventoryStatusId,
    int? SourceLocationId,
    int? DestinationLocationId,
    int? LotId,
    int? SerialNumberId,
    string? SerialNumber,
    int? LicensePlateId,
    InventoryOwnerKind OwnerKind,
    int? InventoryOwnerId,
    string OwnerCodeSnapshot,
    int? KitDefinitionLineId,
    int? ReservationId,
    int? ReservationAllocationId,
    bool IsSubstitution,
    int? SubstitutedForItemId,
    string? Notes,
    long Revision);

public sealed record ValueAddedServiceOrderDto(
    int Id,
    string OrderNumber,
    string IdempotencyKey,
    ValueAddedServiceType Type,
    ValueAddedServiceOrderStatus Status,
    int WarehouseId,
    int SourceLocationId,
    int DestinationLocationId,
    int OutputItemId,
    string OutputItemSku,
    string OutputItemName,
    decimal RequestedOutputQuantity,
    decimal CompletedOutputQuantity,
    decimal ScrapQuantity,
    decimal ReversedOutputQuantity,
    string OutputUnitOfMeasure,
    int? KitDefinitionId,
    int? KitVersionSnapshot,
    string? InstructionSnapshot,
    string? LocalizedInstructionSnapshot,
    string? StationCode,
    string? LabelTemplateCode,
    int? QualityProfileId,
    InventoryOwnerKind OutputOwnerKind,
    int? OutputInventoryOwnerId,
    string OutputOwnerCodeSnapshot,
    int? WarehouseWorkId,
    string CreatedByUserId,
    DateTime CreatedAtUtc,
    DateTime? ReleasedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    DateTime? ReversedAtUtc,
    string? ReversalReason,
    string? ExceptionReason,
    long Revision,
    IReadOnlyList<ValueAddedServiceOrderLineDto> Lines);

public sealed record ValueAddedServiceGenealogyRow(
    int TraceLinkId,
    int OrderId,
    string OrderNumber,
    ValueAddedServiceTraceKind Kind,
    decimal Quantity,
    decimal ReversedQuantity,
    ValueAddedServiceOrderLineDto InputLine,
    ValueAddedServiceOrderLineDto? OutputLine,
    DateTime OccurredAtUtc,
    string IdempotencyKey);

public sealed record ValueAddedServicePageDto(
    IReadOnlyList<ValueAddedServiceOrderDto> Orders,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public interface IValueAddedService
{
    Task<Result<KitDefinitionDto>> CreateKitDefinitionAsync(
        KitDefinitionInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<KitDefinitionDto>> GetKitDefinitionAsync(
        int definitionId,
        CancellationToken cancellationToken = default);

    Task<Result<ValueAddedServiceOrderDto>> CreateOrderAsync(
        ValueAddedServiceOrderInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ValueAddedServiceOrderDto>> ReleaseAsync(
        int orderId,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ValueAddedServiceOrderDto>> CompleteAsync(
        int orderId,
        ValueAddedServiceCompletionInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ValueAddedServiceOrderDto>> CancelAsync(
        int orderId,
        string reason,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ValueAddedServiceOrderDto>> ReverseAsync(
        int orderId,
        ValueAddedServiceReversalInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ValueAddedServicePageDto>> ListOrdersAsync(
        ValueAddedServiceOrderQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ValueAddedServiceGenealogyRow>>> GetGenealogyAsync(
        int orderId,
        CancellationToken cancellationToken = default);
}
