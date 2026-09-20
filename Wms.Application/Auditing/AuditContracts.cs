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
    public const string ItemActivated = "master.item.activated";
    public const string ItemDeactivated = "master.item.deactivated";
    public const string ItemDeleted = "master.item.deleted";
    public const string ItemDuplicated = "master.item.duplicated";
    public const string ItemBulkImported = "master.item.bulk_imported";
    public const string ItemPackagingsChanged = "master.item.packagings_changed";
    public const string UnitOfMeasureCreated = "master.uom.created";
    public const string UnitOfMeasureUpdated = "master.uom.updated";
    public const string UnitOfMeasureActivated = "master.uom.activated";
    public const string UnitOfMeasureDeactivated = "master.uom.deactivated";
    public const string ItemUnitsAssigned = "master.item.units_assigned";
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
    public const string IdentifierResolved = "security.identifier.resolved";
    public const string LotUpdated = "inventory.lot.updated";
    public const string LotStatusChanged = "inventory.lot.status_changed";
    public const string LotCreated = "inventory.lot.created";
    public const string SerialCreated = "inventory.serial.created";
    public const string SerialStatusChanged = "inventory.serial.status_changed";
    public const string SerialCorrected = "inventory.serial.corrected";
    public const string InventoryStatusConfigured = "inventory.status.configured";
    public const string InventoryStatusTransitionConfigured = "inventory.status.transition_configured";
    public const string InventoryStatusChanged = "inventory.status.changed";
    public const string LicensePlateCreated = "inventory.license_plate.created";
    public const string LicensePlateContentChanged = "inventory.license_plate.content_changed";
    public const string LicensePlateMoved = "inventory.license_plate.moved";
    public const string LicensePlateLifecycleChanged = "inventory.license_plate.lifecycle_changed";
    public const string LicensePlateShipped = "inventory.license_plate.shipped";
    public const string LicensePlateNumberingConfigured = "inventory.license_plate.numbering_configured";

    public static IReadOnlyList<string> Catalog { get; } =
    [
        AuthenticationEvent,
        AccountCreated,
        AccountStatusChanged,
        AccessChanged,
        ItemCreated,
        ItemUpdated,
        ItemActivated,
        ItemDeactivated,
        ItemDeleted,
        ItemDuplicated,
        ItemBulkImported,
        ItemPackagingsChanged,
        UnitOfMeasureCreated,
        UnitOfMeasureUpdated,
        UnitOfMeasureActivated,
        UnitOfMeasureDeactivated,
        ItemUnitsAssigned,
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
        IntegrationAction,
        IdentifierResolved,
        LotUpdated,
        LotStatusChanged,
        LotCreated,
        SerialCreated,
        SerialStatusChanged,
        SerialCorrected,
        InventoryStatusConfigured,
        InventoryStatusTransitionConfigured,
        InventoryStatusChanged,
        LicensePlateCreated,
        LicensePlateContentChanged,
        LicensePlateMoved,
        LicensePlateLifecycleChanged,
        LicensePlateShipped,
        LicensePlateNumberingConfigured
    ];
}

public static class WmsAuditEntityTypes
{
    public const string Authentication = "Authentication";
    public const string User = "User";
    public const string AccessAssignment = "AccessAssignment";
    public const string Item = "Item";
    public const string UnitOfMeasure = "UnitOfMeasure";
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
    public const string Identifier = "Identifier";
    public const string Lot = "Lot";
    public const string Serial = "Serial";
    public const string InventoryStatus = "InventoryStatus";
    public const string InventoryStatusTransition = "InventoryStatusTransition";
    public const string LicensePlate = "LicensePlate";
    public const string LicensePlateContent = "LicensePlateContent";
    public const string LicensePlateNumberSequence = "LicensePlateNumberSequence";
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
