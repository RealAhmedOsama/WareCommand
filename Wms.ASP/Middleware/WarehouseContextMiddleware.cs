using Wms.Application.Context;
using Wms.Application.Identity;

namespace Wms.ASP.Middleware;

public sealed class WarehouseContextMiddleware(RequestDelegate next)
{
    public const string CookieName = "warecommand.warehouse";

    public async Task InvokeAsync(
        HttpContext httpContext,
        IWarehouseContext warehouseContext,
        IWarehouseAccessService warehouseAccessService)
    {
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            var warehouses = await warehouseAccessService.GetAccessibleWarehousesAsync(
                WmsPermissions.DashboardView,
                httpContext.RequestAborted);
            var requestedId = ParseWarehouseId(httpContext.Request.Cookies[CookieName]);
            var selected = requestedId.HasValue
                ? warehouses.FirstOrDefault(warehouse => warehouse.Id == requestedId.Value)
                : null;
            selected ??= warehouses.FirstOrDefault(warehouse => warehouse.IsDefault) ??
                         (warehouses.Count > 0 ? warehouses[0] : null);
            warehouseContext.SetWarehouse(selected?.Id);
        }

        await next(httpContext);
    }

    private static int? ParseWarehouseId(string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var warehouseId) && warehouseId > 0
            ? warehouseId
            : null;
}
