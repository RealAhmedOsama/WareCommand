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
    public const string AuditRead = "audit.read";
    public const string WarehouseManage = "warehouse.manage";
    public const string SettingsManage = "settings.manage";
    public const string AccessManage = "access.manage";

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
        AuditRead,
        WarehouseManage,
        SettingsManage,
        AccessManage
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
            [AuditRead] = "View immutable audit history",
            [WarehouseManage] = "Manage warehouse master data",
            [SettingsManage] = "Manage system and warehouse settings",
            [AccessManage] = "Manage users, roles, permissions, and warehouse access"
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
                WmsPermissions.AuditRead,
                WmsPermissions.WarehouseManage
            ],
            [WmsRoleNames.Receiver] =
            [
                WmsPermissions.DashboardView,
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
                WmsPermissions.ReceivingExecute,
                WmsPermissions.WorkRead,
                WmsPermissions.WorkExecute,
                WmsPermissions.PutawayExecute
            ],
            [WmsRoleNames.Picker] =
            [
                WmsPermissions.DashboardView,
                WmsPermissions.ItemsRead,
                WmsPermissions.LocationsRead,
                WmsPermissions.InventoryRead,
                WmsPermissions.SupplierReturnsRead,
                WmsPermissions.WorkRead,
                WmsPermissions.WorkExecute,
                WmsPermissions.PickingExecute
            ],
            [WmsRoleNames.Packer] =
            [
                WmsPermissions.DashboardView,
                WmsPermissions.ItemsRead,
                WmsPermissions.LocationsRead,
                WmsPermissions.InventoryRead,
                WmsPermissions.SupplierReturnsRead,
                WmsPermissions.WorkRead,
                WmsPermissions.WorkExecute,
                WmsPermissions.PackingExecute
            ],
            [WmsRoleNames.InventoryController] =
            [
                WmsPermissions.DashboardView,
                WmsPermissions.ItemsRead,
                WmsPermissions.LocationsRead,
                WmsPermissions.InventoryRead,
                WmsPermissions.InventoryAdjust,
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
                WmsPermissions.ReportsRead
            ],
            [WmsRoleNames.Auditor] =
            [
                WmsPermissions.DashboardView,
                WmsPermissions.ItemsRead,
                WmsPermissions.PurchaseOrdersRead,
                WmsPermissions.AdvanceShippingNoticesRead,
                WmsPermissions.ReceiptsRead,
                WmsPermissions.LocationsRead,
                WmsPermissions.InventoryRead,
                WmsPermissions.QualityRead,
                WmsPermissions.SupplierReturnsRead,
                WmsPermissions.WorkRead,
                WmsPermissions.ReportsRead,
                WmsPermissions.AuditRead
            ],
            [WmsRoleNames.Viewer] =
            [
                WmsPermissions.DashboardView,
                WmsPermissions.ItemsRead,
                WmsPermissions.PurchaseOrdersRead,
                WmsPermissions.AdvanceShippingNoticesRead,
                WmsPermissions.ReceiptsRead,
                WmsPermissions.LocationsRead,
                WmsPermissions.InventoryRead,
                WmsPermissions.QualityRead,
                WmsPermissions.SupplierReturnsRead,
                WmsPermissions.WorkRead,
                WmsPermissions.ReportsRead
            ],
            [WmsRoleNames.WarehouseStaff] =
            [
                WmsPermissions.DashboardView,
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
                WmsPermissions.WorkRead,
                WmsPermissions.WorkExecute,
                WmsPermissions.ReceivingExecute,
                WmsPermissions.PutawayExecute,
                WmsPermissions.PickingExecute
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
    bool IsDefault);

public interface IWarehouseAccessService
{
    Task<bool> HasPermissionAsync(
        string permission,
        CancellationToken cancellationToken = default);

    Task<Result> AuthorizeAsync(
        string permission,
        int? warehouseId = null,
        CancellationToken cancellationToken = default);

    Task<WarehouseAccessScope> GetScopeAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WmsWarehouseOption>> GetAccessibleWarehousesAsync(
        string permission,
        CancellationToken cancellationToken = default);
}
