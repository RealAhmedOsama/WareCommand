using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Attachments;
using Wms.Application.Context;
using Wms.Application.Retention;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Retention;

namespace Wms.Infrastructure.Tests.Retention;

public sealed class RetentionServiceTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly FixedClock clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly TestAttachmentStorage storage = new();
    private readonly WmsDbContext context;
    private readonly RetentionService service;
    private readonly int warehouseId;

    public RetentionServiceTests()
    {
        connection.Open();
        context = new WmsDbContext(
            new DbContextOptionsBuilder<WmsDbContext>()
                .UseSqlite(connection)
                .Options,
            clock);
        context.Database.EnsureCreated();
        var warehouse = new Warehouse("RET-01", "Retention test warehouse");
        context.Warehouses.Add(warehouse);
        context.SaveChanges();
        warehouseId = warehouse.Id;
        service = new RetentionService(
            context,
            clock,
            storage,
            auditWriter: null,
            currentUser: null,
            NullLogger<RetentionService>.Instance);
    }

    [Fact]
    public async Task Policy_minimums_and_immutable_classes_are_enforced()
    {
        var tooShort = await service.SavePolicyAsync(new RetentionPolicyInput(
            RetentionClass.Documents,
            RetentionDays: 30,
            WarehouseId: warehouseId));
        var immutable = await service.SavePolicyAsync(new RetentionPolicyInput(
            RetentionClass.SecurityAudit,
            RetentionDays: 0,
            WarehouseId: warehouseId,
            Enabled: true));

        tooShort.IsFailure.Should().BeTrue();
        tooShort.ErrorCode.Should().Be("retention.minimum_violation");
        immutable.IsFailure.Should().BeTrue();
        immutable.ErrorCode.Should().Be("retention.immutable_class");
    }

    [Fact]
    public async Task Legal_hold_is_visible_in_exact_preview_counts()
    {
        await ConfigureDocumentsAsync();
        var attachment = await AddPendingAttachmentAsync("hold.bin");
        var hold = await service.CreateHoldAsync(new RetentionHoldInput(
            RetentionClass.Documents,
            "attachment",
            attachment.Id.ToString(CultureInfo.InvariantCulture),
            "Regulatory case is still open.",
            warehouseId,
            "CASE-84"));

        var previewWithHold = await service.PreviewAsync(new RetentionPreviewInput(
            clock.UtcNow,
            warehouseId,
            BatchSize: 1));
        previewWithHold.IsSuccess.Should().BeTrue(previewWithHold.Error);
        var heldCount = previewWithHold.Value.Counts.Single(item => item.Class == RetentionClass.Documents);

        await service.ReleaseHoldAsync(hold.Value.Id, "Case closed.");
        var previewWithoutHold = await service.PreviewAsync(new RetentionPreviewInput(
            clock.UtcNow,
            warehouseId,
            BatchSize: 1));
        previewWithoutHold.IsSuccess.Should().BeTrue(previewWithoutHold.Error);
        var eligibleCount = previewWithoutHold.Value.Counts.Single(item => item.Class == RetentionClass.Documents);

        previewWithHold.IsSuccess.Should().BeTrue();
        heldCount.Examined.Should().Be(1);
        heldCount.Held.Should().Be(1);
        heldCount.Eligible.Should().Be(0);
        previewWithoutHold.IsSuccess.Should().BeTrue();
        eligibleCount.Examined.Should().Be(1);
        eligibleCount.Held.Should().Be(0);
        eligibleCount.Eligible.Should().Be(1);
    }

    [Fact]
    public async Task Destructive_run_requires_preview_and_is_idempotent_after_archive()
    {
        await ConfigureDocumentsAsync();
        var attachment = await AddPendingAttachmentAsync("purge.bin");
        var missingGate = await service.RunAsync(new RetentionRunInput(
            DryRun: false,
            AllowDestructive: true,
            BackupVerified: true,
            AuthorizationReference: "CAB-84",
            AsOfUtc: clock.UtcNow,
            WarehouseId: warehouseId,
            BatchSize: 1));

        var preview = await service.PreviewAsync(new RetentionPreviewInput(
            clock.UtcNow,
            warehouseId,
            BatchSize: 1));
        preview.IsSuccess.Should().BeTrue(preview.Error);
        var run = await service.RunAsync(new RetentionRunInput(
            DryRun: false,
            AllowDestructive: true,
            BackupVerified: true,
            AuthorizationReference: "CAB-84",
            AsOfUtc: preview.Value.AsOfUtc,
            WarehouseId: warehouseId,
            BatchSize: 1,
            PreviewRunId: preview.Value.RunId));
        var retry = await service.RunAsync(new RetentionRunInput(
            DryRun: false,
            AllowDestructive: true,
            BackupVerified: true,
            AuthorizationReference: "CAB-84",
            AsOfUtc: preview.Value.AsOfUtc,
            WarehouseId: warehouseId,
            BatchSize: 1,
            RunId: run.Value.RunId,
            PreviewRunId: preview.Value.RunId));
        var persistedAttachment = await context.Attachments.SingleAsync(item => item.Id == attachment.Id);
        var archive = await context.RetentionArchiveReferences.SingleAsync();

        missingGate.IsFailure.Should().BeTrue();
        missingGate.ErrorCode.Should().Be("retention.preview_required");
        preview.IsSuccess.Should().BeTrue();
        preview.Value.EligibleItems.Should().Be(1);
        run.IsSuccess.Should().BeTrue();
        run.Value.Status.Should().Be(RetentionRunStatus.Succeeded);
        run.Value.ArchivedItems.Should().Be(1);
        run.Value.PurgedItems.Should().Be(1);
        retry.IsSuccess.Should().BeTrue();
        persistedAttachment.RetentionState.Should().Be(AttachmentRetentionState.Deleted);
        archive.PurgedAtUtc.Should().NotBeNull();
        storage.DeletedKeys.Should().ContainSingle();
    }

    [Fact]
    public async Task Preview_never_selects_immutable_history()
    {
        var preview = await service.PreviewAsync(new RetentionPreviewInput(
            clock.UtcNow,
            warehouseId));
        preview.IsSuccess.Should().BeTrue(preview.Error);
        var immutable = preview.Value.Counts.Single(item =>
            item.Class == RetentionClass.ImmutableOperationalHistory);
        var audit = preview.Value.Counts.Single(item => item.Class == RetentionClass.SecurityAudit);

        preview.IsSuccess.Should().BeTrue();
        immutable.IsPurgeAllowed.Should().BeFalse();
        immutable.Examined.Should().Be(0);
        audit.IsPurgeAllowed.Should().BeFalse();
        audit.Examined.Should().Be(0);
    }

    private async Task ConfigureDocumentsAsync()
    {
        var result = await service.SavePolicyAsync(new RetentionPolicyInput(
            RetentionClass.Documents,
            RetentionDays: 365,
            WarehouseId: warehouseId));
        result.IsSuccess.Should().BeTrue();
    }

    private async Task<Attachment> AddPendingAttachmentAsync(string fileName)
    {
        var uploadedAt = clock.UtcNow.AddDays(-500);
        var attachment = new Attachment(
            "receipt",
            Guid.NewGuid().ToString("N"),
            warehouseId,
            fileName,
            "attachments/" + fileName,
            "application/octet-stream",
            4,
            new string('a', 64),
            "operator",
            AttachmentClassification.Operational,
            AttachmentScanStatus.Clean,
            uploadedAt,
            uploadedAt.AddDays(1),
            immutableEvidence: false);
        attachment.RequestDeletion("operator", clock.UtcNow);
        context.Attachments.Add(attachment);
        await context.SaveChangesAsync();
        return attachment;
    }

    public void Dispose()
    {
        context.Dispose();
        connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class TestAttachmentStorage : IAttachmentStorage
    {
        public List<string> DeletedKeys { get; } = [];

        public Task StoreAsync(AttachmentStorageWrite write, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(storageKey);
            return Task.CompletedTask;
        }
    }
}
