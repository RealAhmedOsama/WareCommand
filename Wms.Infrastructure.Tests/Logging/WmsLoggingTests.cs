using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog.Events;
using Serilog.Parsing;
using Wms.Application.Context;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.Logging;

namespace Wms.Infrastructure.Tests.Logging;

public sealed class WmsLoggingTests
{
    [Fact]
    public void LoggingOptionsReadConfigurableSinksLevelsAndSlowOperationThreshold()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Wms:Logging:MinimumLevel"] = "Debug",
                ["Wms:Logging:ConsoleEnabled"] = "false",
                ["Wms:Logging:SlowOperationThresholdMilliseconds"] = "2500",
                ["Wms:Logging:File:Enabled"] = "true",
                ["Wms:Logging:File:Path"] = "logs/test-.json",
                ["Wms:Logging:File:RetainedFileCountLimit"] = "21",
                ["Wms:Logging:Overrides:Microsoft"] = "Error"
            })
            .Build();

        var options = WmsLoggingOptions.From(configuration);

        Assert.Equal("Debug", options.MinimumLevel);
        Assert.False(options.ConsoleEnabled);
        Assert.Equal(2500, options.SlowOperationThresholdMilliseconds);
        Assert.True(options.File.Enabled);
        Assert.Equal("logs/test-.json", options.File.Path);
        Assert.Equal(21, options.File.RetainedFileCountLimit);
        Assert.Equal("Error", options.Overrides["Microsoft"]);
    }

    [Fact]
    public void NestedOperationScopesPreserveTheParentCorrelationAndRestoreState()
    {
        var accessor = new WmsOperationContextAccessor();
        var parent = new WmsOperationContext(
            "correlation-1",
            "operation-1",
            "Web",
            "http.request",
            RequestId: "request-1");
        var child = parent with
        {
            OperationId = "operation-2",
            OperationName = "inventory.receipt",
            ReferenceId = "receipt-1",
            WarehouseId = 7
        };

        Assert.Null(accessor.Current);
        using (accessor.Begin(parent))
        {
            Assert.Equal(parent, accessor.Current);
            using (WmsLogging.BeginOperation(NullLogger.Instance, accessor, child))
            {
                Assert.Equal(child, accessor.Current);
            }

            Assert.Equal(parent, accessor.Current);
        }

        Assert.Null(accessor.Current);
    }

    [Fact]
    public void PropagationHeadersContainIdentifiersButNeverOperationPayload()
    {
        var context = new WmsOperationContext(
            "correlation-1",
            "operation-1",
            "Integration",
            "shipment.created",
            "shipment-1",
            "operator-1",
            7);

        var headers = WmsOperationContextPropagation.ToHeaders(context);

        Assert.Equal("correlation-1", headers[WmsOperationContextPropagation.CorrelationIdHeader]);
        Assert.Equal("operation-1", headers[WmsOperationContextPropagation.OperationIdHeader]);
        Assert.Equal("shipment-1", headers[WmsOperationContextPropagation.ReferenceIdHeader]);
        Assert.DoesNotContain("operator-1", headers.Values);
        Assert.DoesNotContain("shipment.created", headers.Values);
    }

    [Fact]
    public void SafeJsonFormatterRedactsSensitiveValuesAndExceptionDetails()
    {
        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Error,
            new InvalidOperationException("Password=super-secret; Host=database.internal; details"),
            new MessageTemplateParser().Parse(
                "Operation failed for barcode {Barcode} with payload {Payload} and user {UserId}"),
            [
                new LogEventProperty("Barcode", new ScalarValue("barcode-secret")),
                new LogEventProperty("Payload", new ScalarValue("payload-secret")),
                new LogEventProperty("LargeNote", new ScalarValue(new string('x', 5000))),
                new LogEventProperty("UserId", new ScalarValue("operator-1"))
            ]);
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);

        new WmsSafeJsonFormatter().Format(logEvent, writer);

        var output = writer.ToString();
        Assert.DoesNotContain("barcode-secret", output, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", output, StringComparison.Ordinal);
        Assert.DoesNotContain("database.internal", output, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 5000), output, StringComparison.Ordinal);
        Assert.Contains(WmsLogRedactor.RedactedValue, output, StringComparison.Ordinal);
        Assert.Contains(WmsLogRedactor.TruncatedValue, output, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(output);
        Assert.Equal("operator-1", document.RootElement
            .GetProperty("properties")
            .GetProperty("UserId")
            .GetString());
    }

    [Fact]
    public void RequestContextUsesNormalizedCorrelationIdentifiers()
    {
        var context = new WmsRequestContext();

        context.Initialize(" correlation/with spaces ", "Web");

        Assert.Equal("correlationwithspaces", context.CorrelationId);
        Assert.Equal("Web", context.SourceClient);
    }
}
