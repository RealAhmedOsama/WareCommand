using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Approvals;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/approvals")]
[Authorize]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class ApprovalsController(IApprovalService approvalService) : ControllerBase
{
    [HttpGet("reason-codes")]
    [Authorize(Policy = WmsPermissions.ApprovalRead)]
    public async Task<IActionResult> ReasonCodes(
        [FromQuery] string? module,
        [FromQuery] string? operation,
        [FromQuery] int? warehouseId,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await approvalService.ListReasonCodesAsync(
            new ReasonCodeQuery(module, operation, warehouseId, includeInactive),
            cancellationToken));

    [HttpPost("reason-codes")]
    [Authorize(Policy = WmsPermissions.ApprovalManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateReasonCode(
        [FromBody] ReasonCodeRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await approvalService.SaveReasonCodeAsync(
            null,
            request.ToInput(),
            cancellationToken));

    [HttpPut("reason-codes/{reasonCodeId:int}")]
    [Authorize(Policy = WmsPermissions.ApprovalManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateReasonCode(
        int reasonCodeId,
        [FromBody] ReasonCodeRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await approvalService.SaveReasonCodeAsync(
            reasonCodeId,
            request.ToInput(),
            cancellationToken));

    [HttpGet("policies")]
    [Authorize(Policy = WmsPermissions.ApprovalRead)]
    public async Task<IActionResult> Policies(
        [FromQuery] string? module,
        [FromQuery] string? operation,
        [FromQuery] int? warehouseId,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await approvalService.ListApprovalPoliciesAsync(
            new ApprovalPolicyQuery(module, operation, warehouseId, includeInactive),
            cancellationToken));

    [HttpPost("policies")]
    [Authorize(Policy = WmsPermissions.ApprovalManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePolicy(
        [FromBody] ApprovalPolicyRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await approvalService.SaveApprovalPolicyAsync(
            null,
            request.ToInput(),
            cancellationToken));

    [HttpPut("policies/{policyId:int}")]
    [Authorize(Policy = WmsPermissions.ApprovalManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePolicy(
        int policyId,
        [FromBody] ApprovalPolicyRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await approvalService.SaveApprovalPolicyAsync(
            policyId,
            request.ToInput(),
            cancellationToken));

    [HttpPost("requests")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestApproval(
        [FromBody] ApprovalRequestRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await approvalService.RequestAsync(
            request.ToInput(),
            cancellationToken));

    [HttpGet("requests/{requestId:int}")]
    [Authorize(Policy = WmsPermissions.ApprovalRead)]
    public async Task<IActionResult> GetRequest(
        int requestId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await approvalService.GetRequestAsync(requestId, cancellationToken));

    [HttpGet("requests")]
    [Authorize(Policy = WmsPermissions.ApprovalRead)]
    public async Task<IActionResult> Requests(
        [FromQuery] ApprovalRequestStatus? status,
        [FromQuery] int? warehouseId,
        [FromQuery] string? requesterUserId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await approvalService.ListRequestsAsync(
            new ApprovalRequestQuery(status, warehouseId, requesterUserId, page, pageSize),
            cancellationToken));

    [HttpPost("requests/{requestId:int}/approve")]
    [Authorize(Policy = WmsPermissions.ApprovalManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(
        int requestId,
        [FromBody] DecisionRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await approvalService.ApproveAsync(
            requestId,
            request.ToInput(),
            cancellationToken));

    [HttpPost("requests/{requestId:int}/reject")]
    [Authorize(Policy = WmsPermissions.ApprovalManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(
        int requestId,
        [FromBody] DecisionRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await approvalService.RejectAsync(
            requestId,
            request.ToInput(),
            cancellationToken));

    [HttpPost("requests/{requestId:int}/cancel")]
    [Authorize(Policy = WmsPermissions.ApprovalManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        int requestId,
        [FromBody] DecisionRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await approvalService.CancelAsync(
            requestId,
            request.ToInput(),
            cancellationToken));

    [HttpPost("requests/{requestId:int}/escalate")]
    [Authorize(Policy = WmsPermissions.ApprovalManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Escalate(
        int requestId,
        [FromBody] DecisionRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await approvalService.EscalateAsync(
            requestId,
            request.ToInput(),
            cancellationToken));

    [HttpPost("requests/{requestId:int}/expire")]
    [Authorize(Policy = WmsPermissions.ApprovalManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Expire(
        int requestId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await approvalService.ExpireAsync(requestId, cancellationToken));

    [HttpPost("requests/{requestId:int}/execution/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BeginExecution(
        int requestId,
        [FromBody] ExecutionRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await approvalService.BeginExecutionAsync(
            requestId,
            new ApprovalExecutionInput(request.IdempotencyKey, request.CurrentStateHash),
            cancellationToken));

    [HttpPost("requests/{requestId:int}/execution/complete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteExecution(
        int requestId,
        [FromBody] CompletionRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await approvalService.CompleteExecutionAsync(
            requestId,
            new ApprovalCompletionInput(request.ResultReference),
            cancellationToken));

    [HttpGet("inbox")]
    [Authorize(Policy = WmsPermissions.ApprovalRead)]
    public async Task<IActionResult> Inbox(
        [FromQuery] bool includeResolved = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await approvalService.ListInboxAsync(
            new ApprovalInboxQuery(includeResolved, page, pageSize),
            cancellationToken));

    [HttpPost("inbox/{inboxItemId:int}/read")]
    [Authorize(Policy = WmsPermissions.ApprovalRead)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkInboxRead(
        int inboxItemId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await approvalService.MarkInboxItemReadAsync(inboxItemId, cancellationToken));

    private IActionResult ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        var error = result.FirstError!;
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict or ErrorType.Concurrency or ErrorType.BusinessRule => StatusCodes.Status409Conflict,
            ErrorType.Dependency => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        return Problem(
            statusCode: statusCode,
            title: error.Type.ToString(),
            detail: error.Message,
            type: $"https://warecommand.local/problems/{error.Code}",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = error.Code,
                ["retryable"] = error.IsRetryable
            });
    }

    public sealed class ReasonCodeRequest
    {
        [Required, StringLength(80)] public string Code { get; init; } = string.Empty;
        public ReasonCodeCategory Category { get; init; }
        [Required, StringLength(100)] public string Module { get; init; } = string.Empty;
        [Required, StringLength(150)] public string Operation { get; init; } = string.Empty;
        [Required, StringLength(200)] public string NameEn { get; init; } = string.Empty;
        [Required, StringLength(200)] public string NameAr { get; init; } = string.Empty;
        [StringLength(1_000)] public string? DescriptionEn { get; init; }
        [StringLength(1_000)] public string? DescriptionAr { get; init; }
        public DateTimeOffset EffectiveFromUtc { get; init; }
        public DateTimeOffset? EffectiveToUtc { get; init; }
        public bool RequiresNotes { get; init; }
        public bool RequiresAttachment { get; init; }
        public ReasonCodeSeverity Severity { get; init; }
        [Range(1, int.MaxValue)] public int? WarehouseId { get; init; }
        public bool IsActive { get; init; } = true;

        public ReasonCodeInput ToInput() => new(
            Code,
            Category,
            Module,
            Operation,
            NameEn,
            NameAr,
            DescriptionEn,
            DescriptionAr,
            EffectiveFromUtc,
            EffectiveToUtc,
            RequiresNotes,
            RequiresAttachment,
            Severity,
            WarehouseId,
            IsActive);
    }

    public sealed class ApprovalPolicyRequest
    {
        [Required, StringLength(80)] public string Code { get; init; } = string.Empty;
        [Required, StringLength(200)] public string Name { get; init; } = string.Empty;
        [Required, StringLength(100)] public string Module { get; init; } = string.Empty;
        [Required, StringLength(150)] public string Operation { get; init; } = string.Empty;
        [Range(1, int.MaxValue)] public int? WarehouseId { get; init; }
        [StringLength(80)] public string? ReasonCode { get; init; }
        [Range(0, int.MaxValue)] public int Priority { get; init; }
        [Range(typeof(decimal), "0", "79228162514264337593543950335")] public decimal? MinimumQuantity { get; init; }
        [Range(typeof(decimal), "0", "79228162514264337593543950335")] public decimal? MinimumValue { get; init; }
        [Range(typeof(decimal), "0", "79228162514264337593543950335")] public decimal? MinimumVariancePercent { get; init; }
        [StringLength(50)] public string? ItemRisk { get; init; }
        [StringLength(50)] public string? StatusRisk { get; init; }
        [MinLength(1), MaxLength(10)] public IReadOnlyList<ApprovalPolicyLevelRequest> Levels { get; init; } = [];
        [Range(1, 43_200)] public int ExpiryMinutes { get; init; } = 60;
        public bool RequireSeparationOfDuties { get; init; } = true;
        public DateTimeOffset EffectiveFromUtc { get; init; }
        public DateTimeOffset? EffectiveToUtc { get; init; }
        public bool IsActive { get; init; } = true;

        public ApprovalPolicyInput ToInput() => new(
            Code,
            Name,
            Module,
            Operation,
            WarehouseId,
            ReasonCode,
            Priority,
            MinimumQuantity,
            MinimumValue,
            MinimumVariancePercent,
            ItemRisk,
            StatusRisk,
            Levels.Select(level => new ApprovalPolicyLevelInput(level.Level, level.Roles)).ToArray(),
            ExpiryMinutes,
            RequireSeparationOfDuties,
            EffectiveFromUtc,
            EffectiveToUtc,
            IsActive);
    }

    public sealed class ApprovalPolicyLevelRequest
    {
        [Range(1, 10)] public int Level { get; init; }
        [MinLength(1), MaxLength(20)] public IReadOnlyList<string> Roles { get; init; } = [];
    }

    public sealed class ApprovalRequestRequest
    {
        [Required, StringLength(100)] public string Module { get; init; } = string.Empty;
        [Required, StringLength(150)] public string Operation { get; init; } = string.Empty;
        [Range(1, int.MaxValue)] public int? WarehouseId { get; init; }
        [Required, StringLength(80)] public string ReasonCode { get; init; } = string.Empty;
        [Required, StringLength(100)] public string SourceEntityType { get; init; } = string.Empty;
        [Required, StringLength(200)] public string SourceEntityId { get; init; } = string.Empty;
        [StringLength(250)] public string? SourceReference { get; init; }
        [Required, StringLength(100)] public string RequiredPermission { get; init; } = string.Empty;
        [Range(typeof(decimal), "0", "79228162514264337593543950335")] public decimal? Quantity { get; init; }
        [Range(typeof(decimal), "0", "79228162514264337593543950335")] public decimal? Value { get; init; }
        public decimal? VariancePercent { get; init; }
        [StringLength(50)] public string? ItemRisk { get; init; }
        [StringLength(50)] public string? StatusRisk { get; init; }
        [Required, StringLength(128)] public string CurrentStateHash { get; init; } = string.Empty;
        [StringLength(2_000)] public string? Notes { get; init; }
        [StringLength(500)] public string? AttachmentReference { get; init; }
        [StringLength(250)] public string? IdempotencyKey { get; init; }

        public ApprovalEvaluationInput ToInput() => new(
            Module,
            Operation,
            WarehouseId,
            ReasonCode,
            SourceEntityType,
            SourceEntityId,
            SourceReference,
            RequiredPermission,
            Quantity,
            Value,
            VariancePercent,
            ItemRisk,
            StatusRisk,
            CurrentStateHash,
            Notes,
            AttachmentReference,
            IdempotencyKey);
    }

    public sealed class DecisionRequest
    {
        [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
        [StringLength(2_000)] public string? Comment { get; init; }

        public ApprovalDecisionInput ToInput() => new(IdempotencyKey, Comment);
    }

    public sealed class ExecutionRequest
    {
        [Required, StringLength(250)] public string IdempotencyKey { get; init; } = string.Empty;
        [Required, StringLength(128)] public string CurrentStateHash { get; init; } = string.Empty;
    }

    public sealed class CompletionRequest
    {
        [StringLength(250)] public string? ResultReference { get; init; }
    }
}
