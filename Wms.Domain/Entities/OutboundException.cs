#pragma warning disable CA1711

using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Durable outbound exception queue row. Operational state is mutable only
/// through the exception service; audit and command rows preserve the history.
/// </summary>
public sealed class OutboundException : Entity
{
    private OutboundException()
    {
    }

    public OutboundException(
        int warehouseId,
        string exceptionNumber,
        string idempotencyKey,
        OutboundExceptionCode code,
        OutboundExceptionSeverity severity,
        string reason,
        string? queueCode = null,
        DateTime? dueAtUtc = null,
        int? salesOrderId = null,
        int? salesOrderLineId = null,
        int? inventoryReservationId = null,
        int? warehouseWorkId = null,
        int? warehouseWorkLineId = null,
        int? shipmentId = null,
        int? shipmentPackageId = null,
        int? shipmentLoadId = null,
        int? itemId = null,
        int? locationId = null,
        int? lotId = null,
        int? serialNumberId = null,
        int? licensePlateId = null,
        decimal? expectedBaseQuantity = null,
        decimal? actualBaseQuantity = null,
        decimal? varianceBaseQuantity = null,
        string? itemSkuSnapshot = null,
        string? notes = null,
        string? attachmentReferences = null,
        string? createdByUserId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity));
        }

        ValidateOptionalId(salesOrderId, nameof(salesOrderId));
        ValidateOptionalId(salesOrderLineId, nameof(salesOrderLineId));
        ValidateOptionalId(inventoryReservationId, nameof(inventoryReservationId));
        ValidateOptionalId(warehouseWorkId, nameof(warehouseWorkId));
        ValidateOptionalId(warehouseWorkLineId, nameof(warehouseWorkLineId));
        ValidateOptionalId(shipmentId, nameof(shipmentId));
        ValidateOptionalId(shipmentPackageId, nameof(shipmentPackageId));
        ValidateOptionalId(shipmentLoadId, nameof(shipmentLoadId));
        ValidateOptionalId(itemId, nameof(itemId));
        ValidateOptionalId(locationId, nameof(locationId));
        ValidateOptionalId(lotId, nameof(lotId));
        ValidateOptionalId(serialNumberId, nameof(serialNumberId));
        ValidateOptionalId(licensePlateId, nameof(licensePlateId));

        WarehouseId = warehouseId;
        ExceptionNumber = Required(exceptionNumber, 80, nameof(exceptionNumber));
        IdempotencyKey = Required(idempotencyKey, 250, nameof(idempotencyKey));
        Code = code;
        Severity = severity;
        Reason = Required(reason, 2_000, nameof(reason));
        QueueCode = OptionalUpper(queueCode, 50) ?? "OUTBOUND-EXCEPTION";
        DueAtUtc = dueAtUtc.HasValue ? NormalizeUtc(dueAtUtc.Value) : null;
        SalesOrderId = salesOrderId;
        SalesOrderLineId = salesOrderLineId;
        InventoryReservationId = inventoryReservationId;
        WarehouseWorkId = warehouseWorkId;
        WarehouseWorkLineId = warehouseWorkLineId;
        ShipmentId = shipmentId;
        ShipmentPackageId = shipmentPackageId;
        ShipmentLoadId = shipmentLoadId;
        ItemId = itemId;
        LocationId = locationId;
        LotId = lotId;
        SerialNumberId = serialNumberId;
        LicensePlateId = licensePlateId;
        ExpectedBaseQuantity = ValidateQuantity(expectedBaseQuantity, nameof(expectedBaseQuantity));
        ActualBaseQuantity = ValidateQuantity(actualBaseQuantity, nameof(actualBaseQuantity));
        VarianceBaseQuantity = varianceBaseQuantity;
        ItemSkuSnapshot = OptionalUpper(itemSkuSnapshot, 50);
        Notes = Optional(notes, 2_000);
        AttachmentReferences = Optional(attachmentReferences, 4_000);
        CreatedByUserId = Required(createdByUserId ?? "system", 450, nameof(createdByUserId));
        Status = OutboundExceptionStatus.Open;
        Revision = 1;
    }

    public int WarehouseId { get; private set; }
    public string ExceptionNumber { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public OutboundExceptionCode Code { get; private set; }
    public OutboundExceptionSeverity Severity { get; private set; }
    public OutboundExceptionStatus Status { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string QueueCode { get; private set; } = string.Empty;
    public DateTime? DueAtUtc { get; private set; }
    public int? SalesOrderId { get; private set; }
    public int? SalesOrderLineId { get; private set; }
    public int? InventoryReservationId { get; private set; }
    public int? WarehouseWorkId { get; private set; }
    public int? WarehouseWorkLineId { get; private set; }
    public int? ShipmentId { get; private set; }
    public int? ShipmentPackageId { get; private set; }
    public int? ShipmentLoadId { get; private set; }
    public int? ItemId { get; private set; }
    public int? LocationId { get; private set; }
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public int? LicensePlateId { get; private set; }
    public decimal? ExpectedBaseQuantity { get; private set; }
    public decimal? ActualBaseQuantity { get; private set; }
    public decimal? VarianceBaseQuantity { get; private set; }
    public string? ItemSkuSnapshot { get; private set; }
    public string? Notes { get; private set; }
    public string? AttachmentReferences { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public string? OwnerUserId { get; private set; }
    public string? AssignedTeamCode { get; private set; }
    public string? AssignedByUserId { get; private set; }
    public DateTime? AssignedAtUtc { get; private set; }
    public string? ReviewStartedByUserId { get; private set; }
    public DateTime? ReviewStartedAtUtc { get; private set; }
    public OutboundExceptionResolution? Resolution { get; private set; }
    public string? ResolutionReason { get; private set; }
    public string? ResolutionDetails { get; private set; }
    public string? ResolvedByUserId { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public SalesOrder? SalesOrder { get; private set; }
    public SalesOrderLine? SalesOrderLine { get; private set; }
    public InventoryReservation? InventoryReservation { get; private set; }
    public WarehouseWork? WarehouseWork { get; private set; }
    public WarehouseWorkLine? WarehouseWorkLine { get; private set; }
    public Shipment? Shipment { get; private set; }
    public ShipmentPackage? ShipmentPackage { get; private set; }
    public ShipmentLoad? ShipmentLoad { get; private set; }
    public Item? Item { get; private set; }
    public Location? Location { get; private set; }
    public Lot? Lot { get; private set; }
    public SerialNumber? SerialNumber { get; private set; }
    public LicensePlate? LicensePlate { get; private set; }

    public bool IsTerminal => Status is OutboundExceptionStatus.Resolved or OutboundExceptionStatus.Cancelled;
    public bool IsOverdue(DateTime atUtc) =>
        !IsTerminal && DueAtUtc.HasValue && DueAtUtc.Value < NormalizeUtc(atUtc);

    public void Assign(string? ownerUserId, string? teamCode, string assignedByUserId, DateTime assignedAtUtc)
    {
        EnsureEditable();
        if (string.IsNullOrWhiteSpace(ownerUserId) && string.IsNullOrWhiteSpace(teamCode))
        {
            throw new ArgumentException("An exception assignment requires a user or team.");
        }

        OwnerUserId = Optional(ownerUserId, 450);
        AssignedTeamCode = OptionalUpper(teamCode, 50);
        AssignedByUserId = Required(assignedByUserId, 450, nameof(assignedByUserId));
        AssignedAtUtc = NormalizeUtc(assignedAtUtc);
        Revision++;
        SetUpdatedAt(assignedAtUtc);
    }

    public void StartReview(string userId, DateTime startedAtUtc)
    {
        EnsureEditable();
        ReviewStartedByUserId = Required(userId, 450, nameof(userId));
        ReviewStartedAtUtc = NormalizeUtc(startedAtUtc);
        Status = OutboundExceptionStatus.UnderReview;
        Revision++;
        SetUpdatedAt(startedAtUtc);
    }

    public void ApplyResolution(
        OutboundExceptionResolution resolution,
        string reason,
        string? details,
        string userId,
        DateTime resolvedAtUtc)
    {
        EnsureEditable();
        if (!Enum.IsDefined(resolution))
        {
            throw new ArgumentOutOfRangeException(nameof(resolution));
        }

        Resolution = resolution;
        ResolutionReason = Required(reason, 2_000, nameof(reason));
        ResolutionDetails = Optional(details, 4_000);
        ResolvedByUserId = Required(userId, 450, nameof(userId));
        ResolvedAtUtc = NormalizeUtc(resolvedAtUtc);
        Status = resolution == OutboundExceptionResolution.HoldOrder
            ? OutboundExceptionStatus.Held
            : OutboundExceptionStatus.Resolved;
        Revision++;
        SetUpdatedAt(resolvedAtUtc);
    }

    public void Cancel(string reason, string userId, DateTime cancelledAtUtc) =>
        ApplyResolution(
            OutboundExceptionResolution.SupervisorOverride,
            reason,
            "cancelled",
            userId,
            cancelledAtUtc);

    private void EnsureEditable()
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException($"An outbound exception in {Status} cannot be changed.");
        }
    }

    private static void ValidateOptionalId(int? value, string parameterName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static decimal? ValidateQuantity(decimal? value, string parameterName) =>
        value is null
            ? null
            : value.Value >= 0m
                ? value
                : throw new ArgumentOutOfRangeException(parameterName);

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
        string.IsNullOrWhiteSpace(value) ? null : Required(value, maximumLength, nameof(value));

    private static string? OptionalUpper(string? value, int maximumLength) =>
        Optional(value, maximumLength)?.ToUpperInvariant();
}

#pragma warning restore CA1711
