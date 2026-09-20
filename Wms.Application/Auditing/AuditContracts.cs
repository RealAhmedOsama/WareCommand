using Wms.Application.Common;

namespace Wms.Application.Auditing;

public static class WmsAuditActions
{
    public const string AuthenticationEvent = "security.authentication";
    public const string AccountCreated = "security.account.created";
    public const string AccountStatusChanged = "security.account.status_changed";
    public const string AccessChanged = "security.access.changed";

    public const string ItemCreated = "master.item.created";
    public const string ItemUpdated = "master.item.updated";
    public const string ItemDeactivated = "master.item.deactivated";
    public const string LocationCreated = "master.location.created";
    public const string LocationUpdated = "master.location.updated";
    public const string LocationActivated = "master.location.activated";
    public const string LocationDeactivated = "master.location.deactivated";
    public const string LocationBulkGenerated = "master.location.bulk_generated";
    public const string LocationBulkImported = "master.location.bulk_imported";
    public const string WarehouseCreated = "master.warehouse.created";
    public const string WarehouseUpdated = "master.warehouse.updated";
    public const string WarehouseActivated = "master.warehouse.activated";
    public const string WarehouseDeactivated = "master.warehouse.deactivated";
    public const string WarehouseConfigurationChanged = "master.warehouse.configuration_changed";

    public const string StockAdjusted = "inventory.stock.adjusted";
    public const string ReceiptRecorded = "inbound.receipt.recorded";
    public const string PutawayCompleted = "inbound.putaway.completed";
    public const string TransferCompleted = "inventory.transfer.completed";
    public const string CountApproved = "inventory.count.approved";
    public const string AllocationChanged = "outbound.allocation.changed";
    public const string PickCompleted = "outbound.pick.completed";
    public const string PackCompleted = "outbound.pack.completed";
    public const string ShipmentCompleted = "outbound.shipment.completed";
    public const string ReturnProcessed = "outbound.return.processed";
    public const string SettingsChanged = "settings.changed";
    public const string IntegrationAction = "integration.action";

    public static IReadOnlyList<string> Catalog { get; } =
    [
        AuthenticationEvent,
        AccountCreated,
        AccountStatusChanged,
        AccessChanged,
        ItemCreated,
        ItemUpdated,
        ItemDeactivated,
        LocationCreated,
        LocationUpdated,
        LocationActivated,
        LocationDeactivated,
        LocationBulkGenerated,
        LocationBulkImported,
        WarehouseCreated,
        WarehouseUpdated,
        WarehouseActivated,
        WarehouseDeactivated,
        WarehouseConfigurationChanged,
        StockAdjusted,
        ReceiptRecorded,
        PutawayCompleted,
        TransferCompleted,
        CountApproved,
        AllocationChanged,
        PickCompleted,
        PackCompleted,
        ShipmentCompleted,
        ReturnProcessed,
        SettingsChanged,
        IntegrationAction
    ];
}

public static class WmsAuditEntityTypes
{
    public const string Authentication = "Authentication";
    public const string User = "User";
    public const string AccessAssignment = "AccessAssignment";
    public const string Item = "Item";
    public const string Location = "Location";
    public const string Warehouse = "Warehouse";
    public const string Stock = "Stock";
    public const string Movement = "Movement";
    public const string Count = "Count";
    public const string Transfer = "Transfer";
    public const string Allocation = "Allocation";
    public const string Package = "Package";
    public const string Shipment = "Shipment";
    public const string Return = "Return";
    public const string Settings = "Settings";
    public const string Integration = "Integration";
}

/// <summary>
/// A safe, scalar-oriented audit command. The infrastructure writer redacts and
/// bounds metadata before it is persisted; callers must not pass entity graphs.
/// </summary>
public sealed record AuditRecord(
    string Action,
    string EntityType,
    string? EntityId = null,
    int? WarehouseId = null,
    IReadOnlyDictionary<string, object?>? Before = null,
    IReadOnlyDictionary<string, object?>? After = null,
    string? ActorUserId = null,
    string? ActorUserName = null,
    bool Succeeded = true,
    string? Details = null);

public interface IAuditWriter
{
    /// <summary>
    /// Adds an immutable audit entry to the current unit of work. This method does
    /// not commit; callers that mutate business data must save it in the same
    /// DbContext transaction.
    /// </summary>
    Task RecordAsync(
        AuditRecord record,
        CancellationToken cancellationToken = default);
}

public sealed record AuditQuery(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? UserId = null,
    int? WarehouseId = null,
    string? Action = null,
    string? EntityType = null,
    int Page = 1,
    int PageSize = 50);

public sealed record AuditEntryDto(
    long Id,
    DateTimeOffset OccurredAtUtc,
    string? ActorUserId,
    string? ActorUserName,
    string Action,
    string EntityType,
    string? EntityId,
    int? WarehouseId,
    string? WarehouseCode,
    string CorrelationId,
    string SourceClient,
    bool Succeeded,
    string? Details,
    string? BeforeJson,
    string? AfterJson);

public sealed record AuditPage(
    IReadOnlyList<AuditEntryDto> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public interface IAuditQueryService
{
    Task<Result<AuditPage>> SearchAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default);
}
