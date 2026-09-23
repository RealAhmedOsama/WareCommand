using Wms.Application.Common;

namespace Wms.Application.Identity;

public static class WmsRoleNames
{
    public const string Administrator = "Administrator";
    public const string WarehouseManager = "WarehouseManager";
    public const string Receiver = "Receiver";
    public const string Picker = "Picker";
    public const string Packer = "Packer";
    public const string InventoryController = "InventoryController";
    public const string Auditor = "Auditor";
    public const string Viewer = "Viewer";
    public const string WarehouseStaff = "WarehouseStaff";

    public static IReadOnlyList<string> Catalog { get; } =
    [
        Administrator,
        WarehouseManager,
        Receiver,
        Picker,
        Packer,
        InventoryController,
        Auditor,
        Viewer,
        WarehouseStaff
    ];
}

public static class WmsPermissions
{
    public const string All = "*";
    public const string DashboardView = "dashboard.view";
    public const string ItemsRead = "items.read";
    public const string ItemsManage = "items.manage";
    public const string SuppliersRead = "suppliers.read";
    public const string SuppliersManage = "suppliers.manage";
    public const string CustomersRead = "customers.read";
    public const string CustomersManage = "customers.manage";
    public const string SalesOrdersRead = "sales_orders.read";
    public const string SalesOrdersManage = "sales_orders.manage";
    public const string PurchaseOrdersRead = "purchase_orders.read";
    public const string PurchaseOrdersManage = "purchase_orders.manage";
    public const string AdvanceShippingNoticesRead = "advance_shipping_notices.read";
    public const string AdvanceShippingNoticesManage = "advance_shipping_notices.manage";
    public const string ReceiptsRead = "receipts.read";
    public const string ReceiptsManage = "receipts.manage";
    public const string LocationsRead = "locations.read";
    public const string LocationsManage = "locations.manage";
    public const string InventoryRead = "inventory.read";
    public const string InventoryAdjust = "inventory.adjust";
    public const string InventoryOwnershipRead = "inventory_ownership.read";
    public const string InventoryOwnershipManage = "inventory_ownership.manage";
    public const string ValueAddedServiceRead = "value_added_service.read";
    public const string ValueAddedServiceManage = "value_added_service.manage";
    public const string ReceivingExecute = "receiving.execute";
    public const string ReceivingOverride = "receiving.override";
    public const string SupplierReturnsRead = "supplier_returns.read";
    public const string SupplierReturnsManage = "supplier_returns.manage";
    public const string QualityRead = "quality.read";
    public const string QualityInspect = "quality.inspect";
    public const string QualityManage = "quality.manage";
    public const string QualityOverride = "quality.override";
    public const string WorkRead = "work.read";
    public const string WorkExecute = "work.execute";
    public const string WorkManage = "work.manage";
    public const string WorkOverride = "work.override";
    public const string PutawayExecute = "putaway.execute";
    public const string PickingExecute = "picking.execute";
    public const string PackingExecute = "packing.execute";
    public const string ShippingExecute = "shipping.execute";
    public const string AllocationManage = "allocation.manage";
    public const string CountingExecute = "counting.execute";
    public const string ReportsRead = "reports.read";
    public const string ForecastingRecalculate = "forecasting.recalculate";
    public const string ForecastingOverride = "forecasting.override";
    public const string AuditRead = "audit.read";
    public const string WarehouseManage = "warehouse.manage";
    public const string SettingsManage = "settings.manage";
    public const string AccessManage = "access.manage";
    public const string ApprovalRead = "approval.read";
    public const string ApprovalManage = "approval.manage";
    public const string AttachmentsRead = "attachments.read";
    public const string AttachmentsManage = "attachments.manage";
    public const string NotificationsRead = "notifications.read";
    public const string NotificationsManage = "notifications.manage";
    public const string AnomalyRead = "anomalies.read";
    public const string AnomalyManage = "anomalies.manage";
    public const string AnomalyRulesManage = "anomalies.rules.manage";

    public static IReadOnlyList<string> Catalog { get; } =
    [
        DashboardView,
        ItemsRead,
        ItemsManage,
        SuppliersRead,
        SuppliersManage,
        CustomersRead,
        CustomersManage,
        SalesOrdersRead,
        SalesOrdersManage,
        PurchaseOrdersRead,
        PurchaseOrdersManage,
        AdvanceShippingNoticesRead,
        AdvanceShippingNoticesManage,
        ReceiptsRead,
        ReceiptsManage,
        LocationsRead,
        LocationsManage,
        InventoryRead,
        InventoryAdjust,
        InventoryOwnershipRead,
        InventoryOwnershipManage,
        ValueAddedServiceRead,
        ValueAddedServiceManage,
        ReceivingExecute,
        ReceivingOverride,
        SupplierReturnsRead,
        SupplierReturnsManage,
        QualityRead,
        QualityInspect,
        QualityManage,
        QualityOverride,
        WorkRead,
        WorkExecute,
        WorkManage,
        WorkOverride,
        PutawayExecute,
        PickingExecute,
        PackingExecute,
        ShippingExecute,
        AllocationManage,
        CountingExecute,
        ReportsRead,
        ForecastingRecalculate,
        ForecastingOverride,
        AuditRead,
        WarehouseManage,
        SettingsManage,
        AccessManage,
        ApprovalRead,
        ApprovalManage,
        AttachmentsRead,
        AttachmentsManage,
        NotificationsRead,
        NotificationsManage,
        AnomalyRead,
        AnomalyManage,
        AnomalyRulesManage
    ];

    public static IReadOnlyDictionary<string, string> Descriptions { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DashboardView] = "View operational dashboard",
            [ItemsRead] = "View item master data",
            [ItemsManage] = "Create and update item master data",
            [SuppliersRead] = "View supplier master data",
            [SuppliersManage] = "Create and update supplier master data",
            [CustomersRead] = "View customer and ship-to master data",
            [CustomersManage] = "Create and update customer and ship-to master data",
            [SalesOrdersRead] = "View outbound sales orders and demand",
            [SalesOrdersManage] = "Create, confirm, hold, cancel, and close sales orders",
            [PurchaseOrdersRead] = "View purchase orders and inbound demand",
            [PurchaseOrdersManage] = "Create, confirm, cancel, and close purchase orders",
            [AdvanceShippingNoticesRead] = "View advance shipping notices and expected inbound loads",
            [AdvanceShippingNoticesManage] = "Create, submit, check in, and complete advance shipping notices",
            [ReceiptsRead] = "View inbound receipt documents and history",
            [ReceiptsManage] = "Create, complete, reverse, and correct inbound receipt documents",
            [LocationsRead] = "View warehouse locations",
            [LocationsManage] = "Create and update warehouse locations",
            [InventoryRead] = "View inventory and availability",
            [InventoryAdjust] = "Adjust inventory quantities",
            [InventoryOwnershipRead] = "View inventory owners and ownership transfers",
            [InventoryOwnershipManage] = "Create inventory owners and approve ownership transfers",
            [ValueAddedServiceRead] = "View kit definitions and value-added service orders",
            [ValueAddedServiceManage] = "Create, release, execute, cancel, and reverse value-added service orders",
            [ReceivingExecute] = "Execute receiving",
            [ReceivingOverride] = "Authorize receiving supervisor overrides",
            [SupplierReturnsRead] = "View supplier returns and return-to-vendor traceability",
            [SupplierReturnsManage] = "Create, approve, release, pack, acknowledge, close, and cancel supplier returns",
            [QualityRead] = "View quality profiles and inspections",
            [QualityInspect] = "Record quality test results and dispositions",
            [QualityManage] = "Manage quality profiles and sampling rules",
            [QualityOverride] = "Authorize quality inspection overrides",
            [WorkRead] = "View warehouse work queues and details",
            [WorkExecute] = "Claim and execute warehouse work",
            [WorkManage] = "Create, assign, and cancel warehouse work",
            [WorkOverride] = "Authorize warehouse work overrides",
            [PutawayExecute] = "Execute putaway",
            [PickingExecute] = "Execute picking",
            [PackingExecute] = "Execute packing",
            [ShippingExecute] = "Confirm shipments",
            [AllocationManage] = "Allocate and release outbound demand",
            [CountingExecute] = "Execute inventory counts",
            [ReportsRead] = "View operational reports",
            [ForecastingRecalculate] = "Request advisory demand forecast recalculation",
            [ForecastingOverride] = "Record versioned advisory forecast overrides",
            [AuditRead] = "View immutable audit history",
            [WarehouseManage] = "Manage warehouse master data",
            [SettingsManage] = "Manage system and warehouse settings",
            [AccessManage] = "Manage users, roles, permissions, and warehouse access",
            [ApprovalRead] = "View reason codes, approval requests, decisions, and inbox items",
            [ApprovalManage] = "Manage reason codes and approval policies and decide approval requests",
            [AttachmentsRead] = "View authorized warehouse attachments and evidence",
            [AttachmentsManage] = "Upload, retain, quarantine, and request deletion of attachments",
            [NotificationsRead] = "View authorized in-app notifications and delivery state",
            [NotificationsManage] = "Manage role and warehouse notification preferences",
            [AnomalyRead] = "View warehouse-scoped anomaly findings",
            [AnomalyManage] = "Investigate, assign, and disposition anomaly findings",
            [AnomalyRulesManage] = "Create versioned anomaly detection rule settings"
        };

    public static bool IsKnown(string permission) =>
        Catalog.Contains(permission, StringComparer.Ordinal);
}

public static class WmsRolePermissionCatalog
{
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Defaults { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [WmsRoleNames.Administrator] = [WmsPermissions.All],
            [WmsRoleNames.WarehouseManager] =
            [
                WmsPermissions.DashboardView,
                WmsPermissions.NotificationsRead,
                WmsPermissions.NotificationsManage,
                WmsPermissions.ItemsRead,
                WmsPermissions.ItemsManage,
                WmsPermissions.SuppliersRead,
                WmsPermissions.SuppliersManage,
                WmsPermissions.CustomersRead,
                WmsPermissions.CustomersManage,
                WmsPermissions.SalesOrdersRead,
                WmsPermissions.SalesOrdersManage,
                WmsPermissions.PurchaseOrdersRead,
                WmsPermissions.PurchaseOrdersManage,
                WmsPermissions.AdvanceShippingNoticesRead,
                WmsPermissions.AdvanceShippingNoticesManage,
                WmsPermissions.ReceiptsRead,
                WmsPermissions.ReceiptsManage,
                WmsPermissions.LocationsRead,
                WmsPermissions.LocationsManage,
                WmsPermissions.InventoryRead,
                WmsPermissions.InventoryAdjust,
                WmsPermissions.InventoryOwnershipRead,
                WmsPermissions.InventoryOwnershipManage,
                WmsPermissions.ValueAddedServiceRead,
                WmsPermissions.ValueAddedServiceManage,
                WmsPermissions.ReceivingExecute,
                WmsPermissions.ReceivingOverride,
                WmsPermissions.SupplierReturnsRead,
                WmsPermissions.SupplierReturnsManage,
                WmsPermissions.QualityRead,
                WmsPermissions.QualityInspect,
                WmsPermissions.QualityManage,
                WmsPermissions.QualityOverride,
                WmsPermissions.WorkRead,
                WmsPermissions.WorkExecute,
                WmsPermissions.WorkManage,
                WmsPermissions.WorkOverride,
                WmsPermissions.PutawayExecute,
                WmsPermissions.PickingExecute,
                WmsPermissions.PackingExecute,
                WmsPermissions.ShippingExecute,
                WmsPermissions.AllocationManage,
                WmsPermissions.CountingExecute,
                WmsPermissions.ReportsRead,
                WmsPermissions.AnomalyRead,
                WmsPermissions.AnomalyManage,
                WmsPermissions.AnomalyRulesManage,
                WmsPermissions.ForecastingRecalculate,
                WmsPermissions.ForecastingOverride,
                WmsPermissions.AuditRead,
                WmsPermissions.WarehouseManage,
                WmsPermissions.ApprovalRead,
                WmsPermissions.ApprovalManage,
                WmsPermissions.AttachmentsRead,
                WmsPermissions.AttachmentsManage
            ],
            [WmsRoleNames.Receiver] =
            [
                WmsPermissions.DashboardView,
                WmsPermissions.NotificationsRead,
                WmsPermissions.ItemsRead,
                WmsPermissions.SuppliersRead,
                WmsPermissions.CustomersRead,
                WmsPermissions.SalesOrdersRead,
                WmsPermissions.PurchaseOrdersRead,
                WmsPermissions.AdvanceShippingNoticesRead,
                WmsPermissions.AdvanceShippingNoticesManage,
                WmsPermissions.ReceiptsRead,
                WmsPermissions.ReceiptsManage,
                WmsPermissions.SupplierReturnsRead,
                WmsPermissions.LocationsRead,
                WmsPermissions.InventoryRead,
                WmsPermissions.InventoryOwnershipRead,
                WmsPermissions.ValueAddedServiceRead,
                WmsPermissions.ValueAddedServiceManage,
                WmsPermissions.ReceivingExecute,
                WmsPermissions.WorkRead,
                WmsPermissions.WorkExecute,
                WmsPermissions.PutawayExecute,
                WmsPermissions.AttachmentsRead,
                WmsPermissions.AttachmentsManage
            ],
            [WmsRoleNames.Picker] =
            [
                WmsPermissions.DashboardView,
                WmsPermissions.NotificationsRead,
                WmsPermissions.ItemsRead,
                WmsPermissions.LocationsRead,
                WmsPermissions.InventoryRead,
                WmsPermissions.InventoryOwnershipRead,
                WmsPermissions.ValueAddedServiceRead,
                WmsPermissions.ValueAddedServiceManage,
                WmsPermissions.SupplierReturnsRead,
                WmsPermissions.WorkRead,
                WmsPermissions.WorkExecute,
                WmsPermissions.PickingExecute
            ],
            [WmsRoleNames.Packer] =
            [
                WmsPermissions.DashboardView,
                WmsPermissions.NotificationsRead,
                WmsPermissions.ItemsRead,
                WmsPermissions.LocationsRead,
                WmsPermissions.InventoryRead,
                WmsPermissions.InventoryOwnershipRead,
                WmsPermissions.ValueAddedServiceRead,
                WmsPermissions.ValueAddedServiceManage,
                WmsPermissions.SupplierReturnsRead,
                WmsPermissions.WorkRead,
                WmsPermissions.WorkExecute,
                WmsPermissions.PackingExecute
            ],
            [WmsRoleNames.InventoryController] =
            [
                WmsPermissions.DashboardView,
                WmsPermissions.NotificationsRead,
                WmsPermissions.NotificationsManage,
                WmsPermissions.ItemsRead,
                WmsPermissions.LocationsRead,
                WmsPermissions.InventoryRead,
                WmsPermissions.InventoryOwnershipRead,
                WmsPermissions.ValueAddedServiceRead,
                WmsPermissions.ValueAddedServiceManage,
                WmsPermissions.InventoryAdjust,
                WmsPermissions.ForecastingRecalculate,
                WmsPermissions.ForecastingOverride,
                WmsPermissions.CountingExecute,
                WmsPermissions.AllocationManage,
                WmsPermissions.QualityRead,
                WmsPermissions.QualityInspect,
                WmsPermissions.QualityOverride,
                WmsPermissions.SupplierReturnsRead,
                WmsPermissions.SupplierReturnsManage,
                WmsPermissions.WorkRead,
                WmsPermissions.WorkExecute,
                WmsPermissions.WorkOverride,
                WmsPermissions.ReportsRead,
                WmsPermissions.AnomalyRead,
                WmsPermissions.AnomalyManage,
                WmsPermissions.ApprovalRead,
                WmsPermissions.ApprovalManage,
                WmsPermissions.AttachmentsRead,
                WmsPermissions.AttachmentsManage
            ],
            [WmsRoleNames.Auditor] =
            [
                WmsPermissions.DashboardView,
                WmsPermissions.NotificationsRead,
                WmsPermissions.ItemsRead,
                WmsPermissions.PurchaseOrdersRead,
                WmsPermissions.AdvanceShippingNoticesRead,
                WmsPermissions.ReceiptsRead,
                WmsPermissions.LocationsRead,
                WmsPermissions.InventoryRead,
                WmsPermissions.InventoryOwnershipRead,
                WmsPermissions.ValueAddedServiceRead,
                WmsPermissions.QualityRead,
                WmsPermissions.SupplierReturnsRead,
                WmsPermissions.WorkRead,
                WmsPermissions.ReportsRead,
                WmsPermissions.AnomalyRead,
                WmsPermissions.AuditRead,
                WmsPermissions.ApprovalRead,
                WmsPermissions.AttachmentsRead
            ],
            [WmsRoleNames.Viewer] =
            [
                WmsPermissions.DashboardView,
                WmsPermissions.NotificationsRead,
                WmsPermissions.ItemsRead,
                WmsPermissions.PurchaseOrdersRead,
                WmsPermissions.AdvanceShippingNoticesRead,
                WmsPermissions.ReceiptsRead,
                WmsPermissions.LocationsRead,
                WmsPermissions.InventoryRead,
                WmsPermissions.InventoryOwnershipRead,
                WmsPermissions.ValueAddedServiceRead,
                WmsPermissions.QualityRead,
                WmsPermissions.SupplierReturnsRead,
                WmsPermissions.WorkRead,
                WmsPermissions.ReportsRead,
                WmsPermissions.AnomalyRead,
                WmsPermissions.AttachmentsRead
            ],
            [WmsRoleNames.WarehouseStaff] =
            [
                WmsPermissions.DashboardView,
                WmsPermissions.NotificationsRead,
                WmsPermissions.ItemsRead,
                WmsPermissions.SuppliersRead,
                WmsPermissions.PurchaseOrdersRead,
                WmsPermissions.AdvanceShippingNoticesRead,
                WmsPermissions.AdvanceShippingNoticesManage,
                WmsPermissions.ReceiptsRead,
                WmsPermissions.ReceiptsManage,
                WmsPermissions.SupplierReturnsRead,
                WmsPermissions.LocationsRead,
                WmsPermissions.InventoryRead,
                WmsPermissions.InventoryOwnershipRead,
                WmsPermissions.ValueAddedServiceRead,
                WmsPermissions.ValueAddedServiceManage,
                WmsPermissions.WorkRead,
                WmsPermissions.WorkExecute,
                WmsPermissions.ReceivingExecute,
                WmsPermissions.PutawayExecute,
                WmsPermissions.PickingExecute,
                WmsPermissions.AttachmentsRead,
                WmsPermissions.AttachmentsManage
            ]
        };
}

public static class WmsAuthorizationClaimTypes
{
    public const string Permission = "warecommand/permission";
}

public sealed record WarehouseAccessScope(
    bool HasGlobalAccess,
    IReadOnlySet<int> WarehouseIds)
{
    public static WarehouseAccessScope None { get; } =
        new(false, new HashSet<int>());
}

public sealed record WmsWarehouseOption(
    int Id,
    string Code,
    string Name,
    bool IsDefault,
    string TimeZoneId = "UTC");

public interface IWarehouseAccessService
{
    public Task<bool> HasPermissionAsync(
        string permission,
        CancellationToken cancellationToken = default);

    public Task<Result> AuthorizeAsync(
        string permission,
        int? warehouseId = null,
        CancellationToken cancellationToken = default);

    public Task<WarehouseAccessScope> GetScopeAsync(
        CancellationToken cancellationToken = default);

    public Task<IReadOnlyList<WmsWarehouseOption>> GetAccessibleWarehousesAsync(
        string permission,
        CancellationToken cancellationToken = default);
}
