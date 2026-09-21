using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Approvals;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Approvals;

/// <summary>
/// Shared reason-code and approval boundary. Business modules call RequestAsync
/// before a sensitive command and Begin/CompleteExecutionAsync around the
/// command's own transaction. The boundary rechecks permission and the state
/// hash so an approval cannot authorize a stale or differently scoped command.
/// </summary>
public sealed class ApprovalService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IApprovalRoleDirectory roleDirectory,
    ICurrentUser currentUser,
    IAuditWriter auditWriter,
    IClock clock) : IApprovalService
{
    private const int MaximumPageSize = 200;

    public async Task<Result<ReasonCodeDto>> SaveReasonCodeAsync(
        int? reasonCodeId,
        ReasonCodeInput input,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ApprovalManage,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ReasonCodeDto>();
        }

        try
        {
            var normalizedCode = input.Code.Trim().ToUpperInvariant();
            var duplicate = await context.ReasonCodes
                .AsNoTracking()
                .AnyAsync(reason =>
                    reason.Id != reasonCodeId &&
                    reason.WarehouseId == input.WarehouseId &&
                    reason.Code == normalizedCode,
                    cancellationToken);
            if (duplicate)
            {
                return Failure<ReasonCodeDto>(
                    WmsErrors.Conflict(
                        "approval.reason_code_duplicate",
                        "The reason code already exists for this warehouse scope."));
            }

            ReasonCode? reason;
            var action = reasonCodeId.HasValue
                ? WmsAuditActions.ReasonCodeChanged
                : WmsAuditActions.ReasonCodeCreated;
            if (reasonCodeId.HasValue)
            {
                reason = await context.ReasonCodes
                    .SingleOrDefaultAsync(item => item.Id == reasonCodeId.Value, cancellationToken);
                if (reason is null)
                {
                    return Failure<ReasonCodeDto>(WmsErrors.NotFound(
                        "approval.reason_code_not_found",
                        "The reason code was not found."));
                }
                reason.Update(
                    input.Code,
                    input.Category,
                    input.Module,
                    input.Operation,
                    input.NameEn,
                    input.NameAr,
                    input.DescriptionEn,
                    input.DescriptionAr,
                    input.EffectiveFromUtc,
                    input.EffectiveToUtc,
                    input.RequiresNotes,
                    input.RequiresAttachment,
                    input.Severity,
                    input.WarehouseId,
                    input.IsActive);
            }
            else
            {
                reason = new ReasonCode(
                    input.Code,
                    input.Category,
                    input.Module,
                    input.Operation,
                    input.NameEn,
                    input.NameAr,
                    input.DescriptionEn,
                    input.DescriptionAr,
                    input.EffectiveFromUtc,
                    input.EffectiveToUtc,
                    input.RequiresNotes,
                    input.RequiresAttachment,
                    input.Severity,
                    input.WarehouseId,
                    input.IsActive);
                context.ReasonCodes.Add(reason);
            }

            await auditWriter.RecordAsync(new AuditRecord(
                action,
                WmsAuditEntityTypes.ReasonCode,
                reasonCodeId?.ToString(CultureInfo.InvariantCulture),
                input.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["code"] = normalizedCode,
                    ["module"] = input.Module,
                    ["operation"] = input.Operation,
                    ["active"] = input.IsActive
                }), cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapReasonCode(reason));
        }
        catch (ArgumentException exception)
        {
            return Failure<ReasonCodeDto>(WmsErrors.Validation(
                "approval.reason_code_invalid",
                exception.Message));
        }
        catch (DbUpdateException exception)
        {
            return Failure<ReasonCodeDto>(WmsErrors.FromException(
                exception,
                "approval.reason_code_save_failed",
                "The reason code could not be saved."));
        }
    }

    public async Task<Result<IReadOnlyList<ReasonCodeDto>>> ListReasonCodesAsync(
        ReasonCodeQuery query,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ApprovalRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<ReasonCodeDto>>();
        }

        var reasons = context.ReasonCodes.AsNoTracking().AsQueryable();
        if (!query.IncludeInactive)
        {
            reasons = reasons.Where(reason => reason.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Module))
        {
            reasons = reasons.Where(reason => reason.Module == query.Module.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.Operation))
        {
            reasons = reasons.Where(reason => reason.Operation == query.Operation.Trim());
        }

        if (query.WarehouseId.HasValue)
        {
            reasons = reasons.Where(reason =>
                reason.WarehouseId == null || reason.WarehouseId == query.WarehouseId);
        }

        var reasonRows = await reasons
            .OrderBy(reason => reason.Code)
            .ThenBy(reason => reason.WarehouseId)
            .ToListAsync(cancellationToken);
        var result = reasonRows.Select(MapReasonCode).ToList();
        return Result.Success<IReadOnlyList<ReasonCodeDto>>(result);
    }

    public async Task<Result<ApprovalPolicyDto>> SaveApprovalPolicyAsync(
        int? policyId,
        ApprovalPolicyInput input,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ApprovalManage,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ApprovalPolicyDto>();
        }

        try
        {
            var normalizedCode = input.Code.Trim().ToUpperInvariant();
            var duplicate = await context.ApprovalPolicies
                .AsNoTracking()
                .AnyAsync(policy =>
                    policy.Id != policyId &&
                    policy.WarehouseId == input.WarehouseId &&
                    policy.Code == normalizedCode,
                    cancellationToken);
            if (duplicate)
            {
                return Failure<ApprovalPolicyDto>(
                    WmsErrors.Conflict(
                        "approval.policy_duplicate",
                        "The approval policy already exists for this warehouse scope."));
            }

            var levels = input.Levels
                .Select(level => new ApprovalLevelDefinition(
                    level.Level,
                    level.Roles
                        .Where(role => !string.IsNullOrWhiteSpace(role))
                        .Select(role => role.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()))
                .ToArray();
            ApprovalPolicy? policy;
            if (policyId.HasValue)
            {
                policy = await context.ApprovalPolicies
                    .SingleOrDefaultAsync(item => item.Id == policyId.Value, cancellationToken);
                if (policy is null)
                {
                    return Failure<ApprovalPolicyDto>(WmsErrors.NotFound(
                        "approval.policy_not_found",
                        "The approval policy was not found."));
                }
                policy.Update(
                    input.Code,
                    input.Name,
                    input.Module,
                    input.Operation,
                    input.WarehouseId,
                    input.ReasonCode,
                    input.Priority,
                    input.MinimumQuantity,
                    input.MinimumValue,
                    input.MinimumVariancePercent,
                    input.ItemRisk,
                    input.StatusRisk,
                    levels,
                    input.ExpiryMinutes,
                    input.RequireSeparationOfDuties,
                    input.EffectiveFromUtc,
                    input.EffectiveToUtc,
                    input.IsActive);
            }
            else
            {
                policy = new ApprovalPolicy(
                    input.Code,
                    input.Name,
                    input.Module,
                    input.Operation,
                    input.WarehouseId,
                    input.ReasonCode,
                    input.Priority,
                    input.MinimumQuantity,
                    input.MinimumValue,
                    input.MinimumVariancePercent,
                    input.ItemRisk,
                    input.StatusRisk,
                    levels,
                    input.ExpiryMinutes,
                    input.RequireSeparationOfDuties,
                    input.EffectiveFromUtc,
                    input.EffectiveToUtc,
                    input.IsActive);
                context.ApprovalPolicies.Add(policy);
            }

            await auditWriter.RecordAsync(new AuditRecord(
                WmsAuditActions.ApprovalPolicyChanged,
                WmsAuditEntityTypes.ApprovalPolicy,
                policyId?.ToString(CultureInfo.InvariantCulture),
                input.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["code"] = normalizedCode,
                    ["module"] = input.Module,
                    ["operation"] = input.Operation,
                    ["priority"] = input.Priority,
                    ["active"] = input.IsActive
                }), cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapPolicy(policy));
        }
        catch (ArgumentException exception)
        {
            return Failure<ApprovalPolicyDto>(WmsErrors.Validation(
                "approval.policy_invalid",
                exception.Message));
        }
        catch (DbUpdateException exception)
        {
            return Failure<ApprovalPolicyDto>(WmsErrors.FromException(
                exception,
                "approval.policy_save_failed",
                "The approval policy could not be saved."));
        }
    }

    public async Task<Result<IReadOnlyList<ApprovalPolicyDto>>> ListApprovalPoliciesAsync(
        ApprovalPolicyQuery query,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ApprovalRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<ApprovalPolicyDto>>();
        }

        var policies = context.ApprovalPolicies.AsNoTracking().AsQueryable();
        if (!query.IncludeInactive)
        {
            policies = policies.Where(policy => policy.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Module))
        {
            policies = policies.Where(policy => policy.Module == query.Module.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.Operation))
        {
            policies = policies.Where(policy => policy.Operation == query.Operation.Trim());
        }

        if (query.WarehouseId.HasValue)
        {
            policies = policies.Where(policy =>
                policy.WarehouseId == null || policy.WarehouseId == query.WarehouseId);
        }

        var policyRows = await policies
            .OrderByDescending(policy => policy.Priority)
            .ThenBy(policy => policy.Code)
            .ToListAsync(cancellationToken);
        var result = policyRows.Select(MapPolicy).ToList();
        return Result.Success<IReadOnlyList<ApprovalPolicyDto>>(result);
    }

    public async Task<Result<ApprovalEvaluationDto>> RequestAsync(
        ApprovalEvaluationInput input,
        CancellationToken cancellationToken = default)
    {
        if (!WmsPermissions.IsKnown(input.RequiredPermission))
        {
            return Failure<ApprovalEvaluationDto>(WmsErrors.Validation(
                "approval.permission_invalid",
                "The protected operation permission is not recognized."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            input.RequiredPermission,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ApprovalEvaluationDto>();
        }

        var requesterUserId = GetCurrentUserId();
        if (requesterUserId is null)
        {
            return Failure<ApprovalEvaluationDto>(WmsErrors.Unauthorized(
                "approval.authentication_required",
                "An authenticated requester is required."));
        }

        var nowUtc = clock.UtcNow;
        var normalizedReasonCode = input.ReasonCode.Trim().ToUpperInvariant();
        var reason = await context.ReasonCodes
            .Where(item => item.IsActive && item.Code == normalizedReasonCode)
            .ToListAsync(cancellationToken);
        var selectedReason = reason
            .Where(item => item.IsEffective(nowUtc) && item.AppliesTo(
                input.Module,
                input.Operation,
                input.WarehouseId))
            .OrderBy(item => item.WarehouseId.HasValue && item.WarehouseId == input.WarehouseId ? 0 : 1)
            .ThenBy(item => item.Id)
            .FirstOrDefault();
        if (selectedReason is null)
        {
            return Failure<ApprovalEvaluationDto>(WmsErrors.Validation(
                "approval.reason_code_invalid",
                "The reason code is not active or is not valid for this module, operation, or warehouse."));
        }

        if (selectedReason.RequiresNotes && string.IsNullOrWhiteSpace(input.Notes))
        {
            return Failure<ApprovalEvaluationDto>(WmsErrors.Validation(
                "approval.notes_required",
                "This reason code requires notes."));
        }

        if (selectedReason.RequiresAttachment && string.IsNullOrWhiteSpace(input.AttachmentReference))
        {
            return Failure<ApprovalEvaluationDto>(WmsErrors.Validation(
                "approval.attachment_required",
                "This reason code requires an attachment reference."));
        }

        var policies = await context.ApprovalPolicies
            .Where(policy => policy.IsActive)
            .ToListAsync(cancellationToken);
        var selectedPolicy = policies
            .Where(policy => policy.Matches(
                input.Module,
                input.Operation,
                input.WarehouseId,
                selectedReason.Code,
                input.Quantity,
                input.Value,
                input.VariancePercent,
                input.ItemRisk,
                input.StatusRisk,
                nowUtc))
            .OrderByDescending(policy => policy.Priority)
            .ThenByDescending(policy => policy.SpecificityScore(
                input.WarehouseId,
                selectedReason.Code,
                input.ItemRisk,
                input.StatusRisk))
            .ThenBy(policy => policy.Code)
            .ThenBy(policy => policy.Id)
            .FirstOrDefault();

        if (selectedPolicy is null)
        {
            return Result.Success(new ApprovalEvaluationDto(
                false,
                selectedReason.Code,
                null,
                null,
                null));
        }

        if (string.IsNullOrWhiteSpace(input.IdempotencyKey))
        {
            return Failure<ApprovalEvaluationDto>(WmsErrors.Validation(
                "approval.idempotency_required",
                "An idempotency key is required when an approval request is created."));
        }

        if (string.IsNullOrWhiteSpace(input.CurrentStateHash))
        {
            return Failure<ApprovalEvaluationDto>(WmsErrors.Validation(
                "approval.state_hash_required",
                "A current state hash is required for an approval request."));
        }

        var existing = await context.ApprovalRequests
            .SingleOrDefaultAsync(
                request => request.RequestIdempotencyKey == input.IdempotencyKey.Trim(),
                cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.CurrentStateHash, input.CurrentStateHash.Trim(), StringComparison.Ordinal) ||
                !string.Equals(existing.SourceEntityType, input.SourceEntityType.Trim(), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(existing.SourceEntityId, input.SourceEntityId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Failure<ApprovalEvaluationDto>(WmsErrors.Conflict(
                    "approval.idempotency_conflict",
                    "The idempotency key was already used for a different protected command."));
            }

            return Result.Success(new ApprovalEvaluationDto(
                true,
                existing.ReasonCode,
                existing.PolicyCode,
                existing.PolicyId,
                await MapRequestAsync(existing, cancellationToken),
                WasReplayed: true));
        }

        try
        {
            var request = new ApprovalRequest(
                input.IdempotencyKey,
                input.Module,
                input.Operation,
                input.WarehouseId,
                selectedReason.Code,
                selectedReason.Id,
                selectedPolicy.Code,
                selectedPolicy.Id,
                input.SourceEntityType,
                input.SourceEntityId,
                input.SourceReference,
                requesterUserId,
                input.RequiredPermission,
                input.Quantity,
                input.Value,
                input.VariancePercent,
                input.ItemRisk,
                input.StatusRisk,
                input.CurrentStateHash,
                input.Notes,
                input.AttachmentReference,
                selectedPolicy.GetApprovalLevels(),
                selectedPolicy.RequireSeparationOfDuties,
                nowUtc,
                nowUtc.AddMinutes(selectedPolicy.ExpiryMinutes));
            context.ApprovalRequests.Add(request);
            await auditWriter.RecordAsync(new AuditRecord(
                WmsAuditActions.ApprovalRequested,
                WmsAuditEntityTypes.ApprovalRequest,
                null,
                input.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["module"] = request.Module,
                    ["operation"] = request.Operation,
                    ["reasonCode"] = request.ReasonCode,
                    ["policyCode"] = request.PolicyCode,
                    ["sourceEntityType"] = request.SourceEntityType,
                    ["sourceEntityId"] = request.SourceEntityId
                }), cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            await NotifyLevelAsync(request, request.CurrentLevel, nowUtc, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(new ApprovalEvaluationDto(
                true,
                selectedReason.Code,
                selectedPolicy.Code,
                selectedPolicy.Id,
                await MapRequestAsync(request, cancellationToken)));
        }
        catch (ArgumentException exception)
        {
            return Failure<ApprovalEvaluationDto>(WmsErrors.Validation(
                "approval.request_invalid",
                exception.Message));
        }
        catch (DbUpdateException exception)
        {
            return Failure<ApprovalEvaluationDto>(WmsErrors.FromException(
                exception,
                "approval.request_save_failed",
                "The approval request could not be created."));
        }
    }

    public async Task<Result<ApprovalRequestDto>> GetRequestAsync(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        var request = await context.ApprovalRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken);
        if (request is null)
        {
            return Failure<ApprovalRequestDto>(WmsErrors.NotFound(
                "approval.request_not_found",
                "The approval request was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ApprovalRead,
            request.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ApprovalRequestDto>();
        }

        return Result.Success(await MapRequestAsync(request, cancellationToken));
    }

    public async Task<Result<ApprovalRequestPageDto>> ListRequestsAsync(
        ApprovalRequestQuery query,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ApprovalRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ApprovalRequestPageDto>();
        }

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        var requests = context.ApprovalRequests.AsNoTracking().AsQueryable();
        if (query.Status.HasValue)
        {
            requests = requests.Where(request => request.Status == query.Status.Value);
        }

        if (query.WarehouseId.HasValue)
        {
            requests = requests.Where(request => request.WarehouseId == query.WarehouseId);
        }

        if (!string.IsNullOrWhiteSpace(query.RequesterUserId))
        {
            requests = requests.Where(request => request.RequesterUserId == query.RequesterUserId);
        }

        var totalCount = await requests.CountAsync(cancellationToken);
        var items = await requests
            .OrderByDescending(request => request.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var dtos = new List<ApprovalRequestDto>(items.Count);
        foreach (var request in items)
        {
            dtos.Add(await MapRequestAsync(request, cancellationToken));
        }

        return Result.Success(new ApprovalRequestPageDto(dtos, page, pageSize, totalCount));
    }

    public Task<Result<ApprovalRequestDto>> ApproveAsync(
        int requestId,
        ApprovalDecisionInput input,
        CancellationToken cancellationToken = default) =>
        ApplyDecisionAsync(requestId, ApprovalDecisionType.Approve, input, cancellationToken);

    public Task<Result<ApprovalRequestDto>> RejectAsync(
        int requestId,
        ApprovalDecisionInput input,
        CancellationToken cancellationToken = default) =>
        ApplyDecisionAsync(requestId, ApprovalDecisionType.Reject, input, cancellationToken);

    public Task<Result<ApprovalRequestDto>> CancelAsync(
        int requestId,
        ApprovalDecisionInput input,
        CancellationToken cancellationToken = default) =>
        ApplyDecisionAsync(requestId, ApprovalDecisionType.Cancel, input, cancellationToken);

    public Task<Result<ApprovalRequestDto>> EscalateAsync(
        int requestId,
        ApprovalDecisionInput input,
        CancellationToken cancellationToken = default) =>
        ApplyDecisionAsync(requestId, ApprovalDecisionType.Escalate, input, cancellationToken);

    public async Task<Result<ApprovalRequestDto>> ExpireAsync(
        int requestId,
        CancellationToken cancellationToken = default)
    {
        var request = await context.ApprovalRequests
            .SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken);
        if (request is null)
        {
            return Failure<ApprovalRequestDto>(WmsErrors.NotFound(
                "approval.request_not_found",
                "The approval request was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ApprovalManage,
            request.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ApprovalRequestDto>();
        }

        var nowUtc = clock.UtcNow;
        if (request.Status == ApprovalRequestStatus.Expired)
        {
            return Result.Success(await MapRequestAsync(request, cancellationToken));
        }

        if (request.Status is not (ApprovalRequestStatus.Pending or ApprovalRequestStatus.Approved) ||
            request.ExpiresAtUtc > nowUtc)
        {
            return Failure<ApprovalRequestDto>(WmsErrors.BusinessRule(
                "approval.expiry_not_reached",
                "The approval request is not eligible for expiry."));
        }

        request.Expire(nowUtc);
        context.ApprovalDecisions.Add(new ApprovalDecision(
            request.Id,
            request.CurrentLevel,
            ApprovalDecisionType.Expire,
            BuildSystemDecisionKey(request),
            GetCurrentUserId() ?? "system",
            "[]",
            "Approval request expired.",
            nowUtc));
        ResolveInboxItems(request.Id, nowUtc);
        await auditWriter.RecordAsync(new AuditRecord(
            WmsAuditActions.ApprovalExpired,
            WmsAuditEntityTypes.ApprovalRequest,
            request.Id.ToString(CultureInfo.InvariantCulture),
            request.WarehouseId,
            Details: "Approval request expired before execution."), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success(await MapRequestAsync(request, cancellationToken));
    }

    public async Task<Result<ApprovalExecutionDto>> BeginExecutionAsync(
        int requestId,
        ApprovalExecutionInput input,
        CancellationToken cancellationToken = default)
    {
        var request = await context.ApprovalRequests
            .SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken);
        if (request is null)
        {
            return Failure<ApprovalExecutionDto>(WmsErrors.NotFound(
                "approval.request_not_found",
                "The approval request was not found."));
        }

        if (!WmsPermissions.IsKnown(request.RequiredPermission))
        {
            return Failure<ApprovalExecutionDto>(WmsErrors.Validation(
                "approval.permission_invalid",
                "The protected operation permission is not recognized."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            request.RequiredPermission,
            request.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ApprovalExecutionDto>();
        }

        if (string.IsNullOrWhiteSpace(input.IdempotencyKey) ||
            string.IsNullOrWhiteSpace(input.CurrentStateHash))
        {
            return Failure<ApprovalExecutionDto>(WmsErrors.Validation(
                "approval.execution_input_invalid",
                "An execution idempotency key and current state hash are required."));
        }

        var existing = await context.ApprovalExecutions
            .SingleOrDefaultAsync(execution => execution.ApprovalRequestId == request.Id, cancellationToken);
        if (existing is not null)
        {
            if (existing.IdempotencyKey == input.IdempotencyKey.Trim() &&
                existing.CurrentStateHash == input.CurrentStateHash.Trim())
            {
                return Result.Success(MapExecution(existing));
            }

            return Failure<ApprovalExecutionDto>(WmsErrors.Conflict(
                "approval.execution_already_started",
                "This approval request already has a different execution attempt."));
        }

        var nowUtc = clock.UtcNow;
        if (request.ExpiresAtUtc <= nowUtc)
        {
            request.Expire(nowUtc);
            ResolveInboxItems(request.Id, nowUtc);
            await context.SaveChangesAsync(cancellationToken);
            return Failure<ApprovalExecutionDto>(WmsErrors.BusinessRule(
                "approval.expired",
                "The approval request expired before execution began."));
        }

        if (request.Status != ApprovalRequestStatus.Approved)
        {
            return Failure<ApprovalExecutionDto>(WmsErrors.BusinessRule(
                "approval.not_approved",
                "Only an approved request can begin execution."));
        }

        if (!string.Equals(request.CurrentStateHash, input.CurrentStateHash.Trim(), StringComparison.Ordinal))
        {
            return Failure<ApprovalExecutionDto>(WmsErrors.Concurrency(
                "approval.stale_state",
                "The protected record changed after approval. Re-evaluate the command."));
        }

        try
        {
            var execution = new ApprovalExecution(
                request.Id,
                input.IdempotencyKey,
                input.CurrentStateHash,
                GetCurrentUserId() ?? "system",
                nowUtc);
            request.BeginExecution(nowUtc);
            context.ApprovalExecutions.Add(execution);
            await auditWriter.RecordAsync(new AuditRecord(
                WmsAuditActions.ApprovalExecutionStarted,
                WmsAuditEntityTypes.ApprovalExecution,
                request.Id.ToString(CultureInfo.InvariantCulture),
                request.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["approvalRequestId"] = request.Id,
                    ["idempotencyKey"] = input.IdempotencyKey.Trim()
                }), cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapExecution(execution));
        }
        catch (ArgumentException exception)
        {
            return Failure<ApprovalExecutionDto>(WmsErrors.Validation(
                "approval.execution_invalid",
                exception.Message));
        }
        catch (DbUpdateException exception)
        {
            return Failure<ApprovalExecutionDto>(WmsErrors.FromException(
                exception,
                "approval.execution_save_failed",
                "The approval execution could not be started."));
        }
    }

    public async Task<Result<ApprovalExecutionDto>> CompleteExecutionAsync(
        int requestId,
        ApprovalCompletionInput input,
        CancellationToken cancellationToken = default)
    {
        var request = await context.ApprovalRequests
            .SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken);
        if (request is null)
        {
            return Failure<ApprovalExecutionDto>(WmsErrors.NotFound(
                "approval.request_not_found",
                "The approval request was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            request.RequiredPermission,
            request.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ApprovalExecutionDto>();
        }

        var execution = await context.ApprovalExecutions
            .SingleOrDefaultAsync(item => item.ApprovalRequestId == request.Id, cancellationToken);
        if (execution is null)
        {
            return Failure<ApprovalExecutionDto>(WmsErrors.NotFound(
                "approval.execution_not_started",
                "The approval execution has not been started."));
        }

        if (execution.Status == ApprovalExecutionStatus.Completed)
        {
            return Result.Success(MapExecution(execution));
        }

        try
        {
            var nowUtc = clock.UtcNow;
            request.CompleteExecution(nowUtc);
            execution.Complete(input.ResultReference, nowUtc);
            await auditWriter.RecordAsync(new AuditRecord(
                WmsAuditActions.ApprovalExecuted,
                WmsAuditEntityTypes.ApprovalExecution,
                request.Id.ToString(CultureInfo.InvariantCulture),
                request.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["approvalRequestId"] = request.Id,
                    ["resultReference"] = input.ResultReference
                }), cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapExecution(execution));
        }
        catch (InvalidOperationException exception)
        {
            return Failure<ApprovalExecutionDto>(WmsErrors.BusinessRule(
                "approval.execution_not_active",
                exception.Message));
        }
        catch (DbUpdateException exception)
        {
            return Failure<ApprovalExecutionDto>(WmsErrors.FromException(
                exception,
                "approval.execution_complete_failed",
                "The approval execution could not be completed."));
        }
    }

    public async Task<Result<ApprovalInboxPageDto>> ListInboxAsync(
        ApprovalInboxQuery query,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ApprovalRead,
            cancellationToken: cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ApprovalInboxPageDto>();
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Failure<ApprovalInboxPageDto>(WmsErrors.Unauthorized(
                "approval.authentication_required",
                "An authenticated user is required to view the approval inbox."));
        }

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        var items = context.ApprovalInboxItems
            .AsNoTracking()
            .Where(item => item.RecipientUserId == userId);
        if (!query.IncludeResolved)
        {
            items = items.Where(item => item.ResolvedAtUtc == null);
        }

        var totalCount = await items.CountAsync(cancellationToken);
        var inboxRows = await items
            .OrderByDescending(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var result = inboxRows.Select(MapInbox).ToList();
        return Result.Success(new ApprovalInboxPageDto(result, page, pageSize, totalCount));
    }

    public async Task<Result<ApprovalInboxItemDto>> MarkInboxItemReadAsync(
        int inboxItemId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ApprovalRead,
            cancellationToken: cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ApprovalInboxItemDto>();
        }

        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Failure<ApprovalInboxItemDto>(WmsErrors.Unauthorized(
                "approval.authentication_required",
                "An authenticated user is required to update the approval inbox."));
        }

        var item = await context.ApprovalInboxItems
            .SingleOrDefaultAsync(
                inboxItem => inboxItem.Id == inboxItemId && inboxItem.RecipientUserId == userId,
                cancellationToken);
        if (item is null)
        {
            return Failure<ApprovalInboxItemDto>(WmsErrors.NotFound(
                "approval.inbox_item_not_found",
                "The approval inbox item was not found."));
        }

        item.MarkRead(clock.UtcNow);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success(MapInbox(item));
    }

    private async Task<Result<ApprovalRequestDto>> ApplyDecisionAsync(
        int requestId,
        ApprovalDecisionType type,
        ApprovalDecisionInput input,
        CancellationToken cancellationToken)
    {
        var request = await context.ApprovalRequests
            .SingleOrDefaultAsync(item => item.Id == requestId, cancellationToken);
        if (request is null)
        {
            return Failure<ApprovalRequestDto>(WmsErrors.NotFound(
                "approval.request_not_found",
                "The approval request was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ApprovalManage,
            request.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ApprovalRequestDto>();
        }

        if (string.IsNullOrWhiteSpace(input.IdempotencyKey))
        {
            return Failure<ApprovalRequestDto>(WmsErrors.Validation(
                "approval.decision_idempotency_required",
                "An idempotency key is required for an approval decision."));
        }

        var existingDecision = await context.ApprovalDecisions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                decision => decision.ApprovalRequestId == request.Id &&
                            decision.IdempotencyKey == input.IdempotencyKey.Trim(),
                cancellationToken);
        if (existingDecision is not null)
        {
            if (existingDecision.Type != type)
            {
                return Failure<ApprovalRequestDto>(WmsErrors.Conflict(
                    "approval.decision_idempotency_conflict",
                    "The decision idempotency key was already used for another decision."));
            }

            return Result.Success(await MapRequestAsync(request, cancellationToken));
        }

        var actorUserId = GetCurrentUserId();
        if (actorUserId is null)
        {
            return Failure<ApprovalRequestDto>(WmsErrors.Unauthorized(
                "approval.authentication_required",
                "An authenticated approver is required."));
        }

        var nowUtc = clock.UtcNow;
        if (request.Status == ApprovalRequestStatus.Pending && request.ExpiresAtUtc <= nowUtc)
        {
            request.Expire(nowUtc);
            ResolveInboxItems(request.Id, nowUtc);
            await context.SaveChangesAsync(cancellationToken);
            return Failure<ApprovalRequestDto>(WmsErrors.BusinessRule(
                "approval.expired",
                "The approval request expired before the decision was recorded."));
        }

        var roles = await roleDirectory.GetRolesAsync(actorUserId, cancellationToken);
        if (type is ApprovalDecisionType.Approve or ApprovalDecisionType.Reject or ApprovalDecisionType.Escalate)
        {
            if (request.RequireSeparationOfDuties &&
                string.Equals(request.RequesterUserId, actorUserId, StringComparison.Ordinal))
            {
                return Failure<ApprovalRequestDto>(WmsErrors.Forbidden(
                    "approval.self_approval_forbidden",
                    "The requester cannot approve or escalate their own request."));
            }

            var currentLevel = request.GetApprovalLevels()
                .Single(level => level.Level == request.CurrentLevel);
            if (!roles.Any(role => currentLevel.Roles.Contains(role, StringComparer.OrdinalIgnoreCase)))
            {
                return Failure<ApprovalRequestDto>(WmsErrors.Forbidden(
                    "approval.approver_role_required",
                    "The current approval level requires a different approver role."));
            }
        }

        try
        {
            var decisionLevel = request.CurrentLevel;
            var actorRoleSnapshot = JsonSerializer.Serialize(
                roles.OrderBy(role => role, StringComparer.OrdinalIgnoreCase).ToArray());
            var decision = new ApprovalDecision(
                request.Id,
                decisionLevel,
                type,
                input.IdempotencyKey,
                actorUserId,
                actorRoleSnapshot,
                input.Comment,
                nowUtc);
            context.ApprovalDecisions.Add(decision);

            var action = type switch
            {
                ApprovalDecisionType.Approve => WmsAuditActions.ApprovalApproved,
                ApprovalDecisionType.Reject => WmsAuditActions.ApprovalRejected,
                ApprovalDecisionType.Cancel => WmsAuditActions.ApprovalCancelled,
                ApprovalDecisionType.Escalate => WmsAuditActions.ApprovalEscalated,
                _ => WmsAuditActions.ApprovalExpired
            };
            switch (type)
            {
                case ApprovalDecisionType.Approve:
                    request.AdvanceApproval(input.Comment, nowUtc);
                    break;
                case ApprovalDecisionType.Reject:
                    request.Reject(input.Comment, nowUtc);
                    break;
                case ApprovalDecisionType.Cancel:
                    request.Cancel(input.Comment, nowUtc);
                    break;
                case ApprovalDecisionType.Escalate:
                    request.Escalate(input.Comment, nowUtc);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported approval decision type.");
            }

            if (request.Status is ApprovalRequestStatus.Rejected or
                ApprovalRequestStatus.Cancelled or
                ApprovalRequestStatus.Approved)
            {
                ResolveInboxItems(request.Id, nowUtc);
            }
            else if (request.CurrentLevel != decisionLevel)
            {
                await NotifyLevelAsync(request, request.CurrentLevel, nowUtc, cancellationToken);
            }

            await auditWriter.RecordAsync(new AuditRecord(
                action,
                WmsAuditEntityTypes.ApprovalRequest,
                request.Id.ToString(CultureInfo.InvariantCulture),
                request.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["status"] = request.Status.ToString(),
                    ["level"] = decisionLevel,
                    ["actorUserId"] = actorUserId
                }), cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(await MapRequestAsync(request, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return Failure<ApprovalRequestDto>(WmsErrors.BusinessRule(
                "approval.decision_not_allowed",
                exception.Message));
        }
        catch (ArgumentException exception)
        {
            return Failure<ApprovalRequestDto>(WmsErrors.Validation(
                "approval.decision_invalid",
                exception.Message));
        }
        catch (DbUpdateException exception)
        {
            return Failure<ApprovalRequestDto>(WmsErrors.FromException(
                exception,
                "approval.decision_save_failed",
                "The approval decision could not be saved."));
        }
    }

    private async Task NotifyLevelAsync(
        ApprovalRequest request,
        int level,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var definition = request.GetApprovalLevels().Single(item => item.Level == level);
        var userIds = await roleDirectory.FindUserIdsAsync(
            definition.Roles,
            request.WarehouseId,
            cancellationToken);
        foreach (var userId in userIds)
        {
            var deduplicationKey = $"approval:{request.Id}:level:{level}:user:{userId}";
            var exists = await context.ApprovalInboxItems
                .AnyAsync(item => item.DeduplicationKey == deduplicationKey, cancellationToken);
            if (exists)
            {
                continue;
            }

            context.ApprovalInboxItems.Add(new ApprovalInboxItem(
                request.Id,
                userId,
                level,
                deduplicationKey,
                $"Approval required: {request.Operation}",
                $"{request.ReasonCode} requires level {level} approval for {request.SourceEntityType} {request.SourceEntityId}.",
                request.WarehouseId,
                nowUtc,
                request.ExpiresAtUtc));
        }
    }

    private void ResolveInboxItems(int requestId, DateTimeOffset resolvedAtUtc)
    {
        foreach (var item in context.ApprovalInboxItems
                     .Where(item => item.ApprovalRequestId == requestId && item.ResolvedAtUtc == null)
                     .ToList())
        {
            item.Resolve(resolvedAtUtc);
        }
    }

    private async Task<ApprovalRequestDto> MapRequestAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken)
    {
        var decisions = await context.ApprovalDecisions
            .AsNoTracking()
            .Where(decision => decision.ApprovalRequestId == request.Id)
            .OrderBy(decision => decision.Id)
            .Select(decision => new ApprovalDecisionDto(
                decision.Id,
                decision.ApprovalRequestId,
                decision.Level,
                decision.Type,
                decision.IdempotencyKey,
                decision.ActorUserId,
                decision.ActorRoleSnapshotJson,
                decision.Comment,
                decision.OccurredAtUtc))
            .ToListAsync(cancellationToken);
        return MapRequest(request, decisions);
    }

    private static ApprovalRequestDto MapRequest(
        ApprovalRequest request,
        IReadOnlyList<ApprovalDecisionDto> decisions) =>
        new(
            request.Id,
            request.RequestIdempotencyKey,
            request.Module,
            request.Operation,
            request.WarehouseId,
            request.ReasonCode,
            request.ReasonCodeId,
            request.PolicyCode,
            request.PolicyId,
            request.SourceEntityType,
            request.SourceEntityId,
            request.SourceReference,
            request.RequesterUserId,
            request.RequiredPermission,
            request.Quantity,
            request.Value,
            request.VariancePercent,
            request.ItemRisk,
            request.StatusRisk,
            request.CurrentStateHash,
            request.Notes,
            request.AttachmentReference,
            MapLevels(request.GetApprovalLevels()),
            request.RequireSeparationOfDuties,
            request.CurrentLevel,
            request.Status,
            request.RequestedAtUtc,
            request.ExpiresAtUtc,
            request.DecidedAtUtc,
            request.ExecutingAtUtc,
            request.ExecutedAtUtc,
            request.LastComment,
            request.Revision,
            decisions);

    private static ReasonCodeDto MapReasonCode(ReasonCode reason) =>
        new(
            reason.Id,
            reason.Code,
            reason.Category,
            reason.Module,
            reason.Operation,
            reason.NameEn,
            reason.NameAr,
            reason.DescriptionEn,
            reason.DescriptionAr,
            reason.EffectiveFromUtc,
            reason.EffectiveToUtc,
            reason.RequiresNotes,
            reason.RequiresAttachment,
            reason.Severity,
            reason.WarehouseId,
            reason.IsActive,
            reason.Revision);

    private static ApprovalPolicyDto MapPolicy(ApprovalPolicy policy) =>
        new(
            policy.Id,
            policy.Code,
            policy.Name,
            policy.Module,
            policy.Operation,
            policy.WarehouseId,
            policy.ReasonCode,
            policy.Priority,
            policy.MinimumQuantity,
            policy.MinimumValue,
            policy.MinimumVariancePercent,
            policy.ItemRisk,
            policy.StatusRisk,
            MapLevels(policy.GetApprovalLevels()),
            policy.ExpiryMinutes,
            policy.RequireSeparationOfDuties,
            policy.EffectiveFromUtc,
            policy.EffectiveToUtc,
            policy.IsActive,
            policy.Revision);

    private static ApprovalExecutionDto MapExecution(ApprovalExecution execution) =>
        new(
            execution.Id,
            execution.ApprovalRequestId,
            execution.IdempotencyKey,
            execution.CurrentStateHash,
            execution.StartedByUserId,
            execution.Status,
            execution.StartedAtUtc,
            execution.CompletedAtUtc,
            execution.ResultReference);

    private static ApprovalInboxItemDto MapInbox(ApprovalInboxItem item) =>
        new(
            item.Id,
            item.ApprovalRequestId,
            item.RecipientUserId,
            item.Level,
            item.DeduplicationKey,
            item.Title,
            item.Message,
            item.WarehouseId,
            item.CreatedAtUtc,
            item.ExpiresAtUtc,
            item.ReadAtUtc,
            item.ResolvedAtUtc);

    private static ApprovalPolicyLevelDto[] MapLevels(
        IReadOnlyList<ApprovalLevelDefinition> levels) =>
        levels.Select(level => new ApprovalPolicyLevelDto(level.Level, level.Roles)).ToArray();

    private string? GetCurrentUserId() =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
            ? currentUser.UserId
            : null;

    private static string BuildSystemDecisionKey(ApprovalRequest request) =>
        $"system-expiry:{request.Id}:{request.ExpiresAtUtc.UtcTicks}";

    private static Result<T> Failure<T>(ResultError error) => Result.Failure<T>(error);
}
