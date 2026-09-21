using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class CycleCountTask : Entity
{
    private readonly List<CycleCountLine> _lines = [];

    private CycleCountTask()
    {
    }

    public CycleCountTask(
        string taskKey,
        string taskNumber,
        int planId,
        int warehouseId,
        int? locationId,
        bool blind,
        CycleCountFreezePolicy freezePolicy,
        DateTime snapshotAtUtc,
        string createdByUserId)
    {
        TaskKey = Required(taskKey, 250, nameof(taskKey));
        TaskNumber = Required(taskNumber, 80, nameof(taskNumber));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(planId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        if (locationId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(locationId));
        }

        if (!Enum.IsDefined(freezePolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(freezePolicy));
        }

        PlanId = planId;
        WarehouseId = warehouseId;
        LocationId = locationId;
        Blind = blind;
        FreezePolicy = freezePolicy;
        SnapshotAtUtc = Normalize(snapshotAtUtc);
        CreatedByUserId = Required(createdByUserId, 450, nameof(createdByUserId));
        Status = CycleCountTaskStatus.Planned;
        Revision = 1;
    }

    public string TaskKey { get; private set; } = string.Empty;
    public string TaskNumber { get; private set; } = string.Empty;
    public int PlanId { get; private set; }
    public int WarehouseId { get; private set; }
    public int? LocationId { get; private set; }
    public int? WarehouseWorkId { get; private set; }
    public bool Blind { get; private set; }
    public CycleCountFreezePolicy FreezePolicy { get; private set; }
    public DateTime SnapshotAtUtc { get; private set; }
    public CycleCountTaskStatus Status { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public string? StartedByUserId { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public string? ApprovedByUserId { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public string? ApprovalReason { get; private set; }
    public long Revision { get; private set; }

    public CycleCountPlan Plan { get; private set; } = null!;
    public Warehouse Warehouse { get; private set; } = null!;
    public Location? Location { get; private set; }
    public WarehouseWork? WarehouseWork { get; private set; }
    public IReadOnlyList<CycleCountLine> Lines => _lines.AsReadOnly();
    public bool IsTerminal => Status is CycleCountTaskStatus.Completed or CycleCountTaskStatus.Cancelled;

    public void AddLine(CycleCountLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (IsTerminal)
        {
            throw new InvalidOperationException("A terminal count task cannot receive lines.");
        }

        if (_lines.Any(existing => existing.Sequence == line.Sequence))
        {
            throw new InvalidOperationException("Count-line sequence values must be unique.");
        }

        _lines.Add(line);
        Touch();
    }

    public void LinkWork(int workId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workId);
        if (WarehouseWorkId.HasValue && WarehouseWorkId.Value != workId)
        {
            throw new InvalidOperationException("The count task is already linked to another work item.");
        }

        WarehouseWorkId = workId;
        Touch();
    }

    public void Start(string userId, DateTime startedAtUtc)
    {
        if (Status != CycleCountTaskStatus.Planned && Status != CycleCountTaskStatus.RecountRequired)
        {
            throw new InvalidOperationException($"Count task in {Status} cannot start.");
        }

        StartedByUserId = Required(userId, 450, nameof(userId));
        StartedAtUtc = Normalize(startedAtUtc);
        Status = CycleCountTaskStatus.InProgress;
        Touch(startedAtUtc);
    }

    public void Submit(DateTime submittedAtUtc)
    {
        if (Status != CycleCountTaskStatus.InProgress ||
            _lines.Count == 0 ||
            _lines.Any(line => !line.CountedQuantity.HasValue))
        {
            throw new InvalidOperationException("Every count line must be counted before submission.");
        }

        SubmittedAtUtc = Normalize(submittedAtUtc);
        Status = _lines.Any(line => line.VarianceQuantity != 0m)
            ? CycleCountTaskStatus.AwaitingApproval
            : CycleCountTaskStatus.Approved;
        Touch(submittedAtUtc);
    }

    public void Approve(string userId, string reason, DateTime approvedAtUtc)
    {
        if (Status != CycleCountTaskStatus.AwaitingApproval)
        {
            throw new InvalidOperationException($"Count task in {Status} is not awaiting approval.");
        }

        ApprovedByUserId = Required(userId, 450, nameof(userId));
        ApprovalReason = Required(reason, 1_000, nameof(reason));
        ApprovedAtUtc = Normalize(approvedAtUtc);
        Status = CycleCountTaskStatus.Approved;
        Touch(approvedAtUtc);
    }

    public void RequireRecount(string reason, DateTime atUtc)
    {
        ApprovalReason = Required(reason, 1_000, nameof(reason));
        Status = CycleCountTaskStatus.RecountRequired;
        Touch(atUtc);
    }

    public void Complete(DateTime completedAtUtc)
    {
        if (Status != CycleCountTaskStatus.Approved ||
            _lines.Any(line => line.VarianceQuantity != 0m && line.Status != CycleCountLineStatus.Approved))
        {
            throw new InvalidOperationException("Only an approved count task can complete.");
        }

        Status = CycleCountTaskStatus.Completed;
        Touch(completedAtUtc);
    }

    public void Cancel(string reason, DateTime cancelledAtUtc)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException("A terminal count task cannot be cancelled.");
        }

        ApprovalReason = Required(reason, 1_000, nameof(reason));
        Status = CycleCountTaskStatus.Cancelled;
        Touch(cancelledAtUtc);
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

    private static DateTime Normalize(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
