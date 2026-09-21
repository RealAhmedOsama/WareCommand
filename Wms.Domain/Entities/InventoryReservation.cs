using Wms.Domain.Common;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;

namespace Wms.Domain.Entities;

/// <summary>
/// Demand-level reservation identity. Allocation rows are the source of truth
/// for quantities; this aggregate stores the demand contract and lifecycle.
/// </summary>
public sealed class InventoryReservation : Entity
{
    private InventoryReservation()
    {
    }

    public InventoryReservation(
        string demandType,
        string demandId,
        int? demandLine,
        int warehouseId,
        int itemId,
        decimal requestedQuantity,
        InventoryReservationMode mode,
        int priority,
        DateTime? expiresAtUtc,
        string actorUserId,
        string correlationId,
        string? reason = null,
        InventoryReservationSelector? selector = null)
    {
        DemandType = NormalizeRequired(demandType, 100, nameof(demandType));
        DemandId = NormalizeRequired(demandId, 200, nameof(demandId));
        if (demandLine is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(demandLine));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedQuantity);
        ArgumentOutOfRangeException.ThrowIfNegative(priority);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        WarehouseId = warehouseId;
        ItemId = itemId;
        DemandLine = demandLine;
        DemandKey = BuildDemandKey(DemandType, DemandId, DemandLine);
        RequestedQuantity = requestedQuantity;
        Mode = mode;
        Priority = priority;
        Status = InventoryReservationStatus.Pending;
        ExpiresAtUtc = NormalizeUtc(expiresAtUtc);
        ActorUserId = NormalizeRequired(actorUserId, 450, nameof(actorUserId));
        CorrelationId = NormalizeRequired(correlationId, 100, nameof(correlationId));
        StatusReason = NormalizeOptional(reason, 1_000);
        SelectorLocationId = selector?.LocationId;
        SelectorLotId = selector?.LotId;
        SelectorSerialNumberId = selector?.SerialNumberId;
        SelectorSerialNumber = NormalizeOptional(selector?.SerialNumber, 100);
        SelectorLicensePlateId = selector?.LicensePlateId;
        SelectorInventoryStatusId = selector?.InventoryStatusId;
        SelectorBaseUnitOfMeasure = NormalizeOptional(selector?.BaseUnitOfMeasure, 20)?.ToUpperInvariant();
        SelectorOwnerKind = selector?.OwnerKind ?? InventoryOwnerKind.CompanyOwned;
        SelectorInventoryOwnerId = selector?.InventoryOwnerId;
        SelectorOwnerCodeSnapshot = NormalizeOptional(selector?.OwnerCodeSnapshot, 80)?.ToUpperInvariant();
    }

    public string DemandType { get; private set; } = string.Empty;
    public string DemandId { get; private set; } = string.Empty;
    public int? DemandLine { get; private set; }
    public string DemandKey { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public int ItemId { get; private set; }
    public decimal RequestedQuantity { get; private set; }
    public InventoryReservationMode Mode { get; private set; }
    public int Priority { get; private set; }
    public InventoryReservationStatus Status { get; private set; } = InventoryReservationStatus.Pending;
    public string? StatusReason { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public int? SelectorLocationId { get; private set; }
    public int? SelectorLotId { get; private set; }
    public int? SelectorSerialNumberId { get; private set; }
    public string? SelectorSerialNumber { get; private set; }
    public int? SelectorLicensePlateId { get; private set; }
    public int? SelectorInventoryStatusId { get; private set; }
    public string? SelectorBaseUnitOfMeasure { get; private set; }
    public InventoryOwnerKind SelectorOwnerKind { get; private set; }
    public int? SelectorInventoryOwnerId { get; private set; }
    public string? SelectorOwnerCodeSnapshot { get; private set; }
    public string ActorUserId { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public int? AllocationStrategyPolicyId { get; private set; }
    public string? AllocationStrategyKey { get; private set; }
    public InventoryAllocationStrategyKind? AllocationStrategy { get; private set; }
    public long? AllocationStrategyRevision { get; private set; }
    public int? AllocationStrategyFixedLocationId { get; private set; }
    public bool AllocationStrategyPreferWholeLicensePlate { get; private set; }
    public int AllocationStrategyMinimumShelfLifeDays { get; private set; }
    public InventoryAllocationMissingExpiryFallback? AllocationStrategyMissingExpiryFallback { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public ICollection<InventoryReservationAllocation> Allocations { get; private set; } =
        new List<InventoryReservationAllocation>();
    public ICollection<InventoryReservationEvent> Events { get; private set; } =
        new List<InventoryReservationEvent>();

    public bool IsOpen => Status is
        InventoryReservationStatus.Pending or
        InventoryReservationStatus.PartiallyReserved or
        InventoryReservationStatus.Reserved or
        InventoryReservationStatus.PartiallyConsumed;

    public bool CanReallocate => Status is
        InventoryReservationStatus.Pending or
        InventoryReservationStatus.PartiallyReserved or
        InventoryReservationStatus.Reserved or
        InventoryReservationStatus.PartiallyConsumed or
        InventoryReservationStatus.Released;

    public InventoryReservationSelector? GetSelector() =>
        SelectorLocationId.HasValue ||
        SelectorLotId.HasValue ||
        SelectorSerialNumberId.HasValue ||
        !string.IsNullOrWhiteSpace(SelectorSerialNumber) ||
        SelectorLicensePlateId.HasValue ||
        SelectorInventoryStatusId.HasValue ||
        !string.IsNullOrWhiteSpace(SelectorBaseUnitOfMeasure)
            || SelectorOwnerKind != InventoryOwnerKind.CompanyOwned
            || SelectorInventoryOwnerId.HasValue
            || !string.IsNullOrWhiteSpace(SelectorOwnerCodeSnapshot)
            ? new InventoryReservationSelector(
                SelectorLocationId,
                SelectorLotId,
                SelectorSerialNumberId,
                SelectorSerialNumber,
                SelectorLicensePlateId,
                SelectorInventoryStatusId,
                SelectorBaseUnitOfMeasure,
                SelectorOwnerKind,
                SelectorInventoryOwnerId,
                SelectorOwnerCodeSnapshot)
            : null;

    public void SetAllocationStrategySnapshot(
        int? policyId,
        string strategyKey,
        InventoryAllocationStrategyKind strategy,
        long revision,
        int? fixedLocationId = null,
        bool preferWholeLicensePlate = false,
        int minimumShelfLifeDays = 0,
        InventoryAllocationMissingExpiryFallback missingExpiryFallback = InventoryAllocationMissingExpiryFallback.Last)
    {
        if (AllocationStrategyKey is not null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(strategyKey) || strategyKey.Length > 80)
        {
            throw new ArgumentException("An allocation strategy key between 1 and 80 characters is required.", nameof(strategyKey));
        }

        if (!Enum.IsDefined(strategy) || revision < 0 || minimumShelfLifeDays < 0 ||
            !Enum.IsDefined(missingExpiryFallback))
        {
            throw new ArgumentOutOfRangeException(nameof(strategy));
        }

        AllocationStrategyPolicyId = policyId;
        AllocationStrategyKey = strategyKey.Trim();
        AllocationStrategy = strategy;
        AllocationStrategyRevision = revision;
        AllocationStrategyFixedLocationId = fixedLocationId;
        AllocationStrategyPreferWholeLicensePlate = preferWholeLicensePlate;
        AllocationStrategyMinimumShelfLifeDays = minimumShelfLifeDays;
        AllocationStrategyMissingExpiryFallback = missingExpiryFallback;
        Touch();
    }

    public void SetStatus(InventoryReservationStatus status, string? reason = null)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (status == Status)
        {
            if (!string.IsNullOrWhiteSpace(reason))
            {
                StatusReason = NormalizeOptional(reason, 1_000);
                Touch();
            }

            return;
        }

        if (!CanTransitionTo(status))
        {
            throw new InvalidOperationException(
                $"Reservation '{Id}' cannot transition from {Status} to {status}.");
        }

        Status = status;
        StatusReason = NormalizeOptional(reason, 1_000);
        Touch();
    }

    public static string BuildDemandKey(
        string demandType,
        string demandId,
        int? demandLine)
    {
        var type = NormalizeRequired(demandType, 100, nameof(demandType));
        var id = NormalizeRequired(demandId, 200, nameof(demandId));
        return $"{type}:{id}:{demandLine?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "*"}";
    }

    private bool CanTransitionTo(InventoryReservationStatus target)
    {
        if (target is InventoryReservationStatus.Cancelled or InventoryReservationStatus.Expired)
        {
            return IsOpen;
        }

        return Status switch
        {
            InventoryReservationStatus.Pending => target is
                InventoryReservationStatus.PartiallyReserved or
                InventoryReservationStatus.Reserved or
                InventoryReservationStatus.Released,
            InventoryReservationStatus.PartiallyReserved => target is
                InventoryReservationStatus.Reserved or
                InventoryReservationStatus.PartiallyConsumed or
                InventoryReservationStatus.Released,
            InventoryReservationStatus.Reserved => target is
                InventoryReservationStatus.PartiallyConsumed or
                InventoryReservationStatus.Consumed or
                InventoryReservationStatus.Released,
            InventoryReservationStatus.PartiallyConsumed => target is
                InventoryReservationStatus.Consumed or
                InventoryReservationStatus.Released,
            InventoryReservationStatus.Released => target == InventoryReservationStatus.Pending,
            _ => false
        };
    }

    private void Touch()
    {
        Revision++;
        SetUpdatedAt();
    }

    private static string NormalizeRequired(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value.HasValue
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : null;
}
