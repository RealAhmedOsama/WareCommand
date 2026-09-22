using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Wms.Application.BulkExchange;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Infrastructure.BulkExchange;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Tests.BulkExchange;

public sealed class BulkImportServiceTests : IAsyncLifetime, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly BulkImportService _service;

    public BulkImportServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(
            new DbContextOptionsBuilder<WmsDbContext>()
                .UseSqlite(_connection)
                .Options,
            new FixedClock(new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero)));
        var access = new Mock<IWarehouseAccessService>();
        access
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _service = new BulkImportService(
            _context,
            new BulkCsvParser(),
            [new RequiredColumnBulkImportValidator()],
            [],
            access.Object,
            new FixedClock(new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero)));
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
    public async Task PreviewPersistsRowErrorsAndDryRunDoesNotMutateBusinessTables()
    {
        var preview = await _service.PreviewAsync(
            new BulkImportPreviewRequest(
                WmsBulkImportTypes.ItemsV1,
                new BulkImportSource(
                    "items.csv",
                    "text/csv",
                    WmsBulkImportFormats.Csv,
                    "SKU,COST\nA-1,1.25\nA-2,not-a-number\n"),
                new BulkImportMappingProfile(
                    WmsBulkImportTypes.ItemsV1,
                    1,
                    "items-basic",
                    [
                        new BulkImportColumnMapping("SKU", "Sku", Required: true),
                        new BulkImportColumnMapping("COST", "StandardCost", Transformation: "decimal")
                    ]),
                "operator-1"));

        preview.TotalRows.Should().Be(2);
        preview.ValidRows.Should().Be(1);
        preview.InvalidRows.Should().Be(1);
        preview.Errors.Should().Contain(error => error.Code == "import.decimal_invalid");
        (await _context.BulkImportRows.CountAsync()).Should().Be(2);
        (await _context.Items.CountAsync()).Should().Be(0);
        var loaded = await _service.GetAsync(preview.ExecutionId);
        loaded!.FileName.Should().Be("items.csv");
        loaded.SourceSha256.Should().NotBeNullOrWhiteSpace();

        var dryRun = await _service.ExecuteAsync(
            new BulkImportExecuteRequest(preview.ExecutionId, "operator-1", DryRun: true));

        dryRun.Status.Should().Be(WmsBulkImportStatuses.Previewed);
        (await _context.Items.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ExecutionWithPreviewErrorsIsBlockedBeforeAnyHandlerRuns()
    {
        var preview = await _service.PreviewAsync(
            new BulkImportPreviewRequest(
                WmsBulkImportTypes.ItemsV1,
                new BulkImportSource("items.csv", "text/csv", WmsBulkImportFormats.Csv, "SKU,NAME\n,Widget\n"),
                new BulkImportMappingProfile(
                    WmsBulkImportTypes.ItemsV1,
                    1,
                    "items-required",
                    [new BulkImportColumnMapping("SKU", "Sku", Required: true)]),
                "operator-1"));

        var result = await _service.ExecuteAsync(
            new BulkImportExecuteRequest(preview.ExecutionId, "operator-1", DryRun: false));

        result.Status.Should().Be(WmsBulkImportStatuses.Failed);
        result.Summary.Should().Contain("validation errors");
        (await _context.Items.CountAsync()).Should().Be(0);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
