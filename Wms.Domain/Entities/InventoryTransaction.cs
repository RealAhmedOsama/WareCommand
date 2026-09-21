using Wms.Domain.Common;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;

namespace Wms.Domain.Entities;

/// <summary>
/// Immutable append-only inventory ledger row.
/// </summary>
public sealed class InventoryTransaction : Entity
{
    private InventoryTransaction()
    {
    }

    public InventoryTransaction(
        InventoryTransactionType type,
        InventoryBalanceKey key,
        decimal quantityDelta,
        decimal quantityBefore,
        decimal quantityAfter,
        decimal reservedQuantityDelta,
        decimal reservedQuantityBefore,
        decimal reservedQuantityAfter,
        string actorUserId,
        DateTime occurredAtUtc,
        string correlationId,
        string idempotencyKey,
        string transactionGroupId,
        int entrySequence,
        string? referenceType = null,
        string? referenceId = null,
        int? referenceLine = null,
        string? reason = null,
        int? movementId = null,
        int? reversalOfTransactionId = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("Actor user ID is required.", nameof(actorUserId));
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("Correlation ID is required.", nameof(correlationId));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));
        }

        if (string.IsNullOrWhiteSpace(transactionGroupId))
        {
            throw new ArgumentException("Transaction group ID is required.", nameof(transactionGroupId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entrySequence);
        EnsureBalanceEquation(quantityBefore, quantityDelta, quantityAfter, nameof(quantityAfter));
        EnsureBalanceEquation(
            reservedQuantityBefore,
            reservedQuantityDelta,
            reservedQuantityAfter,
            nameof(reservedQuantityAfter));

        WarehouseId = key.WarehouseId;
        LocationId = key.LocationId;
        ItemId = key.ItemId;
        LotId = key.LotId;
        SerialNumberId = key.SerialNumberId;
        SerialNumber = key.SerialNumber;
        LicensePlateId = key.LicensePlateId;
        InventoryStatusId = key.InventoryStatusId;
        BaseUnitOfMeasure = key.BaseUnitOfMeasure;
        OwnerKind = key.OwnerKind;
        InventoryOwnerId = key.InventoryOwnerId;
        OwnerCodeSnapshot = key.OwnerCodeSnapshot;
        Type = type;
        QuantityDelta = quantityDelta;
        QuantityBefore = quantityBefore;
        QuantityAfter = quantityAfter;
        ReservedQuantityDelta = reservedQuantityDelta;
        ReservedQuantityBefore = reservedQuantityBefore;
        ReservedQuantityAfter = reservedQuantityAfter;
        ReferenceType = Trim(referenceType, 100);
        ReferenceId = Trim(referenceId, 200);
        ReferenceLine = referenceLine;
        Reason = Trim(reason, 1_000);
        ActorUserId = TrimRequired(actorUserId, 450);
        OccurredAtUtc = NormalizeUtc(occurredAtUtc);
        CorrelationId = TrimRequired(correlationId, 100);
        IdempotencyKey = TrimRequired(idempotencyKey, 250);
        TransactionGroupId = TrimRequired(transactionGroupId, 100);
        EntrySequence = entrySequence;
        MovementId = movementId;
        ReversalOfTransactionId = reversalOfTransactionId;
    }

    public int WarehouseId { get; private set; }
    public int LocationId { get; private set; }
    public int ItemId { get; private set; }
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public string? SerialNumber { get; private set; }
    public int? LicensePlateId { get; private set; }
    public int InventoryStatusId { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public InventoryOwnerKind OwnerKind { get; private set; }
    public int? InventoryOwnerId { get; private set; }
    public string OwnerCodeSnapshot { get; private set; } = InventoryOwnershipDimension.CompanyOwnerCode;
    public InventoryTransactionType Type { get; private set; }
    public decimal QuantityDelta { get; private set; }
    public decimal QuantityBefore { get; private set; }
    public decimal QuantityAfter { get; private set; }
    public decimal ReservedQuantityDelta { get; private set; }
    public decimal ReservedQuantityBefore { get; private set; }
    public decimal ReservedQuantityAfter { get; private set; }
    public string? ReferenceType { get; private set; }
    public string? ReferenceId { get; private set; }
    public int? ReferenceLine { get; private set; }
    public string? Reason { get; private set; }
    public string ActorUserId { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; } = DateTime.UnixEpoch;
    public string CorrelationId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string TransactionGroupId { get; private set; } = string.Empty;
    public int EntrySequence { get; private set; }
    public int? MovementId { get; private set; }
    public int? ReversalOfTransactionId { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Location Location { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public Lot? Lot { get; private set; }
    public SerialNumber? Serial { get; private set; }
    public LicensePlate? LicensePlate { get; private set; }
    public InventoryStatus InventoryStatus { get; private set; } = null!;
    public InventoryOwner? InventoryOwner { get; private set; }
    public Movement? Movement { get; private set; }
    public InventoryTransaction? ReversalOfTransaction { get; private set; }

    private static void EnsureBalanceEquation(
        decimal before,
        decimal delta,
        decimal after,
        string parameterName)
    {
        if (before + delta != after)
        {
            throw new ArgumentException(
                "The ledger after quantity must equal before quantity plus delta.",
                parameterName);
        }
    }

    private static string TrimRequired(string value, int maximumLength)
    {
        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static string? Trim(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}
