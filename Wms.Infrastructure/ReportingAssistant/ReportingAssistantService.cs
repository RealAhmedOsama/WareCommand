using System.Globalization;
using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Dashboard;
using Wms.Application.Identity;
using Wms.Application.Inbound;
using Wms.Application.Inventory;
using Wms.Application.Outbound;
using Wms.Application.Receiving;
using Wms.Application.Reporting;
using Wms.Application.ReportingAssistant;
using Wms.Application.SalesOrders;
using Wms.Application.UseCases.Reports;

namespace Wms.Infrastructure.ReportingAssistant;

/// <summary>
/// Executes the deterministic reporting plan against the existing authorized
/// read services. It has no SQL, model-provider, or command-service path.
/// </summary>
public sealed class ReportingAssistantService(
    IWarehouseAccessService warehouseAccessService,
    IInventoryInquiryService inventoryInquiryService,
    IReportQueryService reportQueryService,
    IReceiptService receiptService,
    ISalesOrderService salesOrderService,
    IInboundExceptionService inboundExceptionService,
    IOutboundExceptionService outboundExceptionService,
    IDashboardReadService dashboardReadService,
    IClock clock,
    ILogger<ReportingAssistantService> logger) : IReportingAssistantService
{
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(8);

    public Task<Result<ReportingAssistantPlan>> PlanAsync(
        ReportingAssistantRequest request,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReportingAssistantPolicy.CreatePlan(request, nowUtc));
    }

    public async Task<Result<ReportingAssistantAnswer>> ExecuteAsync(
        ReportingAssistantQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var timeoutSource = new CancellationTokenSource(ExecutionTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        var executionToken = linkedSource.Token;
        var generatedAtUtc = clock.UtcNow.ToUniversalTime();

        try
        {
            var reportAuthorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.ReportsRead,
                request.WarehouseId,
                executionToken);
            if (reportAuthorization.IsFailure)
            {
                return reportAuthorization.ToFailure<ReportingAssistantAnswer>();
            }

            var scope = await warehouseAccessService.GetScopeAsync(executionToken);
            var allowedWarehouses = scope.HasGlobalAccess
                ? request.WarehouseId is { } requestedWarehouse ? new HashSet<int> { requestedWarehouse } : new HashSet<int>()
                : new HashSet<int>(scope.WarehouseIds);
            var permissionCodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var permission in ReportingAssistantPolicy.RequiredPermissionCatalog)
            {
                if (await warehouseAccessService.HasPermissionAsync(permission, executionToken))
                {
                    permissionCodes.Add(permission);
                }
            }

            var internalRequest = new ReportingAssistantRequest(
                request.Question,
                request.Locale,
                request.WarehouseId,
                allowedWarehouses,
                permissionCodes,
                request.MaximumRows);
            var planResult = ReportingAssistantPolicy.CreatePlan(internalRequest, generatedAtUtc);
            if (planResult.IsFailure)
            {
                var error = planResult.FirstError!;
                if (error.Code is "reporting_assistant.intent_unsupported" or
                    "reporting_assistant.read_only" or
                    "reporting_assistant.query_language_unsupported")
                {
                    return Result.Success(CreateAnswer(
                        ReportingAssistantAnswerStatus.Unsupported,
                        request.Locale,
                        request.WarehouseId,
                        generatedAtUtc,
                        generatedAtUtc,
                        error.Code,
                        Localized(request.Locale, "The question is outside the supported read-only reports.", "السؤال خارج نطاق التقارير المدعومة للقراءة فقط.")));
                }

                return planResult.ToFailure<ReportingAssistantAnswer>();
            }

            var plan = planResult.Value;
            foreach (var permission in plan.RequiredPermissions)
            {
                var authorization = await warehouseAccessService.AuthorizeAsync(
                    permission,
                    plan.WarehouseId,
                    executionToken);
                if (authorization.IsFailure)
                {
                    return authorization.ToFailure<ReportingAssistantAnswer>();
                }
            }
            executionToken.ThrowIfCancellationRequested();

            if (plan.Status == ReportingAssistantPlanStatus.NeedsClarification)
            {
                return Result.Success(new ReportingAssistantAnswer(
                    plan.ClarificationQuestion ?? Localized(
                        plan.Locale,
                        "Please clarify which report you need.",
                        "يرجى تحديد التقرير المطلوب."),
                    [],
                    plan,
                    generatedAtUtc,
                    generatedAtUtc,
                    ReportingAssistantAnswerStatus.NeedsClarification,
                    [],
                    plan.Filters,
                    [Localized(
                        plan.Locale,
                        "Clarification is stateless. Submit the complete question again; access is checked again on every request.",
                        "طلب التوضيح لا يحفظ محادثة. أرسل السؤال كاملاً مجدداً، ويعاد فحص الصلاحيات مع كل طلب.") ]));
            }

            var answer = await ExecutePlanAsync(plan, generatedAtUtc, executionToken);
            executionToken.ThrowIfCancellationRequested();
            logger.LogInformation(
                "Reporting assistant executed tool {ToolName} for warehouse {WarehouseId} with fingerprint {QuestionFingerprint} and {RowCount} rows",
                plan.ToolName,
                plan.WarehouseId,
                plan.QuestionFingerprint,
                answer.Data?.Count ?? 0);
            return Result.Success(answer);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Result.Success(CreateAnswer(
                ReportingAssistantAnswerStatus.TimedOut,
                request.Locale,
                request.WarehouseId,
                generatedAtUtc,
                clock.UtcNow.ToUniversalTime(),
                "reporting_assistant.timeout",
                Localized(request.Locale, "The report took too long to complete. Please try again.", "استغرق التقرير وقتاً طويلاً. يرجى المحاولة مرة أخرى.")));
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Reporting assistant request failed with {ExceptionType}",
                exception.GetType().Name);
            return Result.Success(CreateAnswer(
                ReportingAssistantAnswerStatus.Unavailable,
                request.Locale,
                request.WarehouseId,
                generatedAtUtc,
                clock.UtcNow.ToUniversalTime(),
                "reporting_assistant.unavailable",
                Localized(request.Locale, "The report is temporarily unavailable. Please try again.", "التقرير غير متاح مؤقتاً. يرجى المحاولة مرة أخرى.")));
        }
    }

    private async Task<ReportingAssistantAnswer> ExecutePlanAsync(
        ReportingAssistantPlan plan,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        return plan.QueryKind switch
        {
            ReportingAssistantQueryKind.InventoryBalance => await ExecuteInventoryAsync(plan, generatedAtUtc, cancellationToken),
            ReportingAssistantQueryKind.MovementLedger => await ExecuteMovementAsync(plan, generatedAtUtc, cancellationToken),
            ReportingAssistantQueryKind.ReceivingStatus => await ExecuteReceivingAsync(plan, generatedAtUtc, cancellationToken),
            ReportingAssistantQueryKind.OutboundStatus => await ExecuteOutboundAsync(plan, generatedAtUtc, cancellationToken),
            ReportingAssistantQueryKind.ExceptionSummary => await ExecuteExceptionsAsync(plan, generatedAtUtc, cancellationToken),
            ReportingAssistantQueryKind.WarehouseKpi => await ExecuteKpisAsync(plan, generatedAtUtc, cancellationToken),
            _ => CreateAnswer(
                ReportingAssistantAnswerStatus.Unsupported,
                plan.Locale,
                plan.WarehouseId,
                generatedAtUtc,
                generatedAtUtc,
                "reporting_assistant.tool_unsupported",
                Localized(plan.Locale, "The requested report tool is not supported.", "أداة التقرير المطلوبة غير مدعومة."),
                plan)
        };
    }

    private async Task<ReportingAssistantAnswer> ExecuteInventoryAsync(
        ReportingAssistantPlan plan,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await inventoryInquiryService.QueryAsync(
            new InventoryInquiryQuery(
                WarehouseId: plan.WarehouseId,
                Page: 1,
                PageSize: plan.MaximumRows),
            cancellationToken);
        if (result.IsFailure)
        {
            return FromSourceFailure(plan, generatedAtUtc, result.ErrorCode);
        }

        var cutoff = clock.UtcNow.ToUniversalTime();
        var rows = result.Value.Items.Select(item => Row(
            $"stock:{item.Id}",
            cutoff,
            Text("itemSku", item.ItemSku),
            Text("itemName", item.ItemName),
            Text("locationCode", item.LocationCode),
            Number("availableQuantity", item.AvailableQuantity, "base unit"),
            Number("reservedQuantity", item.QuantityReserved, "base unit"))).ToArray();
        return BuildDataAnswer(plan, generatedAtUtc, cutoff, rows, result.Value.TotalCount,
            Localized(plan.Locale, "Current available inventory.", "المخزون المتاح حالياً."),
            [Localized(
                plan.Locale,
                "Inventory quantities are reported in canonical base units.",
                "تُعرض كميات المخزون بوحدات القياس الأساسية المعتمدة.")]);
    }

    private async Task<ReportingAssistantAnswer> ExecuteMovementAsync(
        ReportingAssistantPlan plan,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await reportQueryService.QueryMovementLedgerAsync(
            new MovementLedgerQuery(
                WarehouseId: plan.WarehouseId,
                Sort: MovementReportSort.Timestamp,
                Descending: true,
                Page: 1,
                PageSize: plan.MaximumRows),
            cancellationToken);
        if (result.IsFailure)
        {
            return FromSourceFailure(plan, generatedAtUtc, result.ErrorCode);
        }

        var cutoff = result.Value.Metadata.DataCutoffUtc;
        var rows = result.Value.Items.Select(item => Row(
            $"movement:{item.Id}",
            cutoff,
            Text("movementType", item.Type),
            Text("itemSku", item.ItemSku),
            Number("quantity", item.DisplayQuantity, item.DisplayUnitOfMeasure),
            Text("timestampUtc", new DateTimeOffset(DateTime.SpecifyKind(item.Timestamp, DateTimeKind.Utc)).ToString("O", CultureInfo.InvariantCulture)),
            Text("referenceNumber", item.ReferenceNumber))).ToArray();
        return BuildDataAnswer(plan, generatedAtUtc, cutoff, rows, result.Value.TotalCount,
            Localized(plan.Locale, "Recent inventory movements.", "أحدث حركات المخزون."), []);
    }

    private async Task<ReportingAssistantAnswer> ExecuteReceivingAsync(
        ReportingAssistantPlan plan,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await receiptService.ListAsync(
            new ReceiptListQuery(
                WarehouseId: plan.WarehouseId,
                SortBy: ReceiptSortField.ReceivedAtUtc,
                Descending: true,
                Page: 1,
                PageSize: plan.MaximumRows),
            cancellationToken);
        if (result.IsFailure)
        {
            return FromSourceFailure(plan, generatedAtUtc, result.ErrorCode);
        }

        var cutoff = clock.UtcNow.ToUniversalTime();
        var rows = result.Value.Receipts.Select(receipt => Row(
            $"receipt:{receipt.Id}",
            cutoff,
            Text("documentNumber", receipt.DocumentNumber),
            Text("status", receipt.Status.ToString()),
            Text("warehouseCode", receipt.WarehouseCode),
            Text("receivedAtUtc", receipt.ReceivedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)))).ToArray();
        return BuildDataAnswer(plan, generatedAtUtc, cutoff, rows, result.Value.TotalCount,
            Localized(plan.Locale, "Receiving document status.", "حالة مستندات الاستلام."),
            [Localized(
                plan.Locale,
                "The receipt source does not expose a database snapshot token; the cutoff is the completed read time.",
                "لا يوفر مصدر الاستلام لقطة ثابتة لقاعدة البيانات؛ يعكس الوقت المرجعي اكتمال القراءة.")]);
    }

    private async Task<ReportingAssistantAnswer> ExecuteOutboundAsync(
        ReportingAssistantPlan plan,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await salesOrderService.ListAsync(
            new SalesOrderListQuery(
                WarehouseId: plan.WarehouseId,
                SortBy: SalesOrderSortField.OrderDate,
                Descending: true,
                Page: 1,
                PageSize: plan.MaximumRows),
            cancellationToken);
        if (result.IsFailure)
        {
            return FromSourceFailure(plan, generatedAtUtc, result.ErrorCode);
        }

        var cutoff = clock.UtcNow.ToUniversalTime();
        var rows = result.Value.SalesOrders.Select(order => Row(
            $"sales-order:{order.Id}",
            cutoff,
            Text("documentNumber", order.DocumentNumber),
            Text("status", order.Status.ToString()),
            Text("warehouseCode", order.WarehouseCode),
            Text("orderDate", order.OrderDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Text("requestedShipDate", order.RequestedShipDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))).ToArray();
        return BuildDataAnswer(plan, generatedAtUtc, cutoff, rows, result.Value.TotalCount,
            Localized(plan.Locale, "Outbound sales order status.", "حالة طلبات الشحن."),
            [Localized(
                plan.Locale,
                "The order source does not expose a database snapshot token; the cutoff is the completed read time.",
                "لا يوفر مصدر الطلبات لقطة ثابتة لقاعدة البيانات؛ يعكس الوقت المرجعي اكتمال القراءة.")]);
    }

    private async Task<ReportingAssistantAnswer> ExecuteExceptionsAsync(
        ReportingAssistantPlan plan,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow.ToUniversalTime();
        var inbound = await inboundExceptionService.ListAsync(
            new InboundExceptionQuery(
                WarehouseId: plan.WarehouseId,
                IncludeClosed: false,
                AsOfUtc: cutoff.UtcDateTime,
                Page: 1,
                PageSize: plan.MaximumRows),
            cancellationToken);
        if (inbound.IsFailure)
        {
            return FromSourceFailure(plan, generatedAtUtc, inbound.ErrorCode);
        }

        var outbound = await outboundExceptionService.ListAsync(
            new OutboundExceptionQuery(
                WarehouseId: plan.WarehouseId,
                IncludeClosed: false,
                AsOfUtc: cutoff.UtcDateTime,
                Page: 1,
                PageSize: plan.MaximumRows),
            cancellationToken);
        if (outbound.IsFailure)
        {
            return FromSourceFailure(plan, generatedAtUtc, outbound.ErrorCode);
        }

        var rows = inbound.Value.Items.Select(exception => Row(
                $"inbound-exception:{exception.Id}",
                cutoff,
                Text("exceptionNumber", exception.ExceptionNumber),
                Text("code", exception.Code.ToString()),
                Text("severity", exception.Severity.ToString()),
                Text("status", exception.Status.ToString()),
                Text("overdue", exception.IsOverdue.ToString(CultureInfo.InvariantCulture)))
            ).Concat(outbound.Value.Items.Select(exception => Row(
                $"outbound-exception:{exception.Id}",
                cutoff,
                Text("exceptionNumber", exception.ExceptionNumber),
                Text("code", exception.Code.ToString()),
                Text("severity", exception.Severity.ToString()),
                Text("status", exception.Status.ToString()),
                Text("overdue", exception.IsOverdue.ToString(CultureInfo.InvariantCulture)))))
            .OrderBy(row => row.Reference, StringComparer.Ordinal)
            .ToArray();
        return BuildDataAnswer(
            plan,
            generatedAtUtc,
            cutoff,
            rows.Take(plan.MaximumRows).ToArray(),
            inbound.Value.TotalCount + outbound.Value.TotalCount,
            Localized(plan.Locale, "Open inbound and outbound exceptions.", "الاستثناءات المفتوحة للوارد والصادر."),
            [Localized(
                plan.Locale,
                "Exception details omit free-text reasons and notes.",
                "تفاصيل الاستثناءات لا تتضمن الأسباب والملاحظات النصية الحرة.")]);
    }

    private async Task<ReportingAssistantAnswer> ExecuteKpisAsync(
        ReportingAssistantPlan plan,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        var result = await dashboardReadService.GetAsync(plan.WarehouseId, cancellationToken);
        if (result.IsFailure)
        {
            return FromSourceFailure(plan, generatedAtUtc, result.ErrorCode);
        }

        var snapshot = result.Value;
        var cutoff = snapshot.GeneratedAtUtc;
        var reference = $"dashboard:{snapshot.WarehouseId?.ToString(CultureInfo.InvariantCulture) ?? "authorized-scope"}:{cutoff:O}";
        var fields = new List<ReportingAssistantField>();
        var limitations = new List<string>();
        if (snapshot.Catalog.Status == DashboardSectionStatus.Available && snapshot.Catalog.Data is { } catalog)
        {
            fields.Add(Number("totalItems", catalog.TotalItems, "items"));
            fields.Add(Number("activeItems", catalog.ActiveItems, "items"));
        }
        else
        {
            limitations.Add(Localized(
                plan.Locale,
                "Item catalog metrics are unavailable for this principal or data source.",
                "مؤشرات دليل الأصناف غير متاحة لهذا المستخدم أو مصدر البيانات."));
        }

        if (snapshot.Inventory.Status == DashboardSectionStatus.Available && snapshot.Inventory.Data is { } inventory)
        {
            fields.Add(Number("stockKeepingUnits", inventory.StockKeepingUnits, "SKUs"));
            fields.Add(Number("onHandQuantity", inventory.OnHandQuantity, "base unit"));
            fields.Add(Number("reservedQuantity", inventory.ReservedQuantity, "base unit"));
            fields.Add(Number("availableQuantity", inventory.AvailableQuantity, "base unit"));
            fields.Add(Number("heldQuantity", inventory.HeldQuantity, "base unit"));
            fields.Add(Number("damagedQuantity", inventory.DamagedQuantity, "base unit"));
            fields.Add(Number("expiredQuantity", inventory.ExpiredQuantity, "base unit"));
            fields.Add(Number("expiringQuantity", inventory.ExpiringQuantity, "base unit"));
            fields.Add(Number("stockLocations", inventory.StockLocations, "locations"));
        }
        else
        {
            limitations.Add(Localized(
                plan.Locale,
                "Inventory KPI metrics are unavailable for this principal or data source.",
                "مؤشرات المخزون غير متاحة لهذا المستخدم أو مصدر البيانات."));
        }

        var data = fields.Count == 0
            ? []
            : new[] { new ReportingAssistantRow(reference, fields, cutoff) };
        var citations = fields.Select(field => new ReportingAssistantCitation(
            plan.ToolName,
            reference,
            field.Name,
            cutoff)).ToArray();
        var status = fields.Count == 0
            ? ReportingAssistantAnswerStatus.Unavailable
            : ReportingAssistantAnswerStatus.Answered;
        return new ReportingAssistantAnswer(
            Localized(plan.Locale, "Warehouse KPI snapshot.", "ملخص مؤشرات المستودع."),
            citations,
            plan,
            generatedAtUtc,
            cutoff,
            status,
            data,
            plan.Filters,
            limitations,
            fields.Count == 0 ? "reporting_assistant.kpi_unavailable" : null);
    }

    private static ReportingAssistantAnswer BuildDataAnswer(
        ReportingAssistantPlan plan,
        DateTimeOffset generatedAtUtc,
        DateTimeOffset cutoff,
        ReportingAssistantRow[] rows,
        int totalCount,
        string summary,
        IReadOnlyList<string> limitations)
    {
        var status = totalCount == 0
            ? ReportingAssistantAnswerStatus.Empty
            : totalCount > rows.Length
                ? ReportingAssistantAnswerStatus.Truncated
                : ReportingAssistantAnswerStatus.Answered;
        var citations = rows.SelectMany(row => row.Fields.Select(field => new ReportingAssistantCitation(
            plan.ToolName,
            row.Reference,
            field.Name,
            row.DataCutoffUtc))).ToArray();
        var effectiveSummary = status switch
        {
            ReportingAssistantAnswerStatus.Empty => Localized(plan.Locale, "No matching records were found.", "لم يتم العثور على سجلات مطابقة."),
            ReportingAssistantAnswerStatus.Truncated => Localized(plan.Locale, "The result is limited to the requested row bound.", "تم تقييد النتائج بالحد الأقصى للصفوف المطلوب."),
            _ => summary
        };
        return new ReportingAssistantAnswer(
            effectiveSummary,
            citations,
            plan,
            generatedAtUtc,
            cutoff,
            status,
            rows,
            plan.Filters,
            limitations,
            status == ReportingAssistantAnswerStatus.Truncated ? "reporting_assistant.row_limit_reached" : null);
    }

    private ReportingAssistantAnswer FromSourceFailure(
        ReportingAssistantPlan plan,
        DateTimeOffset generatedAtUtc,
        string errorCode) => CreateAnswer(
        ReportingAssistantAnswerStatus.Unavailable,
        plan.Locale,
        plan.WarehouseId,
        generatedAtUtc,
        clock.UtcNow.ToUniversalTime(),
        "reporting_assistant.source_unavailable",
        Localized(plan.Locale, "The report source is temporarily unavailable.", "مصدر التقرير غير متاح مؤقتاً."),
        plan,
        errorCode);

    private static ReportingAssistantAnswer CreateAnswer(
        ReportingAssistantAnswerStatus status,
        string locale,
        int? warehouseId,
        DateTimeOffset generatedAtUtc,
        DateTimeOffset cutoff,
        string errorCode,
        string summary,
        ReportingAssistantPlan? plan = null,
        string? sourceErrorCode = null) => new(
        summary,
        [],
        plan,
        generatedAtUtc,
        cutoff,
        status,
        [],
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["warehouseId"] = warehouseId?.ToString(CultureInfo.InvariantCulture)
        },
        [],
        sourceErrorCode ?? errorCode);

    private static ReportingAssistantRow Row(
        string reference,
        DateTimeOffset cutoff,
        params ReportingAssistantField[] fields) => new(reference, fields, cutoff);

    private static ReportingAssistantField Text(string name, string? value) => new(name, TextValue: value);

    private static ReportingAssistantField Number(string name, decimal value, string unit) =>
        new(name, NumberValue: value, Unit: unit);

    private static string Localized(string locale, string english, string arabic) =>
        locale.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? arabic : english;
}
