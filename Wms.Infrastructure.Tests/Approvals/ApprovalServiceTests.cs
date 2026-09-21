using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Wms.Application.Approvals;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Approvals;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Tests.Approvals;

public sealed class ApprovalServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly Mock<IWarehouseAccessService> _warehouseAccess = new();
    private readonly Mock<IApprovalRoleDirectory> _roles = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly WmsDbContext _context;
    private readonly ApprovalService _service;
    private string _actorUserId = "requester";

    public ApprovalServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(
            new DbContextOptionsBuilder<WmsDbContext>()
                .UseSqlite(_connection)
                .Options,
            _clock);
        _context.Database.EnsureCreated();
        _context.Warehouses.Add(new Warehouse("WH-01", "Test warehouse"));
        _context.SaveChanges();

        _warehouseAccess
            .Setup(access => access.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(user => user.UserId).Returns(() => _actorUserId);
        _currentUser.SetupGet(user => user.UserName).Returns(() => _actorUserId);
        _currentUser.SetupGet(user => user.DisplayName).Returns(() => _actorUserId);
        _roles
            .Setup(directory => directory.GetRolesAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string userId, CancellationToken _) => userId switch
            {
                "requester" => new HashSet<string>([WmsRoleNames.WarehouseManager]),
                "approver-1" => new HashSet<string>([WmsRoleNames.WarehouseManager]),
                "approver-2" => new HashSet<string>([WmsRoleNames.Administrator]),
                _ => new HashSet<string>()
            });
        _roles
            .Setup(directory => directory.FindUserIdsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<string> roles, int? _, CancellationToken __) =>
                roles.Contains(WmsRoleNames.Administrator, StringComparer.OrdinalIgnoreCase)
                    ? (IReadOnlyList<string>)["approver-2"]
                    : ["approver-1"]);
        _auditWriter
            .Setup(writer => writer.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new ApprovalService(
            _context,
            _warehouseAccess.Object,
            _roles.Object,
            _currentUser.Object,
            _auditWriter.Object,
            _clock);
    }

    [Fact]
    public async Task Policy_threshold_is_deterministic_and_inclusive_at_boundary()
    {
        await SeedReasonAsync();
        var policy = await SeedPolicyAsync(
            "ADJUSTMENT-THRESHOLD",
            [new ApprovalPolicyLevelInput(1, [WmsRoleNames.WarehouseManager])],
            minimumQuantity: 10);

        var below = await _service.RequestAsync(CreateRequest(
            quantity: 9,
            idempotencyKey: "below-threshold"));
        below.IsSuccess.Should().BeTrue();
        below.Value.ApprovalRequired.Should().BeFalse();

        var atBoundary = await _service.RequestAsync(CreateRequest(
            quantity: 10,
            idempotencyKey: "at-threshold"));
        atBoundary.IsSuccess.Should().BeTrue();
        atBoundary.Value.ApprovalRequired.Should().BeTrue();
        atBoundary.Value.SelectedPolicyCode.Should().Be(policy.Value.Code);
        atBoundary.Value.Request!.Status.Should().Be(ApprovalRequestStatus.Pending);
        (await _context.ApprovalInboxItems.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Required_notes_and_attachment_are_enforced_for_active_localized_reason()
    {
        await SeedReasonAsync(requiresNotes: true, requiresAttachment: true);
        await SeedPolicyAsync(
            "DAMAGE-APPROVAL",
            [new ApprovalPolicyLevelInput(1, [WmsRoleNames.WarehouseManager])]);

        var missingNotes = await _service.RequestAsync(CreateRequest(
            idempotencyKey: "missing-notes"));
        missingNotes.IsFailure.Should().BeTrue();
        missingNotes.ErrorCode.Should().Be("approval.notes_required");

        var missingAttachment = await _service.RequestAsync(CreateRequest(
            idempotencyKey: "missing-attachment",
            notes: "Damage was confirmed during recount."));
        missingAttachment.IsFailure.Should().BeTrue();
        missingAttachment.ErrorCode.Should().Be("approval.attachment_required");

        var valid = await _service.RequestAsync(CreateRequest(
            idempotencyKey: "valid-reason",
            notes: "Damage was confirmed during recount.",
            attachmentReference: "blob://evidence/1"));
        valid.IsSuccess.Should().BeTrue();
        valid.Value.Request!.ReasonCode.Should().Be("DAMAGE");
        valid.Value.Request!.Status.Should().Be(ApprovalRequestStatus.Pending);
    }

    [Fact]
    public async Task Multi_level_approval_enforces_separation_and_roles()
    {
        await SeedReasonAsync();
        await SeedPolicyAsync(
            "MULTI-LEVEL",
            [
                new ApprovalPolicyLevelInput(1, [WmsRoleNames.WarehouseManager]),
                new ApprovalPolicyLevelInput(2, [WmsRoleNames.Administrator])
            ]);

        var request = await _service.RequestAsync(CreateRequest(idempotencyKey: "multi-level"));
        var requestId = request.Value.Request!.Id;

        var selfApproval = await _service.ApproveAsync(
            requestId,
            new ApprovalDecisionInput("self-approval"));
        selfApproval.IsFailure.Should().BeTrue();
        selfApproval.ErrorCode.Should().Be("approval.self_approval_forbidden");

        _actorUserId = "approver-1";
        var firstApproval = await _service.ApproveAsync(
            requestId,
            new ApprovalDecisionInput("level-one"));
        firstApproval.IsSuccess.Should().BeTrue();
        firstApproval.Value.CurrentLevel.Should().Be(2);
        firstApproval.Value.Status.Should().Be(ApprovalRequestStatus.Pending);

        var wrongRole = await _service.ApproveAsync(
            requestId,
            new ApprovalDecisionInput("wrong-level"));
        wrongRole.IsFailure.Should().BeTrue();
        wrongRole.ErrorCode.Should().Be("approval.approver_role_required");

        _actorUserId = "approver-2";
        var finalApproval = await _service.ApproveAsync(
            requestId,
            new ApprovalDecisionInput("level-two"));
        finalApproval.IsSuccess.Should().BeTrue();
        finalApproval.Value.Status.Should().Be(ApprovalRequestStatus.Approved);
        finalApproval.Value.Decisions.Should().HaveCount(2);
    }

    [Fact]
    public async Task Duplicate_decision_is_idempotent_and_history_is_immutable()
    {
        await SeedReasonAsync();
        await SeedPolicyAsync(
            "ONE-LEVEL",
            [new ApprovalPolicyLevelInput(1, [WmsRoleNames.WarehouseManager])]);
        var request = await _service.RequestAsync(CreateRequest(idempotencyKey: "duplicate-decision"));
        var requestId = request.Value.Request!.Id;
        _actorUserId = "approver-1";

        var first = await _service.ApproveAsync(
            requestId,
            new ApprovalDecisionInput("approve-once", "approved"));
        var replay = await _service.ApproveAsync(
            requestId,
            new ApprovalDecisionInput("approve-once", "replayed"));

        first.IsSuccess.Should().BeTrue();
        replay.IsSuccess.Should().BeTrue();
        replay.Value.Status.Should().Be(ApprovalRequestStatus.Approved);
        (await _context.ApprovalDecisions.CountAsync()).Should().Be(1);

        var decision = await _context.ApprovalDecisions.SingleAsync();
        decision.GetType().GetProperty(nameof(ApprovalDecision.Comment))!
            .SetValue(decision, "tampered");
        var act = () => _context.SaveChanges();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Approval decisions are immutable and cannot be updated or deleted.");
    }

    [Fact]
    public async Task Rejection_and_expiry_cannot_start_execution()
    {
        await SeedReasonAsync();
        await SeedPolicyAsync(
            "REJECT-AND-EXPIRE",
            [new ApprovalPolicyLevelInput(1, [WmsRoleNames.WarehouseManager])],
            expiryMinutes: 5);

        var rejected = await _service.RequestAsync(CreateRequest(idempotencyKey: "rejected"));
        _actorUserId = "approver-1";
        var rejection = await _service.RejectAsync(
            rejected.Value.Request!.Id,
            new ApprovalDecisionInput("reject-once", "not enough evidence"));
        rejection.Value.Status.Should().Be(ApprovalRequestStatus.Rejected);
        var rejectedExecution = await _service.BeginExecutionAsync(
            rejected.Value.Request.Id,
            new ApprovalExecutionInput("rejected-execution", "state-1"));
        rejectedExecution.IsFailure.Should().BeTrue();
        rejectedExecution.ErrorCode.Should().Be("approval.not_approved");
        (await _context.ApprovalExecutions.CountAsync()).Should().Be(0);

        _actorUserId = "requester";
        var expiring = await _service.RequestAsync(CreateRequest(idempotencyKey: "expiring"));
        _clock.Now = _clock.Now.AddMinutes(6);
        var expired = await _service.ExpireAsync(expiring.Value.Request!.Id);
        expired.IsSuccess.Should().BeTrue();
        expired.Value.Status.Should().Be(ApprovalRequestStatus.Expired);
        var expiredExecution = await _service.BeginExecutionAsync(
            expiring.Value.Request.Id,
            new ApprovalExecutionInput("expired-execution", "state-1"));
        expiredExecution.IsFailure.Should().BeTrue();
        expiredExecution.ErrorCode.Should().Be("approval.expired");
    }

    [Fact]
    public async Task Stale_state_blocks_execution_and_successful_execution_replays_once()
    {
        await SeedReasonAsync();
        await SeedPolicyAsync(
            "EXECUTION-GATE",
            [new ApprovalPolicyLevelInput(1, [WmsRoleNames.WarehouseManager])]);
        var request = await _service.RequestAsync(CreateRequest(
            idempotencyKey: "execution-request",
            currentStateHash: "state-before"));
        _actorUserId = "approver-1";
        var approved = await _service.ApproveAsync(
            request.Value.Request!.Id,
            new ApprovalDecisionInput("execution-approval"));
        approved.Value.Status.Should().Be(ApprovalRequestStatus.Approved);

        var stale = await _service.BeginExecutionAsync(
            approved.Value.Id,
            new ApprovalExecutionInput("execution-lease", "state-after"));
        stale.IsFailure.Should().BeTrue();
        stale.ErrorCode.Should().Be("approval.stale_state");

        var started = await _service.BeginExecutionAsync(
            approved.Value.Id,
            new ApprovalExecutionInput("execution-lease", "state-before"));
        started.IsSuccess.Should().BeTrue();
        started.Value.Status.Should().Be(ApprovalExecutionStatus.Started);

        var startReplay = await _service.BeginExecutionAsync(
            approved.Value.Id,
            new ApprovalExecutionInput("execution-lease", "state-before"));
        startReplay.IsSuccess.Should().BeTrue();
        startReplay.Value.Id.Should().Be(started.Value.Id);

        var completed = await _service.CompleteExecutionAsync(
            approved.Value.Id,
            new ApprovalCompletionInput("inventory-adjustment-1"));
        completed.IsSuccess.Should().BeTrue();
        completed.Value.Status.Should().Be(ApprovalExecutionStatus.Completed);

        var completeReplay = await _service.CompleteExecutionAsync(
            approved.Value.Id,
            new ApprovalCompletionInput("different-reference"));
        completeReplay.IsSuccess.Should().BeTrue();
        completeReplay.Value.Id.Should().Be(completed.Value.Id);
        (await _context.ApprovalExecutions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Request_requires_operation_authorization_and_records_audit_and_inbox_notification()
    {
        await SeedReasonAsync();
        await SeedPolicyAsync(
            "AUTHORIZED",
            [new ApprovalPolicyLevelInput(1, [WmsRoleNames.WarehouseManager])]);
        _auditWriter.Invocations.Clear();
        _warehouseAccess
            .Setup(access => access.AuthorizeAsync(
                WmsPermissions.InventoryAdjust,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(WmsErrors.Forbidden(
                "authorization.permission_denied",
                "denied")));

        var result = await _service.RequestAsync(CreateRequest(idempotencyKey: "unauthorized"));
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("authorization.permission_denied");
        (await _context.ApprovalRequests.CountAsync()).Should().Be(0);
        _auditWriter.Verify(writer => writer.RecordAsync(
            It.IsAny<AuditRecord>(),
            It.IsAny<CancellationToken>()), Times.Never);

        _warehouseAccess.Reset();
        _warehouseAccess
            .Setup(access => access.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        var allowed = await _service.RequestAsync(CreateRequest(idempotencyKey: "authorized"));
        allowed.IsSuccess.Should().BeTrue();
        (await _context.ApprovalInboxItems.CountAsync()).Should().Be(1);
        _auditWriter.Verify(writer => writer.RecordAsync(
            It.Is<AuditRecord>(record => record.Action == WmsAuditActions.ApprovalRequested),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private async Task<Result<ReasonCodeDto>> SeedReasonAsync(
        bool requiresNotes = false,
        bool requiresAttachment = false)
    {
        return await _service.SaveReasonCodeAsync(
            null,
            new ReasonCodeInput(
                "DAMAGE",
                ReasonCodeCategory.Damage,
                "inventory",
                "adjust",
                "Damage",
                "تالف",
                "Inventory was damaged.",
                "تم تسجيل تلف المخزون.",
                _clock.Now.AddHours(-1),
                null,
                requiresNotes,
                requiresAttachment,
                ReasonCodeSeverity.High,
                1));
    }

    private async Task<Result<ApprovalPolicyDto>> SeedPolicyAsync(
        string code,
        IReadOnlyList<ApprovalPolicyLevelInput> levels,
        decimal? minimumQuantity = null,
        int expiryMinutes = 60)
    {
        return await _service.SaveApprovalPolicyAsync(
            null,
            new ApprovalPolicyInput(
                code,
                code,
                "inventory",
                "adjust",
                1,
                "DAMAGE",
                100,
                minimumQuantity,
                null,
                null,
                null,
                null,
                levels,
                expiryMinutes,
                true,
                _clock.Now.AddHours(-1),
                null));
    }

    private static ApprovalEvaluationInput CreateRequest(
        decimal? quantity = 1,
        string idempotencyKey = "request-1",
        string currentStateHash = "state-1",
        string? notes = null,
        string? attachmentReference = null) =>
        new(
            "inventory",
            "adjust",
            1,
            "DAMAGE",
            "Stock",
            "stock-1",
            "adjustment-1",
            WmsPermissions.InventoryAdjust,
            quantity,
            10,
            null,
            null,
            null,
            currentStateHash,
            notes,
            attachmentReference,
            idempotencyKey);

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;

        public DateTimeOffset UtcNow => Now;
    }
}
