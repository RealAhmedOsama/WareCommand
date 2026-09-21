using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Warehouse-scoped cross-dock eligibility and destination policy. Matching is
/// intentionally persisted as a snapshot on the plan; changing a policy does
/// not rewrite an already explained inbound decision.
/// </summary>
public sealed class CrossDockPolicy : Entity
{
    private CrossDockPolicy()
    {
    }

    public CrossDockPolicy(
        int warehouseId,
        string policyKey,
        string name,
        int priority = 50,
        int? itemId = null,
        string? itemCategory = null,
        int? supplierId = null,
        string? inboundSourceType = null,
        int? customerId = null,
        int? salesOrderId = null,
        int? destinationLocationId = null,
        decimal quantityTolerancePercent = 0m,
        int minimumShelfLifeDays = 0,
        bool requireExpiry = false,
        bool allowPlanned = true,
        bool allowOpportunistic = true,
        DateTime? effectiveFromUtc = null,
        DateTime? effectiveToUtc = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        if (itemId is <= 0 || supplierId is <= 0 || customerId is <= 0 ||
            salesOrderId is <= 0 || destinationLocationId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId),
                "Optional identifiers must be positive when supplied.");
        }

        if (priority < 0 || quantityTolerancePercent is < 0m or > 100m || minimumShelfLifeDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        var effectiveFrom = NormalizeUtc(effectiveFromUtc) ?? DateTime.UnixEpoch;
        var effectiveTo = NormalizeUtc(effectiveToUtc);
        if (effectiveTo.HasValue && effectiveTo.Value <= effectiveFrom)
        {
            throw new ArgumentException(
                "The effective end must be later than the effective start.",
                nameof(effectiveToUtc));
        }

        if (!allowPlanned && !allowOpportunistic)
        {
            throw new ArgumentException(
                "At least one cross-dock mode must be enabled.",
                nameof(allowPlanned));
        }

        WarehouseId = warehouseId;
        PolicyKey = Required(policyKey, 80, nameof(policyKey));
        Name = Required(name, 200, nameof(name));
        Priority = priority;
        ItemId = itemId;
        ItemCategory = Optional(itemCategory, 100);
        SupplierId = supplierId;
        InboundSourceType = OptionalUpper(inboundSourceType, 30);
        CustomerId = customerId;
        SalesOrderId = salesOrderId;
        DestinationLocationId = destinationLocationId;
        QuantityTolerancePercent = quantityTolerancePercent;
        MinimumShelfLifeDays = minimumShelfLifeDays;
        RequireExpiry = requireExpiry;
        AllowPlanned = allowPlanned;
        AllowOpportunistic = allowOpportunistic;
        EffectiveFromUtc = effectiveFrom;
        EffectiveToUtc = effectiveTo;
        IsActive = true;
        Revision = 1;
    }

    public int WarehouseId { get; private set; }
    public string PolicyKey { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int Priority { get; private set; }
    public int? ItemId { get; private set; }
    public string? ItemCategory { get; private set; }
    public int? SupplierId { get; private set; }
    public string? InboundSourceType { get; private set; }
    public int? CustomerId { get; private set; }
    public int? SalesOrderId { get; private set; }
    public int? DestinationLocationId { get; private set; }
    public decimal QuantityTolerancePercent { get; private set; }
    public int MinimumShelfLifeDays { get; private set; }
    public bool RequireExpiry { get; private set; }
    public bool AllowPlanned { get; private set; }
    public bool AllowOpportunistic { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveToUtc { get; private set; }
    public bool IsActive { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Item? Item { get; private set; }
    public Supplier? Supplier { get; private set; }
    public Customer? Customer { get; private set; }
    public SalesOrder? SalesOrder { get; private set; }
    public Location? DestinationLocation { get; private set; }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        Revision++;
        SetUpdatedAt();
    }

    public void Update(
        string name,
        int priority,
        int? itemId,
        string? itemCategory,
        int? supplierId,
        string? inboundSourceType,
        int? customerId,
        int? salesOrderId,
        int? destinationLocationId,
        decimal quantityTolerancePercent,
        int minimumShelfLifeDays,
        bool requireExpiry,
        bool allowPlanned,
        bool allowOpportunistic,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc,
        bool isActive)
    {
        if (itemId is <= 0 || supplierId is <= 0 || customerId is <= 0 ||
            salesOrderId is <= 0 || destinationLocationId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId),
                "Optional identifiers must be positive when supplied.");
        }

        if (priority < 0 || quantityTolerancePercent is < 0m or > 100m || minimumShelfLifeDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        if (!allowPlanned && !allowOpportunistic)
        {
            throw new ArgumentException("At least one cross-dock mode must be enabled.", nameof(allowPlanned));
        }

        var start = DateTime.SpecifyKind(effectiveFromUtc, DateTimeKind.Utc);
        var end = effectiveToUtc.HasValue
            ? DateTime.SpecifyKind(effectiveToUtc.Value, DateTimeKind.Utc)
            : (DateTime?)null;
        if (end.HasValue && end.Value <= start)
        {
            throw new ArgumentException("The effective end must be later than the effective start.", nameof(effectiveToUtc));
        }

        Name = Required(name, 200, nameof(name));
        Priority = priority;
        ItemId = itemId;
        ItemCategory = Optional(itemCategory, 100);
        SupplierId = supplierId;
        InboundSourceType = OptionalUpper(inboundSourceType, 30);
        CustomerId = customerId;
        SalesOrderId = salesOrderId;
        DestinationLocationId = destinationLocationId;
        QuantityTolerancePercent = quantityTolerancePercent;
        MinimumShelfLifeDays = minimumShelfLifeDays;
        RequireExpiry = requireExpiry;
        AllowPlanned = allowPlanned;
        AllowOpportunistic = allowOpportunistic;
        EffectiveFromUtc = start;
        EffectiveToUtc = end;
        IsActive = isActive;
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

    private static string? OptionalUpper(string? value, int maximumLength) =>
        Optional(value, maximumLength)?.ToUpperInvariant();

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value.HasValue
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : null;
}
