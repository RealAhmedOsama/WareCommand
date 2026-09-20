// Wms.Application/UseCases/Locations/GetLocationsUseCase.cs

using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.DTOs;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Repositories;

namespace Wms.Application.UseCases.Locations;

public interface IGetLocationsUseCase
{
    Task<Result<IEnumerable<LocationDto>>> ExecuteAsync(string? searchTerm = null,
        CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<LocationDto>>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<LocationDto>>> GetReceivableLocationsAsync(CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<LocationDto>>> GetPickableLocationsAsync(CancellationToken cancellationToken = default);
    Task<Result<LocationDto>> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
}

public class GetLocationsUseCase : IGetLocationsUseCase
{
    private readonly ILogger<GetLocationsUseCase> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public GetLocationsUseCase(
        IUnitOfWork unitOfWork,
        ILogger<GetLocationsUseCase> logger,
        IWarehouseAccessService warehouseAccessService)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _warehouseAccessService = warehouseAccessService;
    }

    public async Task<Result<IEnumerable<LocationDto>>> ExecuteAsync(string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.LocationsRead,
                cancellationToken: cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<IEnumerable<LocationDto>>();
            }

            var locations = string.IsNullOrWhiteSpace(searchTerm)
                ? await _unitOfWork.Locations.GetAllAsync(cancellationToken)
                : await _unitOfWork.Locations.SearchAsync(searchTerm, cancellationToken);

            var locationDtos = locations.Select(MapToDto);
            return Result.Success(locationDtos);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving locations");
            return Result.Failure<IEnumerable<LocationDto>>(WmsErrors.FromException(ex,
                "locations.read_failed",
                "Error retrieving locations. Please try again."));
        }
    }

    public async Task<Result<IEnumerable<LocationDto>>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.LocationsRead,
                cancellationToken: cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<IEnumerable<LocationDto>>();
            }

            var locations = await _unitOfWork.Locations.GetAllAsync(cancellationToken);
            var locationDtos = locations.Select(MapToDto);
            return Result.Success(locationDtos);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving locations");
            return Result.Failure<IEnumerable<LocationDto>>(WmsErrors.FromException(ex,
                "locations.read_failed",
                "Error retrieving locations. Please try again."));
        }
    }

    public async Task<Result<IEnumerable<LocationDto>>> GetReceivableLocationsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.LocationsRead,
                cancellationToken: cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<IEnumerable<LocationDto>>();
            }

            var locations = await _unitOfWork.Locations.GetReceivableLocationsAsync(cancellationToken);
            var locationDtos = locations.Select(MapToDto);
            return Result.Success(locationDtos);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving receivable locations");
            return Result.Failure<IEnumerable<LocationDto>>(WmsErrors.FromException(ex,
                "locations.read_failed",
                "Error retrieving locations. Please try again."));
        }
    }

    public async Task<Result<IEnumerable<LocationDto>>> GetPickableLocationsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.LocationsRead,
                cancellationToken: cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<IEnumerable<LocationDto>>();
            }

            var locations = await _unitOfWork.Locations.GetPickableLocationsAsync(cancellationToken);
            var locationDtos = locations.Select(MapToDto);
            return Result.Success(locationDtos);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving pickable locations");
            return Result.Failure<IEnumerable<LocationDto>>(WmsErrors.FromException(ex,
                "locations.read_failed",
                "Error retrieving locations. Please try again."));
        }
    }

    public async Task<Result<LocationDto>> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.LocationsRead,
                cancellationToken: cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<LocationDto>();
            }

            var location = await _unitOfWork.Locations.GetByCodeAsync(code, cancellationToken);
            if (location == null)
                return Result.Failure<LocationDto>(WmsErrors.NotFound(
                    "location.not_found",
                    $"Location with code '{code}' was not found."));

            return Result.Success(MapToDto(location));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving location {LocationCode}", code);
            return Result.Failure<LocationDto>(WmsErrors.FromException(ex,
                "location.read_failed",
                "Error retrieving location. Please try again."));
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
