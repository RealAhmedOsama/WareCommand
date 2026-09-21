using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Explainable inbound-to-outbound match snapshot. This aggregate is planning
/// state only until a later execution boundary creates a reservation and work.
/// </summary>
public sealed class CrossDockPlan : Entity
{
    private readonly List<CrossDockPlanLine> _lines = [];

    private CrossDockPlan()
    {
    }

    public CrossDockPlan(
        string planNumber,
        string creationKey,
        int warehouseId,
        int receiptId,
        int receiptLineId,
        int itemId,
        int policyId,
        CrossDockMode mode,
        decimal receivedBaseQuantity,
        decimal matchedBaseQuantity,
        decimal fallbackBaseQuantity,
        int sourceLocationId,
        int? destinationLocationId,
        int inventoryStatusId,
        string baseUnitOfMeasure,
        string? lotNumber,
        DateTime? expiryDate,
        string? serialNumber,
        int? licensePlateId,
        string explanation,
        string createdByUserId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(receiptId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(receiptLineId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(policyId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceLocationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inventoryStatusId);
        if (destinationLocationId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationLocationId));
        }

        if (!Enum.IsDefined(mode) || receivedBaseQuantity < 0m || matchedBaseQuantity < 0m ||
            fallbackBaseQuantity < 0m || matchedBaseQuantity + fallbackBaseQuantity != receivedBaseQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(receivedBaseQuantity));
        }

        PlanNumber = Required(planNumber, 80, nameof(planNumber));
        CreationKey = Required(creationKey, 250, nameof(creationKey));
        WarehouseId = warehouseId;
        ReceiptId = receiptId;
        ReceiptLineId = receiptLineId;
        ItemId = itemId;
        PolicyId = policyId;
        Mode = mode;
        ReceivedBaseQuantity = receivedBaseQuantity;
        MatchedBaseQuantity = matchedBaseQuantity;
        FallbackBaseQuantity = fallbackBaseQuantity;
        SourceLocationId = sourceLocationId;
        DestinationLocationId = destinationLocationId;
        InventoryStatusId = inventoryStatusId;
        BaseUnitOfMeasure = Required(baseUnitOfMeasure, 20, nameof(baseUnitOfMeasure)).ToUpperInvariant();
        LotNumber = Optional(lotNumber, 100);
        ExpiryDate = expiryDate.HasValue ? Entity.NormalizeUtc(expiryDate.Value) : null;
        SerialNumber = Optional(serialNumber, 100);
        LicensePlateId = licensePlateId;
        Explanation = Required(explanation, 2_000, nameof(explanation));
        CreatedByUserId = Required(createdByUserId, 450, nameof(createdByUserId));
        Status = matchedBaseQuantity > 0m
            ? CrossDockPlanStatus.Matched
            : CrossDockPlanStatus.Fallback;
        Revision = 1;
    }

    public string PlanNumber { get; private set; } = string.Empty;
    public string CreationKey { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public int ReceiptId { get; private set; }
    public int ReceiptLineId { get; private set; }
    public int ItemId { get; private set; }
    public int PolicyId { get; private set; }
    public CrossDockMode Mode { get; private set; }
    public decimal ReceivedBaseQuantity { get; private set; }
    public decimal MatchedBaseQuantity { get; private set; }
    public decimal FallbackBaseQuantity { get; private set; }
    public int SourceLocationId { get; private set; }
    public int? DestinationLocationId { get; private set; }
    public int InventoryStatusId { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public string? LotNumber { get; private set; }
    public DateTime? ExpiryDate { get; private set; }
    public string? SerialNumber { get; private set; }
    public int? LicensePlateId { get; private set; }
    public CrossDockPlanStatus Status { get; private set; }
    public string Explanation { get; private set; } = string.Empty;
    public string CreatedByUserId { get; private set; } = string.Empty;
    public string? CancelledByUserId { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Receipt Receipt { get; private set; } = null!;
    public ReceiptLine ReceiptLine { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public CrossDockPolicy Policy { get; private set; } = null!;
    public Location SourceLocation { get; private set; } = null!;
    public Location? DestinationLocation { get; private set; }
    public IReadOnlyList<CrossDockPlanLine> Lines => _lines.AsReadOnly();

    public bool CanCancel => Status is CrossDockPlanStatus.Proposed or
        CrossDockPlanStatus.Matched or CrossDockPlanStatus.Fallback or
        CrossDockPlanStatus.ReservationPending or CrossDockPlanStatus.WorkPending;

    public void AddLine(CrossDockPlanLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!CanCancel)
        {
            throw new InvalidOperationException($"A cross-dock plan in {Status} cannot add matches.");
        }

        if (_lines.Any(existing => existing.Sequence == line.Sequence))
        {
            throw new InvalidOperationException("Cross-dock line sequence values must be unique.");
        }

        _lines.Add(line);
        Revision++;
        SetUpdatedAt();
    }

    public void Cancel(string userId, string reason, DateTime cancelledAtUtc)
    {
        if (!CanCancel)
        {
            throw new InvalidOperationException($"A cross-dock plan in {Status} cannot be cancelled.");
        }

        CancelledByUserId = Required(userId, 450, nameof(userId));
        CancellationReason = Required(reason, 1_000, nameof(reason));
        CancelledAtUtc = NormalizeUtc(cancelledAtUtc);
        Status = CrossDockPlanStatus.Cancelled;
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

    private static string? Optional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.");
    }

}
