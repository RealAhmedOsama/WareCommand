using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Wms.Application.Telemetry;

/// <summary>
/// Shared tracing and metric instruments for the modular WMS. Labels are deliberately
/// limited to fixed vocabularies so user input cannot create unbounded time series.
/// </summary>
public static class WmsTelemetry
{
    public const string ActivitySourceName = "WareCommand.Wms";
    public const string MeterName = "WareCommand.Wms";

    public static ActivitySource ActivitySource { get; } = new(ActivitySourceName, "1.0.0");

    public static Meter Meter { get; } = new(MeterName, "1.0.0");

    private static readonly Counter<long> RequestCounter = Meter.CreateCounter<long>(
        "warecommand.requests",
        unit: "{request}");
    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "warecommand.request.duration",
        unit: "ms");
    private static readonly Histogram<double> DatabaseCommandDuration = Meter.CreateHistogram<double>(
        "warecommand.database.command.duration",
        unit: "ms");
    private static readonly Histogram<double> InventoryOperationDuration = Meter.CreateHistogram<double>(
        "warecommand.inventory.operation.duration",
        unit: "ms");
    private static readonly Counter<long> InventoryOperationCounter = Meter.CreateCounter<long>(
        "warecommand.inventory.operations",
        unit: "{operation}");
    private static readonly Counter<double> InventoryUnitsCounter = Meter.CreateCounter<double>(
        "warecommand.inventory.units",
        unit: "{unit}");
    private static readonly Counter<long> InventoryConflictCounter = Meter.CreateCounter<long>(
        "warecommand.inventory.conflicts",
        unit: "{conflict}");
    private static readonly Counter<long> JobFailureCounter = Meter.CreateCounter<long>(
        "warecommand.jobs.failures",
        unit: "{failure}");
    private static readonly Counter<long> BackupFailureCounter = Meter.CreateCounter<long>(
        "warecommand.backups.failures",
        unit: "{failure}");
    private static long _jobBacklog;
    private static long _storageFreeBytes;

    static WmsTelemetry()
    {
        JobBacklog = Meter.CreateObservableGauge(
            "warecommand.jobs.backlog",
            static () => new Measurement<long>(Volatile.Read(ref _jobBacklog)),
            unit: "{job}");
        StorageFreeBytes = Meter.CreateObservableGauge(
            "warecommand.storage.free",
            static () => new Measurement<long>(Volatile.Read(ref _storageFreeBytes)),
            unit: "By");
    }

    public static ObservableGauge<long> JobBacklog { get; }

    public static ObservableGauge<long> StorageFreeBytes { get; }

    public static WmsInventoryOperationScope BeginInventoryOperation(
        string operation,
        int? warehouseId = null)
    {
        var normalizedOperation = NormalizeOperation(operation);
        var activity = ActivitySource.StartActivity(
            $"wms.inventory.{normalizedOperation}",
            ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag("wms.operation", normalizedOperation);
            if (warehouseId.HasValue)
            {
                activity.SetTag("wms.warehouse.id", warehouseId.Value);
            }
        }

        return new WmsInventoryOperationScope(normalizedOperation, activity);
    }

    /// <summary>
    /// Starts a safe activity for an outbound integration. Payloads, URLs, credentials,
    /// barcodes, and reference values must be recorded by the owning adapter's logs only
    /// after applying the shared redaction rules.
    /// </summary>
    public static Activity? StartExternalOperation(string system, string operation)
    {
        var normalizedSystem = NormalizeToken(system, "unknown");
        var normalizedOperation = NormalizeToken(operation, "unknown");
        var activity = ActivitySource.StartActivity(
            $"wms.external.{normalizedOperation}",
            ActivityKind.Client);

        if (activity is not null)
        {
            activity.SetTag("wms.external.system", normalizedSystem);
            activity.SetTag("wms.external.operation", normalizedOperation);
        }

        return activity;
    }

    public static void RecordRequest(int statusCode, double durationMilliseconds)
    {
        var statusClass = statusCode switch
        {
            >= 200 and < 300 => "2xx",
            >= 300 and < 400 => "3xx",
            >= 400 and < 500 => "4xx",
            _ => "5xx"
        };
        var tags = new TagList
        {
            { "status_class", statusClass }
        };
        RequestCounter.Add(1, tags);
        RequestDuration.Record(Math.Max(0, durationMilliseconds), tags);
    }

    public static void RecordDatabaseCommand(
        string commandKind,
        double durationMilliseconds,
        bool succeeded)
    {
        var tags = new TagList
        {
            { "command_kind", NormalizeToken(commandKind, "other") },
            { "outcome", succeeded ? "success" : "failure" }
        };
        DatabaseCommandDuration.Record(Math.Max(0, durationMilliseconds), tags);
    }

    public static void RecordJobFailure(string jobKind = "unknown")
    {
        JobFailureCounter.Add(
            1,
            new TagList { { "job_kind", NormalizeToken(jobKind, "unknown") } });
    }

    public static void SetJobBacklog(long backlog)
    {
        Interlocked.Exchange(ref _jobBacklog, Math.Max(0, backlog));
    }

    public static void SetStorageFreeBytes(long freeBytes)
    {
        Interlocked.Exchange(ref _storageFreeBytes, Math.Max(0, freeBytes));
    }

    public static void RecordBackupFailure(string backupKind = "unknown")
    {
        BackupFailureCounter.Add(
            1,
            new TagList { { "backup_kind", NormalizeToken(backupKind, "unknown") } });
    }

    public static string NormalizeOperation(string operation) => operation switch
    {
        "receipt" or "inventory.receipt" => "receipt",
        "putaway" or "inventory.putaway" => "putaway",
        "pick" or "inventory.pick" => "pick",
        "pack" or "inventory.pack" => "pack",
        "ship" or "inventory.shipment" => "ship",
        "adjustment" or "inventory.adjustment" => "adjustment",
        "transfer" or "inventory.transfer" => "transfer",
        "count" or "inventory.count" => "count",
        _ => "other"
    };

    private static string NormalizeToken(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.')
            .ToArray());

        return normalized.Length is 0 or > 32 ? fallback : normalized;
    }

    public sealed class WmsInventoryOperationScope : IDisposable
    {
        private readonly Activity? _activity;
        private readonly string _operation;
        private readonly long _startedAt;
        private int _completed;

        internal WmsInventoryOperationScope(string operation, Activity? activity)
        {
            _operation = operation;
            _activity = activity;
            _startedAt = Stopwatch.GetTimestamp();
        }

        public void Complete(decimal? quantity = null)
        {
            if (!TryComplete("success"))
            {
                return;
            }

            if (quantity.HasValue && quantity.Value != 0)
            {
                InventoryUnitsCounter.Add(
                    Math.Abs((double)quantity.Value),
                    new TagList { { "operation", _operation } });
            }
        }

        public void Fail(Exception? exception = null, bool conflict = false)
        {
            var outcome = conflict ? "conflict" : "failure";
            if (!TryComplete(outcome))
            {
                return;
            }

            if (conflict)
            {
                InventoryConflictCounter.Add(
                    1,
                    new TagList { { "operation", _operation } });
            }

            if (_activity is not null)
            {
                _activity.SetStatus(ActivityStatusCode.Error);
                if (exception is not null)
                {
                    _activity.SetTag("error.type", exception.GetType().Name);
                }
            }
        }

        public void Cancel()
        {
            if (!TryComplete("cancelled"))
            {
                return;
            }

            _activity?.SetStatus(ActivityStatusCode.Error);
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _completed, 1, 0) == 0)
            {
                Record("abandoned");
                _activity?.SetStatus(ActivityStatusCode.Error);
            }

            _activity?.Dispose();
        }

        private bool TryComplete(string outcome)
        {
            if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0)
            {
                return false;
            }

            Record(outcome);
            if (outcome == "success")
            {
                _activity?.SetStatus(ActivityStatusCode.Ok);
            }

            return true;
        }

        private void Record(string outcome)
        {
            InventoryOperationCounter.Add(
                1,
                new TagList
                {
                    { "operation", _operation },
                    { "outcome", outcome }
                });
            InventoryOperationDuration.Record(
                Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds,
                new TagList
                {
                    { "operation", _operation },
                    { "outcome", outcome }
                });
        }
    }
}
