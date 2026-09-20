// Wms.Application/UseCases/Locations/CreateLocationUseCase.cs

using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.DTOs;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Repositories;

namespace Wms.Application.UseCases.Locations;

public record CreateLocationDto(
    string Code,
    string Name,
    int WarehouseId,
    int? ParentLocationId = null,
    bool IsPickable = true,
    bool IsReceivable = true,
    int Capacity = 0
);

public record UpdateLocationDto(
    int Id,
    string Name,
    bool IsPickable = true,
    bool IsReceivable = true,
    int Capacity = 0
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
    private readonly IWarehouseAccessService _warehouseAccessService;

    public CreateLocationUseCase(
        IUnitOfWork unitOfWork,
        ILogger<CreateLocationUseCase> logger,
        IWarehouseAccessService warehouseAccessService)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
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
                return Result.Failure<LocationDto>(authorization.Error);
            }

            // Check if code already exists
            var existingLocation = await _unitOfWork.Locations.GetByCodeAsync(request.Code, cancellationToken);
            if (existingLocation != null)
                return Result.Failure<LocationDto>($"Location with code '{request.Code}' already exists");

            // Validate parent location if specified
            if (request.ParentLocationId.HasValue)
            {
                var parentLocation =
                    await _unitOfWork.Locations.GetByIdAsync(request.ParentLocationId.Value, cancellationToken);
                if (parentLocation == null)
                    return Result.Failure<LocationDto>($"Parent location with ID {request.ParentLocationId} not found");

                if (parentLocation.WarehouseId != request.WarehouseId)
                {
                    return Result.Failure<LocationDto>("A parent location must belong to the selected warehouse.");
                }
            }

            // Create new location
            var location = new Location(
                request.Code,
                request.Name,
                request.WarehouseId,
                request.ParentLocationId);

            location.SetPickable(request.IsPickable);
            location.SetReceivable(request.IsReceivable);

            if (request.Capacity > 0)
                location.SetCapacity(request.Capacity);

            await _unitOfWork.Locations.AddAsync(location, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Location created: {LocationCode} by {UserId}", request.Code, userId);

            return Result.Success(MapToDto(location));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating location {LocationCode}", request.Code);
            return Result.Failure<LocationDto>($"Error creating location: {ex.Message}");
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
            location.UpdatedAt
        );
    }
}

public class UpdateLocationUseCase : IUpdateLocationUseCase
{
    private readonly ILogger<UpdateLocationUseCase> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public UpdateLocationUseCase(
        IUnitOfWork unitOfWork,
        ILogger<UpdateLocationUseCase> logger,
        IWarehouseAccessService warehouseAccessService)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _warehouseAccessService = warehouseAccessService;
    }

    public async Task<Result<LocationDto>> ExecuteAsync(UpdateLocationDto request, string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var location = await _unitOfWork.Locations.GetByIdAsync(request.Id, cancellationToken);
            if (location == null)
                return Result.Failure<LocationDto>($"Location with ID {request.Id} not found");

            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.LocationsManage,
                location.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return Result.Failure<LocationDto>(authorization.Error);
            }

            // Update location details
            location.UpdateDetails(request.Name);
            location.SetPickable(request.IsPickable);
            location.SetReceivable(request.IsReceivable);

            if (request.Capacity >= 0)
                location.SetCapacity(request.Capacity);

            await _unitOfWork.Locations.UpdateAsync(location, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Location updated: {LocationCode} by {UserId}", location.Code, userId);

            return Result.Success(MapToDto(location));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating location {LocationId}", request.Id);
            return Result.Failure<LocationDto>($"Error updating location: {ex.Message}");
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
            location.UpdatedAt
        );
    }
}

public class DeleteLocationUseCase : IDeleteLocationUseCase
{
    private readonly ILogger<DeleteLocationUseCase> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public DeleteLocationUseCase(
        IUnitOfWork unitOfWork,
        ILogger<DeleteLocationUseCase> logger,
        IWarehouseAccessService warehouseAccessService)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _warehouseAccessService = warehouseAccessService;
    }

    public async Task<Result> ExecuteAsync(int locationId, string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var location = await _unitOfWork.Locations.GetByIdAsync(locationId, cancellationToken);
            if (location == null)
                return Result.Failure($"Location with ID {locationId} not found");

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
            if (stockItems.Any(s => s.QuantityAvailable.Value > 0))
            {
                return Result.Failure("Cannot delete location with existing stock. Please move stock first.");
            }

            // Check if location has child locations
            var childLocations = await _unitOfWork.Locations.GetChildLocationsAsync(locationId, cancellationToken);
            if (childLocations.Any())
            {
                return Result.Failure(
                    "Cannot delete location with child locations. Please delete child locations first.");
            }

            // Soft delete by deactivating
            location.Deactivate();
            await _unitOfWork.Locations.UpdateAsync(location, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Location deactivated: {LocationCode} by {UserId}", location.Code, userId);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting location {LocationId}", locationId);
            return Result.Failure($"Error deleting location: {ex.Message}");
        }
    }
}
