using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Auditing;
using Wms.Application.Identity;
using Wms.ASP.Models;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.AuditRead)]
public sealed class AuditController(
    IAuditQueryService auditQueryService,
    IWarehouseAccessService warehouseAccessService,
    ILogger<AuditController> logger) : Controller
{
    [HttpGet]
    [EnableRateLimiting(WmsRateLimitPolicies.Report)]
    public async Task<IActionResult> Index(AuditLogViewModel model, CancellationToken cancellationToken)
    {
        model.Page = Math.Max(model.Page, 1);
        model.PageSize = Math.Clamp(model.PageSize, 1, 200);
        model.Warehouses = await warehouseAccessService.GetAccessibleWarehousesAsync(
            WmsPermissions.AuditRead,
            cancellationToken);

        var result = await auditQueryService.SearchAsync(
            new AuditQuery(
                ToStartUtc(model.FromDate),
                ToEndUtc(model.ToDate),
                model.UserId,
                model.WarehouseId,
                model.Action,
                model.EntityType,
                model.Page,
                model.PageSize),
            cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Audit search was denied or failed: {Error}", result.Error);
            TempData["ErrorMessage"] = result.Error;
            return View(model);
        }

        model.Entries = result.Value.Items;
        model.Page = result.Value.Page;
        model.PageSize = result.Value.PageSize;
        model.TotalCount = result.Value.TotalCount;
        model.TotalPages = result.Value.TotalPages;
        return View(model);
    }

    [HttpGet]
    [EnableRateLimiting(WmsRateLimitPolicies.Report)]
    public async Task<IActionResult> Export(AuditLogViewModel model, CancellationToken cancellationToken)
    {
        const int pageSize = 200;
        const int maximumRows = 10_000;
        var entries = new List<AuditEntryDto>(pageSize);
        var page = 1;
        var query = BuildQuery(model, page, pageSize);

        while (entries.Count < maximumRows)
        {
            var result = await auditQueryService.SearchAsync(
                query with { Page = page },
                cancellationToken);
            if (result.IsFailure)
            {
                logger.LogWarning("Audit export was denied or failed: {Error}", result.Error);
                TempData["ErrorMessage"] = result.Error;
                return RedirectToAction(nameof(Index));
            }

            entries.AddRange(result.Value.Items);
            if (result.Value.Items.Count == 0 || page >= result.Value.TotalPages)
            {
                break;
            }

            page++;
        }

        var csv = BuildCsv(entries);
        return File(
            Encoding.UTF8.GetBytes(csv),
            "text/csv; charset=utf-8",
            "audit-export.csv");
    }

    private static AuditQuery BuildQuery(AuditLogViewModel model, int page, int pageSize) =>
        new(
            ToStartUtc(model.FromDate),
            ToEndUtc(model.ToDate),
            model.UserId,
            model.WarehouseId,
            model.Action,
            model.EntityType,
            page,
            pageSize);

    private static string BuildCsv(IEnumerable<AuditEntryDto> entries)
    {
        var builder = new StringBuilder();
        AppendCsvRow(
            builder,
            "Id",
            "OccurredAtUtc",
            "ActorUserId",
            "ActorUserName",
            "Action",
            "EntityType",
            "EntityId",
            "WarehouseId",
            "WarehouseCode",
            "CorrelationId",
            "SourceClient",
            "Succeeded",
            "Details",
            "BeforeJson",
            "AfterJson");

        foreach (var entry in entries)
        {
            AppendCsvRow(
                builder,
                entry.Id,
                entry.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture),
                entry.ActorUserId,
                entry.ActorUserName,
                entry.Action,
                entry.EntityType,
                entry.EntityId,
                entry.WarehouseId,
                entry.WarehouseCode,
                entry.CorrelationId,
                entry.SourceClient,
                entry.Succeeded,
                entry.Details,
                entry.BeforeJson,
                entry.AfterJson);
        }

        return builder.ToString();
    }

    private static void AppendCsvRow(StringBuilder builder, params object?[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            var value = Convert.ToString(values[index], CultureInfo.InvariantCulture) ?? string.Empty;
            builder.Append('"');
            builder.Append(value.Replace("\"", "\"\"", StringComparison.Ordinal));
            builder.Append('"');
        }

        builder.AppendLine();
    }

    private static DateTimeOffset? ToStartUtc(DateTime? value) =>
        value.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Utc))
            : null;

    private static DateTimeOffset? ToEndUtc(DateTime? value) =>
        value.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(
                value.Value.Date.AddDays(1).AddTicks(-1),
                DateTimeKind.Utc))
            : null;
}
