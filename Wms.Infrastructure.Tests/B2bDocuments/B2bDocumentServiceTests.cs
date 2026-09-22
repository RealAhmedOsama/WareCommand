using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wms.Application.B2bDocuments;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.B2bDocuments;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Tests.B2bDocuments;

public sealed class B2bDocumentServiceTests : IAsyncLifetime, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 22, 14, 0, 0, TimeSpan.Zero));
    private readonly WmsDbContext _context;
    private readonly B2bDocumentService _service;

    public B2bDocumentServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(
            new DbContextOptionsBuilder<WmsDbContext>()
                .UseSqlite(_connection)
                .Options,
            _clock);
        var access = new Mock<IWarehouseAccessService>();
        access
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        var requestContext = new WmsRequestContext("Test");
        requestContext.Initialize("b2b-test", "Test");
        _service = new B2bDocumentService(
            _context,
            access.Object,
            _clock,
            requestContext,
            NullLogger<B2bDocumentService>.Instance);
    }

    public async Task InitializeAsync() => await _context.Database.EnsureCreatedAsync();

    public async Task DisposeAsync() => await _context.DisposeAsync();

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task DuplicateControlNumbersAreIdempotentAndMismatchedPayloadIsRejected()
    {
        var partner = await CreatePartnerAsync();
        var request = new B2bDocumentSubmitRequest(partner.Value.Id, CreateEnvelope());

        var first = await _service.SubmitAsync(request);
        var duplicate = await _service.SubmitAsync(request);
        var mismatch = await _service.SubmitAsync(request with
        {
            Envelope = CreateEnvelope() with
            {
                Fields = new Dictionary<string, string?> { ["poNumber"] = "PO-2" }
            }
        });

        first.IsSuccess.Should().BeTrue(first.Error);
        first.Value.WasDuplicate.Should().BeFalse();
        duplicate.IsSuccess.Should().BeTrue(duplicate.Error);
        duplicate.Value.WasDuplicate.Should().BeTrue();
        mismatch.IsFailure.Should().BeTrue();
        mismatch.ErrorCode.Should().Be("b2b.duplicate_payload_mismatch");
        (await _context.B2bDocuments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task InvalidEnvelopeIsQuarantinedAndCanBeReplayed()
    {
        var partner = await CreatePartnerAsync();
        var result = await _service.SubmitAsync(
            new B2bDocumentSubmitRequest(
                partner.Value.Id,
                CreateEnvelope() with
                {
                    GroupControlNumber = string.Empty,
                    DeclaredLineCount = 4
                }));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Document.Status.Should().Be(WmsB2bDocumentStatuses.Quarantined);
        result.Value.Document.ValidationErrors.Should().Contain(error => error.Code == "b2b.control_number_required");
        result.Value.Document.ValidationErrors.Should().Contain(error => error.Code == "b2b.control_total_invalid");

        var replay = await _service.ReplayAsync(new B2bDocumentReplayRequest(result.Value.Document.Id));

        replay.IsSuccess.Should().BeTrue(replay.Error);
        replay.Value.Status.Should().Be(WmsB2bDocumentStatuses.Replayed);
        replay.Value.ReplayCount.Should().Be(1);
    }

    [Fact]
    public async Task AcknowledgementCorrelatesToTheOriginalDocumentWithoutDuplicateAckRows()
    {
        var partner = await CreatePartnerAsync();
        var submitted = await _service.SubmitAsync(
            new B2bDocumentSubmitRequest(partner.Value.Id, CreateEnvelope()));

        var request = new B2bAcknowledgementRequest(
            submitted.Value.Document.Id,
            "997",
            WmsB2bAcknowledgementStatuses.Generated);
        var acknowledgement = await _service.AcknowledgeAsync(request);
        var duplicate = await _service.AcknowledgeAsync(request);

        acknowledgement.IsSuccess.Should().BeTrue(acknowledgement.Error);
        acknowledgement.Value.DocumentId.Should().Be(submitted.Value.Document.Id);
        acknowledgement.Value.ControlNumber.Should().Be($"ACK-{submitted.Value.Document.Id}-997");
        duplicate.IsSuccess.Should().BeTrue(duplicate.Error);
        duplicate.Value.Id.Should().Be(acknowledgement.Value.Id);
        (await _context.B2bAcknowledgements.CountAsync()).Should().Be(1);
        (await _context.B2bDocuments.SingleAsync()).AcknowledgementStatus
            .Should().Be(WmsB2bAcknowledgementStatuses.Generated);
    }

    [Fact]
    public async Task PartnerCredentialReferenceMustNotContainRawSecretMaterial()
    {
        var mapping = await SaveMappingAsync();
        var result = await _service.CreateTradingPartnerAsync(
            new TradingPartnerCreateRequest(
                "ERP-01",
                "ERP",
                WmsB2bStandards.Canonical,
                "https://example.test?password=raw",
                [17],
                [WmsB2bDocumentTypes.PurchaseOrder],
                mapping.Value.Name,
                mapping.Value.Version,
                Activate: true));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("b2b.credential_reference_invalid");
    }

    private async Task<Result<TradingPartnerDto>> CreatePartnerAsync()
    {
        var mapping = await SaveMappingAsync();
        return await _service.CreateTradingPartnerAsync(
            new TradingPartnerCreateRequest(
                "ERP-01",
                "ERP",
                WmsB2bStandards.Canonical,
                "secret-manager://b2b/erp-01",
                [17],
                [WmsB2bDocumentTypes.PurchaseOrder],
                mapping.Value.Name,
                mapping.Value.Version,
                Activate: true));
    }

    private async Task<Result<B2bMappingProfileDto>> SaveMappingAsync() =>
        await _service.SaveMappingProfileAsync(
            new B2bMappingProfileRequest(
                WmsB2bDocumentTypes.PurchaseOrder,
                "po-canonical-v1",
                1,
                [new B2bMappingRule("poNumber", "purchaseOrderNumber", "trim")]));

    private static B2bDocumentEnvelope CreateEnvelope() =>
        new(
            Guid.NewGuid(),
            WmsB2bDocumentTypes.PurchaseOrder,
            "1.0",
            WmsB2bDirections.Inbound,
            WmsB2bTransportModes.Api,
            "I-100",
            "G-100",
            "D-100",
            17,
            new Dictionary<string, string?> { ["poNumber"] = "PO-1" },
            1,
            1,
            new DateTimeOffset(2026, 9, 22, 13, 59, 0, TimeSpan.Zero),
            "b2b-correlation");

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
