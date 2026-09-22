using System.Security.Cryptography;
using System.Text;
using Wms.Application.Common;
using Wms.Application.Identity;

namespace Wms.Application.ReportingAssistant;

public enum ReportingAssistantQueryKind
{
    InventoryBalance,
    MovementLedger,
    ReceivingStatus,
    OutboundStatus,
    ExceptionSummary,
    WarehouseKpi
}

public enum ReportingAssistantPlanStatus
{
    Ready,
    NeedsClarification
}

/// <summary>
/// A bounded, read-only natural-language reporting request. The planner never
/// receives a database query or a provider prompt; it emits an allow-listed
/// report tool plan for the authenticated application layer to execute.
/// </summary>
public sealed record ReportingAssistantRequest(
    string Question,
    string Locale = "en-US",
    int? WarehouseId = null,
    IReadOnlySet<int>? AllowedWarehouseIds = null,
    IReadOnlySet<string>? PermissionCodes = null,
    int MaximumRows = 50);

public sealed record ReportingAssistantPlan(
    ReportingAssistantPlanStatus Status,
    ReportingAssistantQueryKind? QueryKind,
    string ToolName,
    string Locale,
    int? WarehouseId,
    int MaximumRows,
    IReadOnlyList<string> RequiredPermissions,
    IReadOnlyDictionary<string, string?> Filters,
    string QuestionFingerprint,
    DateTimeOffset PlannedAtUtc,
    string? ClarificationQuestion);

public sealed record ReportingAssistantCitation(
    string SourceTool,
    string Reference,
    string? Field,
    DateTimeOffset DataCutoffUtc);

public sealed record ReportingAssistantAnswer(
    string Summary,
    IReadOnlyList<ReportingAssistantCitation> Citations,
    ReportingAssistantPlan Plan,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset DataCutoffUtc);

public interface IReportingAssistantService
{
    Task<Result<ReportingAssistantPlan>> PlanAsync(
        ReportingAssistantRequest request,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}

public static class ReportingAssistantPolicy
{
    public const int MaximumQuestionLength = 1_000;
    public const int MaximumRows = 200;
    public const string InventoryTool = "inventory.stock.read";
    public const string MovementTool = "reports.movements.read";
    public const string ReceivingTool = "receipts.status.read";
    public const string OutboundTool = "sales-orders.status.read";
    public const string ExceptionTool = "reports.exceptions.read";
    public const string KpiTool = "reports.kpi.read";

    private static readonly Dictionary<ReportingAssistantQueryKind, string[]> ToolNames =
        new()
        {
            [ReportingAssistantQueryKind.InventoryBalance] = [InventoryTool],
            [ReportingAssistantQueryKind.MovementLedger] = [MovementTool],
            [ReportingAssistantQueryKind.ReceivingStatus] = [ReceivingTool],
            [ReportingAssistantQueryKind.OutboundStatus] = [OutboundTool],
            [ReportingAssistantQueryKind.ExceptionSummary] = [ExceptionTool],
            [ReportingAssistantQueryKind.WarehouseKpi] = [KpiTool]
        };

    private static readonly IReadOnlyDictionary<ReportingAssistantQueryKind, string[]> Keywords =
        new Dictionary<ReportingAssistantQueryKind, string[]>
        {
            [ReportingAssistantQueryKind.InventoryBalance] =
            ["inventory", "stock", "on hand", "available", "مخزون", "رصيد"],
            [ReportingAssistantQueryKind.MovementLedger] =
            ["movement", "movements", "transaction", "ledger", "حركة", "حركات"],
            [ReportingAssistantQueryKind.ReceivingStatus] =
            ["receiving", "receipt", "receipts", "inbound", "استلام", "وارد"],
            [ReportingAssistantQueryKind.OutboundStatus] =
            ["order", "orders", "shipment", "shipments", "outbound", "picking", "طلب", "شحنة", "شحن"],
            [ReportingAssistantQueryKind.ExceptionSummary] =
            ["exception", "exceptions", "shortage", "overdue", "blocked", "استثناء", "عجز", "متأخر"],
            [ReportingAssistantQueryKind.WarehouseKpi] =
            ["kpi", "performance", "throughput", "dashboard", "مؤشر", "أداء"]
        };

    private static readonly Dictionary<ReportingAssistantQueryKind, string[]> RequiredPermissions =
        new()
        {
            [ReportingAssistantQueryKind.InventoryBalance] =
            [WmsPermissions.ReportsRead, WmsPermissions.InventoryRead],
            [ReportingAssistantQueryKind.MovementLedger] = [WmsPermissions.ReportsRead],
            [ReportingAssistantQueryKind.ReceivingStatus] =
            [WmsPermissions.ReportsRead, WmsPermissions.ReceiptsRead],
            [ReportingAssistantQueryKind.OutboundStatus] =
            [WmsPermissions.ReportsRead, WmsPermissions.SalesOrdersRead],
            [ReportingAssistantQueryKind.ExceptionSummary] = [WmsPermissions.ReportsRead],
            [ReportingAssistantQueryKind.WarehouseKpi] = [WmsPermissions.ReportsRead]
        };

    private static readonly string[] MutationTerms =
    [
        "delete", "remove", "update", "adjust", "create", "cancel", "transfer", "approve",
        "execute", "reverse", "set stock", "change quantity", "حذف", "عدل", "اضبط", "انشئ",
        "إلغاء", "نقل", "اعتمد", "نفذ"
    ];

    public static Result Validate(ReportingAssistantRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var question = request.Question?.Trim() ?? string.Empty;
        if (question.Length is < 3 or > MaximumQuestionLength)
        {
            return Result.Failure(WmsErrors.Validation(
                "reporting_assistant.question_invalid",
                $"The question must contain between 3 and {MaximumQuestionLength} characters."));
        }

        if (!IsSupportedLocale(request.Locale))
        {
            return Result.Failure(WmsErrors.Validation(
                "reporting_assistant.locale_invalid",
                "The reporting assistant supports English and Arabic locales."));
        }

        if (request.MaximumRows is < 1 or > MaximumRows)
        {
            return Result.Failure(WmsErrors.Validation(
                "reporting_assistant.row_limit_invalid",
                $"The requested row limit must be between 1 and {MaximumRows}."));
        }

        if (ContainsMutationTerm(question))
        {
            return Result.Failure(WmsErrors.Forbidden(
                "reporting_assistant.read_only",
                "The reporting assistant only supports read-only warehouse questions."));
        }

        var matchedKinds = FindKinds(question);
        if (matchedKinds.Length == 0)
        {
            return Result.Failure(WmsErrors.Validation(
                "reporting_assistant.intent_unsupported",
                "The question does not map to an approved warehouse reporting tool."));
        }

        if (request.WarehouseId is not null &&
            (request.AllowedWarehouseIds is null ||
             !request.AllowedWarehouseIds.Contains(request.WarehouseId.Value)))
        {
            return Result.Failure(WmsErrors.Forbidden(
                "reporting_assistant.warehouse_forbidden",
                "The requested warehouse is outside the authenticated warehouse scope."));
        }

        var permissions = request.PermissionCodes;
        if (permissions is null)
        {
            return Result.Failure(WmsErrors.Unauthorized(
                "reporting_assistant.permissions_missing",
                "An authenticated permission set is required for reporting."));
        }

        var missingPermissions = matchedKinds
            .SelectMany(kind => RequiredPermissions[kind])
            .Distinct(StringComparer.Ordinal)
            .Where(permission => !HasPermission(permissions, permission))
            .ToArray();
        if (missingPermissions.Length > 0)
        {
            return Result.Failure(WmsErrors.Forbidden(
                "reporting_assistant.permission_denied",
                "The authenticated user does not have permission for the requested report."));
        }

        return Result.Success();
    }

    public static Result<ReportingAssistantPlan> CreatePlan(
        ReportingAssistantRequest request,
        DateTimeOffset nowUtc)
    {
        var validation = Validate(request);
        if (validation.IsFailure)
        {
            return validation.ToFailure<ReportingAssistantPlan>();
        }

        var matchedKinds = FindKinds(request.Question);
        ReportingAssistantQueryKind? selectedKind = matchedKinds.Length == 1 ? matchedKinds[0] : null;
        var status = selectedKind is null
            ? ReportingAssistantPlanStatus.NeedsClarification
            : ReportingAssistantPlanStatus.Ready;
        var toolName = selectedKind is { } kind
            ? ToolNames[kind][0]
            : "reports.clarification.required";
        var permissions = selectedKind is { } selected
            ? RequiredPermissions[selected]
            : matchedKinds.SelectMany(kind => RequiredPermissions[kind])
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        var fingerprint = Fingerprint(request);
        var clarification = selectedKind is null
            ? IsArabic(request.Locale)
                ? "هل تريد تقرير المخزون أم تقرير الحركات أم حالة الطلبات؟"
                : "Do you want an inventory, movement, or order-status report?"
            : null;

        return Result.Success(new ReportingAssistantPlan(
            status,
            selectedKind,
            toolName,
            request.Locale.Trim(),
            request.WarehouseId,
            request.MaximumRows,
            permissions,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["warehouseId"] = request.WarehouseId?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            fingerprint,
            nowUtc,
            clarification));
    }

    private static ReportingAssistantQueryKind[] FindKinds(string question)
    {
        var normalized = question.Trim();
        return Keywords
            .Where(pair => pair.Value.Any(keyword =>
                normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .Select(pair => pair.Key)
            .OrderBy(kind => kind)
            .ToArray();
    }

    private static bool ContainsMutationTerm(string question) =>
        MutationTerms.Any(term =>
            question.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static bool HasPermission(IReadOnlySet<string> permissions, string requiredPermission) =>
        permissions.Contains(WmsPermissions.All) ||
        permissions.Contains(requiredPermission);

    private static bool IsSupportedLocale(string? locale) =>
        !string.IsNullOrWhiteSpace(locale) &&
        (locale.StartsWith("en", StringComparison.OrdinalIgnoreCase) ||
         locale.StartsWith("ar", StringComparison.OrdinalIgnoreCase));

    private static bool IsArabic(string locale) =>
        locale.StartsWith("ar", StringComparison.OrdinalIgnoreCase);

    private static string Fingerprint(ReportingAssistantRequest request)
    {
        var input = string.Join(
            "|",
            request.Question.Trim().ToUpperInvariant(),
            request.Locale.Trim().ToUpperInvariant(),
            request.WarehouseId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "*");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))
            .ToLowerInvariant();
    }
}
