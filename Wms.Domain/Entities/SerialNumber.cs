using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public class SerialNumber : Entity
{
    private SerialNumber()
    {
    }

    public SerialNumber(string number, int itemId, int? lotId = null)
    {
        Number = NormalizeNumber(number);
        ItemId = itemId;
        LotId = lotId;
        Status = SerialStatus.Available;
    }

    public string Number { get; private set; } = string.Empty;
    public int ItemId { get; private set; }
    public int? LotId { get; private set; }
    public int? CurrentWarehouseId { get; private set; }
    public int? CurrentLocationId { get; private set; }
    public int? CurrentLicensePlateId { get; private set; }
    public string? CurrentLicensePlate { get; private set; }
    public SerialStatus Status { get; private set; } = SerialStatus.Available;
    public string? StatusReason { get; private set; }
    public string? ReceiptReference { get; private set; }
    public string? ShipmentReference { get; private set; }
    public bool HasMigrationConflict { get; private set; }
    public string? ConflictReason { get; private set; }
    public DateTime? LastMovedAt { get; private set; }
    public long Revision { get; private set; }

    public Item Item { get; private set; } = null!;
    public Lot? Lot { get; private set; }
    public Warehouse? CurrentWarehouse { get; private set; }
    public Location? CurrentLocation { get; private set; }
    public LicensePlate? CurrentLicensePlateEntity { get; private set; }

    public bool IsAllocationEligible =>
        (Status is SerialStatus.Available or SerialStatus.Returned) &&
        CurrentLocationId.HasValue &&
        !HasMigrationConflict;

    public void RecordReceipt(
        int warehouseId,
        int locationId,
        int? lotId,
        string? referenceNumber,
        bool quarantine,
        DateTime timestampUtc,
        int? licensePlateId = null)
    {
        if (Id != 0 &&
            Status != SerialStatus.Returned &&
            (CurrentLocationId.HasValue || !string.IsNullOrWhiteSpace(ReceiptReference)))
        {
            throw new InvalidOperationException(
                $"Serial '{Number}' already has an active lifecycle state ({Status}).");
        }

        LotId = lotId;
        CurrentWarehouseId = warehouseId;
        CurrentLocationId = locationId;
        CurrentLicensePlateId = licensePlateId;
        CurrentLicensePlate = null;
        ReceiptReference = NormalizeOptional(referenceNumber, 100);
        ShipmentReference = null;
        Status = quarantine ? SerialStatus.Quarantine : SerialStatus.Available;
        StatusReason = quarantine ? "quality inspection required" : null;
        LastMovedAt = NormalizeUtcValue(timestampUtc);
        Touch(timestampUtc);
    }

    public void MoveTo(
        int warehouseId,
        int locationId,
        string? licensePlate,
        DateTime timestampUtc,
        int? licensePlateId = null)
    {
        EnsureMovable();
        CurrentWarehouseId = warehouseId;
        CurrentLocationId = locationId;
        CurrentLicensePlateId = licensePlateId;
        CurrentLicensePlate = NormalizeOptional(licensePlate, 100);
        LastMovedAt = NormalizeUtcValue(timestampUtc);
        Touch(timestampUtc);
    }

    public void RecordPick(DateTime timestampUtc)
    {
        EnsureMovable();
        CurrentWarehouseId = null;
        CurrentLocationId = null;
        CurrentLicensePlateId = null;
        CurrentLicensePlate = null;
        LastMovedAt = NormalizeUtcValue(timestampUtc);
        Touch(timestampUtc);
    }

    public void RecordShipment(string? referenceNumber, DateTime timestampUtc)
    {
        SetStatus(SerialStatus.Shipped, "shipment completed", timestampUtc);
        CurrentWarehouseId = null;
        CurrentLocationId = null;
        CurrentLicensePlateId = null;
        CurrentLicensePlate = null;
        ShipmentReference = NormalizeOptional(referenceNumber, 100);
        LastMovedAt = NormalizeUtcValue(timestampUtc);
        Touch(timestampUtc);
    }

    public void RecordCorrection(string reason, DateTime timestampUtc)
    {
        SetStatus(SerialStatus.Corrected, reason, timestampUtc);
        CurrentWarehouseId = null;
        CurrentLocationId = null;
        CurrentLicensePlateId = null;
        CurrentLicensePlate = null;
        LastMovedAt = NormalizeUtc(timestampUtc);
        Touch(timestampUtc);
    }

    public void SetStatus(SerialStatus status, string reason, DateTime? timestampUtc = null)
    {
        if (status == Status)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required for a serial status change.", nameof(reason));
        }

        if (!CanTransitionTo(status))
        {
            throw new InvalidOperationException(
                $"Serial '{Number}' cannot transition from {Status} to {status}.");
        }

        Status = status;
        StatusReason = NormalizeOptional(reason, 1_000);
        if (status is SerialStatus.Scrapped or SerialStatus.Shipped)
        {
            CurrentWarehouseId = null;
            CurrentLocationId = null;
            CurrentLicensePlateId = null;
            CurrentLicensePlate = null;
        }

        if (timestampUtc.HasValue)
        {
            LastMovedAt = NormalizeUtcValue(timestampUtc.Value);
            Touch(timestampUtc.Value);
        }
        else
        {
            Touch();
        }
    }

    public bool CanTransitionTo(SerialStatus target)
    {
        if (Status == SerialStatus.Scrapped)
        {
            return false;
        }

        if (Status == SerialStatus.Shipped)
        {
            return target is SerialStatus.Returned or SerialStatus.Corrected;
        }

        if (Status == SerialStatus.Returned)
        {
            return target is SerialStatus.Available or SerialStatus.Quarantine or SerialStatus.Hold;
        }

        return target != Status;
    }

    public void MarkMigrationConflict(string reason)
    {
        HasMigrationConflict = true;
        ConflictReason = NormalizeOptional(reason, 1_000);
        Status = SerialStatus.Corrected;
        StatusReason = "migration conflict";
        Touch();
    }

    public static string NormalizeNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            throw new ArgumentException("Serial number is required.", nameof(number));
        }

        var normalized = number.Trim().ToUpperInvariant();
        if (normalized.Length > 100)
        {
            throw new ArgumentException("Serial number cannot exceed 100 characters.", nameof(number));
        }

        return normalized;
    }

    private void EnsureMovable()
    {
        if (Status is SerialStatus.Shipped or SerialStatus.Scrapped or SerialStatus.Corrected)
        {
            throw new InvalidOperationException(
                $"Serial '{Number}' cannot be moved while it is {Status}.");
        }
    }

    private void Touch(DateTime? timestampUtc = null)
    {
        Revision++;
        if (timestampUtc.HasValue)
        {
            SetUpdatedAt(timestampUtc.Value);
        }
        else
        {
            SetUpdatedAt();
        }
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
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                nameof(value));
        }

        return normalized;
    }

    private static DateTime NormalizeUtcValue(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
