using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wms.Application.Common;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;

namespace Wms.Application.Inventory;

public sealed record ReplenishmentGenerationQuery(
    int? WarehouseId = null,
    int? PolicyId = null,
    int Limit = 200,
    bool DryRun = false);

public sealed record ReplenishmentWorkPlanLineDto(
    int Sequence,
    int SourceLocationId,
    int DestinationLocationId,
    decimal PlannedQuantity,
    int? LotId,
    int? SerialNumberId,
    string? SerialNumber,
    int? LicensePlateId,
    int InventoryStatusId,
    string BaseUnitOfMeasure,
    string SelectionReason,
    InventoryOwnerKind OwnerKind = InventoryOwnerKind.CompanyOwned,
    int? InventoryOwnerId = null,
    string OwnerCodeSnapshot = InventoryOwnershipDimension.CompanyOwnerCode);

public sealed record ReplenishmentWorkPlanDto(
    int PolicyId,
    int ItemId,
    string ItemSku,
    int WarehouseId,
    int? DestinationLocationId,
    decimal SignalShortfallQuantity,
    decimal OpenWorkQuantity,
    decimal PlannedQuantity,
    string Decision,
    int? WorkId,
    string? WorkNumber,
    IReadOnlyList<ReplenishmentWorkPlanLineDto> Lines)
{
    public string? SourceStateFingerprint { get; init; }
    public decimal? CapacityAvailableQuantity { get; init; }
}

public sealed record ReplenishmentGenerationResultDto(
    int PoliciesExamined,
    int SignalsExamined,
    int WorkCreated,
    int WorkReused,
    int Blocked,
    IReadOnlyList<ReplenishmentWorkPlanDto> Plans);

public interface IReplenishmentExecutionService
{
    Task<Result<ReplenishmentGenerationResultDto>> GenerateAsync(
        ReplenishmentGenerationQuery query,
        string actorUserId,
        CancellationToken cancellationToken = default);
}

public static class ReplenishmentWorkPlanFingerprint
{
    public static string Create(
        InventoryReplenishmentSignalDto signal,
        ReplenishmentWorkPlanDto plan)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(plan);
        var invariant = CultureInfo.InvariantCulture;
        var canonical = string.Join(
            "|",
            signal.PolicyId,
            signal.ItemId,
            signal.WarehouseId,
            signal.LocationId,
            signal.QuantityBasis,
            signal.EvaluatedQuantity.ToString(invariant),
            signal.OnHandQuantity.ToString(invariant),
            signal.ReservedQuantity.ToString(invariant),
            signal.PhysicalAvailableQuantity.ToString(invariant),
            signal.AvailableToPromiseQuantity.ToString(invariant),
            signal.InboundQuantity.ToString(invariant),
            signal.OrderedQuantity.ToString(invariant),
            signal.MinimumQuantity.ToString(invariant),
            signal.SafetyStockQuantity.ToString(invariant),
            signal.ReorderPointQuantity.ToString(invariant),
            signal.TargetQuantity.ToString(invariant),
            signal.MaximumQuantity.ToString(invariant),
            signal.SignalKind,
            signal.ShortfallQuantity.ToString(invariant),
            signal.ClassificationClass ?? string.Empty,
            plan.DestinationLocationId,
            plan.CapacityAvailableQuantity?.ToString(invariant) ?? string.Empty,
            plan.OpenWorkQuantity.ToString(invariant),
            plan.PlannedQuantity.ToString(invariant),
            string.Join(";", plan.Lines.OrderBy(line => line.Sequence).Select(line => string.Join(
                ":",
                line.Sequence,
                line.SourceLocationId,
                line.DestinationLocationId,
                line.PlannedQuantity.ToString(invariant),
                line.LotId,
                line.SerialNumberId,
                line.SerialNumber ?? string.Empty,
                line.LicensePlateId,
                line.InventoryStatusId,
                line.BaseUnitOfMeasure,
                line.OwnerKind,
                line.InventoryOwnerId,
                line.OwnerCodeSnapshot))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}
