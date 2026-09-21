using Wms.Domain.Common;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;

namespace Wms.Domain.Entities;

/// <summary>
/// Durable outbound demand. Customer and ship-to values are snapshots on this
/// aggregate, so later master-data edits cannot rewrite confirmed history.
/// </summary>
public sealed class SalesOrder : Entity
{
    private readonly List<SalesOrderLine> _lines = new();

    private SalesOrder()
    {
    }

    public SalesOrder(
        string documentNumber,
        int warehouseId,
        string warehouseCodeSnapshot,
        int customerId,
        string customerCodeSnapshot,
        string customerLegalNameSnapshot,
        string? customerLocalizedNameSnapshot,
        string? customerContactNameSnapshot,
        string? customerContactEmailSnapshot,
        string? customerContactPhoneSnapshot,
        int? shipToAddressId,
        string? shipToCodeSnapshot,
        string? shipToRecipientNameSnapshot,
        string? shipToPhoneSnapshot,
        string? shipToCountryCodeSnapshot,
        string? shipToRegionSnapshot,
        string? shipToCitySnapshot,
        string? shipToPostalCodeSnapshot,
        string? shipToAddressLine1Snapshot,
        string? shipToAddressLine2Snapshot,
        string? shipToDeliveryInstructionsSnapshot,
        DateOnly orderDate,
        DateOnly? requestedShipDate,
        string? externalReference,
        string sourceType,
        string? sourceReference,
        int priority,
        string? defaultCarrierCodeSnapshot,
        string? defaultCarrierServiceCodeSnapshot,
        string? packagingProfileSnapshot,
        string? labelProfileSnapshot,
        bool allowPartialShipmentSnapshot,
        string? notes,
        string? createdByUserId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(customerId);
        DocumentNumber = NormalizeRequired(documentNumber, 50, nameof(documentNumber));
        WarehouseId = warehouseId;
        WarehouseCodeSnapshot = NormalizeRequired(warehouseCodeSnapshot, 20, nameof(warehouseCodeSnapshot));
        CustomerId = customerId;
        CustomerCodeSnapshot = NormalizeRequired(customerCodeSnapshot, 50, nameof(customerCodeSnapshot));
        CustomerLegalNameSnapshot = NormalizeRequired(customerLegalNameSnapshot, 200, nameof(customerLegalNameSnapshot));
        CustomerLocalizedNameSnapshot = NormalizeOptional(customerLocalizedNameSnapshot, 200);
        CustomerContactNameSnapshot = NormalizeOptional(customerContactNameSnapshot, 200);
        CustomerContactEmailSnapshot = NormalizeOptional(customerContactEmailSnapshot, 320);
        CustomerContactPhoneSnapshot = NormalizeOptional(customerContactPhoneSnapshot, 50);
        ShipToAddressId = shipToAddressId is <= 0 ? throw new ArgumentOutOfRangeException(nameof(shipToAddressId)) : shipToAddressId;
        ShipToCodeSnapshot = NormalizeOptionalUpper(shipToCodeSnapshot, 50);
        ShipToRecipientNameSnapshot = NormalizeOptional(shipToRecipientNameSnapshot, 200);
        ShipToPhoneSnapshot = NormalizeOptional(shipToPhoneSnapshot, 50);
        ShipToCountryCodeSnapshot = NormalizeCountryCode(shipToCountryCodeSnapshot);
        ShipToRegionSnapshot = NormalizeOptional(shipToRegionSnapshot, 100);
        ShipToCitySnapshot = NormalizeOptional(shipToCitySnapshot, 100);
        ShipToPostalCodeSnapshot = NormalizeOptional(shipToPostalCodeSnapshot, 30);
        ShipToAddressLine1Snapshot = NormalizeOptional(shipToAddressLine1Snapshot, 200);
        ShipToAddressLine2Snapshot = NormalizeOptional(shipToAddressLine2Snapshot, 200);
        ShipToDeliveryInstructionsSnapshot = NormalizeOptional(shipToDeliveryInstructionsSnapshot, 1_000);
        OrderDate = orderDate;
        RequestedShipDate = requestedShipDate;
        ExternalReference = NormalizeOptionalUpper(externalReference, 100);
        SourceType = NormalizeRequired(sourceType, 30, nameof(sourceType)).ToUpperInvariant();
        SourceReference = NormalizeOptional(sourceReference, 200);
        Priority = priority is >= 0 and <= 999
            ? priority
            : throw new ArgumentOutOfRangeException(nameof(priority));
        DefaultCarrierCodeSnapshot = NormalizeOptionalUpper(defaultCarrierCodeSnapshot, 50);
        DefaultCarrierServiceCodeSnapshot = NormalizeOptionalUpper(defaultCarrierServiceCodeSnapshot, 80);
        PackagingProfileSnapshot = NormalizeOptional(packagingProfileSnapshot, 100);
        LabelProfileSnapshot = NormalizeOptional(labelProfileSnapshot, 100);
        AllowPartialShipmentSnapshot = allowPartialShipmentSnapshot;
        Notes = NormalizeOptional(notes, 2_000);
        CreatedByUserId = NormalizeOptional(createdByUserId, 450);
    }

    public string DocumentNumber { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public string WarehouseCodeSnapshot { get; private set; } = string.Empty;
    public int CustomerId { get; private set; }
    public string CustomerCodeSnapshot { get; private set; } = string.Empty;
    public string CustomerLegalNameSnapshot { get; private set; } = string.Empty;
    public string? CustomerLocalizedNameSnapshot { get; private set; }
    public string? CustomerContactNameSnapshot { get; private set; }
    public string? CustomerContactEmailSnapshot { get; private set; }
    public string? CustomerContactPhoneSnapshot { get; private set; }
    public int? ShipToAddressId { get; private set; }
    public string? ShipToCodeSnapshot { get; private set; }
    public string? ShipToRecipientNameSnapshot { get; private set; }
    public string? ShipToPhoneSnapshot { get; private set; }
    public string? ShipToCountryCodeSnapshot { get; private set; }
    public string? ShipToRegionSnapshot { get; private set; }
    public string? ShipToCitySnapshot { get; private set; }
    public string? ShipToPostalCodeSnapshot { get; private set; }
    public string? ShipToAddressLine1Snapshot { get; private set; }
    public string? ShipToAddressLine2Snapshot { get; private set; }
    public string? ShipToDeliveryInstructionsSnapshot { get; private set; }
    public DateOnly OrderDate { get; private set; }
    public DateOnly? RequestedShipDate { get; private set; }
    public string? ExternalReference { get; private set; }
    public string SourceType { get; private set; } = "MANUAL";
    public string? SourceReference { get; private set; }
    public int Priority { get; private set; }
    public string? DefaultCarrierCodeSnapshot { get; private set; }
    public string? DefaultCarrierServiceCodeSnapshot { get; private set; }
    public string? PackagingProfileSnapshot { get; private set; }
    public string? LabelProfileSnapshot { get; private set; }
    public bool AllowPartialShipmentSnapshot { get; private set; }
    public string? Notes { get; private set; }
    public SalesOrderStatus Status { get; private set; } = SalesOrderStatus.Draft;
    public SalesOrderStatus? StatusBeforeHold { get; private set; }
    public string? HoldReason { get; private set; }
    public string? CreatedByUserId { get; private set; }
    public string? ConfirmedByUserId { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public string? HeldByUserId { get; private set; }
    public DateTime? HeldAtUtc { get; private set; }
    public string? CancelledByUserId { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? ClosedByUserId { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public long Revision { get; private set; } = 1;

    public Warehouse Warehouse { get; private set; } = null!;
    public Customer Customer { get; private set; } = null!;
    public IReadOnlyList<SalesOrderLine> Lines => _lines.AsReadOnly();
    public bool CanEdit => Status == SalesOrderStatus.Draft;
    public bool IsOnHold => Status == SalesOrderStatus.Held;
    public bool HasExecutionHistory => _lines.Any(line =>
        line.AllocatedBaseQuantity > 0m ||
        line.PickedBaseQuantity > 0m ||
        line.PackedBaseQuantity > 0m ||
        line.ShippedBaseQuantity > 0m);

    public void ReplaceDraftLines(IEnumerable<SalesOrderLine> lines)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(lines);
        var materialized = lines.ToArray();
        if (materialized.Length == 0)
        {
            throw new InvalidOperationException("A sales order must contain at least one line.");
        }

        if (materialized.Select(line => line.LineNumber).Distinct().Count() != materialized.Length)
        {
            throw new InvalidOperationException("Sales-order line numbers must be unique.");
        }

        _lines.Clear();
        _lines.AddRange(materialized.OrderBy(line => line.LineNumber));
        Touch();
    }

    public void RefreshCustomerSnapshot(CustomerOrderSnapshot snapshot)
    {
        if (Status != SalesOrderStatus.Draft)
        {
            throw new InvalidOperationException("Customer snapshots can only be refreshed before confirmation.");
        }

        if (snapshot.CustomerId != CustomerId)
        {
            throw new InvalidOperationException("The snapshot customer does not match the order customer.");
        }

        CustomerCodeSnapshot = NormalizeRequired(snapshot.CustomerCode, 50, nameof(snapshot.CustomerCode));
        CustomerLegalNameSnapshot = NormalizeRequired(snapshot.CustomerLegalName, 200, nameof(snapshot.CustomerLegalName));
        CustomerLocalizedNameSnapshot = NormalizeOptional(snapshot.CustomerLocalizedName, 200);
        CustomerContactNameSnapshot = NormalizeOptional(snapshot.CustomerContactName, 200);
        CustomerContactEmailSnapshot = NormalizeOptional(snapshot.CustomerContactEmail, 320);
        CustomerContactPhoneSnapshot = NormalizeOptional(snapshot.CustomerContactPhone, 50);
        ShipToAddressId = snapshot.ShipToAddressId is <= 0 ? throw new ArgumentOutOfRangeException(nameof(snapshot)) : snapshot.ShipToAddressId;
        ShipToCodeSnapshot = NormalizeOptionalUpper(snapshot.ShipToCode, 50);
        ShipToRecipientNameSnapshot = NormalizeOptional(snapshot.ShipToRecipientName, 200);
        ShipToPhoneSnapshot = NormalizeOptional(snapshot.ShipToPhone, 50);
        ShipToCountryCodeSnapshot = NormalizeCountryCode(snapshot.ShipToCountryCode);
        ShipToRegionSnapshot = NormalizeOptional(snapshot.ShipToRegion, 100);
        ShipToCitySnapshot = NormalizeOptional(snapshot.ShipToCity, 100);
        ShipToPostalCodeSnapshot = NormalizeOptional(snapshot.ShipToPostalCode, 30);
        ShipToAddressLine1Snapshot = NormalizeOptional(snapshot.ShipToAddressLine1, 200);
        ShipToAddressLine2Snapshot = NormalizeOptional(snapshot.ShipToAddressLine2, 200);
        ShipToDeliveryInstructionsSnapshot = NormalizeOptional(snapshot.ShipToDeliveryInstructions, 1_000);
        DefaultCarrierCodeSnapshot = NormalizeOptionalUpper(snapshot.DefaultCarrierCode, 50);
        DefaultCarrierServiceCodeSnapshot = NormalizeOptionalUpper(snapshot.DefaultCarrierServiceCode, 80);
        Priority = snapshot.Priority is >= 0 and <= 999 ? snapshot.Priority : throw new ArgumentOutOfRangeException(nameof(snapshot));
        PackagingProfileSnapshot = NormalizeOptional(snapshot.PackagingProfile, 100);
        LabelProfileSnapshot = NormalizeOptional(snapshot.LabelProfile, 100);
        AllowPartialShipmentSnapshot = snapshot.AllowPartialShipment;
        Touch();
    }

    public void ChangeDraftCustomer(int customerId, CustomerOrderSnapshot snapshot)
    {
        EnsureDraft();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(customerId);
        if (snapshot.CustomerId != customerId)
        {
            throw new InvalidOperationException("The customer snapshot does not match the selected customer.");
        }

        CustomerId = customerId;
        RefreshCustomerSnapshot(snapshot);
    }

    public void UpdateDraft(
        DateOnly orderDate,
        DateOnly? requestedShipDate,
        string? externalReference,
        string sourceType,
        string? sourceReference,
        int priority,
        string? defaultCarrierCode,
        string? defaultCarrierServiceCode,
        string? packagingProfile,
        string? labelProfile,
        bool allowPartialShipment,
        string? notes)
    {
        EnsureDraft();
        OrderDate = orderDate;
        RequestedShipDate = requestedShipDate;
        ExternalReference = NormalizeOptionalUpper(externalReference, 100);
        SourceType = NormalizeRequired(sourceType, 30, nameof(sourceType)).ToUpperInvariant();
        SourceReference = NormalizeOptional(sourceReference, 200);
        Priority = priority is >= 0 and <= 999
            ? priority
            : throw new ArgumentOutOfRangeException(nameof(priority));
        DefaultCarrierCodeSnapshot = NormalizeOptionalUpper(defaultCarrierCode, 50);
        DefaultCarrierServiceCodeSnapshot = NormalizeOptionalUpper(defaultCarrierServiceCode, 80);
        PackagingProfileSnapshot = NormalizeOptional(packagingProfile, 100);
        LabelProfileSnapshot = NormalizeOptional(labelProfile, 100);
        AllowPartialShipmentSnapshot = allowPartialShipment;
        Notes = NormalizeOptional(notes, 2_000);
        Touch();
    }

    public void Confirm(string userId, DateTime confirmedAtUtc)
    {
        EnsureUser(userId);
        if (Status != SalesOrderStatus.Draft)
        {
            throw new InvalidOperationException($"A sales order in {Status} cannot be confirmed.");
        }

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("A sales order must contain at least one line before confirmation.");
        }

        Status = SalesOrderStatus.Confirmed;
        ConfirmedByUserId = userId.Trim();
        ConfirmedAtUtc = NormalizeUtcValue(confirmedAtUtc);
        Touch();
    }

    public void Hold(string userId, string reason, DateTime heldAtUtc)
    {
        EnsureUser(userId);
        if (Status is SalesOrderStatus.Cancelled or SalesOrderStatus.Closed or SalesOrderStatus.Shipped)
        {
            throw new InvalidOperationException($"A sales order in {Status} cannot be held.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A hold reason is required.", nameof(reason));
        }

        StatusBeforeHold = Status;
        Status = SalesOrderStatus.Held;
        HoldReason = NormalizeRequired(reason, 1_000, nameof(reason));
        HeldByUserId = userId.Trim();
        HeldAtUtc = NormalizeUtcValue(heldAtUtc);
        Touch();
    }

    public void ReleaseHold()
    {
        if (Status != SalesOrderStatus.Held)
        {
            throw new InvalidOperationException("Only held sales orders can be released.");
        }

        Status = StatusBeforeHold is SalesOrderStatus.Held or null
            ? SalesOrderStatus.Confirmed
            : StatusBeforeHold.Value;
        StatusBeforeHold = null;
        HoldReason = null;
        Touch();
    }

    public void Cancel(string userId, DateTime cancelledAtUtc)
    {
        EnsureUser(userId);
        if (Status is not (SalesOrderStatus.Draft or
            SalesOrderStatus.Confirmed or
            SalesOrderStatus.Held or
            SalesOrderStatus.Allocating or
            SalesOrderStatus.PartiallyAllocated or
            SalesOrderStatus.Released or
            SalesOrderStatus.Exception))
        {
            throw new InvalidOperationException($"A sales order in {Status} cannot be cancelled after release or execution.");
        }

        Status = SalesOrderStatus.Cancelled;
        CancelledByUserId = userId.Trim();
        CancelledAtUtc = NormalizeUtcValue(cancelledAtUtc);
        Touch();
    }

    public void Close(string userId, DateTime closedAtUtc)
    {
        EnsureUser(userId);
        if (Status != SalesOrderStatus.Shipped)
        {
            throw new InvalidOperationException("Only shipped sales orders can be closed.");
        }

        Status = SalesOrderStatus.Closed;
        ClosedByUserId = userId.Trim();
        ClosedAtUtc = NormalizeUtcValue(closedAtUtc);
        Touch();
    }

    public void SetAllocationStatus(SalesOrderStatus status)
    {
        if (status is not (SalesOrderStatus.Allocating or SalesOrderStatus.PartiallyAllocated or SalesOrderStatus.Released or SalesOrderStatus.Exception))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (Status is SalesOrderStatus.Draft or SalesOrderStatus.Cancelled or SalesOrderStatus.Closed or SalesOrderStatus.Held)
        {
            throw new InvalidOperationException($"A sales order in {Status} cannot enter allocation status.");
        }

        Status = status;
        Touch();
    }

    public void RestoreConfirmedAfterAllocationCancellation()
    {
        if (Status is not (SalesOrderStatus.Allocating or
            SalesOrderStatus.PartiallyAllocated or
            SalesOrderStatus.Released or
            SalesOrderStatus.Exception))
        {
            throw new InvalidOperationException(
                $"A sales order in {Status} cannot be restored after allocation cancellation.");
        }

        Status = SalesOrderStatus.Confirmed;
        StatusBeforeHold = null;
        HoldReason = null;
        Touch();
    }

    public void MarkPartiallyShipped()
    {
        if (Status is SalesOrderStatus.Cancelled or SalesOrderStatus.Closed or SalesOrderStatus.Shipped)
        {
            throw new InvalidOperationException($"A sales order in {Status} cannot be marked partially shipped.");
        }

        Status = SalesOrderStatus.PartiallyShipped;
        Touch();
    }

    public void MarkShipped(string userId, DateTime shippedAtUtc)
    {
        EnsureUser(userId);
        if (Status is SalesOrderStatus.Cancelled or SalesOrderStatus.Closed)
        {
            throw new InvalidOperationException($"A sales order in {Status} cannot be marked shipped.");
        }

        Status = SalesOrderStatus.Shipped;
        ConfirmedByUserId = userId.Trim();
        ConfirmedAtUtc ??= NormalizeUtcValue(shippedAtUtc);
        Touch();
    }

    private void EnsureDraft()
    {
        if (!CanEdit)
        {
            throw new InvalidOperationException("Only draft sales orders can be edited.");
        }
    }

    private static void EnsureUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID is required.", nameof(userId));
        }
    }

    private void Touch()
    {
        Revision++;
        SetUpdatedAt();
    }

    private static DateTime NormalizeUtcValue(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string NormalizeRequired(string value, int maximumLength, string parameterName)
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

    private static string? NormalizeOptional(string? value, int maximumLength)
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

    private static string? NormalizeOptionalUpper(string? value, int maximumLength) =>
        NormalizeOptional(value, maximumLength)?.ToUpperInvariant();

    private static string? NormalizeCountryCode(string? value)
    {
        var normalized = NormalizeOptionalUpper(value, 2);
        if (normalized is not null &&
            (normalized.Length != 2 || normalized.Any(character => character is < 'A' or > 'Z')))
        {
            throw new ArgumentException("The country code must contain exactly two letters.");
        }

        return normalized;
    }
}
