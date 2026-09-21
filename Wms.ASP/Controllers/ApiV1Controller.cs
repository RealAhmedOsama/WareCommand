using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Api.V1;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.Items;
using Wms.Application.Locations;
using Wms.Application.Reporting;
using Wms.Application.Warehouses;
using Wms.ASP.Identity;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize(AuthenticationSchemes = WmsApiClientAuthenticationDefaults.Scheme)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class ApiV1Controller(
    IWarehouseManagementService warehouseService,
    IItemManagementService itemService,
    ILocationManagementService locationService,
    IInventoryInquiryService inventoryService,
    IReportQueryService reportService) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiVersionInfo), StatusCodes.Status200OK)]
    public IActionResult Version() => Ok(new ApiVersionInfo(
        WmsApiV1.Version,
        "foundation",
        "3.1.0",
        "Bearer API-client credentials with explicit scopes and warehouse restrictions; human WareCommand session cookies are not accepted on versioned resources.",
        "v1 is additive. Breaking changes require a new version; deprecated operations remain documented for at least one release cycle.",
        WmsApiV1.QueryResources,
        WmsApiV1.CommandResources));

    [HttpGet("warehouses")]
    [Authorize(Policy = WmsPermissions.WarehouseManage)]
    [ProducesResponseType(typeof(ApiPage<ApiWarehouse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Warehouses(
        [FromQuery, StringLength(200)] string? searchTerm,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = WmsApiV1.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidatePaging(page, pageSize, out var pagingProblem))
        {
            return pagingProblem;
        }

        var result = await warehouseService.ListPageAsync(
            new WarehouseListQuery(includeInactive, searchTerm, page, pageSize),
            cancellationToken);
        return ToActionResult(result, value => new ApiPage<ApiWarehouse>(
            value.Items.Select(item => item.ToApi()).ToArray(),
            value.Page,
            value.PageSize,
            value.TotalCount,
            value.TotalPages));
    }

    [HttpGet("items")]
    [Authorize(Policy = WmsPermissions.ItemsRead)]
    [ProducesResponseType(typeof(ApiPage<ApiItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Items(
        [FromQuery, StringLength(200)] string? searchTerm,
        [FromQuery] ItemType? type,
        [FromQuery] ItemLifecycleStatus? lifecycleStatus,
        [FromQuery, StringLength(100)] string? category,
        [FromQuery] bool includeInactive = false,
        [FromQuery] ItemSortField sortBy = ItemSortField.Sku,
        [FromQuery] bool descending = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = WmsApiV1.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidatePaging(page, pageSize, out var pagingProblem))
        {
            return pagingProblem;
        }

        var result = await itemService.ListAsync(
            new ItemListQuery(
                searchTerm,
                type,
                lifecycleStatus,
                category,
                includeInactive,
                sortBy,
                descending,
                page,
                pageSize),
            cancellationToken);
        return ToActionResult(result, value => new ApiPage<ApiItem>(
            value.Items.Select(item => item.ToApi()).ToArray(),
            value.Page,
            value.PageSize,
            value.TotalCount,
            value.TotalPages));
    }

    [HttpGet("locations")]
    [Authorize(Policy = WmsPermissions.LocationsRead)]
    [ProducesResponseType(typeof(ApiPage<ApiLocation>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Locations(
        [FromQuery] int? warehouseId,
        [FromQuery, StringLength(200)] string? searchTerm,
        [FromQuery] LocationType? type,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = WmsApiV1.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidatePaging(page, pageSize, out var pagingProblem))
        {
            return pagingProblem;
        }

        var result = await locationService.ListAsync(
            new LocationListQuery(
                warehouseId,
                searchTerm,
                type,
                includeInactive,
                page,
                pageSize),
            cancellationToken);
        return ToActionResult(result, value => new ApiPage<ApiLocation>(
            value.Items.Select(item => item.ToApi()).ToArray(),
            value.Page,
            value.PageSize,
            value.TotalCount,
            value.TotalPages));
    }

    [HttpGet("inventory/stock")]
    [Authorize(Policy = WmsPermissions.InventoryRead)]
    [ProducesResponseType(typeof(ApiPage<ApiInventoryStock>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Inventory(
        [FromQuery] int? warehouseId,
        [FromQuery] int? locationId,
        [FromQuery] int? itemId,
        [FromQuery] int? lotId,
        [FromQuery] int? serialNumberId,
        [FromQuery] int? licensePlateId,
        [FromQuery] int? inventoryStatusId,
        [FromQuery, StringLength(200)] string? scanValue,
        [FromQuery, StringLength(200)] string? searchTerm,
        [FromQuery] DateTime? expiryFrom,
        [FromQuery] DateTime? expiryTo,
        [FromQuery] bool availableOnly = false,
        [FromQuery] bool includeZero = false,
        [FromQuery] InventoryInquirySort sort = InventoryInquirySort.ItemSku,
        [FromQuery] bool descending = false,
        [FromQuery] InventoryOwnerKind ownerKind = InventoryOwnerKind.CompanyOwned,
        [FromQuery] int? inventoryOwnerId = null,
        [FromQuery, StringLength(100)] string? ownerCodeSnapshot = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = WmsApiV1.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidatePaging(page, pageSize, out var pagingProblem))
        {
            return pagingProblem;
        }

        var result = await inventoryService.QueryAsync(
            new InventoryInquiryQuery(
                warehouseId,
                locationId,
                itemId,
                lotId,
                serialNumberId,
                licensePlateId,
                inventoryStatusId,
                scanValue,
                searchTerm,
                expiryFrom,
                expiryTo,
                availableOnly,
                includeZero,
                sort,
                descending,
                page,
                pageSize,
                ownerKind,
                inventoryOwnerId,
                ownerCodeSnapshot),
            cancellationToken);
        return ToActionResult(result, value => new ApiPage<ApiInventoryStock>(
            value.Items.Select(item => item.ToApi()).ToArray(),
            value.Page,
            value.PageSize,
            value.TotalCount,
            value.TotalPages));
    }

    [HttpGet("reports/movements")]
    [Authorize(Policy = WmsPermissions.ReportsRead)]
    [ProducesResponseType(typeof(ApiMovementPage), StatusCodes.Status200OK)]
    public async Task<IActionResult> MovementReport(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] int? warehouseId,
        [FromQuery, StringLength(200)] string? searchTerm,
        [FromQuery, StringLength(100)] string? itemSku,
        [FromQuery, StringLength(100)] string? locationCode,
        [FromQuery] MovementType? movementType,
        [FromQuery, StringLength(450)] string? userId,
        [FromQuery, StringLength(100)] string? referenceNumber,
        [FromQuery, StringLength(100)] string? lotNumber,
        [FromQuery, StringLength(100)] string? serialNumber,
        [FromQuery, StringLength(100)] string? licensePlateNumber,
        [FromQuery] MovementReportSort sort = MovementReportSort.Timestamp,
        [FromQuery] bool descending = true,
        [FromQuery, StringLength(50)] string? displayUnitOfMeasure = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = WmsApiV1.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidatePaging(page, pageSize, out var pagingProblem))
        {
            return pagingProblem;
        }

        var result = await reportService.QueryMovementLedgerAsync(
            new MovementLedgerQuery(
                fromDate,
                toDate,
                warehouseId,
                searchTerm,
                itemSku,
                locationCode,
                movementType,
                userId,
                referenceNumber,
                lotNumber,
                serialNumber,
                licensePlateNumber,
                sort,
                descending,
                page,
                pageSize,
                displayUnitOfMeasure),
            cancellationToken);
        return ToActionResult(result, value => new ApiMovementPage(
            value.Items.Select(item => item.ToApi()).ToArray(),
            value.Page,
            value.PageSize,
            value.TotalCount,
            value.TotalPages,
            value.Metadata.ToApi()));
    }

    private bool TryValidatePaging(int page, int pageSize, out IActionResult problem)
    {
        if (page >= 1 && pageSize is >= 1 and <= WmsApiV1.MaximumPageSize)
        {
            problem = null!;
            return true;
        }

        problem = Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation",
            detail: $"page must be at least 1 and pageSize must be between 1 and {WmsApiV1.MaximumPageSize}.",
            type: "https://warecommand.local/problems/api.invalid_paging",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = "api.invalid_paging",
                ["retryable"] = false
            });
        return false;
    }

    private IActionResult ToActionResult<TSource, TApi>(
        Result<TSource> result,
        Func<TSource, TApi> map)
    {
        if (result.IsSuccess)
        {
            return Ok(map(result.Value));
        }

        var error = result.FirstError!;
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict or ErrorType.Concurrency or ErrorType.BusinessRule => StatusCodes.Status409Conflict,
            ErrorType.Dependency => StatusCodes.Status503ServiceUnavailable,
            ErrorType.Cancelled => StatusCodes.Status499ClientClosedRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        var extensions = new Dictionary<string, object?>
        {
            ["errorCode"] = error.Code,
            ["retryable"] = error.IsRetryable
        };
        if (error.FieldErrors is not null)
        {
            extensions["fieldErrors"] = error.FieldErrors;
        }

        return Problem(
            statusCode: statusCode,
            title: error.Type.ToString(),
            detail: error.Message,
            type: $"https://warecommand.local/problems/{error.Code}",
            extensions: extensions);
    }
}
