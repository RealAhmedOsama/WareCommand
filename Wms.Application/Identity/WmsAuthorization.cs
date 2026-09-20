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
    public const string LocationsRead = "locations.read";
    public const string LocationsManage = "locations.manage";
    public const string InventoryRead = "inventory.read";
    public const string InventoryAdjust = "inventory.adjust";
    public const string ReceivingExecute = "receiving.execute";
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
        LocationsRead,
        LocationsManage,
        InventoryRead,
        InventoryAdjust,
        ReceivingExecute,
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
            [LocationsRead] = "View warehouse locations",
            [LocationsManage] = "Create and update warehouse locations",
            [InventoryRead] = "View inventory and availability",
            [InventoryAdjust] = "Adjust inventory quantities",
            [ReceivingExecute] = "Execute receiving",
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
                WmsPermissions.LocationsRead,
                WmsPermissions.LocationsManage,
                WmsPermissions.InventoryRead,
                WmsPermissions.InventoryAdjust,
                WmsPermissions.ReceivingExecute,
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
                WmsPermissions.LocationsRead,
                WmsPermissions.InventoryRead,
                WmsPermissions.ReceivingExecute,
                WmsPermissions.PutawayExecute
            ],
            [WmsRoleNames.Picker] =
            [
                WmsPermissions.DashboardView,
                WmsPermissions.ItemsRead,
                WmsPermissions.LocationsRead,
                WmsPermissions.InventoryRead,
                WmsPermissions.PickingExecute
            ],
            [WmsRoleNames.Packer] =
            [
                WmsPermissions.DashboardView,
                WmsPermissions.ItemsRead,
                WmsPermissions.LocationsRead,
                WmsPermissions.InventoryRead,
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
                WmsPermissions.ReportsRead
            ],
            [WmsRoleNames.Auditor] =
            [
                WmsPermissions.DashboardView,
                WmsPermissions.ItemsRead,
                WmsPermissions.LocationsRead,
                WmsPermissions.InventoryRead,
                WmsPermissions.ReportsRead,
                WmsPermissions.AuditRead
            ],
            [WmsRoleNames.Viewer] =
            [
                WmsPermissions.DashboardView,
                WmsPermissions.ItemsRead,
                WmsPermissions.LocationsRead,
                WmsPermissions.InventoryRead,
                WmsPermissions.ReportsRead
            ],
            [WmsRoleNames.WarehouseStaff] =
            [
                WmsPermissions.DashboardView,
                WmsPermissions.ItemsRead,
                WmsPermissions.LocationsRead,
                WmsPermissions.InventoryRead,
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
