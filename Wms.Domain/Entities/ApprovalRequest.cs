using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Snapshot of an approved sensitive command. The request owns the approval
/// state; decision rows and execution rows provide immutable history and the
/// exactly-once execution gate respectively.
/// </summary>
public sealed class ApprovalRequest : Entity
{
    private ApprovalRequest()
    {
    }

    public ApprovalRequest(
        string requestIdempotencyKey,
        string module,
        string operation,
        int? warehouseId,
        string reasonCode,
        int reasonCodeId,
        string? policyCode,
        int? policyId,
        string sourceEntityType,
        string sourceEntityId,
        string? sourceReference,
        string requesterUserId,
        string requiredPermission,
        decimal? quantity,
        decimal? value,
        decimal? variancePercent,
        string? itemRisk,
        string? statusRisk,
        string currentStateHash,
        string? notes,
        string? attachmentReference,
        IReadOnlyList<ApprovalLevelDefinition> approvalLevels,
        bool requireSeparationOfDuties,
        DateTimeOffset requestedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (warehouseId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warehouseId));
        }

        if (reasonCodeId <= 0 || !policyId.HasValue || policyId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reasonCodeId));
        }

        var requested = requestedAtUtc.ToUniversalTime();
        var expires = expiresAtUtc.ToUniversalTime();
        if (expires <= requested)
        {
            throw new ArgumentException("The approval expiry must be later than the request time.", nameof(expiresAtUtc));
        }

        RequestIdempotencyKey = Required(requestIdempotencyKey, 250, nameof(requestIdempotencyKey));
        Module = Required(module, 100, nameof(module));
        Operation = Required(operation, 150, nameof(operation));
        ReasonCode = Required(reasonCode, 80, nameof(reasonCode)).ToUpperInvariant();
        ReasonCodeId = reasonCodeId;
        PolicyCode = Optional(policyCode, 80)?.ToUpperInvariant();
        PolicyId = policyId;
        SourceEntityType = Required(sourceEntityType, 100, nameof(sourceEntityType));
        SourceEntityId = Required(sourceEntityId, 200, nameof(sourceEntityId));
        SourceReference = Optional(sourceReference, 250);
        RequesterUserId = Required(requesterUserId, 450, nameof(requesterUserId));
        RequiredPermission = Required(requiredPermission, 100, nameof(requiredPermission));
        Quantity = quantity;
        Value = value;
        VariancePercent = variancePercent;
        ItemRisk = Optional(itemRisk, 50);
        StatusRisk = Optional(statusRisk, 50);
        CurrentStateHash = Required(currentStateHash, 128, nameof(currentStateHash));
        Notes = Optional(notes, 2_000);
        AttachmentReference = Optional(attachmentReference, 500);
        ApprovalLevelsJson = ApprovalPolicy.SerializeLevels(approvalLevels);
        RequireSeparationOfDuties = requireSeparationOfDuties;
        CurrentLevel = 1;
        Status = ApprovalRequestStatus.Pending;
        RequestedAtUtc = requested;
        ExpiresAtUtc = expires;
        Revision = 1;
    }

    public string RequestIdempotencyKey { get; private set; } = string.Empty;
    public string Module { get; private set; } = string.Empty;
    public string Operation { get; private set; } = string.Empty;
    public int? WarehouseId { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public int ReasonCodeId { get; private set; }
    public string? PolicyCode { get; private set; }
    public int? PolicyId { get; private set; }
    public string SourceEntityType { get; private set; } = string.Empty;
    public string SourceEntityId { get; private set; } = string.Empty;
    public string? SourceReference { get; private set; }
    public string RequesterUserId { get; private set; } = string.Empty;
    public string RequiredPermission { get; private set; } = string.Empty;
    public decimal? Quantity { get; private set; }
    public decimal? Value { get; private set; }
    public decimal? VariancePercent { get; private set; }
    public string? ItemRisk { get; private set; }
    public string? StatusRisk { get; private set; }
    public string CurrentStateHash { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public string? AttachmentReference { get; private set; }
    public string ApprovalLevelsJson { get; private set; } = "[]";
    public bool RequireSeparationOfDuties { get; private set; }
    public int CurrentLevel { get; private set; }
    public ApprovalRequestStatus Status { get; private set; }
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }
    public DateTimeOffset? ExecutingAtUtc { get; private set; }
    public DateTimeOffset? ExecutedAtUtc { get; private set; }
    public string? LastComment { get; private set; }
    public long Revision { get; private set; }

    public IReadOnlyList<ApprovalLevelDefinition> GetApprovalLevels() =>
        System.Text.Json.JsonSerializer.Deserialize<List<ApprovalLevelDefinition>>(ApprovalLevelsJson)
        ?? throw new InvalidOperationException("The approval request has no valid approval levels.");

    public bool IsTerminal => Status is ApprovalRequestStatus.Rejected or
        ApprovalRequestStatus.Cancelled or ApprovalRequestStatus.Expired or ApprovalRequestStatus.Executed;

    public void AdvanceApproval(string? comment, DateTimeOffset decidedAtUtc)
    {
        EnsurePending();
        var levels = GetApprovalLevels();
        LastComment = Optional(comment, 2_000);
        if (CurrentLevel < levels.Count)
        {
            CurrentLevel++;
        }
        else
        {
            Status = ApprovalRequestStatus.Approved;
            DecidedAtUtc = decidedAtUtc.ToUniversalTime();
        }

        Revision++;
        SetUpdatedAt(decidedAtUtc.UtcDateTime);
    }

    public void Reject(string? comment, DateTimeOffset decidedAtUtc)
    {
        EnsurePending();
        Status = ApprovalRequestStatus.Rejected;
        DecidedAtUtc = decidedAtUtc.ToUniversalTime();
        LastComment = Optional(comment, 2_000);
        Revision++;
        SetUpdatedAt(decidedAtUtc.UtcDateTime);
    }

    public void Cancel(string? comment, DateTimeOffset cancelledAtUtc)
    {
        if (Status is not (ApprovalRequestStatus.Pending or ApprovalRequestStatus.Approved))
        {
            throw new InvalidOperationException("Only pending or approved requests can be cancelled.");
        }

        Status = ApprovalRequestStatus.Cancelled;
        DecidedAtUtc = cancelledAtUtc.ToUniversalTime();
        LastComment = Optional(comment, 2_000);
        Revision++;
        SetUpdatedAt(cancelledAtUtc.UtcDateTime);
    }

    public void Escalate(string? comment, DateTimeOffset escalatedAtUtc)
    {
        EnsurePending();
        var levels = GetApprovalLevels();
        if (CurrentLevel >= levels.Count)
        {
            throw new InvalidOperationException("The approval request is already at its highest level.");
        }

        CurrentLevel++;
        LastComment = Optional(comment, 2_000);
        Revision++;
        SetUpdatedAt(escalatedAtUtc.UtcDateTime);
    }

    public void Expire(DateTimeOffset expiredAtUtc)
    {
        if (Status is not (ApprovalRequestStatus.Pending or ApprovalRequestStatus.Approved))
        {
            return;
        }

        Status = ApprovalRequestStatus.Expired;
        DecidedAtUtc = expiredAtUtc.ToUniversalTime();
        LastComment = "Approval request expired.";
        Revision++;
        SetUpdatedAt(expiredAtUtc.UtcDateTime);
    }

    public void BeginExecution(DateTimeOffset startedAtUtc)
    {
        if (Status != ApprovalRequestStatus.Approved)
        {
            throw new InvalidOperationException("Only an approved request can begin execution.");
        }

        Status = ApprovalRequestStatus.Executing;
        ExecutingAtUtc = startedAtUtc.ToUniversalTime();
        Revision++;
        SetUpdatedAt(startedAtUtc.UtcDateTime);
    }

    public void CompleteExecution(DateTimeOffset completedAtUtc)
    {
        if (Status != ApprovalRequestStatus.Executing)
        {
            throw new InvalidOperationException("Only an executing request can be completed.");
        }

        Status = ApprovalRequestStatus.Executed;
        ExecutedAtUtc = completedAtUtc.ToUniversalTime();
        Revision++;
        SetUpdatedAt(completedAtUtc.UtcDateTime);
    }

    private void EnsurePending()
    {
        if (Status != ApprovalRequestStatus.Pending)
        {
            throw new InvalidOperationException("Only a pending approval request can receive this decision.");
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
}
