// Wms.Application/UseCases/Locations/CreateLocationUseCase.cs

using System.Globalization;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.DTOs;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Repositories;

namespace Wms.Application.UseCases.Locations;

public record CreateLocationDto(
    string Code,
    string Name,
    int WarehouseId,
    int? ParentLocationId = null,
    bool IsPickable = true,
    bool IsReceivable = true,
    int Capacity = 0,
    LocationType Type = LocationType.Storage,
    string? Barcode = null,
    int Priority = 0,
    bool IsCountable = true,
    bool AllowMixedItems = true,
    bool AllowMixedLots = true,
    decimal? MaxUnits = 1000,
    decimal? MaxWeightKg = null,
    decimal? MaxVolumeCubicMeters = null,
    int? MaxPallets = null,
    int? MaxLpns = null,
    string? StorageProfile = null,
    decimal? MinimumTemperatureCelsius = null,
    decimal? MaximumTemperatureCelsius = null,
    string? HazardClass = null,
    string? AccessRestriction = null,
    string? ConstraintAttributesJson = null
);

public record UpdateLocationDto(
    int Id,
    string Name,
    bool IsPickable = true,
    bool IsReceivable = true,
    int Capacity = 0,
    int? ParentLocationId = null,
    LocationType Type = LocationType.Storage,
    string? Barcode = null,
    int Priority = 0,
    bool IsCountable = true,
    bool AllowMixedItems = true,
    bool AllowMixedLots = true,
    decimal? MaxUnits = 1000,
    decimal? MaxWeightKg = null,
    decimal? MaxVolumeCubicMeters = null,
    int? MaxPallets = null,
    int? MaxLpns = null,
    string? StorageProfile = null,
    decimal? MinimumTemperatureCelsius = null,
    decimal? MaximumTemperatureCelsius = null,
    string? HazardClass = null,
    string? AccessRestriction = null,
    string? ConstraintAttributesJson = null
);

public interface ICreateLocationUseCase
{
    Task<Result<LocationDto>> ExecuteAsync(CreateLocationDto request, string userId,
        CancellationToken cancellationToken = default);
}

public interface IUpdateLocationUseCase
{
    Task<Result<LocationDto>> ExecuteAsync(UpdateLocationDto request, string userId,
        CancellationToken cancellationToken = default);
}

public interface IDeleteLocationUseCase
{
    Task<Result> ExecuteAsync(int locationId, string userId,
        CancellationToken cancellationToken = default);
}

public class CreateLocationUseCase : ICreateLocationUseCase
{
    private readonly ILogger<CreateLocationUseCase> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditWriter _auditWriter;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public CreateLocationUseCase(
        IUnitOfWork unitOfWork,
        ILogger<CreateLocationUseCase> logger,
        IAuditWriter auditWriter,
        IWarehouseAccessService warehouseAccessService)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _auditWriter = auditWriter;
        _warehouseAccessService = warehouseAccessService;
    }

    public async Task<Result<LocationDto>> ExecuteAsync(CreateLocationDto request, string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.LocationsManage,
                request.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<LocationDto>();
            }

            // Check if code already exists
            var existingLocation = await _unitOfWork.Locations.GetByWarehouseAndCodeAsync(
                request.WarehouseId,
                request.Code,
                cancellationToken);
            if (existingLocation != null)
                return Result.Failure<LocationDto>(WmsErrors.Conflict(
                    "location.code_conflict",
                    $"Location with code '{request.Code}' already exists."));

            // Validate parent location if specified
            if (request.ParentLocationId.HasValue)
            {
                var parentValidation = await ValidateParentAsync(
                    request.WarehouseId,
                    request.ParentLocationId.Value,
                    currentLocationId: null,
                    cancellationToken);
                if (parentValidation.IsFailure)
                {
                    return parentValidation.ToFailure<LocationDto>();
                }
            }

            // Create new location
            var location = new Location(
                request.Code,
                request.Name,
                request.WarehouseId,
                request.ParentLocationId,
                request.Type,
                request.Barcode,
                request.Priority,
                request.IsPickable,
                request.IsReceivable,
                request.IsCountable,
                request.AllowMixedItems,
                request.AllowMixedLots,
                request.MaxUnits ?? (request.Capacity > 0 ? request.Capacity : 1_000),
                request.MaxWeightKg,
                request.MaxVolumeCubicMeters,
                request.MaxPallets,
                request.MaxLpns,
                request.StorageProfile,
                request.MinimumTemperatureCelsius,
                request.MaximumTemperatureCelsius,
                request.HazardClass,
                request.AccessRestriction,
                request.ConstraintAttributesJson);

            await _unitOfWork.Locations.AddAsync(location, cancellationToken);
            await _auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.LocationCreated,
                    WmsAuditEntityTypes.Location,
                    request.Code,
                    request.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["code"] = request.Code,
                        ["name"] = request.Name,
                        ["isPickable"] = request.IsPickable,
                        ["isReceivable"] = request.IsReceivable,
                        ["capacity"] = request.Capacity,
                        ["parentLocationId"] = request.ParentLocationId,
                        ["type"] = request.Type.ToString(),
                        ["barcode"] = request.Barcode,
                        ["priority"] = request.Priority,
                        ["isCountable"] = request.IsCountable,
                        ["allowMixedItems"] = request.AllowMixedItems,
                        ["allowMixedLots"] = request.AllowMixedLots,
                        ["maxUnits"] = request.MaxUnits,
                        ["maxWeightKg"] = request.MaxWeightKg,
                        ["maxVolumeCubicMeters"] = request.MaxVolumeCubicMeters,
                        ["maxPallets"] = request.MaxPallets,
                        ["maxLpns"] = request.MaxLpns
                    }),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Location created: {LocationCode} by {UserId}", request.Code, userId);

            return Result.Success(MapToDto(location));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating location {LocationCode}", request.Code);
            return Result.Failure<LocationDto>(WmsErrors.FromException(ex,
                "location.create_failed",
                "Error creating location. Please try again."));
        }
    }

    private async Task<Result> ValidateParentAsync(
        int warehouseId,
        int parentLocationId,
        int? currentLocationId,
        CancellationToken cancellationToken)
    {
        var visited = new HashSet<int>();
        if (currentLocationId.HasValue)
        {
            visited.Add(currentLocationId.Value);
        }

        var currentId = parentLocationId;
        for (var depth = 0; depth < 32; depth++)
        {
            if (!visited.Add(currentId))
            {
                return Result.Failure(WmsErrors.Validation(
                    "location.hierarchy_cycle",
                    "A location cannot be its own ancestor."));
            }

            var parent = await _unitOfWork.Locations.GetByIdAsync(currentId, cancellationToken);
            if (parent is null)
            {
                return Result.Failure(WmsErrors.NotFound(
                    "location.parent_not_found",
                    "The selected parent location was not found."));
            }

            if (parent.WarehouseId != warehouseId)
            {
                return Result.Failure(WmsErrors.BusinessRule(
                    "location.parent_warehouse_mismatch",
                    "A parent location must belong to the same warehouse."));
            }

            if (!parent.IsActive)
            {
                return Result.Failure(WmsErrors.BusinessRule(
                    "location.parent_inactive",
                    "A location cannot be placed under an inactive parent."));
            }

            if (!parent.ParentLocationId.HasValue)
            {
                return Result.Success();
            }

            currentId = parent.ParentLocationId.Value;
        }

        return Result.Failure(WmsErrors.Validation(
            "location.hierarchy_depth_exceeded",
            "Location hierarchy cannot exceed 32 levels."));
    }

    private static LocationDto MapToDto(Location location)
    {
        return new LocationDto(
            location.Id,
            location.Code,
            location.Name,
            location.WarehouseId,
            location.ParentLocationId,
            location.IsPickable,
            location.IsReceivable,
            location.IsActive,
            location.Capacity,
            location.GetFullPath(),
            location.CreatedAt,
            location.UpdatedAt,
            location.Type,
            location.Barcode,
            location.Priority,
            location.IsCountable,
            location.AllowMixedItems,
            location.AllowMixedLots,
            location.MaxUnits,
            location.MaxWeightKg,
            location.MaxVolumeCubicMeters,
            location.MaxPallets,
            location.MaxLpns,
            location.StorageProfile,
            location.MinimumTemperatureCelsius,
            location.MaximumTemperatureCelsius,
            location.HazardClass,
            location.AccessRestriction,
            location.ConstraintAttributesJson
        );
    }
}

public class UpdateLocationUseCase : IUpdateLocationUseCase
{
    private readonly ILogger<UpdateLocationUseCase> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditWriter _auditWriter;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public UpdateLocationUseCase(
        IUnitOfWork unitOfWork,
        ILogger<UpdateLocationUseCase> logger,
        IAuditWriter auditWriter,
        IWarehouseAccessService warehouseAccessService)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _auditWriter = auditWriter;
        _warehouseAccessService = warehouseAccessService;
    }

    public async Task<Result<LocationDto>> ExecuteAsync(UpdateLocationDto request, string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var location = await _unitOfWork.Locations.GetByIdAsync(request.Id, cancellationToken);
            if (location == null)
                return Result.Failure<LocationDto>(WmsErrors.NotFound(
                    "location.not_found",
                    $"Location with ID {request.Id} was not found."));

            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.LocationsManage,
                location.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<LocationDto>();
            }

            var before = new Dictionary<string, object?>
            {
                ["name"] = location.Name,
                ["isPickable"] = location.IsPickable,
                ["isReceivable"] = location.IsReceivable,
                ["capacity"] = location.Capacity,
                ["type"] = location.Type.ToString(),
                ["barcode"] = location.Barcode,
                ["parentLocationId"] = location.ParentLocationId
            };

            var currentStock = await _unitOfWork.Stock.GetByLocationIdAsync(
                location.Id,
                cancellationToken);
            var currentUnits = currentStock.Sum(stock => stock.QuantityAvailable.Value);
            if (request.Type != location.Type && currentStock.Any(stock =>
                    stock.QuantityAvailable.Value > 0 || stock.QuantityReserved.Value > 0))
            {
                return Result.Failure<LocationDto>(WmsErrors.Conflict(
                    "location.restructure_blocked",
                    "A location with stock or reservations cannot change type."));
            }

            var requestedMaxUnits = request.MaxUnits ?? (request.Capacity > 0 ? request.Capacity : 1_000);
            if (currentUnits > requestedMaxUnits)
            {
                return Result.Failure<LocationDto>(WmsErrors.Conflict(
                    "location.capacity_reduction_blocked",
                    "The configured unit capacity is below the stock currently stored at this location."));
            }

            // Update location details
            location.UpdateDetails(request.Name);
            location.UpdateDefinition(
                request.Type,
                request.Barcode,
                request.Priority,
                request.IsPickable,
                request.IsReceivable,
                request.IsCountable,
                request.AllowMixedItems,
                request.AllowMixedLots,
                requestedMaxUnits,
                request.MaxWeightKg,
                request.MaxVolumeCubicMeters,
                request.MaxPallets,
                request.MaxLpns,
                request.StorageProfile,
                request.MinimumTemperatureCelsius,
                request.MaximumTemperatureCelsius,
                request.HazardClass,
                request.AccessRestriction,
                request.ConstraintAttributesJson);

            await _unitOfWork.Locations.UpdateAsync(location, cancellationToken);
            await _auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.LocationUpdated,
                    WmsAuditEntityTypes.Location,
                    location.Id.ToString(CultureInfo.InvariantCulture),
                    location.WarehouseId,
                    Before: before,
                    After: new Dictionary<string, object?>
                    {
                        ["name"] = location.Name,
                        ["isPickable"] = location.IsPickable,
                        ["isReceivable"] = location.IsReceivable,
                        ["capacity"] = location.Capacity,
                        ["type"] = location.Type.ToString(),
                        ["barcode"] = location.Barcode,
                        ["parentLocationId"] = location.ParentLocationId,
                        ["maxUnits"] = location.MaxUnits,
                        ["maxWeightKg"] = location.MaxWeightKg,
                        ["maxVolumeCubicMeters"] = location.MaxVolumeCubicMeters,
                        ["maxPallets"] = location.MaxPallets,
                        ["maxLpns"] = location.MaxLpns
                    }),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Location updated: {LocationCode} by {UserId}", location.Code, userId);

            return Result.Success(MapToDto(location));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating location {LocationId}", request.Id);
            return Result.Failure<LocationDto>(WmsErrors.FromException(ex,
                "location.update_failed",
                "Error updating location. Please try again."));
        }
    }

    private static LocationDto MapToDto(Location location)
    {
        return new LocationDto(
            location.Id,
            location.Code,
            location.Name,
            location.WarehouseId,
            location.ParentLocationId,
            location.IsPickable,
            location.IsReceivable,
            location.IsActive,
            location.Capacity,
            location.GetFullPath(),
            location.CreatedAt,
            location.UpdatedAt,
            location.Type,
            location.Barcode,
            location.Priority,
            location.IsCountable,
            location.AllowMixedItems,
            location.AllowMixedLots,
            location.MaxUnits,
            location.MaxWeightKg,
            location.MaxVolumeCubicMeters,
            location.MaxPallets,
            location.MaxLpns,
            location.StorageProfile,
            location.MinimumTemperatureCelsius,
            location.MaximumTemperatureCelsius,
            location.HazardClass,
            location.AccessRestriction,
            location.ConstraintAttributesJson
        );
    }
}

public class DeleteLocationUseCase : IDeleteLocationUseCase
{
    private readonly ILogger<DeleteLocationUseCase> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditWriter _auditWriter;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public DeleteLocationUseCase(
        IUnitOfWork unitOfWork,
        ILogger<DeleteLocationUseCase> logger,
        IAuditWriter auditWriter,
        IWarehouseAccessService warehouseAccessService)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _auditWriter = auditWriter;
        _warehouseAccessService = warehouseAccessService;
    }

    public async Task<Result> ExecuteAsync(int locationId, string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var location = await _unitOfWork.Locations.GetByIdAsync(locationId, cancellationToken);
            if (location == null)
                return Result.Failure(WmsErrors.NotFound(
                    "location.not_found",
                    $"Location with ID {locationId} was not found."));

            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.LocationsManage,
                location.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization;
            }

            // Check if location has stock before deleting
            var stockItems = await _unitOfWork.Stock.GetByLocationIdAsync(locationId, cancellationToken);
            if (stockItems.Any(s =>
                    s.QuantityAvailable.Value > 0 || s.QuantityReserved.Value > 0))
            {
                return Result.Failure(WmsErrors.Conflict(
                    "location.stock_exists",
                    "Cannot delete a location with available or reserved stock. Move or release stock first."));
            }

            // Check if location has child locations
            var childLocations = await _unitOfWork.Locations.GetChildLocationsAsync(locationId, cancellationToken);
            if (childLocations.Any())
            {
                return Result.Failure(WmsErrors.Conflict(
                    "location.children_exist",
                    "Cannot delete a location with child locations. Delete child locations first."));
            }

            // Soft delete by deactivating
            var before = new Dictionary<string, object?> { ["isActive"] = location.IsActive };
            location.Deactivate();
            await _unitOfWork.Locations.UpdateAsync(location, cancellationToken);
            await _auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.LocationDeactivated,
                    WmsAuditEntityTypes.Location,
                    location.Id.ToString(CultureInfo.InvariantCulture),
                    location.WarehouseId,
                    Before: before,
                    After: new Dictionary<string, object?> { ["isActive"] = location.IsActive }),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Location deactivated: {LocationCode} by {UserId}", location.Code, userId);

            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting location {LocationId}", locationId);
            return Result.Failure(WmsErrors.FromException(ex,
                "location.delete_failed",
                "Error deleting location. Please try again."));
        }
    }
}
