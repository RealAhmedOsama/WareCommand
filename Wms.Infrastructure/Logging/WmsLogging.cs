using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using Wms.Application.Context;
using Wms.Application.Logging;

namespace Wms.Infrastructure.Logging;

public static class WmsLogging
{
    public static void Configure(
        LoggerConfiguration loggerConfiguration,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(loggerConfiguration);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var options = WmsLoggingOptions.From(configuration);
        loggerConfiguration
            .MinimumLevel.Is(ParseLevel(options.MinimumLevel))
            .Enrich.FromLogContext()
            .Enrich.WithProperty(WmsLogProperties.Application, "WareCommand")
            .Enrich.WithProperty(WmsLogProperties.Environment, environment.EnvironmentName);

        foreach (var levelOverride in options.Overrides)
        {
            loggerConfiguration.MinimumLevel.Override(
                levelOverride.Key,
                ParseLevel(levelOverride.Value));
        }

        var formatter = new WmsSafeJsonFormatter();
        var sinkConfigured = false;

        if (options.ConsoleEnabled)
        {
            loggerConfiguration.WriteTo.Console(formatter);
            sinkConfigured = true;
        }

        if (options.File.Enabled)
        {
            loggerConfiguration.WriteTo.File(
                formatter,
                options.File.Path,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: options.File.RetainedFileCountLimit,
                fileSizeLimitBytes: options.File.FileSizeLimitBytes,
                rollOnFileSizeLimit: true);
            sinkConfigured = true;
        }

        if (!sinkConfigured)
        {
            loggerConfiguration.WriteTo.Console(formatter);
        }
    }

    public static LoggerConfiguration CreateBootstrapLogger(string environmentName) =>
        new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .Enrich.WithProperty(WmsLogProperties.Application, "WareCommand")
            .Enrich.WithProperty(WmsLogProperties.Environment, environmentName)
            .WriteTo.Console(new WmsSafeJsonFormatter());

    public static IDisposable BeginOperation(
        Microsoft.Extensions.Logging.ILogger logger,
        IWmsOperationContextAccessor accessor,
        WmsOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(context);

        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [WmsLogProperties.CorrelationId] = context.CorrelationId,
            [WmsLogProperties.OperationId] = context.OperationId,
            [WmsLogProperties.Operation] = context.OperationName,
            [WmsLogProperties.SourceClient] = context.SourceClient,
            [WmsLogProperties.ClientType] = context.SourceClient
        };
        AddIfPresent(properties, WmsLogProperties.ReferenceId, context.ReferenceId);
        AddIfPresent(properties, WmsLogProperties.UserId, context.UserId);
        AddIfPresent(properties, WmsLogProperties.RequestId, context.RequestId);
        if (context.WarehouseId.HasValue)
        {
            properties[WmsLogProperties.WarehouseId] = context.WarehouseId.Value;
        }

        var operationScope = accessor.Begin(context);
        var loggerScope = logger.BeginScope(properties);
        var logContextScopes = properties
            .Where(property => property.Value is not null)
            .Select(property => LogContext.PushProperty(property.Key, property.Value))
            .ToArray();

        return new CompositeScope(operationScope, loggerScope, logContextScopes);
    }

    public static IDisposable BeginOperation(
        Microsoft.Extensions.Logging.ILogger logger,
        IWmsOperationContextAccessor accessor,
        IRequestContext requestContext,
        string operationName,
        string? referenceId = null,
        string? userId = null,
        int? warehouseId = null)
    {
        ArgumentNullException.ThrowIfNull(requestContext);

        var parent = accessor.Current;
        var context = new WmsOperationContext(
            parent?.CorrelationId ?? requestContext.CorrelationId,
            WmsExecutionIdentifiers.NewOperationId(),
            parent?.SourceClient ?? requestContext.SourceClient,
            operationName,
            WmsExecutionIdentifiers.NormalizeOptional(referenceId),
            userId ?? parent?.UserId,
            warehouseId ?? parent?.WarehouseId,
            parent?.RequestId);

        return BeginOperation(logger, accessor, context);
    }

    private static LogEventLevel ParseLevel(string? value) =>
        Enum.TryParse<LogEventLevel>(value, ignoreCase: true, out var level)
            ? level
            : LogEventLevel.Information;

    private static void AddIfPresent(
        Dictionary<string, object?> properties,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            properties[name] = value;
        }
    }

    private sealed class CompositeScope(
        IDisposable operationScope,
        IDisposable? loggerScope,
        IReadOnlyList<IDisposable> logContextScopes) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            for (var index = logContextScopes.Count - 1; index >= 0; index--)
            {
                logContextScopes[index].Dispose();
            }

            loggerScope?.Dispose();
            operationScope.Dispose();
        }
    }
}
