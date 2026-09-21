using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class Wave : Entity
{
    private readonly List<WaveLine> _lines = [];
    private readonly List<WaveProcessingHistory> _history = [];

    private Wave()
    {
    }

    public Wave(
        int warehouseId,
        string waveNumber,
        string creationKey,
        int? templateId,
        string? templateKey,
        WaveTriggerType triggerType,
        int priority,
        DateTime? plannedStartAtUtc,
        DateTime? plannedReleaseAtUtc,
        string criteriaJson,
        int capacityLimit,
        string createdByUserId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        if (!Enum.IsDefined(triggerType))
        {
            throw new ArgumentOutOfRangeException(nameof(triggerType));
        }

        if (priority is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        if (capacityLimit is <= 0 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(capacityLimit));
        }

        WarehouseId = warehouseId;
        WaveNumber = Required(waveNumber, 80, nameof(waveNumber));
        CreationKey = Required(creationKey, 250, nameof(creationKey));
        TemplateId = templateId;
        TemplateKey = Optional(templateKey, 80);
        TriggerType = triggerType;
        Priority = priority;
        PlannedStartAtUtc = NormalizeUtc(plannedStartAtUtc);
        PlannedReleaseAtUtc = NormalizeUtc(plannedReleaseAtUtc);
        CriteriaJson = Required(criteriaJson, 8_000, nameof(criteriaJson));
        CapacityLimit = capacityLimit;
        CreatedByUserId = Required(createdByUserId, 450, nameof(createdByUserId));
        Status = WaveStatus.Planned;
        Revision = 1;
    }

    public int WarehouseId { get; private set; }
    public string WaveNumber { get; private set; } = string.Empty;
    public string CreationKey { get; private set; } = string.Empty;
    public int? TemplateId { get; private set; }
    public string? TemplateKey { get; private set; }
    public WaveTriggerType TriggerType { get; private set; }
    public int Priority { get; private set; }
    public DateTime? PlannedStartAtUtc { get; private set; }
    public DateTime? PlannedReleaseAtUtc { get; private set; }
    public string CriteriaJson { get; private set; } = "{}";
    public int CapacityLimit { get; private set; }
    public WaveStatus Status { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public string? LastProcessIdempotencyKey { get; private set; }
    public DateTime? LastProcessedAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public WaveTemplate? Template { get; private set; }
    public IReadOnlyList<WaveLine> Lines => _lines.AsReadOnly();
    public IReadOnlyList<WaveProcessingHistory> History => _history.AsReadOnly();

    public bool IsTerminal => Status is WaveStatus.Completed or WaveStatus.Cancelled;
    public bool CanProcess => Status is
        WaveStatus.Planned or
        WaveStatus.Processing or
        WaveStatus.PartiallyProcessed or
        WaveStatus.Failed;

    public void AddLine(WaveLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (Status != WaveStatus.Planned)
        {
            throw new InvalidOperationException("Lines can only be added before wave processing starts.");
        }

        if (_lines.Any(existing => existing.SalesOrderLineId == line.SalesOrderLineId))
        {
            throw new InvalidOperationException("A sales-order line cannot be added to a wave twice.");
        }

        _lines.Add(line);
        Touch();
    }

    public void StartProcessing(string idempotencyKey, DateTime processedAtUtc)
    {
        if (!CanProcess)
        {
            throw new InvalidOperationException($"Wave '{WaveNumber}' cannot be processed while it is {Status}.");
        }

        Status = WaveStatus.Processing;
        LastProcessIdempotencyKey = Required(idempotencyKey, 250, nameof(idempotencyKey));
        LastProcessedAtUtc = NormalizeUtc(processedAtUtc);
        LastError = null;
        Touch(processedAtUtc);
    }

    public void SetResult(WaveStatus status, string? error = null, DateTime? processedAtUtc = null)
    {
        if (status is not (WaveStatus.PartiallyProcessed or WaveStatus.Released or WaveStatus.Completed or WaveStatus.Failed))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        Status = status;
        LastError = Optional(error, 2_000);
        LastProcessedAtUtc = NormalizeUtc(processedAtUtc);
        Touch(processedAtUtc ?? DateTime.UtcNow);
    }

    public void RemoveLine(int salesOrderLineId, string? reason = null)
    {
        if (Status is not (WaveStatus.Planned or WaveStatus.PartiallyProcessed or WaveStatus.Failed))
        {
            throw new InvalidOperationException("A line can only be removed before release or after a failed/partial process.");
        }

        var line = _lines.SingleOrDefault(value => value.SalesOrderLineId == salesOrderLineId)
            ?? throw new KeyNotFoundException($"Sales-order line '{salesOrderLineId}' is not in this wave.");
        line.MarkRemoved(reason);
        Touch();
    }

    public void Cancel(string reason, DateTime cancelledAtUtc)
    {
        if (Status is WaveStatus.Released or WaveStatus.Completed or WaveStatus.Cancelled)
        {
            throw new InvalidOperationException($"Wave '{WaveNumber}' cannot be cancelled while it is {Status}.");
        }

        Status = WaveStatus.Cancelled;
        LastError = Required(reason, 2_000, nameof(reason));
        foreach (var line in _lines.Where(line => line.Status is not (WaveLineStatus.Released or WaveLineStatus.Removed)))
        {
            line.MarkCancelled(reason);
        }

        Touch(cancelledAtUtc);
    }

    public void AddHistory(WaveProcessingHistory entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _history.Add(entry);
        Touch();
    }

    private void Touch(DateTime? timestampUtc = null)
    {
        Revision++;
        SetUpdatedAt(timestampUtc);
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

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;

}
