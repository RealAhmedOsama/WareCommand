using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Identity;

namespace Wms.Application.Administration;

public enum AdministrationSurfaceStatus
{
    Ready,
    Partial,
    DeploymentOnly
}

public enum AdministrationSecretPolicy
{
    NotApplicable,
    NeverDisplay,
    DeploymentManaged
}

public enum AdministrationReadinessStatus
{
    Ready,
    Warning,
    Blocked
}

public sealed record AdministrationModuleDescriptor(
    string Key,
    string Category,
    string CategoryArabic,
    string Title,
    string TitleArabic,
    string Description,
    string DescriptionArabic,
    string Route,
    string RequiredPermission,
    bool WarehouseScoped,
    AdministrationSurfaceStatus Status,
    AdministrationSecretPolicy SecretPolicy,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> SupportedActions);

public sealed record AdministrationModuleDto(
    string Key,
    string Category,
    string CategoryArabic,
    string Title,
    string TitleArabic,
    string Description,
    string DescriptionArabic,
    string Route,
    string RequiredPermission,
    bool WarehouseScoped,
    AdministrationSurfaceStatus Status,
    AdministrationSecretPolicy SecretPolicy,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> SupportedActions,
    bool IsAuthorized);

public sealed record AdministrationReadinessCheckDto(
    string Key,
    string AreaKey,
    string Title,
    string TitleArabic,
    AdministrationReadinessStatus Status,
    bool IsBlocking,
    string Detail,
    string DetailArabic,
    string Route,
    IReadOnlyList<string> Dependencies);

public sealed record AdministrationReadinessDto(
    DateTimeOffset GeneratedAtUtc,
    int? WarehouseId,
    IReadOnlyList<AdministrationReadinessCheckDto> Checks)
{
    public AdministrationReadinessStatus OverallStatus =>
        Checks.Any(check => check.Status == AdministrationReadinessStatus.Blocked)
            ? AdministrationReadinessStatus.Blocked
            : Checks.Any(check => check.Status == AdministrationReadinessStatus.Warning)
                ? AdministrationReadinessStatus.Warning
                : AdministrationReadinessStatus.Ready;

    public int ReadyCount => Checks.Count(check => check.Status == AdministrationReadinessStatus.Ready);

    public int WarningCount => Checks.Count(check => check.Status == AdministrationReadinessStatus.Warning);

    public int BlockedCount => Checks.Count(check => check.Status == AdministrationReadinessStatus.Blocked);
}

public sealed record AdministrationHistoryQuery(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? UserId = null,
    int? WarehouseId = null,
    string? Action = null,
    string? EntityType = null,
    int Page = 1,
    int PageSize = 25);

public interface IAdministrationService
{
    Task<Result<IReadOnlyList<AdministrationModuleDto>>> GetCatalogAsync(
        CancellationToken cancellationToken = default);

    Task<Result<AdministrationReadinessDto>> GetReadinessAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default);

    Task<Result<AuditPage>> GetHistoryAsync(
        AdministrationHistoryQuery query,
        CancellationToken cancellationToken = default);
}

public static class AdministrationCatalog
{
    public static IReadOnlyList<AdministrationModuleDescriptor> Items { get; } =
    [
        new(
            "organization.settings",
            "Organization",
            "المنظمة",
            "Company and warehouse settings",
            "إعدادات الشركة والمستودعات",
            "Configure company defaults and warehouse overrides through the typed settings screen.",
            "تهيئة الإعدادات الافتراضية للشركة وتجاوزات المستودعات من خلال شاشة الإعدادات المعرّفة.",
            "/Settings",
            WmsPermissions.SettingsManage,
            true,
            AdministrationSurfaceStatus.Partial,
            AdministrationSecretPolicy.NeverDisplay,
            ["typed-settings", "warehouse-scope"],
            ["view", "edit", "import", "export", "history"]),
        new(
            "organization.warehouses",
            "Organization",
            "المنظمة",
            "Warehouses and operational profiles",
            "المستودعات والملفات التشغيلية",
            "Manage warehouse profiles, operational locations, workflow readiness, and number sequences.",
            "إدارة ملفات المستودعات والمواقع التشغيلية وجاهزية سير العمل وتسلسلات الأرقام.",
            "/Warehouses",
            WmsPermissions.WarehouseManage,
            true,
            AdministrationSurfaceStatus.Ready,
            AdministrationSecretPolicy.NotApplicable,
            ["warehouse-scope", "operational-locations"],
            ["view", "create", "edit", "activate", "deactivate", "dependencies"]),
        new(
            "master-data.locations",
            "Master data",
            "البيانات الأساسية",
            "Locations and storage profiles",
            "المواقع وملفات التخزين",
            "Maintain locations, hierarchy, capacity, and warehouse-specific operating flags.",
            "إدارة المواقع وتسلسلها وسعتها وخصائص التشغيل الخاصة بالمستودع.",
            "/Locations",
            WmsPermissions.LocationsRead,
            true,
            AdministrationSurfaceStatus.Ready,
            AdministrationSecretPolicy.NotApplicable,
            ["warehouses", "warehouse-scope"],
            ["view", "create", "edit", "activate", "deactivate", "dependencies"]),
        new(
            "master-data.items",
            "Master data",
            "البيانات الأساسية",
            "Items, UOM, and packaging",
            "الأصناف ووحدات القياس والتعبئة",
            "Use the item and unit-of-measure screens for typed master data and conversion rules.",
            "استخدم شاشات الأصناف ووحدات القياس للبيانات الأساسية وقواعد التحويل المعرّفة.",
            "/Items",
            WmsPermissions.ItemsRead,
            true,
            AdministrationSurfaceStatus.Partial,
            AdministrationSecretPolicy.NotApplicable,
            ["warehouses", "units-of-measure", "packaging"],
            ["view", "create", "edit", "activate", "deactivate", "bulk"]),
        new(
            "master-data.units",
            "Master data",
            "البيانات الأساسية",
            "Units of measure",
            "وحدات القياس",
            "Manage units and conversions without editing persistence tables directly.",
            "إدارة وحدات القياس والتحويلات دون تعديل جداول التخزين مباشرة.",
            "/UnitsOfMeasure",
            WmsPermissions.ItemsRead,
            false,
            AdministrationSurfaceStatus.Ready,
            AdministrationSecretPolicy.NotApplicable,
            ["items"],
            ["view", "create", "edit", "activate", "deactivate"]),
        new(
            "master-data.suppliers",
            "Master data",
            "البيانات الأساسية",
            "Suppliers and supplier references",
            "الموردون ومرجعيات الموردين",
            "Manage suppliers and item references through the supplier master screen.",
            "إدارة الموردين ومرجعيات الأصناف من شاشة بيانات الموردين.",
            "/SupplierMaster",
            WmsPermissions.SuppliersRead,
            true,
            AdministrationSurfaceStatus.Ready,
            AdministrationSecretPolicy.NotApplicable,
            ["items", "warehouses"],
            ["view", "create", "edit", "activate", "deactivate", "bulk", "import", "export"]),
        new(
            "master-data.customers",
            "Master data",
            "البيانات الأساسية",
            "Customers and ship-to addresses",
            "العملاء وعناوين الشحن",
            "Manage customer master data and shipping destinations.",
            "إدارة بيانات العملاء ووجهات الشحن.",
            "/Customers",
            WmsPermissions.CustomersRead,
            true,
            AdministrationSurfaceStatus.Ready,
            AdministrationSecretPolicy.NotApplicable,
            ["warehouses"],
            ["view", "create", "edit", "activate", "deactivate"]),
        new(
            "access.users",
            "Access control",
            "التحكم في الوصول",
            "Users and warehouse access",
            "المستخدمون والوصول إلى المستودعات",
            "Manage active users and their assigned warehouse scope.",
            "إدارة المستخدمين النشطين ونطاق المستودعات المعيّن لهم.",
            "/Account/Users",
            WmsPermissions.AccessManage,
            true,
            AdministrationSurfaceStatus.Ready,
            AdministrationSecretPolicy.NeverDisplay,
            ["rbac", "warehouse-scope"],
            ["view", "create", "edit", "activate", "deactivate", "history"]),
        new(
            "access.roles",
            "Access control",
            "التحكم في الوصول",
            "Roles and permissions",
            "الأدوار والصلاحيات",
            "Manage role permissions using the authorization catalog; no arbitrary permission names are accepted.",
            "إدارة صلاحيات الأدوار باستخدام كتالوج الصلاحيات دون قبول أسماء صلاحيات عشوائية.",
            "/Account/Access",
            WmsPermissions.AccessManage,
            false,
            AdministrationSurfaceStatus.Ready,
            AdministrationSecretPolicy.NeverDisplay,
            ["rbac", "permission-catalog"],
            ["view", "edit", "history"]),
        new(
            "workflow.statuses",
            "Workflow policy",
            "سياسات سير العمل",
            "Statuses, reason codes, and approvals",
            "الحالات وأكواد الأسباب والموافقات",
            "Configure typed inventory statuses, reason codes, approval policies, and inbox actions.",
            "تهيئة حالات المخزون المعرّفة وأكواد الأسباب وسياسات الموافقة وإجراءات صندوق الوارد.",
            "/api/approvals/reason-codes",
            WmsPermissions.ApprovalRead,
            true,
            AdministrationSurfaceStatus.Partial,
            AdministrationSecretPolicy.NotApplicable,
            ["approval-contracts", "warehouse-scope"],
            ["view", "create", "edit", "activate", "deactivate", "history"]),
        new(
            "workflow.inventory-statuses",
            "Workflow policy",
            "سياسات سير العمل",
            "Inventory statuses",
            "حالات المخزون",
            "Manage inventory status definitions and allowed transitions.",
            "إدارة تعريفات حالات المخزون والانتقالات المسموح بها.",
            "/InventoryStatuses",
            WmsPermissions.SettingsManage,
            true,
            AdministrationSurfaceStatus.Ready,
            AdministrationSecretPolicy.NotApplicable,
            ["items", "warehouse-scope"],
            ["view", "create", "edit", "activate", "deactivate", "dependencies"]),
        new(
            "platform.jobs",
            "Operations",
            "التشغيل",
            "Background jobs",
            "المهام الخلفية",
            "Inspect the authorized Hangfire operations dashboard and durable WMS job state.",
            "فحص لوحة عمليات Hangfire المصرّح بها وحالة مهام WareCommand الدائمة.",
            "/hangfire",
            WmsPermissions.AccessManage,
            false,
            AdministrationSurfaceStatus.Partial,
            AdministrationSecretPolicy.DeploymentManaged,
            ["job-runner", "database"],
            ["view", "retry", "history"]),
        new(
            "platform.notifications",
            "Operations",
            "التشغيل",
            "Notifications and delivery state",
            "الإشعارات وحالة التسليم",
            "Use the notification API for scoped preferences, in-app state, and delivery diagnostics.",
            "استخدم واجهة الإشعارات للتفضيلات وحالة التطبيق وتشخيص التسليم ضمن النطاق.",
            "/api/notifications",
            WmsPermissions.NotificationsRead,
            true,
            AdministrationSurfaceStatus.Partial,
            AdministrationSecretPolicy.DeploymentManaged,
            ["notification-preferences", "warehouse-scope"],
            ["view", "edit", "history"]),
        new(
            "platform.attachments",
            "Operations",
            "التشغيل",
            "Attachments and evidence",
            "المرفقات والأدلة",
            "Manage authorized attachment metadata and retention state; storage credentials remain deployment-managed.",
            "إدارة بيانات المرفقات وحالة الاحتفاظ بها؛ تظل بيانات التخزين السرية ضمن إعدادات النشر.",
            "/api/attachments",
            WmsPermissions.AttachmentsRead,
            true,
            AdministrationSurfaceStatus.Partial,
            AdministrationSecretPolicy.DeploymentManaged,
            ["attachment-policy", "retention", "warehouse-scope"],
            ["view", "upload", "quarantine", "history"]),
        new(
            "platform.labels",
            "Operations",
            "التشغيل",
            "Labels and printers",
            "الملصقات والطابعات",
            "Manage label templates and print requests through their typed service boundary.",
            "إدارة قوالب الملصقات وطلبات الطباعة من خلال حدود الخدمة المعرّفة.",
            "/Labels",
            WmsPermissions.SettingsManage,
            true,
            AdministrationSurfaceStatus.Partial,
            AdministrationSecretPolicy.DeploymentManaged,
            ["label-templates", "printer-endpoint", "warehouse-scope"],
            ["view", "create", "edit", "activate", "deactivate", "history"]),
        new(
            "platform.retention",
            "Platform governance",
            "حوكمة المنصة",
            "Retention and legal holds",
            "الاحتفاظ القانوني وحالات التجميد",
            "Preview and manage retention policies through the guarded retention API.",
            "معاينة وإدارة سياسات الاحتفاظ من خلال واجهة الاحتفاظ المحمية.",
            "/api/retention",
            WmsPermissions.SettingsManage,
            true,
            AdministrationSurfaceStatus.Partial,
            AdministrationSecretPolicy.DeploymentManaged,
            ["retention-policy", "legal-holds", "backup-gate"],
            ["view", "preview", "edit", "history"]),
        new(
            "platform.audit",
            "Platform governance",
            "حوكمة المنصة",
            "Audit and change history",
            "سجل التدقيق وتاريخ التغييرات",
            "Review immutable, paged audit history with warehouse and entity filters.",
            "مراجعة سجل التدقيق غير القابل للتعديل مع التصفية حسب المستودع والكيان.",
            "/Audit",
            WmsPermissions.AuditRead,
            true,
            AdministrationSurfaceStatus.Ready,
            AdministrationSecretPolicy.NeverDisplay,
            ["immutable-audit", "warehouse-scope"],
            ["view", "filter", "export"]),
        new(
            "platform.health",
            "Platform governance",
            "حوكمة المنصة",
            "System health and deployment diagnostics",
            "صحة النظام وتشخيصات النشر",
            "Readiness endpoints expose health state without exposing deployment secrets or editable settings.",
            "تعرض نقاط الجاهزية حالة الصحة دون كشف أسرار النشر أو إعدادات قابلة للتعديل.",
            "/health/ready",
            WmsPermissions.AccessManage,
            false,
            AdministrationSurfaceStatus.DeploymentOnly,
            AdministrationSecretPolicy.DeploymentManaged,
            ["database", "storage", "background-jobs", "backup"],
            ["view"])
    ];
}
