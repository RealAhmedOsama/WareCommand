using Microsoft.Extensions.Logging;

namespace Wms.Application.Logging;

/// <summary>
/// Stable event IDs for operational logs. Message templates are intentionally safe and structured.
/// </summary>
public static class WmsLogEvents
{
    public static readonly EventId ApplicationStarted = new(1000, nameof(ApplicationStarted));
    public static readonly EventId ApplicationStopping = new(1001, nameof(ApplicationStopping));
    public static readonly EventId ApplicationStopped = new(1002, nameof(ApplicationStopped));

    public static readonly EventId RequestStarted = new(1100, nameof(RequestStarted));
    public static readonly EventId RequestCompleted = new(1101, nameof(RequestCompleted));
    public static readonly EventId RequestFailed = new(1102, nameof(RequestFailed));
    public static readonly EventId SlowOperation = new(1103, nameof(SlowOperation));

    public static readonly EventId DatabaseMigrationStarted = new(1200, nameof(DatabaseMigrationStarted));
    public static readonly EventId DatabaseMigrationCompleted = new(1201, nameof(DatabaseMigrationCompleted));
    public static readonly EventId DatabaseMigrationFailed = new(1202, nameof(DatabaseMigrationFailed));

    public static readonly EventId InventoryReceiptCompleted = new(2000, nameof(InventoryReceiptCompleted));
    public static readonly EventId InventoryPutawayCompleted = new(2001, nameof(InventoryPutawayCompleted));
    public static readonly EventId InventoryPickCompleted = new(2002, nameof(InventoryPickCompleted));
    public static readonly EventId InventoryAdjustmentCompleted = new(2003, nameof(InventoryAdjustmentCompleted));
    public static readonly EventId InventoryCommandDuplicate = new(2004, nameof(InventoryCommandDuplicate));
    public static readonly EventId InventoryCommandPayloadMismatch = new(2005, nameof(InventoryCommandPayloadMismatch));
    public static readonly EventId InventoryCommandReplayed = new(2006, nameof(InventoryCommandReplayed));
    public static readonly EventId InventoryCommandInProgress = new(2007, nameof(InventoryCommandInProgress));
    public static readonly EventId InventoryCommandReclaimed = new(2008, nameof(InventoryCommandReclaimed));
    public static readonly EventId InventoryOperationFailed = new(2099, nameof(InventoryOperationFailed));

    public static readonly EventId JobStarted = new(3000, nameof(JobStarted));
    public static readonly EventId JobCompleted = new(3001, nameof(JobCompleted));
    public static readonly EventId JobFailed = new(3002, nameof(JobFailed));

    public static readonly EventId ExternalCallStarted = new(4000, nameof(ExternalCallStarted));
    public static readonly EventId ExternalCallCompleted = new(4001, nameof(ExternalCallCompleted));
    public static readonly EventId ExternalCallFailed = new(4002, nameof(ExternalCallFailed));

    public static readonly EventId AuthenticationFailed = new(5000, nameof(AuthenticationFailed));
}

public static class WmsLogProperties
{
    public const string Application = "Application";
    public const string Environment = "Environment";
    public const string CorrelationId = "CorrelationId";
    public const string OperationId = "OperationId";
    public const string Operation = "Operation";
    public const string ReferenceId = "ReferenceId";
    public const string SourceClient = "SourceClient";
    public const string ClientType = "ClientType";
    public const string UserId = "UserId";
    public const string WarehouseId = "WarehouseId";
    public const string RequestId = "RequestId";
    public const string RequestPath = "RequestPath";
    public const string DurationMs = "DurationMs";
    public const string StatusCode = "StatusCode";
    public const string Outcome = "Outcome";
    public const string ErrorCode = "ErrorCode";
    public const string Retryable = "Retryable";
    public const string ExternalSystem = "ExternalSystem";
    public const string JobName = "JobName";
}
