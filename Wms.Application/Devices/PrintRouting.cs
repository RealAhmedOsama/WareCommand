using Wms.Application.Common;

namespace Wms.Application.Devices;

/// <summary>
/// Resolves station/warehouse print intent to a configured adapter route. It
/// carries adapter keys only; network addresses and credentials stay in the
/// host/secret-provider boundary.
/// </summary>
public sealed class WmsPrintRouteCatalog
{
    private readonly IReadOnlyList<WmsPrintRoute> _routes;

    public WmsPrintRouteCatalog(IEnumerable<WmsPrintRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        _routes = routes.ToArray();
    }

    public Result<WmsPrintRoute> Resolve(WmsPrintRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TemplateName))
        {
            return Result.Failure<WmsPrintRoute>(WmsErrors.Validation(
                "device.print_template_required",
                "A print template is required."));
        }

        if (request.Copies is < 1 or > 100)
        {
            return Result.Failure<WmsPrintRoute>(WmsErrors.Validation(
                "device.print_copies_invalid",
                "Print copies must be between 1 and 100."));
        }

        var candidates = _routes
            .Where(route => route.IsEnabled &&
                            string.Equals(route.TemplateName, request.TemplateName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                            route.Format == request.Format &&
                            (request.WarehouseId is null || route.WarehouseId is null || route.WarehouseId == request.WarehouseId) &&
                            (request.StationCode is null || route.StationCode is null ||
                             string.Equals(route.StationCode, request.StationCode, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(route => request.StationCode is not null &&
                                        string.Equals(route.StationCode, request.StationCode, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(route => request.WarehouseId.HasValue && route.WarehouseId == request.WarehouseId)
            .ThenBy(route => route.Name, StringComparer.Ordinal)
            .ToArray();

        var route = candidates.FirstOrDefault();
        return route is null
            ? Result.Failure<WmsPrintRoute>(WmsErrors.NotFound(
                "device.print_route_not_found",
                "No enabled print route matches the requested station, warehouse, template, and format."))
            : Result.Success(route);
    }
}
