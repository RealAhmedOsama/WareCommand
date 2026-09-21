using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class ReturnAuthorization : Entity
{
    private readonly List<ReturnLine> _lines = [];
    private readonly List<ReturnReceipt> _receipts = [];
    private readonly List<ReturnDisposition> _dispositions = [];

    private ReturnAuthorization()
    {
    }

    public ReturnAuthorization(
        string rmaNumber,
        int warehouseId,
        int? customerId,
        int? salesOrderId,
        int? shipmentId,
        int? packageId,
        int returnLocationId,
        bool unplanned,
        string reason,
        string createdByUserId,
        DateTime createdAtUtc)
    {
        RmaNumber = Required(rmaNumber, 80, nameof(rmaNumber));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(returnLocationId);
        if (customerId is <= 0 || salesOrderId is <= 0 || shipmentId is <= 0 || packageId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(customerId));
        }

        WarehouseId = warehouseId;
        CustomerId = customerId;
        SalesOrderId = salesOrderId;
        ShipmentId = shipmentId;
        PackageId = packageId;
        ReturnLocationId = returnLocationId;
        Unplanned = unplanned;
        Reason = Required(reason, 1_000, nameof(reason));
        CreatedByUserId = Required(createdByUserId, 450, nameof(createdByUserId));
        CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
        Status = ReturnStatus.Requested;
        Revision = 1;
    }

    public string RmaNumber { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public int? CustomerId { get; private set; }
    public int? SalesOrderId { get; private set; }
    public int? ShipmentId { get; private set; }
    public int? PackageId { get; private set; }
    public int ReturnLocationId { get; private set; }
    public bool Unplanned { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public ReturnStatus Status { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public string? AuthorizedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? AuthorizedAtUtc { get; private set; }
    public DateTime? ReceivedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Customer? Customer { get; private set; }
    public SalesOrder? SalesOrder { get; private set; }
    public Shipment? Shipment { get; private set; }
    public IReadOnlyList<ReturnLine> Lines => _lines.AsReadOnly();
    public IReadOnlyList<ReturnReceipt> Receipts => _receipts.AsReadOnly();
    public IReadOnlyList<ReturnDisposition> Dispositions => _dispositions.AsReadOnly();

    public decimal ExpectedQuantity => _lines.Sum(line => line.ExpectedQuantity);
    public decimal ReceivedQuantity => _lines.Sum(line => line.ReceivedQuantity);
    public decimal DisposedQuantity => _lines.Sum(line => line.DisposedQuantity);

    public void AddLine(ReturnLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.ReturnAuthorizationId != 0 && line.ReturnAuthorizationId != Id)
        {
            throw new InvalidOperationException("The return line belongs to another RMA.");
        }

        if (Status != ReturnStatus.Requested)
        {
            throw new InvalidOperationException("Return lines can only be added to a requested RMA.");
        }

        _lines.Add(line);
        Touch();
    }

    public void AddReceipt(ReturnReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.ReturnAuthorizationId != 0 && receipt.ReturnAuthorizationId != Id)
        {
            throw new InvalidOperationException("The return receipt belongs to another RMA.");
        }

        _receipts.Add(receipt);
        ReceivedAtUtc ??= receipt.ReceivedAtUtc;
        if (Status == ReturnStatus.Authorized)
        {
            Status = ReturnStatus.Received;
        }

        Touch();
    }

    public void AddDisposition(ReturnDisposition disposition)
    {
        ArgumentNullException.ThrowIfNull(disposition);
        if (disposition.ReturnAuthorizationId != 0 && disposition.ReturnAuthorizationId != Id)
        {
            throw new InvalidOperationException("The return disposition belongs to another RMA.");
        }

        _dispositions.Add(disposition);
        Touch();
    }

    public void Authorize(string userId, DateTime authorizedAtUtc)
    {
        if (Status != ReturnStatus.Requested)
        {
            throw new InvalidOperationException($"An RMA in {Status} cannot be authorized.");
        }

        AuthorizedByUserId = Required(userId, 450, nameof(userId));
        AuthorizedAtUtc = DateTime.SpecifyKind(authorizedAtUtc, DateTimeKind.Utc);
        Status = ReturnStatus.Authorized;
        Touch();
    }

    public void StartInspection()
    {
        if (Status is not (ReturnStatus.Received or ReturnStatus.Authorized or ReturnStatus.Inspecting))
        {
            throw new InvalidOperationException($"An RMA in {Status} cannot enter inspection.");
        }

        Status = ReturnStatus.Inspecting;
        Touch();
    }

    public void MarkDisposed()
    {
        if (Status is not (ReturnStatus.Received or ReturnStatus.Inspecting))
        {
            throw new InvalidOperationException($"An RMA in {Status} cannot be disposed.");
        }

        if (_lines.Any(line => line.RemainingToDispose > 0m))
        {
            throw new InvalidOperationException("Every received return quantity must have a disposition.");
        }

        Status = ReturnStatus.Disposed;
        Touch();
    }

    public void Close(DateTime closedAtUtc)
    {
        if (Status != ReturnStatus.Disposed)
        {
            throw new InvalidOperationException($"An RMA in {Status} cannot be closed.");
        }

        Status = ReturnStatus.Closed;
        ClosedAtUtc = DateTime.SpecifyKind(closedAtUtc, DateTimeKind.Utc);
        Touch();
    }

    public void Cancel()
    {
        if (Status is ReturnStatus.Closed or ReturnStatus.Disposed)
        {
            throw new InvalidOperationException($"An RMA in {Status} cannot be cancelled.");
        }

        Status = ReturnStatus.Cancelled;
        Touch();
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
}
