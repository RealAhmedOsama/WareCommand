using System.Globalization;
using Wms.Domain.Common;
using Wms.Domain.Enums;
using Wms.Domain.Identification;

namespace Wms.Domain.Entities;

public sealed class LicensePlate : Entity
{
    private LicensePlate()
    {
    }

    public LicensePlate(
        string number,
        LicensePlateType type,
        int warehouseId,
        int? currentLocationId = null,
        bool isSscc = false,
        decimal? grossWeightKg = null,
        decimal? lengthCm = null,
        decimal? widthCm = null,
        decimal? heightCm = null,
        string? sourceReference = null,
        string? notes = null)
    {
        Number = NormalizeNumber(number, isSscc);
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        if (currentLocationId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentLocationId));
        }

        Type = type;
        WarehouseId = warehouseId;
        CurrentLocationId = currentLocationId;
        IsSscc = isSscc;
        GrossWeightKg = ValidateNonNegative(grossWeightKg, nameof(grossWeightKg));
        LengthCm = ValidateNonNegative(lengthCm, nameof(lengthCm));
        WidthCm = ValidateNonNegative(widthCm, nameof(widthCm));
        HeightCm = ValidateNonNegative(heightCm, nameof(heightCm));
        SourceReference = NormalizeOptional(sourceReference, 100);
        Notes = NormalizeOptional(notes, 1_000);
        Status = LicensePlateStatus.Open;
        IsActive = true;
    }

    public string Number { get; private set; } = string.Empty;
    public LicensePlateType Type { get; private set; } = LicensePlateType.Other;
    public int WarehouseId { get; private set; }
    public int? CurrentLocationId { get; private set; }
    public bool IsSscc { get; private set; }
    public LicensePlateStatus Status { get; private set; } = LicensePlateStatus.Open;
    public bool IsActive { get; private set; } = true;
    public int? ParentLicensePlateId { get; private set; }
    public decimal? GrossWeightKg { get; private set; }
    public decimal? LengthCm { get; private set; }
    public decimal? WidthCm { get; private set; }
    public decimal? HeightCm { get; private set; }
    public string? SourceReference { get; private set; }
    public string? Notes { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Location? CurrentLocation { get; private set; }
    public LicensePlate? ParentLicensePlate { get; private set; }
    public ICollection<LicensePlate> ChildLicensePlates { get; private set; } = new List<LicensePlate>();
    public ICollection<LicensePlateContent> Contents { get; private set; } = new List<LicensePlateContent>();

    public bool IsMutable => Status is LicensePlateStatus.Open or LicensePlateStatus.Returned;

    public bool HasPhysicalCapacityData =>
        GrossWeightKg.HasValue || LengthCm.HasValue || WidthCm.HasValue || HeightCm.HasValue;

    public void SetParent(int? parentLicensePlateId)
    {
        EnsureMutable();
        if (parentLicensePlateId.HasValue && parentLicensePlateId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parentLicensePlateId));
        }

        if (parentLicensePlateId == Id && Id != 0)
        {
            throw new InvalidOperationException("A license plate cannot contain itself.");
        }

        ParentLicensePlateId = parentLicensePlateId;
        Touch();
    }

    public void MoveTo(int warehouseId, int locationId)
    {
        EnsureMutable();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(locationId);
        if (warehouseId != WarehouseId)
        {
            throw new InvalidOperationException("A license plate cannot move between warehouses without a transfer workflow.");
        }

        CurrentLocationId = locationId;
        Touch();
    }

    public void Close()
    {
        if (Status is LicensePlateStatus.Shipped or LicensePlateStatus.Voided)
        {
            throw new InvalidOperationException($"License plate '{Number}' cannot be closed while it is {Status}.");
        }

        Status = LicensePlateStatus.Closed;
        Touch();
    }

    public void Reopen()
    {
        if (Status != LicensePlateStatus.Closed)
        {
            throw new InvalidOperationException($"Only a closed license plate can be reopened; current state is {Status}.");
        }

        Status = LicensePlateStatus.Open;
        IsActive = true;
        Touch();
    }

    public void Ship()
    {
        if (Status != LicensePlateStatus.Closed)
        {
            throw new InvalidOperationException($"License plate '{Number}' must be closed before shipping.");
        }

        Status = LicensePlateStatus.Shipped;
        IsActive = false;
        CurrentLocationId = null;
        ParentLicensePlateId = null;
        Touch();
    }

    public void ReturnTo(int warehouseId, int locationId)
    {
        if (Status != LicensePlateStatus.Shipped)
        {
            throw new InvalidOperationException($"Only a shipped license plate can be returned; current state is {Status}.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(locationId);
        if (warehouseId != WarehouseId)
        {
            throw new InvalidOperationException("A returned license plate must return to its owning warehouse.");
        }

        Status = LicensePlateStatus.Returned;
        IsActive = true;
        CurrentLocationId = locationId;
        ParentLicensePlateId = null;
        Touch();
    }

    public void Void()
    {
        if (Status == LicensePlateStatus.Shipped)
        {
            throw new InvalidOperationException("A shipped license plate cannot be voided.");
        }

        Status = LicensePlateStatus.Voided;
        IsActive = false;
        CurrentLocationId = null;
        ParentLicensePlateId = null;
        Touch();
    }

    public void UpdateDetails(
        LicensePlateType type,
        decimal? grossWeightKg,
        decimal? lengthCm,
        decimal? widthCm,
        decimal? heightCm,
        string? sourceReference,
        string? notes)
    {
        EnsureMutable();
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        Type = type;
        GrossWeightKg = ValidateNonNegative(grossWeightKg, nameof(grossWeightKg));
        LengthCm = ValidateNonNegative(lengthCm, nameof(lengthCm));
        WidthCm = ValidateNonNegative(widthCm, nameof(widthCm));
        HeightCm = ValidateNonNegative(heightCm, nameof(heightCm));
        SourceReference = NormalizeOptional(sourceReference, 100);
        Notes = NormalizeOptional(notes, 1_000);
        Touch();
    }

    private void EnsureMutable()
    {
        if (!IsMutable)
        {
            throw new InvalidOperationException($"License plate '{Number}' is {Status} and cannot be mutated.");
        }
    }

    private void Touch()
    {
        Revision++;
        SetUpdatedAt();
    }

    private static string NormalizeNumber(string value, bool isSscc)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A license plate number is required.", nameof(value));
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > 100 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("A license plate number must contain no more than 100 printable characters.", nameof(value));
        }

        if (isSscc)
        {
            normalized = BarcodeParser.ParseSscc(normalized).NormalizedValue;
        }

        return normalized;
    }

    private static decimal? ValidateNonNegative(decimal? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value cannot be negative.");
        }

        return value;
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength || normalized.Any(char.IsControl))
        {
            throw new ArgumentException($"The value cannot exceed {maximumLength} printable characters.", nameof(value));
        }

        return normalized;
    }
}
