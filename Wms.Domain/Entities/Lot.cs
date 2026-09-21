// Wms.Domain/Entities/Lot.cs

using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public class Lot : Entity
{
    // EF Constructor
    private Lot()
    {
    }

    public Lot(
        string number,
        int itemId,
        DateTime? expiryDate = null,
        DateTime? manufacturedDate = null,
        DateTime? retestDate = null,
        DateTime? holdUntil = null,
        string? supplierLotNumber = null,
        string? notes = null,
        LotStatus status = LotStatus.Active)
    {
        Number = NormalizeNumber(number);
        ItemId = itemId;
        ApplyDetails(
            expiryDate,
            manufacturedDate,
            retestDate,
            holdUntil,
            supplierLotNumber,
            notes,
            stampUpdatedAt: false);
        if (status != LotStatus.Active)
        {
            SetStatus(status, "initial lot status", stampUpdatedAt: false);
        }
    }

    public string Number { get; private set; } = string.Empty;
    public int ItemId { get; private set; }
    public DateTime? ExpiryDate { get; private set; }
    public DateTime? ManufacturedDate { get; private set; }
    public DateTime? RetestDate { get; private set; }
    public DateTime? HoldUntil { get; private set; }
    public string? SupplierLotNumber { get; private set; }
    public string? Notes { get; private set; }
    public LotStatus Status { get; private set; } = LotStatus.Active;
    public bool IsActive { get; private set; } = true;
    public string? RecallReason { get; private set; }
    public DateTime? RecalledAt { get; private set; }

    // Navigation properties
    public Item Item { get; private set; } = null!;

    public void UpdateDates(DateTime? expiryDate = null, DateTime? manufacturedDate = null)
    {
        UpdateDetails(
            expiryDate,
            manufacturedDate,
            RetestDate,
            HoldUntil,
            SupplierLotNumber,
            Notes);
    }

    public void UpdateDetails(
        DateTime? expiryDate,
        DateTime? manufacturedDate,
        DateTime? retestDate,
        DateTime? holdUntil,
        string? supplierLotNumber,
        string? notes)
    {
        if (Status is LotStatus.Recalled or LotStatus.Closed)
        {
            throw new InvalidOperationException(
                $"Lot '{Number}' cannot be edited while it is {Status.ToString().ToLowerInvariant()}.");
        }

        ApplyDetails(
            expiryDate,
            manufacturedDate,
            retestDate,
            holdUntil,
            supplierLotNumber,
            notes,
            stampUpdatedAt: true);
    }

    public void SetStatus(
        LotStatus status,
        string? reason,
        bool stampUpdatedAt = true,
        DateTime? changedAtUtc = null)
    {
        if (status == Status)
        {
            if (status == LotStatus.Recalled && string.IsNullOrWhiteSpace(RecallReason) &&
                string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("A recall reason is required.", nameof(reason));
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required for a lot status change.", nameof(reason));
        }

        if (!CanTransitionTo(status))
        {
            throw new InvalidOperationException(
                $"Lot '{Number}' cannot transition from {Status} to {status}.");
        }

        if (status == LotStatus.Recalled && string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A recall reason is required.", nameof(reason));
        }

        Status = status;
        IsActive = status != LotStatus.Closed;
        if (status == LotStatus.Recalled)
        {
            RecallReason = NormalizeText(reason, 1_000, nameof(reason));
            RecalledAt = NormalizeUtc(changedAtUtc ?? DateTime.UtcNow);
        }
        else if (status != LotStatus.Recalled)
        {
            RecallReason = null;
            RecalledAt = null;
        }

        if (stampUpdatedAt)
        {
            SetUpdatedAt(changedAtUtc);
        }
    }

    public bool IsExpired(DateOnly businessDate)
    {
        return ExpiryDate.HasValue &&
               DateOnly.FromDateTime(ExpiryDate.Value) < businessDate;
    }

    public bool IsExpiringSoon(DateOnly businessDate, int warningDays = 30)
    {
        if (warningDays < 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(warningDays);
        }

        return ExpiryDate.HasValue &&
               DateOnly.FromDateTime(ExpiryDate.Value) <= businessDate.AddDays(warningDays);
    }

    public bool IsAllocationEligible(DateOnly businessDate)
    {
        if (IsExpired(businessDate))
        {
            return false;
        }

        if (RetestDate.HasValue && DateOnly.FromDateTime(RetestDate.Value) < businessDate)
        {
            return false;
        }

        if (Status is not (LotStatus.Active or LotStatus.Released))
        {
            return false;
        }

        return !HoldUntil.HasValue || businessDate > DateOnly.FromDateTime(HoldUntil.Value);
    }

    public bool IsReceivingAllowed(DateOnly businessDate, bool blockExpired)
    {
        if (Status is LotStatus.Expired or LotStatus.Recalled or LotStatus.Closed)
        {
            return false;
        }

        return !blockExpired || !IsExpired(businessDate);
    }

    public bool MarkExpired(DateOnly businessDate)
    {
        if (Status is not (LotStatus.Active or LotStatus.Released) || !IsExpired(businessDate))
        {
            return false;
        }

        SetStatus(LotStatus.Expired, "expiry boundary reached");
        return true;
    }

    public bool CanTransitionTo(LotStatus target)
    {
        if (Status == LotStatus.Closed)
        {
            return false;
        }

        if (Status == LotStatus.Recalled)
        {
            return target == LotStatus.Closed;
        }

        return target != Status;
    }

    public static string NormalizeNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            throw new ArgumentException("Lot number is required", nameof(number));
        }

        var normalized = number.Trim().ToUpperInvariant();
        if (normalized.Length > 100)
        {
            throw new ArgumentException("Lot number cannot exceed 100 characters.", nameof(number));
        }

        return normalized;
    }

    private void ApplyDetails(
        DateTime? expiryDate,
        DateTime? manufacturedDate,
        DateTime? retestDate,
        DateTime? holdUntil,
        string? supplierLotNumber,
        string? notes,
        bool stampUpdatedAt)
    {
        var normalizedExpiryDate = NormalizeDateOnly(expiryDate);
        var normalizedManufacturedDate = NormalizeDateOnly(manufacturedDate);
        var normalizedRetestDate = NormalizeDateOnly(retestDate);
        if (normalizedExpiryDate.HasValue && normalizedManufacturedDate.HasValue &&
            normalizedExpiryDate < normalizedManufacturedDate)
        {
            throw new ArgumentException("Expiry date cannot be before manufactured date");
        }

        if (normalizedRetestDate.HasValue && normalizedManufacturedDate.HasValue &&
            normalizedRetestDate < normalizedManufacturedDate)
        {
            throw new ArgumentException("Retest date cannot be before manufactured date");
        }

        if (normalizedRetestDate.HasValue && normalizedExpiryDate.HasValue &&
            normalizedRetestDate > normalizedExpiryDate)
        {
            throw new ArgumentException("Retest date cannot be after expiry date");
        }

        ExpiryDate = normalizedExpiryDate;
        ManufacturedDate = normalizedManufacturedDate;
        RetestDate = normalizedRetestDate;
        HoldUntil = NormalizeDateOnly(holdUntil);
        SupplierLotNumber = NormalizeOptionalText(supplierLotNumber, 100);
        Notes = NormalizeOptionalText(notes, 1_000);

        if (stampUpdatedAt)
        {
            SetUpdatedAt();
        }
    }

    private static DateTime? NormalizeDateOnly(DateTime? value) =>
        value.HasValue
            ? DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Unspecified)
            : null;

    private static string? NormalizeOptionalText(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeText(value, maximumLength, nameof(value));

    private static string NormalizeText(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }
}
