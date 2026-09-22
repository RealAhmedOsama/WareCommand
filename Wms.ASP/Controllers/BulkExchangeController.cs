using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.BulkExchange;
using Wms.Application.Identity;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/bulk")]
[Authorize(Policy = WmsPermissions.SettingsManage)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class BulkExchangeController(
    IBulkImportService bulkImportService,
    IBulkExportService bulkExportService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("capabilities")]
    public IActionResult Capabilities() => Ok(new
    {
        formats = new[] { WmsBulkImportFormats.Csv, WmsBulkImportFormats.Excel },
        importTypes = new[]
        {
            WmsBulkImportTypes.ItemsV1,
            WmsBulkImportTypes.SuppliersV1,
            WmsBulkImportTypes.CustomersV1,
            WmsBulkImportTypes.SalesOrdersV1,
            WmsBulkImportTypes.PurchaseOrdersV1,
            WmsBulkImportTypes.AdvanceShippingNoticesV1
        },
        modes = new[] { WmsBulkImportModes.DryRun, WmsBulkImportModes.Execute }
    });

    [HttpPost("preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(
        [FromBody] PreviewRequest request,
        CancellationToken cancellationToken = default) =>
        Ok(await bulkImportService.PreviewAsync(
            new BulkImportPreviewRequest(
                request.ImportType,
                request.Source,
                request.Mapping,
                currentUser.RequireUserId(),
                request.WarehouseId,
                request.IdempotencyKey,
                request.CorrelationId),
            cancellationToken));

    [HttpPost("imports/{executionId:long}/execute")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Execute(
        long executionId,
        [FromBody] ExecuteRequest? request,
        CancellationToken cancellationToken = default) =>
        Ok(await bulkImportService.ExecuteAsync(
            new BulkImportExecuteRequest(
                executionId,
                currentUser.RequireUserId(),
                request?.DryRun ?? true),
            cancellationToken));

    [HttpGet("imports/{executionId:long}")]
    public async Task<IActionResult> Get(
        long executionId,
        CancellationToken cancellationToken = default)
    {
        var execution = await bulkImportService.GetAsync(executionId, cancellationToken);
        return execution is null ? NotFound() : Ok(execution);
    }

    [HttpPost("imports/{executionId:long}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        long executionId,
        CancellationToken cancellationToken = default) =>
        await bulkImportService.CancelAsync(
                executionId,
                currentUser.RequireUserId(),
                cancellationToken)
            ? NoContent()
            : NotFound();

    [HttpPost("export/{fileName}")]
    [ValidateAntiForgeryToken]
    public IActionResult Export(
        string fileName,
        [FromBody] BulkExportRequest request)
    {
        var document = bulkExportService.CreateCsv(fileName, request);
        return File(document.Content, document.ContentType, document.FileName);
    }

    public sealed record PreviewRequest(
        string ImportType,
        BulkImportSource Source,
        BulkImportMappingProfile Mapping,
        int? WarehouseId = null,
        string? IdempotencyKey = null,
        string? CorrelationId = null);

    public sealed record ExecuteRequest(bool DryRun = true);
}
