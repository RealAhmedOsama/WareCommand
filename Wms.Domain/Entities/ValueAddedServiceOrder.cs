using Wms.Domain.Common;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;

namespace Wms.Domain.Entities;

public sealed class ValueAddedServiceOrder : Entity
{
    private readonly List<ValueAddedServiceOrderLine> _lines = [];

    private ValueAddedServiceOrder()
    {
    }

    public ValueAddedServiceOrder(
        string orderNumber,
        string idempotencyKey,
        ValueAddedServiceType type,
        int warehouseId,
        int sourceLocationId,
        int destinationLocationId,
        int outputItemId,
        decimal requestedOutputQuantity,
        string outputUnitOfMeasure,
        string createdByUserId,
        DateTime createdAtUtc,
        int? kitDefinitionId = null,
        int? kitVersionSnapshot = null,
        string? instructionSnapshot = null,
        string? localizedInstructionSnapshot = null,
        string? stationCode = null,
        string? labelTemplateCode = null,
        int? qualityProfileId = null,
        InventoryOwnerKind outputOwnerKind = InventoryOwnerKind.CompanyOwned,
        int? outputInventoryOwnerId = null,
        string? outputOwnerCodeSnapshot = null,
        InventoryOwnerKind inputOwnerKind = InventoryOwnerKind.CompanyOwned,
        int? inputInventoryOwnerId = null,
        string? inputOwnerCodeSnapshot = null,
        int? inputInventoryStatusId = null,
        int? inputLotId = null,
        int? inputSerialNumberId = null,
        string? inputSerialNumber = null,
        int? inputLicensePlateId = null)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceLocationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(destinationLocationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputItemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedOutputQuantity);
        if (kitDefinitionId is <= 0 || kitVersionSnapshot is <= 0 || qualityProfileId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(kitDefinitionId));
        }

        if (inputInventoryOwnerId is <= 0 || inputInventoryStatusId is <= 0 || inputLotId is <= 0 ||
            inputSerialNumberId is <= 0 || inputLicensePlateId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputInventoryOwnerId));
        }

        OrderNumber = Required(orderNumber, 80, nameof(orderNumber));
        IdempotencyKey = Required(idempotencyKey, 250, nameof(idempotencyKey));
        Type = type;
        WarehouseId = warehouseId;
        SourceLocationId = sourceLocationId;
        DestinationLocationId = destinationLocationId;
        OutputItemId = outputItemId;
        RequestedOutputQuantity = requestedOutputQuantity;
        OutputUnitOfMeasure = Required(outputUnitOfMeasure, 20, nameof(outputUnitOfMeasure)).ToUpperInvariant();
        CreatedByUserId = Required(createdByUserId, 450, nameof(createdByUserId));
        CreatedAtUtc = NormalizeUtc(createdAtUtc);
        KitDefinitionId = kitDefinitionId;
        KitVersionSnapshot = kitVersionSnapshot;
        InstructionSnapshot = Optional(instructionSnapshot, 8_000);
        LocalizedInstructionSnapshot = Optional(localizedInstructionSnapshot, 8_000);
        StationCode = OptionalUpper(stationCode, 50);
        LabelTemplateCode = Optional(labelTemplateCode, 120);
        QualityProfileId = qualityProfileId;
        OutputOwnerKind = outputOwnerKind;
        OutputInventoryOwnerId = outputInventoryOwnerId;
        OutputOwnerCodeSnapshot = InventoryOwnershipDimension.NormalizeOwnerCode(
            outputOwnerKind,
            outputInventoryOwnerId,
            outputOwnerCodeSnapshot);
        InputOwnerKind = inputOwnerKind;
        InputInventoryOwnerId = inputInventoryOwnerId;
        InputOwnerCodeSnapshot = InventoryOwnershipDimension.NormalizeOwnerCode(
            inputOwnerKind,
            inputInventoryOwnerId,
            inputOwnerCodeSnapshot);
        InputInventoryStatusId = inputInventoryStatusId;
        InputLotId = inputLotId;
        InputSerialNumberId = inputSerialNumberId;
        InputSerialNumber = string.IsNullOrWhiteSpace(inputSerialNumber)
            ? null
            : inputSerialNumber.Trim().ToUpperInvariant();
        InputLicensePlateId = inputLicensePlateId;
        Status = ValueAddedServiceOrderStatus.Draft;
        Revision = 1;
    }

    public string OrderNumber { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public ValueAddedServiceType Type { get; private set; }
    public int WarehouseId { get; private set; }
    public int SourceLocationId { get; private set; }
    public int DestinationLocationId { get; private set; }
    public int OutputItemId { get; private set; }
    public decimal RequestedOutputQuantity { get; private set; }
    public decimal CompletedOutputQuantity { get; private set; }
    public decimal ScrapQuantity { get; private set; }
    public decimal ReversedOutputQuantity { get; private set; }
    public string OutputUnitOfMeasure { get; private set; } = string.Empty;
    public int? KitDefinitionId { get; private set; }
    public int? KitVersionSnapshot { get; private set; }
    public string? InstructionSnapshot { get; private set; }
    public string? LocalizedInstructionSnapshot { get; private set; }
    public string? StationCode { get; private set; }
    public string? LabelTemplateCode { get; private set; }
    public int? QualityProfileId { get; private set; }
    public InventoryOwnerKind OutputOwnerKind { get; private set; }
    public int? OutputInventoryOwnerId { get; private set; }
    public string OutputOwnerCodeSnapshot { get; private set; } = InventoryOwnershipDimension.CompanyOwnerCode;
    public InventoryOwnerKind InputOwnerKind { get; private set; }
    public int? InputInventoryOwnerId { get; private set; }
    public string InputOwnerCodeSnapshot { get; private set; } = InventoryOwnershipDimension.CompanyOwnerCode;
    public int? InputInventoryStatusId { get; private set; }
    public int? InputLotId { get; private set; }
    public int? InputSerialNumberId { get; private set; }
    public string? InputSerialNumber { get; private set; }
    public int? InputLicensePlateId { get; private set; }
    public ValueAddedServiceOrderStatus Status { get; private set; }
    public int? WarehouseWorkId { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReleasedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancelledByUserId { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTime? ReversedAtUtc { get; private set; }
    public string? ReversedByUserId { get; private set; }
    public string? ReversalReason { get; private set; }
    public string? ExceptionReason { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Location SourceLocation { get; private set; } = null!;
    public Location DestinationLocation { get; private set; } = null!;
    public Item OutputItem { get; private set; } = null!;
    public KitDefinition? KitDefinition { get; private set; }
    public WarehouseWork? WarehouseWork { get; private set; }
    public ICollection<ValueAddedServiceOrderLine> Lines => _lines;
    public ICollection<ValueAddedServiceTraceLink> TraceLinks { get; private set; } =
        new List<ValueAddedServiceTraceLink>();
    public bool IsTerminal => Status is
        ValueAddedServiceOrderStatus.Completed or
        ValueAddedServiceOrderStatus.Cancelled or
        ValueAddedServiceOrderStatus.Reversed;
    public decimal RemainingOutputQuantity => RequestedOutputQuantity - CompletedOutputQuantity;

    public void AddLine(ValueAddedServiceOrderLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (IsTerminal)
        {
            throw new InvalidOperationException($"VAS order '{OrderNumber}' is terminal.");
        }

        if (_lines.Any(existing => existing.LineNumber == line.LineNumber))
        {
            throw new InvalidOperationException("VAS line numbers must be unique within an order.");
        }

        _lines.Add(line);
        Touch();
    }

    public void SetWarehouseWork(int workId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workId);
        if (WarehouseWorkId.HasValue && WarehouseWorkId != workId)
        {
            throw new InvalidOperationException("The VAS order is already linked to another work item.");
        }

        WarehouseWorkId = workId;
        Touch();
    }

    public void Release(DateTime releasedAtUtc)
    {
        if (Status != ValueAddedServiceOrderStatus.Draft)
        {
            throw new InvalidOperationException($"VAS order '{OrderNumber}' cannot be released from {Status}.");
        }

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("A VAS order requires at least one input or output line.");
        }

        Status = ValueAddedServiceOrderStatus.Released;
        ReleasedAtUtc = NormalizeUtc(releasedAtUtc);
        Touch(releasedAtUtc);
    }

    public void Start(DateTime startedAtUtc)
    {
        if (Status is not (ValueAddedServiceOrderStatus.Released or ValueAddedServiceOrderStatus.PartiallyCompleted))
        {
            throw new InvalidOperationException($"VAS order '{OrderNumber}' cannot start from {Status}.");
        }

        Status = ValueAddedServiceOrderStatus.InProgress;
        Touch(startedAtUtc);
    }

    public void RecordCompletion(decimal outputQuantity, decimal scrapQuantity, DateTime completedAtUtc)
    {
        if (Status is not (ValueAddedServiceOrderStatus.Released or
            ValueAddedServiceOrderStatus.InProgress or
            ValueAddedServiceOrderStatus.PartiallyCompleted))
        {
            throw new InvalidOperationException($"VAS order '{OrderNumber}' cannot be completed from {Status}.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(outputQuantity);
        ArgumentOutOfRangeException.ThrowIfNegative(scrapQuantity);
        if (outputQuantity == 0m && scrapQuantity == 0m)
        {
            throw new ArgumentException("A VAS completion must produce output or record scrap.");
        }

        if (CompletedOutputQuantity + outputQuantity > RequestedOutputQuantity)
        {
            throw new InvalidOperationException("VAS completion exceeds the requested output quantity.");
        }

        CompletedOutputQuantity += outputQuantity;
        ScrapQuantity += scrapQuantity;
        Status = CompletedOutputQuantity == RequestedOutputQuantity
            ? ValueAddedServiceOrderStatus.Completed
            : ValueAddedServiceOrderStatus.PartiallyCompleted;
        CompletedAtUtc = Status == ValueAddedServiceOrderStatus.Completed
            ? NormalizeUtc(completedAtUtc)
            : CompletedAtUtc;
        Touch(completedAtUtc);
    }

    public void Cancel(string userId, string reason, DateTime cancelledAtUtc)
    {
        if (CompletedOutputQuantity != 0m || Status is ValueAddedServiceOrderStatus.Completed or ValueAddedServiceOrderStatus.Reversed)
        {
            throw new InvalidOperationException("A VAS order with completed output must be reversed, not cancelled.");
        }

        if (Status is ValueAddedServiceOrderStatus.Cancelled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A cancellation reason is required.", nameof(reason));
        }

        Status = ValueAddedServiceOrderStatus.Cancelled;
        CancelledByUserId = Required(userId, 450, nameof(userId));
        CancellationReason = Required(reason, 1_000, nameof(reason));
        CancelledAtUtc = NormalizeUtc(cancelledAtUtc);
        Touch(cancelledAtUtc);
    }

    public void MarkReversed(string userId, string reason, DateTime reversedAtUtc)
    {
        if (Status is not (ValueAddedServiceOrderStatus.Completed or ValueAddedServiceOrderStatus.PartiallyCompleted))
        {
            throw new InvalidOperationException($"VAS order '{OrderNumber}' cannot be reversed from {Status}.");
        }

        ReversedOutputQuantity = CompletedOutputQuantity;
        ReversedByUserId = Required(userId, 450, nameof(userId));
        ReversalReason = Required(reason, 1_000, nameof(reason));
        ReversedAtUtc = NormalizeUtc(reversedAtUtc);
        Status = ValueAddedServiceOrderStatus.Reversed;
        Touch(reversedAtUtc);
    }

    public void MarkException(string reason, DateTime occurredAtUtc)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException("A terminal VAS order cannot be put into exception status.");
        }

        ExceptionReason = Required(reason, 1_000, nameof(reason));
        Status = ValueAddedServiceOrderStatus.Exception;
        Touch(occurredAtUtc);
    }

    private void Touch(DateTime? timestampUtc = null)
    {
        Revision++;
        SetUpdatedAt(timestampUtc.HasValue ? NormalizeUtc(timestampUtc.Value) : null);
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

    private static string? OptionalUpper(string? value, int maximumLength) =>
        Optional(value, maximumLength)?.ToUpperInvariant();

}
