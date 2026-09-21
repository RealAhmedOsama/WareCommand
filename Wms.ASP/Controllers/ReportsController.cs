using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Reporting;
using Wms.Application.Settings;
using Wms.ASP.Extensions;
using Wms.ASP.Models;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.ReportsRead)]
public sealed class ReportsController(
    IReportQueryService reportQueryService,
    IWarehouseAccessService warehouseAccessService,
    IWmsSettingsService settingsService,
    ILogger<ReportsController> logger) : Controller
{
    [HttpGet]
    [EnableRateLimiting(WmsRateLimitPolicies.Report)]
    public async Task<IActionResult> Index(
        MovementReportViewModel model,
        CancellationToken cancellationToken = default)
    {
        model.Page = Math.Max(1, model.Page);
        model.PageSize = Math.Clamp(model.PageSize, 1, 200);
        model.Warehouses = await warehouseAccessService.GetAccessibleWarehousesAsync(
            WmsPermissions.ReportsRead,
            cancellationToken);

        var query = ToQuery(model);
        if (model.GroupBy == MovementReportGroupBy.None)
        {
            var result = await reportQueryService.QueryMovementLedgerAsync(
                query,
                cancellationToken);
            if (result.IsFailure)
            {
                return RenderFailure(model, result.FirstError!);
            }

            model.Rows = result.Value.Items;
            model.Page = result.Value.Page;
            model.PageSize = result.Value.PageSize;
            model.TotalCount = result.Value.TotalCount;
            model.TotalPages = result.Value.TotalPages;
            model.Metadata = result.Value.Metadata;
        }
        else
        {
            var result = await reportQueryService.GroupMovementLedgerAsync(
                query,
                model.GroupBy,
                cancellationToken);
            if (result.IsFailure)
            {
                return RenderFailure(model, result.FirstError!);
            }

            model.Groups = result.Value.Groups;
            model.Page = result.Value.Page;
            model.PageSize = result.Value.PageSize;
            model.TotalCount = result.Value.TotalGroups;
            model.TotalPages = result.Value.TotalPages;
            model.Metadata = result.Value.Metadata;
        }

        return View(model);
    }

    [HttpGet]
    [EnableRateLimiting(WmsRateLimitPolicies.Report)]
    public async Task<IActionResult> Export(
        MovementReportViewModel model,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.GetAsync(cancellationToken: cancellationToken);
        var maximumRows = settings.IsSuccess
            ? Math.Max(1, settings.Value.Values.Reports.MaximumRows)
            : WmsSettingsDefaults.Create().Reports.MaximumRows;
        var pageSize = Math.Min(maximumRows, 200);
        var query = ToQuery(model) with { Page = 1, PageSize = pageSize };
        var firstPage = await reportQueryService.QueryMovementLedgerAsync(
            query,
            cancellationToken);
        if (firstPage.IsFailure)
        {
            logger.LogWarning("Movement report export failed: {Error}", firstPage.Error);
            TempData["ErrorMessage"] = this.LocalizeError(firstPage.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = "attachment; filename=movement-ledger.csv";
        await Response.StartAsync(cancellationToken);
        await Response.Body.WriteAsync(Encoding.UTF8.GetPreamble(), cancellationToken);
        await using var writer = new StreamWriter(
            Response.Body,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            1024,
            leaveOpen: true);

        await writer.WriteLineAsync(
            "Type,SKU,Item,From Location,To Location,Quantity,Base UOM,Display Quantity,Display UOM,Lot,Serial,User,Reference,Notes,Timestamp UTC");
        var written = await WriteRowsAsync(writer, firstPage.Value.Items, maximumRows, cancellationToken);
        var page = firstPage.Value.Page + 1;
        while (written < maximumRows && page <= firstPage.Value.TotalPages)
        {
            var nextPage = await reportQueryService.QueryMovementLedgerAsync(
                query with { Page = page },
                cancellationToken);
            if (nextPage.IsFailure)
            {
                logger.LogError(
                    "Movement report export stopped after {WrittenRows} rows: {Error}",
                    written,
                    nextPage.Error);
                HttpContext.Abort();
                return new EmptyResult();
            }

            written += await WriteRowsAsync(
                writer,
                nextPage.Value.Items,
                maximumRows - written,
                cancellationToken);
            page++;
        }

        await writer.FlushAsync(cancellationToken);
        return new EmptyResult();
    }

    private ViewResult RenderFailure(
        MovementReportViewModel model,
        ResultError error)
    {
        logger.LogWarning("Movement report query failed: {Error}", error);
        TempData["ErrorMessage"] = this.LocalizeError(error);
        return View(model);
    }

    private static MovementLedgerQuery ToQuery(MovementReportViewModel model) =>
        new(
            ToDateOnly(model.FromDate),
            ToDateOnly(model.ToDate),
            model.WarehouseId,
            model.SearchTerm,
            model.ItemSku,
            model.LocationCode,
            model.MovementType,
            model.UserId,
            model.ReferenceNumber,
            model.LotNumber,
            model.SerialNumber,
            model.LicensePlateNumber,
            model.Sort,
            model.Descending,
            model.Page,
            model.PageSize,
            model.DisplayUnitOfMeasure);

    private static DateOnly? ToDateOnly(DateTime? value) =>
        value.HasValue
            ? DateOnly.FromDateTime(value.Value)
            : null;

    private static async Task<int> WriteRowsAsync(
        StreamWriter writer,
        IReadOnlyList<Wms.Application.UseCases.Reports.MovementReportDto> rows,
        int remaining,
        CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var row in rows)
        {
            if (count >= remaining)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(",",
                Csv(row.Type),
                Csv(row.ItemSku),
                Csv(row.ItemName),
                Csv(row.FromLocationCode),
                Csv(row.ToLocationCode),
                Csv(row.Quantity.ToString(CultureInfo.InvariantCulture)),
                Csv(row.BaseUnitOfMeasure),
                Csv(row.DisplayQuantity.ToString(CultureInfo.InvariantCulture)),
                Csv(row.DisplayUnitOfMeasure),
                Csv(row.LotNumber),
                Csv(row.SerialNumber),
                Csv(row.UserId),
                Csv(row.ReferenceNumber),
                Csv(row.Notes),
                Csv(row.Timestamp.ToString("O", CultureInfo.InvariantCulture))));
            count++;
        }

        return count;
    }

    private static string Csv(string? value)
    {
        var normalized = value ?? string.Empty;
        return $"\"{normalized.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
