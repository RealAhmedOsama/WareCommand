using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wms.Application.Attachments;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Attachments;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Tests.Attachments;

public sealed class AttachmentServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly Mock<IWarehouseAccessService> _warehouseAccess = new();
    private readonly Mock<IAttachmentReferenceAccessService> _referenceAccess = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly InMemoryAttachmentStorage _storage = new();
    private readonly TestScanner _scanner = new();
    private readonly WmsDbContext _context;
    private readonly AttachmentStorageOptions _options = new()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "warecommand-attachment-tests"),
        MaximumFileSizeBytes = 10_000,
        MaximumAttachmentsPerReference = 20,
        MaximumRequestBodyBytes = 12_000
    };
    private readonly AttachmentService _service;
    private readonly int _warehouseId;

    public AttachmentServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(
            new DbContextOptionsBuilder<WmsDbContext>()
                .UseSqlite(_connection)
                .Options,
            _clock);
        _context.Database.EnsureCreated();
        var warehouse = new Warehouse("ATT-01", "Attachment test warehouse");
        _context.Warehouses.Add(warehouse);
        _context.SaveChanges();
        _warehouseId = warehouse.Id;

        _warehouseAccess
            .Setup(access => access.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _referenceAccess
            .Setup(access => access.AuthorizeAsync(
                It.IsAny<AttachmentReferenceAccessRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _auditWriter
            .Setup(writer => writer.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(user => user.UserId).Returns("attachment-operator");

        _service = new AttachmentService(
            _context,
            _warehouseAccess.Object,
            _referenceAccess.Object,
            _storage,
            _scanner,
            _auditWriter.Object,
            _currentUser.Object,
            _clock,
            _options,
            NullLogger<AttachmentService>.Instance);
    }

    [Fact]
    public async Task Upload_rejects_traversal_and_mime_signature_mismatch_without_storage()
    {
        var traversal = await _service.UploadAsync(
            CreateInput("../evidence.pdf", "application/pdf", 5),
            new MemoryStream("%PDF-"u8.ToArray()));
        var mismatch = await _service.UploadAsync(
            CreateInput("evidence.png", "image/png", 5),
            new MemoryStream("%PDF-"u8.ToArray()));

        traversal.IsFailure.Should().BeTrue();
        traversal.ErrorCode.Should().Be("attachments.file_name_invalid");
        mismatch.IsFailure.Should().BeTrue();
        mismatch.ErrorCode.Should().Be("attachments.content_signature_mismatch");
        _storage.StoredKeys.Should().BeEmpty();
        (await _context.Attachments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Upload_rejects_duplicate_hash_and_reference_count_abuse()
    {
        var first = await _service.UploadAsync(
            CreateInput("evidence-1.png", "image/png", 9),
            new MemoryStream(Png(1)));
        var duplicate = await _service.UploadAsync(
            CreateInput("evidence-copy.png", "image/png", 9),
            new MemoryStream(Png(1)));

        first.IsSuccess.Should().BeTrue();
        duplicate.IsFailure.Should().BeTrue();
        duplicate.ErrorCode.Should().Be("attachments.duplicate");

        _options.MaximumAttachmentsPerReference.Should().Be(20);
        var limitedOptions = new AttachmentStorageOptions
        {
            RootPath = _options.RootPath,
            MaximumFileSizeBytes = 10_000,
            MaximumAttachmentsPerReference = 1,
            MaximumRequestBodyBytes = 12_000
        };
        var limitedService = new AttachmentService(
            _context,
            _warehouseAccess.Object,
            _referenceAccess.Object,
            _storage,
            _scanner,
            _auditWriter.Object,
            _currentUser.Object,
            _clock,
            limitedOptions,
            NullLogger<AttachmentService>.Instance);

        var countLimited = await limitedService.UploadAsync(
            CreateInput("evidence-2.png", "image/png", 9),
            new MemoryStream(Png(2)));

        countLimited.IsFailure.Should().BeTrue();
        countLimited.ErrorCode.Should().Be("attachments.count_limit");
        (await _context.Attachments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Storage_failure_does_not_persist_metadata()
    {
        var failingStorage = new FailingAttachmentStorage();
        var service = new AttachmentService(
            _context,
            _warehouseAccess.Object,
            _referenceAccess.Object,
            failingStorage,
            _scanner,
            _auditWriter.Object,
            _currentUser.Object,
            _clock,
            _options,
            NullLogger<AttachmentService>.Instance);

        var result = await service.UploadAsync(
            CreateInput("evidence.png", "image/png", 9),
            new MemoryStream(Png(3)));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("attachments.storage_unavailable");
        (await _context.Attachments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Reference_authorization_blocks_idor_before_storage()
    {
        _referenceAccess
            .Setup(access => access.AuthorizeAsync(
                It.IsAny<AttachmentReferenceAccessRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(WmsErrors.Forbidden(
                "authorization.reference_denied",
                "The reference is not accessible.")));

        var result = await _service.UploadAsync(
            CreateInput("evidence.png", "image/png", 9),
            new MemoryStream(Png(4)));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("authorization.reference_denied");
        _storage.StoredKeys.Should().BeEmpty();
        (await _context.Attachments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Suspicious_upload_is_persisted_as_quarantined_and_cannot_download()
    {
        _scanner.IsConfigured = true;
        _scanner.Status = AttachmentScanStatus.Suspicious;
        var result = await _service.UploadAsync(
            CreateInput("evidence.png", "image/png", 9),
            new MemoryStream(Png(5)));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("attachments.quarantined");
        var attachment = await _context.Attachments.SingleAsync();
        attachment.RetentionState.Should().Be(AttachmentRetentionState.Quarantined);
        var download = await _service.OpenDownloadAsync(attachment.Id, preview: false);
        download.IsFailure.Should().BeTrue();
        download.ErrorCode.Should().Be("attachments.not_available");
    }

    [Fact]
    public async Task Scanner_failure_is_retained_as_quarantined_for_follow_up()
    {
        _scanner.IsConfigured = true;
        _scanner.ThrowOnScan = true;
        var result = await _service.UploadAsync(
            CreateInput("scanner-failure.png", "image/png", 9),
            new MemoryStream(Png(9)));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("attachments.quarantined");
        var attachment = await _context.Attachments.SingleAsync();
        attachment.ScanStatus.Should().Be(AttachmentScanStatus.Failed);
        attachment.RetentionState.Should().Be(AttachmentRetentionState.Quarantined);
    }

    [Fact]
    public async Task Immutable_evidence_and_active_retention_cannot_be_deleted()
    {
        var immutable = await _service.UploadAsync(
            CreateInput("evidence.png", "image/png", 9, immutableEvidence: true),
            new MemoryStream(Png(6)));
        immutable.IsSuccess.Should().BeTrue();
        var immutableId = immutable.Value.Id;

        var immutableDelete = await _service.RequestDeletionAsync(immutableId);
        immutableDelete.IsFailure.Should().BeTrue();
        immutableDelete.ErrorCode.Should().Be("attachments.mutation_not_allowed");

        var retained = await _service.UploadAsync(
            CreateInput(
                "retained.png",
                "image/png",
                9,
                retentionUntilUtc: _clock.UtcNow.AddDays(1)),
            new MemoryStream(Png(7)));
        retained.IsSuccess.Should().BeTrue();

        var retainedDelete = await _service.RequestDeletionAsync(retained.Value.Id);
        retainedDelete.IsFailure.Should().BeTrue();
        retainedDelete.ErrorCode.Should().Be("attachments.mutation_not_allowed");
    }

    [Fact]
    public async Task Download_returns_storage_content_and_metadata_without_exposing_storage_key()
    {
        var upload = await _service.UploadAsync(
            CreateInput("photo.png", "image/png", 9),
            new MemoryStream(Png(8)));
        upload.IsSuccess.Should().BeTrue();

        var download = await _service.OpenDownloadAsync(upload.Value.Id, preview: true);
        download.IsSuccess.Should().BeTrue();
        download.Value.FileName.Should().Be("photo.png");
        download.Value.ContentType.Should().Be("image/png");
        (await ReadAllAsync(download.Value.Content)).Should().Equal(Png(8));
        typeof(AttachmentDto).GetProperty("StorageKey").Should().BeNull();
    }

    private AttachmentUploadInput CreateInput(
        string fileName,
        string contentType,
        long length,
        DateTimeOffset? retentionUntilUtc = null,
        bool immutableEvidence = false) => new(
        AttachmentReferenceTypes.Receipt,
        "1",
        _warehouseId,
        fileName,
        contentType,
        length,
        AttachmentClassification.Photo,
        retentionUntilUtc,
        immutableEvidence);

    private static byte[] Png(byte marker) =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, marker
    ];

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        await using (stream)
        {
            await using var output = new MemoryStream();
            await stream.CopyToAsync(output);
            return output.ToArray();
        }
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed class FixedClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = value;
    }

    private sealed class InMemoryAttachmentStorage : IAttachmentStorage
    {
        private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> StoredKeys => _files.Keys.ToArray();

        public async Task StoreAsync(
            AttachmentStorageWrite write,
            CancellationToken cancellationToken = default)
        {
            await using var output = new MemoryStream();
            await write.Content.CopyToAsync(output, cancellationToken);
            _files[write.StorageKey] = output.ToArray();
        }

        public Task<Stream> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Stream>(new MemoryStream(_files[storageKey], writable: false));
        }

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _files.TryRemove(storageKey, out _);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingAttachmentStorage : IAttachmentStorage
    {
        public Task StoreAsync(AttachmentStorageWrite write, CancellationToken cancellationToken = default) =>
            throw new IOException("storage unavailable");

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            throw new IOException("storage unavailable");

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestScanner : IAttachmentScanner
    {
        public bool IsConfigured { get; set; }

        public AttachmentScanStatus Status { get; set; } = AttachmentScanStatus.Clean;

        public bool ThrowOnScan { get; set; }

        public Task<AttachmentScanResult> ScanAsync(
            AttachmentScanRequest request,
            CancellationToken cancellationToken = default) =>
            ThrowOnScan
                ? throw new InvalidOperationException("scanner unavailable")
                : Task.FromResult(new AttachmentScanResult(Status));
    }
}
