using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class WarehouseWork : Entity
{
    private readonly List<WarehouseWorkLine> _lines = [];

    private WarehouseWork()
    {
    }

    public WarehouseWork(
        string workNumber,
        string creationKey,
        WarehouseWorkType type,
        int warehouseId,
        string sourceEntityType,
        string sourceEntityId,
        int priority = 50,
        string? sourceLineReference = null,
        string? queueCode = null,
        DateTime? dueAtUtc = null,
        string? teamCode = null,
        string? notes = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        WorkNumber = Required(workNumber, 80, nameof(workNumber));
        CreationKey = Required(creationKey, 250, nameof(creationKey));
        Type = type;
        WarehouseId = warehouseId;
        SourceEntityType = Required(sourceEntityType, 100, nameof(sourceEntityType));
        SourceEntityId = Required(sourceEntityId, 200, nameof(sourceEntityId));
        SourceLineReference = Optional(sourceLineReference, 100);
        QueueCode = OptionalUpper(queueCode, 50) ?? type.ToString().ToUpperInvariant();
        Priority = priority >= 0 ? priority : throw new ArgumentOutOfRangeException(nameof(priority));
        DueAtUtc = dueAtUtc.HasValue ? NormalizeUtc(dueAtUtc.Value) : null;
        TeamCode = OptionalUpper(teamCode, 50);
        Notes = Optional(notes, 2_000);
        Status = WarehouseWorkStatus.Open;
        Revision = 1;
    }

    public string WorkNumber { get; private set; } = string.Empty;
    public string CreationKey { get; private set; } = string.Empty;
    public WarehouseWorkType Type { get; private set; }
    public int WarehouseId { get; private set; }
    public string SourceEntityType { get; private set; } = string.Empty;
    public string SourceEntityId { get; private set; } = string.Empty;
    public string? SourceLineReference { get; private set; }
    public string QueueCode { get; private set; } = string.Empty;
    public int Priority { get; private set; }
    public DateTime? DueAtUtc { get; private set; }
    public string? TeamCode { get; private set; }
    public string? Notes { get; private set; }
    public WarehouseWorkStatus Status { get; private set; }
    public string? AssignedUserId { get; private set; }
    public string? AssignedTeamCode { get; private set; }
    public string? AssignedByUserId { get; private set; }
    public DateTime? AssignedAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? PausedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CompletedByUserId { get; private set; }
    public string? CancelledByUserId { get; private set; }
    public string? CancellationReason { get; private set; }
    public WarehouseWorkExceptionType? ExceptionType { get; private set; }
    public string? ExceptionReason { get; private set; }
    public string? ExceptionByUserId { get; private set; }
    public DateTime? ExceptionAtUtc { get; private set; }
    public bool SupervisorOverride { get; private set; }
    public string? OverrideReason { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public IReadOnlyList<WarehouseWorkLine> Lines => _lines.AsReadOnly();
    public bool IsTerminal => Status is WarehouseWorkStatus.Completed or WarehouseWorkStatus.Cancelled;
    public bool HasExceptions => ExceptionType.HasValue;

    public void AddLine(WarehouseWorkLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsureNotTerminal();
        if (line.WarehouseWorkId != 0 && line.WarehouseWorkId != Id)
        {
            throw new InvalidOperationException("The work line already belongs to another work item.");
        }

        if (_lines.Any(existing => existing.Sequence == line.Sequence))
        {
            throw new InvalidOperationException("Work-line sequence values must be unique.");
        }

        _lines.Add(line);
        Touch();
    }

    public void MakeAvailable(DateTime availableAtUtc)
    {
        EnsureTransition(WarehouseWorkStatus.Available);
        Status = WarehouseWorkStatus.Available;
        Touch(availableAtUtc);
    }

    public void Assign(
        string? userId,
        string? teamCode,
        string assignedByUserId,
        DateTime assignedAtUtc,
        bool supervisorOverride = false,
        string? overrideReason = null)
    {
        EnsureTransition(WarehouseWorkStatus.Assigned);
        ValidateOverride(supervisorOverride, overrideReason);
        if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(teamCode))
        {
            throw new ArgumentException("A work assignment requires a user or team.");
        }

        AssignedUserId = Optional(userId, 450);
        AssignedTeamCode = OptionalUpper(teamCode, 50);
        AssignedByUserId = Required(assignedByUserId, 450, nameof(assignedByUserId));
        AssignedAtUtc = NormalizeUtc(assignedAtUtc);
        Status = WarehouseWorkStatus.Assigned;
        SetOverride(supervisorOverride, overrideReason);
        Touch(assignedAtUtc);
    }

    public void Release(string userId, DateTime releasedAtUtc, string? reason = null)
    {
        if (Status is not (WarehouseWorkStatus.Assigned or WarehouseWorkStatus.InProgress or WarehouseWorkStatus.Paused or WarehouseWorkStatus.Exception))
        {
            throw new InvalidOperationException($"Work in {Status} cannot be released.");
        }

        AssignedUserId = null;
        AssignedTeamCode = null;
        AssignedByUserId = null;
        AssignedAtUtc = null;
        if (Status != WarehouseWorkStatus.Exception)
        {
            Status = WarehouseWorkStatus.Available;
        }

        ExceptionReason = string.IsNullOrWhiteSpace(reason) ? ExceptionReason : Optional(reason, 1_000);
        Touch(releasedAtUtc);
    }

    public void Start(string userId, DateTime startedAtUtc)
    {
        EnsureTransition(WarehouseWorkStatus.InProgress);
        if (!string.IsNullOrWhiteSpace(AssignedUserId) &&
            !string.Equals(AssignedUserId, userId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only the assigned worker can start this work.");
        }

        AssignedUserId ??= Required(userId, 450, nameof(userId));
        StartedAtUtc ??= NormalizeUtc(startedAtUtc);
        Status = WarehouseWorkStatus.InProgress;
        Touch(startedAtUtc);
    }

    public void Pause(string userId, DateTime pausedAtUtc, string? reason = null)
    {
        EnsureTransition(WarehouseWorkStatus.Paused);
        EnsureAssignee(userId);
        Status = WarehouseWorkStatus.Paused;
        PausedAtUtc = NormalizeUtc(pausedAtUtc);
        ExceptionReason = string.IsNullOrWhiteSpace(reason) ? ExceptionReason : Optional(reason, 1_000);
        Touch(pausedAtUtc);
    }

    public void Resume(string userId, DateTime resumedAtUtc)
    {
        EnsureTransition(WarehouseWorkStatus.InProgress);
        EnsureAssignee(userId);
        Status = WarehouseWorkStatus.InProgress;
        PausedAtUtc = null;
        Touch(resumedAtUtc);
    }

    public void RecordException(
        WarehouseWorkExceptionType exceptionType,
        string reason,
        string userId,
        DateTime occurredAtUtc)
    {
        EnsureNotTerminal();
        if (!Enum.IsDefined(exceptionType))
        {
            throw new ArgumentOutOfRangeException(nameof(exceptionType));
        }

        Status = WarehouseWorkStatus.Exception;
        ExceptionType = exceptionType;
        ExceptionReason = Required(reason, 1_000, nameof(reason));
        ExceptionByUserId = Required(userId, 450, nameof(userId));
        ExceptionAtUtc = NormalizeUtc(occurredAtUtc);
        Touch(occurredAtUtc);
    }

    public void ResolveException(string userId, string reason, DateTime resolvedAtUtc)
    {
        if (Status != WarehouseWorkStatus.Exception)
        {
            throw new InvalidOperationException($"Work in {Status} is not waiting on an exception resolution.");
        }

        var normalizedUserId = Required(userId, 450, nameof(userId));
        ExceptionReason = Required(reason, 1_000, nameof(reason));
        ExceptionByUserId = normalizedUserId;
        Status = WarehouseWorkStatus.Available;
        AssignedUserId = null;
        AssignedTeamCode = null;
        AssignedByUserId = null;
        AssignedAtUtc = null;
        Touch(resolvedAtUtc);
    }

    public void Complete(
        string userId,
        DateTime completedAtUtc,
        bool supervisorOverride = false,
        string? overrideReason = null)
    {
        EnsureTransition(WarehouseWorkStatus.Completed);
        EnsureAssignee(userId);
        ValidateOverride(supervisorOverride, overrideReason);
        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("Work cannot complete without at least one line.");
        }

        if (_lines.Any(line => line.ActualQuantity < line.PlannedQuantity) && !supervisorOverride)
        {
            throw new InvalidOperationException("Short work requires a supervisor override or an exception route.");
        }

        if (_lines.Any(line => line.ActualQuantity > line.PlannedQuantity))
        {
            throw new InvalidOperationException("Actual work quantity cannot exceed the planned quantity.");
        }

        Status = WarehouseWorkStatus.Completed;
        CompletedByUserId = Required(userId, 450, nameof(userId));
        CompletedAtUtc = NormalizeUtc(completedAtUtc);
        SetOverride(supervisorOverride, overrideReason);
        Touch(completedAtUtc);
    }

    public void Cancel(string userId, string reason, DateTime cancelledAtUtc)
    {
        EnsureNotTerminal();
        Status = WarehouseWorkStatus.Cancelled;
        CancelledByUserId = Required(userId, 450, nameof(userId));
        CancellationReason = Required(reason, 1_000, nameof(reason));
        CancelledAtUtc = NormalizeUtc(cancelledAtUtc);
        Touch(cancelledAtUtc);
    }

    private void EnsureTransition(WarehouseWorkStatus target)
    {
        if (Status == target)
        {
            throw new InvalidOperationException($"Work is already in {target}.");
        }

        var allowed = Status switch
        {
            WarehouseWorkStatus.Open => target is WarehouseWorkStatus.Available or WarehouseWorkStatus.Assigned or WarehouseWorkStatus.Cancelled,
            WarehouseWorkStatus.Available => target is WarehouseWorkStatus.Assigned or WarehouseWorkStatus.Cancelled or WarehouseWorkStatus.Exception,
            WarehouseWorkStatus.Assigned => target is WarehouseWorkStatus.InProgress or WarehouseWorkStatus.Available or WarehouseWorkStatus.Cancelled or WarehouseWorkStatus.Exception,
            WarehouseWorkStatus.InProgress => target is WarehouseWorkStatus.Paused or WarehouseWorkStatus.Completed or WarehouseWorkStatus.Cancelled or WarehouseWorkStatus.Exception,
            WarehouseWorkStatus.Paused => target is WarehouseWorkStatus.InProgress or WarehouseWorkStatus.Cancelled or WarehouseWorkStatus.Exception,
            WarehouseWorkStatus.Exception => target is WarehouseWorkStatus.Available or WarehouseWorkStatus.Assigned or WarehouseWorkStatus.InProgress or WarehouseWorkStatus.Cancelled,
            _ => false
        };
        if (!allowed)
        {
            throw new InvalidOperationException($"Work cannot transition from {Status} to {target}.");
        }
    }

    private void EnsureAssignee(string userId)
    {
        var normalized = Required(userId, 450, nameof(userId));
        if (!string.IsNullOrWhiteSpace(AssignedUserId) &&
            !string.Equals(AssignedUserId, normalized, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only the assigned worker can change this work state.");
        }

        AssignedUserId ??= normalized;
    }

    private void EnsureNotTerminal()
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException($"Work in {Status} cannot be changed.");
        }
    }

    private static void ValidateOverride(bool supervisorOverride, string? overrideReason)
    {
        if (supervisorOverride && string.IsNullOrWhiteSpace(overrideReason))
        {
            throw new InvalidOperationException("A supervisor override reason is required.");
        }
    }

    private void SetOverride(bool supervisorOverride, string? overrideReason)
    {
        SupervisorOverride = supervisorOverride;
        OverrideReason = supervisorOverride ? Optional(overrideReason, 1_000) : null;
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
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", nameof(value));
    }

    private static string? OptionalUpper(string? value, int maximumLength) =>
        Optional(value, maximumLength)?.ToUpperInvariant();
}
