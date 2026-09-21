using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Devices;
using Wms.Application.Labels;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Labels;

namespace Wms.Infrastructure.Tests.Labels;

public sealed class LabelServicesTests : IAsyncLifetime, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));

    public LabelServicesTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new WmsDbContext(options, _clock);
        _auditWriter
            .Setup(writer => writer.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public async Task InitializeAsync() => await _context.Database.EnsureCreatedAsync();

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task TemplateVersionsActivateAndRollbackWithinScope()
    {
        var service = CreateTemplateService();
        var first = await service.SaveVersionAsync(new WmsLabelTemplateSaveRequest(
            CreateDefinition(1),
            "admin"));
        var second = await service.SaveVersionAsync(new WmsLabelTemplateSaveRequest(
            CreateDefinition(2),
            "admin"));

        first.IsSuccess.Should().BeTrue(first.Error);
        second.IsSuccess.Should().BeTrue(second.Error);
        (await service.ActivateAsync("item-label", 1, "en-US", 7, "admin"))
            .IsSuccess.Should().BeTrue();
        (await service.ActivateAsync("item-label", 2, "en-US", 7, "admin"))
            .IsSuccess.Should().BeTrue();

        var activeV2 = await service.ResolveActiveAsync(new WmsLabelTemplateResolutionRequest(
            "item-label",
            WmsLabelDocumentType.Item,
            "en-US",
            WmsPrintFormat.Pdf,
            WarehouseId: 7));
        activeV2.IsSuccess.Should().BeTrue(activeV2.Error);
        activeV2.Value.Definition.Version.Should().Be(2);

        var rollback = await service.RollbackAsync("item-label", 1, "en-US", 7, "admin");
        rollback.IsSuccess.Should().BeTrue(rollback.Error);
        var activeV1 = await service.ResolveActiveAsync(new WmsLabelTemplateResolutionRequest(
            "item-label",
            WmsLabelDocumentType.Item,
            "en-US",
            WmsPrintFormat.Pdf,
            WarehouseId: 7));

        activeV1.Value.Definition.Version.Should().Be(1);
        (await _context.LabelTemplates.CountAsync(template => template.IsActive)).Should().Be(1);
    }

    [Fact]
    public async Task FailedPrinterJobIsAuditedAndRetryIsIdempotent()
    {
        var templateService = CreateTemplateService();
        var definition = CreateDefinition(1) with
        {
            Format = WmsPrintFormat.Zpl,
            Routes =
            [
                new WmsPrintRoute
                {
                    Name = "dock-zpl",
                    TemplateName = "item-label",
                    Format = WmsPrintFormat.Zpl,
                    Transport = WmsPrintTransport.NetworkZpl,
                    WarehouseId = 7,
                    AdapterKey = "fake-printer"
                }
            ]
        };
        (await templateService.SaveVersionAsync(new WmsLabelTemplateSaveRequest(definition, "admin")))
            .IsSuccess.Should().BeTrue();
        (await templateService.ActivateAsync("item-label", 1, "en-US", 7, "admin"))
            .IsSuccess.Should().BeTrue();

        var attempts = 0;
        var adapter = new Mock<IWmsPrintAdapter>();
        adapter.SetupGet(value => value.AdapterKey).Returns("fake-printer");
        adapter.Setup(value => value.PrintAsync(
                It.IsAny<WmsPrintRequest>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                attempts++;
                return attempts == 1
                    ? Result.Failure(WmsErrors.Dependency("printer.timeout", "Printer unavailable."))
                    : Result.Success();
            });

        var printService = new WmsLabelPrintService(
            _context,
            templateService,
            _clock,
            _auditWriter.Object,
            [adapter.Object],
            NullLogger<WmsLabelPrintService>.Instance);
        var request = new WmsLabelPrintRequest(
            new WmsLabelTemplateResolutionRequest(
                "item-label",
                WmsLabelDocumentType.Item,
                "en-US",
                WmsPrintFormat.Zpl,
                WarehouseId: 7),
            new Dictionary<string, string?> { ["sku"] = "SKU-1" },
            WarehouseId: 7,
            StationCode: "DOCK-A",
            SourceReference: "ITEM-1",
            IdempotencyKey: "print-1",
            ActorUserId: "operator");

        var failed = await printService.PrintAsync(request);
        failed.IsFailure.Should().BeTrue();
        var job = await _context.PrintJobs.SingleAsync();
        job.Status.Should().Be(WmsPrintJobStatus.Failed.ToString());
        job.AttemptCount.Should().Be(1);

        var retried = await printService.RetryAsync(job.Id, "operator");
        retried.IsSuccess.Should().BeTrue(retried.Error);
        retried.Value.Status.Should().Be(WmsPrintJobStatus.Succeeded);
        retried.Value.AttemptCount.Should().Be(2);
        attempts.Should().Be(2);

        var duplicate = await printService.PrintAsync(request);
        duplicate.IsSuccess.Should().BeTrue(duplicate.Error);
        duplicate.Value.JobId.Should().Be(job.Id);
        attempts.Should().Be(2);
        _auditWriter.Verify(
            writer => writer.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(2));
    }

    [Fact]
    public async Task ReprintWithoutReasonIsRejectedBeforeCreatingAJob()
    {
        var templateService = CreateTemplateService();
        var definition = CreateDefinition(1);
        (await templateService.SaveVersionAsync(new WmsLabelTemplateSaveRequest(definition, "admin")))
            .IsSuccess.Should().BeTrue();
        (await templateService.ActivateAsync("item-label", 1, "en-US", 7, "admin"))
            .IsSuccess.Should().BeTrue();
        var printService = new WmsLabelPrintService(
            _context,
            templateService,
            _clock,
            _auditWriter.Object,
            [],
            NullLogger<WmsLabelPrintService>.Instance);

        var result = await printService.PrintAsync(new WmsLabelPrintRequest(
            new WmsLabelTemplateResolutionRequest(
                "item-label", WmsLabelDocumentType.Item, "en-US", WmsPrintFormat.Pdf, WarehouseId: 7),
            new Dictionary<string, string?> { ["sku"] = "SKU-1" },
            WarehouseId: 7,
            IsReprint: true,
            ActorUserId: "operator"));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("label.reprint_reason_required");
        (await _context.PrintJobs.CountAsync()).Should().Be(0);
    }

    private WmsLabelTemplateService CreateTemplateService() =>
        new(
            _context,
            _clock,
            _auditWriter.Object,
            NullLogger<WmsLabelTemplateService>.Instance);

    private static WmsLabelTemplateDefinition CreateDefinition(int version) => new()
    {
        Name = "item-label",
        Version = version,
        DocumentType = WmsLabelDocumentType.Item,
        Language = "en-US",
        Format = WmsPrintFormat.Pdf,
        WarehouseId = 7,
        Body = "SKU {{sku}}",
        Fields =
        [
            new WmsLabelFieldDefinition { Key = "sku", Kind = WmsLabelFieldKind.Text }
        ]
    };

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
