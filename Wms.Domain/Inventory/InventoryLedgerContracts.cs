using Wms.Domain.Enums;

namespace Wms.Domain.Inventory;

/// <summary>
/// The complete identity of one materialized inventory balance.
/// </summary>
public sealed record InventoryBalanceKey
{
    public InventoryBalanceKey(
        int warehouseId,
        int locationId,
        int itemId,
        int? lotId,
        int? serialNumberId,
        string? serialNumber,
        int? licensePlateId,
        int inventoryStatusId,
        string baseUnitOfMeasure)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(locationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inventoryStatusId);

        if (string.IsNullOrWhiteSpace(baseUnitOfMeasure))
        {
            throw new ArgumentException("Base unit of measure is required.", nameof(baseUnitOfMeasure));
        }

        WarehouseId = warehouseId;
        LocationId = locationId;
        ItemId = itemId;
        LotId = lotId;
        SerialNumberId = serialNumberId;
        SerialNumber = string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber.Trim();
        LicensePlateId = licensePlateId;
        InventoryStatusId = inventoryStatusId;
        BaseUnitOfMeasure = baseUnitOfMeasure.Trim().ToUpperInvariant();
    }

    public int WarehouseId { get; }
    public int LocationId { get; }
    public int ItemId { get; }
    public int? LotId { get; }
    public int? SerialNumberId { get; }
    public string? SerialNumber { get; }
    public int? LicensePlateId { get; }
    public int InventoryStatusId { get; }
    public string BaseUnitOfMeasure { get; }
}

/// <summary>
/// One requested append-only ledger leg. A move is represented by two legs:
/// an outbound negative delta and an inbound positive delta.
/// </summary>
public sealed record InventoryLedgerEntryRequest(
    InventoryTransactionType Type,
    InventoryBalanceKey Key,
    decimal QuantityDelta,
    decimal ReservedQuantityDelta = 0m,
    string? ReferenceType = null,
    string? ReferenceId = null,
    int? ReferenceLine = null,
    string? Reason = null,
    string ActorUserId = "system",
    DateTime? OccurredAtUtc = null,
    string? CorrelationId = null,
    string? IdempotencyKey = null,
    string? TransactionGroupId = null,
    int EntrySequence = 1,
    int? MovementId = null,
    int? ReversalOfTransactionId = null);

public sealed record InventoryReconciliationIssue(
    InventoryBalanceKey Key,
    decimal MaterializedOnHand,
    decimal LedgerOnHand,
    decimal MaterializedReserved,
    decimal LedgerReserved,
    string Code);

public sealed record InventoryReconciliationReport(
    int BalanceCount,
    int TransactionCount,
    IReadOnlyList<InventoryReconciliationIssue> Issues)
{
    public bool IsBalanced => Issues.Count == 0;
}
