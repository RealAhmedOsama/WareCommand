using Wms.Domain.Common;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;

namespace Wms.Domain.Entities;

public sealed class ValueAddedServiceOrderLine : Entity
{
    private ValueAddedServiceOrderLine()
    {
    }

    public ValueAddedServiceOrderLine(
        int lineNumber,
        ValueAddedServiceLineKind kind,
        int itemId,
        decimal plannedQuantity,
        string baseUnitOfMeasure,
        int inventoryStatusId,
        int? sourceLocationId = null,
        int? destinationLocationId = null,
        int? lotId = null,
        int? serialNumberId = null,
        string? serialNumber = null,
        int? licensePlateId = null,
        InventoryOwnerKind ownerKind = InventoryOwnerKind.CompanyOwned,
        int? inventoryOwnerId = null,
        string? ownerCodeSnapshot = null,
        int? kitDefinitionLineId = null,
        int? reservationId = null,
        int? reservationAllocationId = null,
        bool isSubstitution = false,
        int? substitutedForItemId = null,
        string? notes = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineNumber);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(plannedQuantity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inventoryStatusId);
        if (sourceLocationId is <= 0 || destinationLocationId is <= 0 || lotId is <= 0 ||
            serialNumberId is <= 0 || licensePlateId is <= 0 || kitDefinitionLineId is <= 0 ||
            reservationId is <= 0 || reservationAllocationId is <= 0 || substitutedForItemId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceLocationId));
        }

        LineNumber = lineNumber;
        Kind = kind;
        ItemId = itemId;
        PlannedQuantity = plannedQuantity;
        BaseUnitOfMeasure = Required(baseUnitOfMeasure, 20, nameof(baseUnitOfMeasure)).ToUpperInvariant();
        InventoryStatusId = inventoryStatusId;
        SourceLocationId = sourceLocationId;
        DestinationLocationId = destinationLocationId;
        LotId = lotId;
        SerialNumberId = serialNumberId;
        SerialNumber = Optional(serialNumber, 100);
        LicensePlateId = licensePlateId;
        OwnerKind = ownerKind;
        InventoryOwnerId = inventoryOwnerId;
        OwnerCodeSnapshot = InventoryOwnershipDimension.NormalizeOwnerCode(
            ownerKind,
            inventoryOwnerId,
            ownerCodeSnapshot);
        KitDefinitionLineId = kitDefinitionLineId;
        ReservationId = reservationId;
        ReservationAllocationId = reservationAllocationId;
        IsSubstitution = isSubstitution;
        SubstitutedForItemId = substitutedForItemId;
        Notes = Optional(notes, 1_000);
        Revision = 1;
    }

    public int ValueAddedServiceOrderId { get; private set; }
    public int LineNumber { get; private set; }
    public ValueAddedServiceLineKind Kind { get; private set; }
    public int ItemId { get; private set; }
    public decimal PlannedQuantity { get; private set; }
    public decimal ConsumedQuantity { get; private set; }
    public decimal ProducedQuantity { get; private set; }
    public decimal ScrapQuantity { get; private set; }
    public decimal ReversedQuantity { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public int InventoryStatusId { get; private set; }
    public int? SourceLocationId { get; private set; }
    public int? DestinationLocationId { get; private set; }
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public string? SerialNumber { get; private set; }
    public int? LicensePlateId { get; private set; }
    public InventoryOwnerKind OwnerKind { get; private set; }
    public int? InventoryOwnerId { get; private set; }
    public string OwnerCodeSnapshot { get; private set; } = InventoryOwnershipDimension.CompanyOwnerCode;
    public int? KitDefinitionLineId { get; private set; }
    public int? ReservationId { get; private set; }
    public int? ReservationAllocationId { get; private set; }
    public bool IsSubstitution { get; private set; }
    public int? SubstitutedForItemId { get; private set; }
    public string? Notes { get; private set; }
    public long Revision { get; private set; }

    public ValueAddedServiceOrder Order { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public Location? SourceLocation { get; private set; }
    public Location? DestinationLocation { get; private set; }
    public Lot? Lot { get; private set; }
    public SerialNumber? Serial { get; private set; }
    public LicensePlate? LicensePlate { get; private set; }
    public InventoryStatus InventoryStatus { get; private set; } = null!;
    public InventoryOwner? InventoryOwner { get; private set; }
    public KitDefinitionLine? KitDefinitionLine { get; private set; }
    public InventoryReservation? Reservation { get; private set; }
    public InventoryReservationAllocation? ReservationAllocation { get; private set; }

    public decimal RemainingQuantity =>
        PlannedQuantity - ConsumedQuantity - ScrapQuantity;

    public void RecordConsumed(decimal quantity)
    {
        EnsureInput(quantity);
        if (ConsumedQuantity + ScrapQuantity + quantity > PlannedQuantity)
        {
            throw new InvalidOperationException("VAS input consumption exceeds the planned quantity.");
        }

        ConsumedQuantity += quantity;
        Touch();
    }

    public void RecordScrap(decimal quantity)
    {
        EnsureInput(quantity);
        if (ConsumedQuantity + ScrapQuantity + quantity > PlannedQuantity)
        {
            throw new InvalidOperationException("VAS scrap exceeds the planned input quantity.");
        }

        ScrapQuantity += quantity;
        Touch();
    }

    public void RecordProduced(decimal quantity)
    {
        if (Kind != ValueAddedServiceLineKind.Output)
        {
            throw new InvalidOperationException("Only output lines can record production.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (ProducedQuantity + quantity > PlannedQuantity)
        {
            throw new InvalidOperationException("VAS production exceeds the planned output quantity.");
        }

        ProducedQuantity += quantity;
        Touch();
    }

    public void RecordReversed(decimal quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        var reversible = Kind == ValueAddedServiceLineKind.Output
            ? ProducedQuantity
            : ConsumedQuantity + ScrapQuantity;
        if (ReversedQuantity + quantity > reversible)
        {
            throw new InvalidOperationException("VAS reversal exceeds the completed line quantity.");
        }

        ReversedQuantity += quantity;
        Touch();
    }

    public void ReplaceWithSubstitution(
        int itemId,
        decimal plannedQuantity,
        string baseUnitOfMeasure,
        int? substitutedForItemId)
    {
        if (Kind is not (ValueAddedServiceLineKind.Input or ValueAddedServiceLineKind.AdditionalInput))
        {
            throw new InvalidOperationException("Only input lines can use an approved substitution.");
        }

        if (ConsumedQuantity != 0m || ScrapQuantity != 0m || ReservationId.HasValue)
        {
            throw new InvalidOperationException("A reserved or executed VAS input cannot be substituted.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(plannedQuantity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(substitutedForItemId ?? 1);
        ItemId = itemId;
        PlannedQuantity = plannedQuantity;
        BaseUnitOfMeasure = Required(baseUnitOfMeasure, 20, nameof(baseUnitOfMeasure)).ToUpperInvariant();
        LotId = null;
        SerialNumberId = null;
        SerialNumber = null;
        LicensePlateId = null;
        IsSubstitution = true;
        SubstitutedForItemId = substitutedForItemId;
        Touch();
    }

    public void AttachReservation(
        int reservationId,
        InventoryReservationAllocationResult allocation)
    {
        if (Kind is not (ValueAddedServiceLineKind.Input or ValueAddedServiceLineKind.AdditionalInput))
        {
            throw new InvalidOperationException("Only input lines can be linked to a reservation.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(reservationId);
        ArgumentNullException.ThrowIfNull(allocation);
        if (allocation.ItemId != ItemId)
        {
            throw new InvalidOperationException("The reservation allocation item does not match the VAS input line.");
        }

        SourceLocationId = allocation.LocationId;
        LotId = allocation.LotId;
        SerialNumberId = allocation.SerialNumberId;
        SerialNumber = Optional(allocation.SerialNumber, 100);
        LicensePlateId = allocation.LicensePlateId;
        InventoryStatusId = allocation.InventoryStatusId;
        BaseUnitOfMeasure = Required(allocation.BaseUnitOfMeasure, 20, nameof(allocation.BaseUnitOfMeasure))
            .ToUpperInvariant();
        OwnerKind = allocation.OwnerKind;
        InventoryOwnerId = allocation.InventoryOwnerId;
        OwnerCodeSnapshot = InventoryOwnershipDimension.NormalizeOwnerCode(
            allocation.OwnerKind,
            allocation.InventoryOwnerId,
            allocation.OwnerCodeSnapshot);
        PlannedQuantity = allocation.AllocatedQuantity;
        ReservationId = reservationId;
        ReservationAllocationId = allocation.AllocationId;
        Touch();
    }

    public void SetProducedDimension(
        int? lotId,
        int? serialNumberId,
        string? serialNumber,
        int? licensePlateId,
        int inventoryStatusId,
        InventoryOwnerKind ownerKind,
        int? inventoryOwnerId,
        string? ownerCodeSnapshot)
    {
        if (Kind != ValueAddedServiceLineKind.Output)
        {
            throw new InvalidOperationException("Only output lines can set a produced inventory dimension.");
        }

        if (lotId is <= 0 || serialNumberId is <= 0 || licensePlateId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lotId));
        }

        LotId = lotId;
        SerialNumberId = serialNumberId;
        SerialNumber = Optional(serialNumber, 100);
        LicensePlateId = licensePlateId;
        InventoryStatusId = inventoryStatusId;
        OwnerKind = ownerKind;
        InventoryOwnerId = inventoryOwnerId;
        OwnerCodeSnapshot = InventoryOwnershipDimension.NormalizeOwnerCode(
            ownerKind,
            inventoryOwnerId,
            ownerCodeSnapshot);
        Touch();
    }

    private void EnsureInput(decimal quantity)
    {
        if (Kind is not (ValueAddedServiceLineKind.Input or ValueAddedServiceLineKind.AdditionalInput))
        {
            throw new InvalidOperationException("Only input lines can consume or record scrap.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
    }

    private void Touch()
    {
        Revision++;
        SetUpdatedAt();
    }

    private static string Required(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string? Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Length <= maximumLength
                ? value.Trim()
                : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.");
}
