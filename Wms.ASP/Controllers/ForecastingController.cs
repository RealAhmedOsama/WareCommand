using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Forecasting;
using Wms.Application.Identity;
using Wms.Application.Jobs;
using Wms.ASP.Jobs;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/forecasting")]
[Authorize(Policy = WmsPermissions.ReportsRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class ForecastingController(
    IForecastingService forecastingService,
    IWarehouseAccessService warehouseAccessService,
    IWmsJobDispatcher jobDispatcher,
    ICurrentUser currentUser,
    IClock clock) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ForecastRunPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] int? warehouseId,
        [FromQuery] int? itemId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await forecastingService.SearchAsync(
            new ForecastingSearchQuery(warehouseId, itemId, page, pageSize),
            cancellationToken));

    [HttpGet("{runId:int}")]
    [ProducesResponseType(typeof(ForecastRunDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        int runId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await forecastingService.GetAsync(runId, cancellationToken));

    [HttpGet("{runId:int}/actuals")]
    [ProducesResponseType(typeof(ForecastActualComparisonDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Actuals(
        int runId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await forecastingService.CompareActualsAsync(runId, cancellationToken));

    [HttpGet("{runId:int}/export.csv")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Export(
        int runId,
        CancellationToken cancellationToken = default)
    {
        var result = await forecastingService.GetAsync(runId, cancellationToken);
        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        var run = result.Value;
        if (run.Points.Count > 1_000)
        {
            return Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Export limit exceeded",
                detail: "The forecast run exceeds the bounded export size.");
        }

        var csv = new StringBuilder();
        AppendCsvRow(csv,
            "warehouse_id",
            "item_id",
            "is_stale",
            "item_sku",
            "item_name",
            "base_uom",
            "period_start",
            "actual_demand",
            "forecast_quantity",
            "lower_bound",
            "upper_bound",
            "override_quantity",
            "override_version",
            "stockout_censored");
        foreach (var point in run.Points)
        {
            AppendCsvRow(csv,
                run.WarehouseId.ToString(CultureInfo.InvariantCulture),
                run.ItemId.ToString(CultureInfo.InvariantCulture),
                run.IsStale?.ToString().ToLowerInvariant() ?? string.Empty,
                SafeCsvText(run.ItemSku),
                SafeCsvText(run.ItemName),
                SafeCsvText(run.BaseUnitOfMeasure),
                point.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Format(point.ActualDemand),
                Format(point.ForecastQuantity),
                Format(point.LowerBound),
                Format(point.UpperBound),
                Format(point.OverrideQuantity),
                point.OverrideVersion?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                point.WasStockoutCensored ? "true" : "false");
        }

        return File(
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv.ToString()),
            "text/csv; charset=utf-8",
            $"forecast-run-{run.Id}.csv");
    }

    [HttpPost("recalculate")]
    [Authorize(Policy = WmsPermissions.ForecastingRecalculate)]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(typeof(ForecastRecalculationAcceptedDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Recalculate(
        [FromBody] ForecastRecalculationInput input,
        CancellationToken cancellationToken = default)
    {
        if (input.WarehouseId <= 0)
        {
            ModelState.AddModelError(nameof(input.WarehouseId), "A positive warehouse identifier is required.");
        }

        if (input.ItemId is <= 0)
        {
            ModelState.AddModelError(nameof(input.ItemId), "An item identifier must be positive when supplied.");
        }

        if (!Enum.IsDefined(input.Granularity))
        {
            ModelState.AddModelError(nameof(input.Granularity), "The forecast granularity is not supported.");
        }

        if (input.HorizonPeriods is < 1 or > ForecastBaselineEngine.MaximumHorizonPeriods)
        {
            ModelState.AddModelError(
                nameof(input.HorizonPeriods),
                $"The forecast horizon must be between 1 and {ForecastBaselineEngine.MaximumHorizonPeriods} periods.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ForecastingRecalculate,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return ToActionResult(Result.Failure<ForecastRecalculationAcceptedDto>(authorization.FirstError!));
        }

        var actorId = currentUser.RequireUserId();
        var clockNow = clock.UtcNow;
        var itemScope = input.ItemId?.ToString(CultureInfo.InvariantCulture) ?? "*";
        var idempotencyKey = string.Create(
            CultureInfo.InvariantCulture,
            $"{WmsJobNames.ForecastRecalculation}:manual:{input.WarehouseId}:{itemScope}:{input.Granularity}:{input.HorizonPeriods}:{clockNow:yyyyMMddHHmm}");
        var referenceId = string.Create(
            CultureInfo.InvariantCulture,
            $"{input.Granularity}:{input.HorizonPeriods}:{itemScope}");
        try
        {
            var jobId = jobDispatcher.Enqueue(
                WmsJobNames.ForecastRecalculation,
                idempotencyKey,
                actorId,
                currentUser.UserName,
                input.WarehouseId,
                referenceId);
            return Accepted(new ForecastRecalculationAcceptedDto(
                jobId,
                idempotencyKey,
                input.WarehouseId,
                input.ItemId,
                input.Granularity,
                input.HorizonPeriods));
        }
        catch (InvalidOperationException)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Forecast jobs unavailable",
                detail: "Durable background jobs are disabled for this host.");
        }
    }

    [HttpPost("{runId:int}/overrides")]
    [Authorize(Policy = WmsPermissions.ForecastingOverride)]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(typeof(ForecastOverrideDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateOverride(
        int runId,
        [FromBody] ForecastOverrideInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await forecastingService.CreateOverrideAsync(
            runId,
            input,
            currentUser.RequireUserId(),
            cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { runId }, result.Value)
            : ToActionResult(result);
    }

    private IActionResult ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        var error = result.FirstError!;
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict or ErrorType.Concurrency or ErrorType.BusinessRule =>
                StatusCodes.Status409Conflict,
            ErrorType.Dependency => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        return Problem(
            statusCode: statusCode,
            title: error.Type.ToString(),
            detail: error.Message,
            type: $"https://warecommand.local/problems/{error.Code}",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = error.Code,
                ["retryable"] = error.IsRetryable
            });
    }

    private static string Format(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string SafeCsvText(string value)
    {
        var normalized = value ?? string.Empty;
        var firstCharacter = normalized.TrimStart().FirstOrDefault();
        return firstCharacter is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n'
            ? $"'{normalized}"
            : normalized;
    }

    private static void AppendCsvRow(StringBuilder builder, params string[] values)
    {
        builder.AppendJoin(',', values.Select(EscapeCsv)).Append("\r\n");
    }

    private static string EscapeCsv(string value) =>
        $"\"{(value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

public sealed record ForecastRecalculationInput(
    int WarehouseId,
    int? ItemId = null,
    ForecastGranularity Granularity = ForecastGranularity.Weekly,
    int HorizonPeriods = 26);

public sealed record ForecastRecalculationAcceptedDto(
    string JobId,
    string IdempotencyKey,
    int WarehouseId,
    int? ItemId,
    ForecastGranularity Granularity,
    int HorizonPeriods);
