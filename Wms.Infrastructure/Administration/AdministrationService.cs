using Microsoft.EntityFrameworkCore;
using Wms.Application.Administration;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Connectors;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Administration;

public sealed class AdministrationService(
    WmsDbContext context,
    IClock clock,
    IWarehouseAccessService warehouseAccessService,
    IAuditQueryService auditQueryService,
    IEnumerable<IConnectorAdapter>? connectorAdapters = null) : IAdministrationService
{
    private static readonly WarehouseOperationalLocationRole[] InboundRoles =
    [
        WarehouseOperationalLocationRole.Receiving,
        WarehouseOperationalLocationRole.Staging,
        WarehouseOperationalLocationRole.Quarantine,
        WarehouseOperationalLocationRole.Returns,
        WarehouseOperationalLocationRole.Transit
    ];

    private static readonly WarehouseOperationalLocationRole[] OutboundRoles =
    [
        WarehouseOperationalLocationRole.Storage,
        WarehouseOperationalLocationRole.Staging,
        WarehouseOperationalLocationRole.Packing,
        WarehouseOperationalLocationRole.Shipping,
        WarehouseOperationalLocationRole.Transit
    ];

    public async Task<Result<IReadOnlyList<AdministrationModuleDto>>> GetCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(cancellationToken: cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<AdministrationModuleDto>>();
        }

        var permissions = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var permission in AdministrationCatalog.Items
                     .Select(item => item.RequiredPermission)
                     .Distinct(StringComparer.Ordinal))
        {
            permissions[permission] = await warehouseAccessService.HasPermissionAsync(
                permission,
                cancellationToken);
        }

        var modules = AdministrationCatalog.Items
            .Select(item => new AdministrationModuleDto(
                item.Key,
                item.Category,
                item.CategoryArabic,
                item.Title,
                item.TitleArabic,
                item.Description,
                item.DescriptionArabic,
                item.Route,
                item.RequiredPermission,
                item.WarehouseScoped,
                item.Status,
                item.SecretPolicy,
                item.Dependencies,
                item.SupportedActions,
                permissions[item.RequiredPermission]))
            .ToArray();

        return Result.Success<IReadOnlyList<AdministrationModuleDto>>(modules);
    }

    public async Task<Result<AdministrationReadinessDto>> GetReadinessAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(warehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<AdministrationReadinessDto>();
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var warehouseQuery = context.Warehouses
            .AsNoTracking()
            .Where(warehouse => warehouse.IsActive);
        if (warehouseId.HasValue)
        {
            warehouseQuery = warehouseQuery.Where(warehouse => warehouse.Id == warehouseId.Value);
        }
        else if (!scope.HasGlobalAccess)
        {
            warehouseQuery = warehouseQuery.Where(warehouse => scope.WarehouseIds.Contains(warehouse.Id));
        }

        var warehouses = await warehouseQuery
            .OrderBy(warehouse => warehouse.Code)
            .Select(warehouse => new WarehouseReadinessRow(
                warehouse.Id,
                warehouse.Code,
                warehouse.WorkflowEnabled))
            .ToListAsync(cancellationToken);
        var warehouseIds = warehouses.Select(warehouse => warehouse.Id).ToArray();

        var roleRows = warehouseIds.Length == 0
            ? []
            : await context.WarehouseOperationalLocations
                .AsNoTracking()
                .Where(reference => warehouseIds.Contains(reference.WarehouseId) &&
                                    reference.Location.IsActive)
                .Select(reference => new WarehouseRoleRow(reference.WarehouseId, reference.Role))
                .ToListAsync(cancellationToken);
        var configuredRoles = roleRows
            .GroupBy(row => row.WarehouseId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.Role).ToHashSet());

        var checks = new List<AdministrationReadinessCheckDto>();
        var hasGlobalSettings = await context.GlobalSettings
            .AsNoTracking()
            .AnyAsync(settings => settings.Id == Wms.Infrastructure.Settings.WmsGlobalSettingsEntity.GlobalId,
                cancellationToken);
        checks.Add(new AdministrationReadinessCheckDto(
            "global-settings",
            "organization.settings",
            "Global settings",
            "الإعدادات العامة",
            hasGlobalSettings
                ? AdministrationReadinessStatus.Ready
                : AdministrationReadinessStatus.Blocked,
            !hasGlobalSettings,
            hasGlobalSettings
                ? "Typed global settings are available to workflow services."
                : "Global settings must be saved before inbound or outbound workflows can be enabled.",
            hasGlobalSettings
                ? "الإعدادات العامة المعرّفة متاحة لخدمات سير العمل."
                : "يجب حفظ الإعدادات العامة قبل تفعيل سير عمل الوارد أو الصادر.",
            "/Settings",
            ["typed-settings", "company-profile"]));

        var hasActiveWarehouse = warehouses.Count > 0;
        checks.Add(new AdministrationReadinessCheckDto(
            "active-warehouse",
            "organization.warehouses",
            "Active warehouse",
            "المستودع النشط",
            hasActiveWarehouse
                ? AdministrationReadinessStatus.Ready
                : AdministrationReadinessStatus.Blocked,
            !hasActiveWarehouse,
            hasActiveWarehouse
                ? "At least one active warehouse is available in the selected scope."
                : "Create and activate a warehouse before enabling inbound or outbound execution.",
            hasActiveWarehouse
                ? "يوجد مستودع نشط واحد على الأقل ضمن النطاق المحدد."
                : "أنشئ مستودعاً وفعّله قبل تفعيل تنفيذ الوارد أو الصادر.",
            "/Warehouses",
            ["warehouse-master", "warehouse-scope"]));

        if (warehouses.Count == 0)
        {
            checks.Add(CreateExecutionCheck(
                "inbound-execution",
                "Inbound execution",
                "تنفيذ الوارد",
                "No active warehouse is available for inbound execution.",
                "لا يوجد مستودع نشط لتنفيذ عمليات الوارد.",
                ["active-warehouse", "operational-locations"],
                isReady: false));
            checks.Add(CreateExecutionCheck(
                "outbound-execution",
                "Outbound execution",
                "تنفيذ الصادر",
                "No active warehouse is available for outbound execution.",
                "لا يوجد مستودع نشط لتنفيذ عمليات الصادر.",
                ["active-warehouse", "operational-locations"],
                isReady: false));
        }
        else
        {
            foreach (var warehouse in warehouses)
            {
                configuredRoles.TryGetValue(warehouse.Id, out var roles);
                roles ??= [];
                var missingInbound = InboundRoles.Except(roles).ToArray();
                var missingOutbound = OutboundRoles.Except(roles).ToArray();
                checks.Add(CreateExecutionCheck(
                    $"inbound-execution-{warehouse.Id}",
                    $"Inbound execution ({warehouse.Code})",
                    $"تنفيذ الوارد ({warehouse.Code})",
                    missingInbound.Length == 0
                        ? $"Inbound operational locations are configured for {warehouse.Code}."
                        : $"Inbound execution is blocked for {warehouse.Code}; missing: {FormatRoles(missingInbound)}.",
                    missingInbound.Length == 0
                        ? $"تم تهيئة مواقع التشغيل للوارد في {warehouse.Code}."
                        : $"تنفيذ الوارد متوقف في {warehouse.Code}؛ المواقع الناقصة: {FormatRolesArabic(missingInbound)}.",
                    ["active-warehouse", "operational-locations"],
                    missingInbound.Length == 0));
                checks.Add(CreateExecutionCheck(
                    $"outbound-execution-{warehouse.Id}",
                    $"Outbound execution ({warehouse.Code})",
                    $"تنفيذ الصادر ({warehouse.Code})",
                    missingOutbound.Length == 0
                        ? $"Outbound operational locations are configured for {warehouse.Code}."
                        : $"Outbound execution is blocked for {warehouse.Code}; missing: {FormatRoles(missingOutbound)}.",
                    missingOutbound.Length == 0
                        ? $"تم تهيئة مواقع التشغيل للصادر في {warehouse.Code}."
                        : $"تنفيذ الصادر متوقف في {warehouse.Code}؛ المواقع الناقصة: {FormatRolesArabic(missingOutbound)}.",
                    ["active-warehouse", "operational-locations"],
                    missingOutbound.Length == 0));

                if (!warehouse.WorkflowEnabled && missingInbound.Length == 0 && missingOutbound.Length == 0)
                {
                    checks.Add(new AdministrationReadinessCheckDto(
                        $"workflow-disabled-{warehouse.Id}",
                        "organization.warehouses",
                        $"Workflow activation ({warehouse.Code})",
                        $"تفعيل سير العمل ({warehouse.Code})",
                        AdministrationReadinessStatus.Warning,
                        false,
                        $"The warehouse is configured but workflow execution is still disabled for {warehouse.Code}.",
                        $"المستودع مهيأ، لكن تنفيذ سير العمل ما زال معطلاً في {warehouse.Code}.",
                        $"/Warehouses/Details/{warehouse.Id}",
                        ["warehouse-master", "workflow-activation"]));
                }
            }
        }

        var auditStoreReady = true;
        try
        {
            await context.AuditEntries
                .AsNoTracking()
                .Select(entry => entry.Id)
                .Take(1)
                .ToListAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            auditStoreReady = false;
        }

        checks.Add(new AdministrationReadinessCheckDto(
            "audit-store",
            "platform.audit",
            "Audit history",
            "سجل التدقيق",
            auditStoreReady
                ? AdministrationReadinessStatus.Ready
                : AdministrationReadinessStatus.Blocked,
            !auditStoreReady,
            auditStoreReady
                ? "Immutable audit storage is available for administration changes."
                : "The audit store is unavailable; administration changes must not be activated.",
            auditStoreReady
                ? "تخزين سجل التدقيق غير القابل للتعديل متاح لتغييرات الإدارة."
                : "مخزن التدقيق غير متاح؛ يجب عدم تفعيل تغييرات الإدارة.",
            "/Audit",
            ["immutable-audit", "database"]));

        var retentionConfigured = await context.RetentionPolicies
            .AsNoTracking()
            .AnyAsync(policy => policy.Enabled, cancellationToken);
        checks.Add(new AdministrationReadinessCheckDto(
            "retention-policy",
            "platform.retention",
            "Retention policy",
            "سياسة الاحتفاظ",
            retentionConfigured
                ? AdministrationReadinessStatus.Ready
                : AdministrationReadinessStatus.Warning,
            false,
            retentionConfigured
                ? "At least one enabled retention policy is registered."
                : "No enabled retention policy is registered; review retention before production operations.",
            retentionConfigured
                ? "تم تسجيل سياسة احتفاظ مفعلة واحدة على الأقل."
                : "لا توجد سياسة احتفاظ مفعلة؛ راجع الاحتفاظ قبل تشغيل الإنتاج.",
            "/api/retention",
            ["retention-policy", "backup-gate"]));

        checks.Add(new AdministrationReadinessCheckDto(
            "deployment-health",
            "platform.health",
            "Deployment health",
            "صحة النشر",
            AdministrationReadinessStatus.Warning,
            false,
            "Database, storage, background-job, backup, and critical configuration health are reported by /health/ready.",
            "يتم الإبلاغ عن صحة قاعدة البيانات والتخزين والمهام الخلفية والنسخ الاحتياطي والإعدادات الحرجة عبر /health/ready.",
            "/health/ready",
            ["database", "storage", "background-jobs", "backup"]));

        var activeConnectorInstances = await context.ConnectorInstances
            .AsNoTracking()
            .Where(instance => instance.Status == WmsConnectorStatuses.Active)
            .ToListAsync(cancellationToken);
        activeConnectorInstances = activeConnectorInstances
            .Where(instance => scope.HasGlobalAccess ||
                ReadWarehouseIds(instance.AllowedWarehouseIdsJson).Any(warehouseIds.Contains))
            .ToList();
        var operationalConnectorTypes = (connectorAdapters ?? [])
            .Where(adapter => adapter.ImplementationStatus == WmsConnectorImplementationStatuses.Implemented)
            .Select(adapter => adapter.ConnectorType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingTransportCount = activeConnectorInstances.Count(instance =>
            !operationalConnectorTypes.Contains(instance.ConnectorType));
        var unverifiedCount = activeConnectorInstances.Count(instance =>
            operationalConnectorTypes.Contains(instance.ConnectorType) &&
            (instance.LastHealthCheckAtUtc is null ||
             instance.HealthStatus != WmsConnectorHealthStatuses.Healthy));
        var connectorStatus = activeConnectorInstances.Count == 0 ||
                              missingTransportCount > 0 ||
                              unverifiedCount > 0
            ? AdministrationReadinessStatus.Warning
            : AdministrationReadinessStatus.Ready;
        var connectorDetail = activeConnectorInstances.Count == 0
            ? "No active production connector is configured. Published connector types are contract-only, and reference fixtures are excluded from operational readiness. Warehouse workflows remain available."
            : missingTransportCount > 0
                ? $"{missingTransportCount} active connector instance(s) have no implemented provider transport. They cannot connect or synchronize."
                : unverifiedCount > 0
                    ? $"{unverifiedCount} active connector instance(s) have not passed a provider connection check."
                    : "Every active connector has an implemented transport and a successful connection check; partner acceptance remains a separate gate.";
        checks.Add(new AdministrationReadinessCheckDto(
            "connector-transports",
            "organization.integrations",
            "Connector transports",
            "وسائل نقل الموصلات",
            connectorStatus,
            false,
            connectorDetail,
            "تعرض هذه الحالة التنفيذ المحلي للموصلات والتحقق من الاتصال؛ قبول الشريك الخارجي يظل خطوة مستقلة.",
            "/api/connectors/capabilities",
            ["provider-transport", "credential-resolution", "live-acceptance"]));

        return Result.Success(new AdministrationReadinessDto(
            clock.UtcNow,
            warehouseId,
            checks));
    }

    public async Task<Result<AuditPage>> GetHistoryAsync(
        AdministrationHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var authorization = await AuthorizeAsync(query.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<AuditPage>();
        }

        return await auditQueryService.SearchAsync(
            new AuditQuery(
                query.FromUtc,
                query.ToUtc,
                query.UserId,
                query.WarehouseId,
                query.Action,
                query.EntityType,
                Math.Max(query.Page, 1),
                Math.Clamp(query.PageSize, 1, 200)),
            cancellationToken);
    }

    private async Task<Result> AuthorizeAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default) =>
        await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AccessManage,
            warehouseId,
            cancellationToken);

    private static AdministrationReadinessCheckDto CreateExecutionCheck(
        string key,
        string title,
        string titleArabic,
        string detail,
        string detailArabic,
        IReadOnlyList<string> dependencies,
        bool isReady) =>
        new(
            key,
            key.StartsWith("inbound", StringComparison.Ordinal)
                ? "inbound-execution"
                : "outbound-execution",
            title,
            titleArabic,
            isReady
                ? AdministrationReadinessStatus.Ready
                : AdministrationReadinessStatus.Blocked,
            !isReady,
            detail,
            detailArabic,
            "/Warehouses",
            dependencies);

    private static string FormatRoles(IEnumerable<WarehouseOperationalLocationRole> roles) =>
        string.Join(", ", roles.Select(role => role.ToString()));

    private static string FormatRolesArabic(IEnumerable<WarehouseOperationalLocationRole> roles) =>
        string.Join(", ", roles.Select(role => role switch
        {
            WarehouseOperationalLocationRole.Receiving => "الاستلام",
            WarehouseOperationalLocationRole.Staging => "التجهيز المرحلي",
            WarehouseOperationalLocationRole.Storage => "التخزين",
            WarehouseOperationalLocationRole.Packing => "التعبئة",
            WarehouseOperationalLocationRole.Shipping => "الشحن",
            WarehouseOperationalLocationRole.Quarantine => "الحجر",
            WarehouseOperationalLocationRole.Damaged => "التالف",
            WarehouseOperationalLocationRole.Returns => "المرتجعات",
            WarehouseOperationalLocationRole.Transit => "العبور",
            _ => role.ToString()
        }));

    private static int[] ReadWarehouseIds(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<int[]>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private sealed record WarehouseReadinessRow(int Id, string Code, bool WorkflowEnabled);

    private sealed record WarehouseRoleRow(int WarehouseId, WarehouseOperationalLocationRole Role);
}
