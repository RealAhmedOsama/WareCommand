using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class WaveTemplate : Entity
{
    private WaveTemplate()
    {
    }

    public WaveTemplate(
        int warehouseId,
        string templateKey,
        string name,
        WaveTriggerType triggerType,
        int priority,
        int limit,
        bool releaseToWarehouse,
        string? scheduleCron = null,
        DateOnly? requestedShipDateFrom = null,
        DateOnly? requestedShipDateTo = null,
        string? carrierCode = null,
        int? customerId = null,
        int? minimumPriority = null,
        string? sourceType = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        Validate(triggerType, priority, limit, requestedShipDateFrom, requestedShipDateTo);
        WarehouseId = warehouseId;
        TemplateKey = Required(templateKey, 80, nameof(templateKey)).ToUpperInvariant();
        Name = Required(name, 200, nameof(name));
        TriggerType = triggerType;
        Priority = priority;
        Limit = limit;
        ReleaseToWarehouse = releaseToWarehouse;
        ScheduleCron = Optional(scheduleCron, 100);
        RequestedShipDateFrom = requestedShipDateFrom;
        RequestedShipDateTo = requestedShipDateTo;
        CarrierCode = OptionalUpper(carrierCode, 50);
        CustomerId = customerId;
        MinimumPriority = minimumPriority;
        SourceType = OptionalUpper(sourceType, 30);
        IsActive = true;
        Revision = 1;
    }

    public int WarehouseId { get; private set; }
    public string TemplateKey { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public WaveTriggerType TriggerType { get; private set; }
    public int Priority { get; private set; }
    public int Limit { get; private set; }
    public bool ReleaseToWarehouse { get; private set; }
    public string? ScheduleCron { get; private set; }
    public DateOnly? RequestedShipDateFrom { get; private set; }
    public DateOnly? RequestedShipDateTo { get; private set; }
    public string? CarrierCode { get; private set; }
    public int? CustomerId { get; private set; }
    public int? MinimumPriority { get; private set; }
    public string? SourceType { get; private set; }
    public bool IsActive { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Customer? Customer { get; private set; }

    public void Update(
        string name,
        WaveTriggerType triggerType,
        int priority,
        int limit,
        bool releaseToWarehouse,
        string? scheduleCron,
        DateOnly? requestedShipDateFrom,
        DateOnly? requestedShipDateTo,
        string? carrierCode,
        int? customerId,
        int? minimumPriority,
        string? sourceType)
    {
        Validate(triggerType, priority, limit, requestedShipDateFrom, requestedShipDateTo);
        Name = Required(name, 200, nameof(name));
        TriggerType = triggerType;
        Priority = priority;
        Limit = limit;
        ReleaseToWarehouse = releaseToWarehouse;
        ScheduleCron = Optional(scheduleCron, 100);
        RequestedShipDateFrom = requestedShipDateFrom;
        RequestedShipDateTo = requestedShipDateTo;
        CarrierCode = OptionalUpper(carrierCode, 50);
        CustomerId = customerId;
        MinimumPriority = minimumPriority;
        SourceType = OptionalUpper(sourceType, 30);
        Revision++;
        SetUpdatedAt();
    }

    public void Activate()
    {
        IsActive = true;
        Revision++;
        SetUpdatedAt();
    }

    public void Deactivate()
    {
        IsActive = false;
        Revision++;
        SetUpdatedAt();
    }

    private static void Validate(
        WaveTriggerType triggerType,
        int priority,
        int limit,
        DateOnly? requestedShipDateFrom,
        DateOnly? requestedShipDateTo)
    {
        if (!Enum.IsDefined(triggerType))
        {
            throw new ArgumentOutOfRangeException(nameof(triggerType));
        }

        if (priority is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        if (limit is <= 0 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        if (requestedShipDateTo.HasValue && requestedShipDateFrom.HasValue &&
            requestedShipDateTo.Value < requestedShipDateFrom.Value)
        {
            throw new ArgumentException(
                "The requested ship-date end cannot be earlier than the start.",
                nameof(requestedShipDateTo));
        }
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
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }

    private static string? Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Required(value, maximumLength, nameof(value));

    private static string? OptionalUpper(string? value, int maximumLength) =>
        Optional(value, maximumLength)?.ToUpperInvariant();
}
