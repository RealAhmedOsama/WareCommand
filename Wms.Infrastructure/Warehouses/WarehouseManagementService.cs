using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Settings;
using Wms.Application.Time;
using Wms.Application.Warehouses;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Warehouses;

public sealed class WarehouseManagementService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    ILogger<WarehouseManagementService> logger) : IWarehouseManagementService
{
    public async Task<Result<IReadOnlyList<WarehouseSummaryDto>>> ListAsync(
        bool includeInactive = true,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizePermissionAsync(cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<WarehouseSummaryDto>>();
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = context.Warehouses.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(warehouse => warehouse.IsActive);
        }

        if (!scope.HasGlobalAccess)
        {
            query = query.Where(warehouse => scope.WarehouseIds.Contains(warehouse.Id));
        }

        var rows = await query
            .OrderBy(warehouse => warehouse.Code)
            .Select(warehouse => new
            {
                warehouse.Id,
                warehouse.Code,
                warehouse.Name,
                warehouse.ArabicName,
                warehouse.IsActive,
                warehouse.WorkflowEnabled,
                LocationCount = context.Locations.Count(location => location.WarehouseId == warehouse.Id),
                ActiveLocationCount = context.Locations.Count(location =>
                    location.WarehouseId == warehouse.Id && location.IsActive),
                ConfiguredOperationalLocationCount = context.WarehouseOperationalLocations
                    .Where(reference => reference.WarehouseId == warehouse.Id)
                    .Select(reference => reference.Role)
                    .Distinct()
                    .Count(),
                AssignedUserCount = context.UserWarehouseAssignments
                    .Count(assignment => assignment.WarehouseId == warehouse.Id)
            })
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<WarehouseSummaryDto>>(rows
            .Select(row => new WarehouseSummaryDto(
                row.Id,
                row.Code,
                row.Name,
                row.ArabicName,
                row.IsActive,
                row.WorkflowEnabled,
                row.ConfiguredOperationalLocationCount == Warehouse.RequiredOperationalLocationRoles.Count,
                row.LocationCount,
                row.ActiveLocationCount,
                row.ConfiguredOperationalLocationCount,
                row.AssignedUserCount))
            .ToArray());
    }

    public async Task<Result<WarehouseDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeWarehouseAsync(id, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WarehouseDto>();
        }

        var warehouse = await LoadWarehouseAsync(id, cancellationToken);
        return warehouse is null
            ? Result.Failure<WarehouseDto>(WmsErrors.NotFound(
                "warehouse.not_found",
                "The requested warehouse was not found."))
            : Result.Success(await ToDtoAsync(warehouse, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<WarehouseLocationOptionDto>>> ListLocationOptionsAsync(
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeWarehouseAsync(warehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<WarehouseLocationOptionDto>>();
        }

        var options = await context.Locations
            .AsNoTracking()
            .Where(location => location.WarehouseId == warehouseId)
            .OrderBy(location => location.Code)
            .Select(location => new WarehouseLocationOptionDto(
                location.Id,
                location.Code,
                location.Name,
                location.IsActive))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<WarehouseLocationOptionDto>>(options);
    }

    public async Task<Result<WarehouseDto>> CreateAsync(
        CreateWarehouseRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizePermissionAsync(cancellationToken, requireGlobalScope: true);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WarehouseDto>();
        }

        var normalized = NormalizeAndValidate(request);
        if (normalized.IsFailure)
        {
            return normalized.ToFailure<WarehouseDto>();
        }

        try
        {
            var duplicate = await context.Warehouses
                .AsNoTracking()
                .AnyAsync(warehouse => warehouse.Code == normalized.Value.Code, cancellationToken);
            if (duplicate)
            {
                return Result.Failure<WarehouseDto>(WmsErrors.Conflict(
                    "warehouse.code_conflict",
                    $"Warehouse code '{normalized.Value.Code}' is already in use."));
            }

            var warehouse = new Warehouse(
                normalized.Value.Code,
                normalized.Value.Name,
                normalized.Value.ArabicName,
                normalized.Value.Address,
                normalized.Value.ContactName,
                normalized.Value.ContactPhone,
                normalized.Value.ContactEmail,
                normalized.Value.TimeZone,
                normalized.Value.AllowNegativeStock,
                normalized.Value.RequireLocationForAdjustment,
                normalized.Value.BlockExpiredReceipt,
                normalized.Value.ExpiryWarningDays);
            context.Warehouses.Add(warehouse);
            await context.SaveChangesAsync(cancellationToken);

            var sequence = new WarehouseNumberSequence(warehouse.Id);
            sequence.SetNextNumbers(
                normalized.Value.NextReceiptNumber,
                normalized.Value.NextOrderNumber,
                normalized.Value.NextWorkNumber,
                normalized.Value.NextShipmentNumber,
                normalized.Value.NextTransferNumber,
                normalized.Value.NextCountNumber);
            context.WarehouseNumberSequences.Add(sequence);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.WarehouseCreated,
                    WmsAuditEntityTypes.Warehouse,
                    warehouse.Id.ToString(CultureInfo.InvariantCulture),
                    warehouse.Id,
                    After: ToAuditValues(warehouse, sequence)),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            return Result.Success(await ToDtoAsync(warehouse, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not create warehouse {WarehouseCode}", request.Code);
            return Result.Failure<WarehouseDto>(WmsErrors.FromException(
                exception,
                "warehouse.create_failed",
                "The warehouse could not be created."));
        }
    }

    public async Task<Result<WarehouseDto>> UpdateAsync(
        UpdateWarehouseRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeWarehouseAsync(request.Id, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WarehouseDto>();
        }

        var normalized = NormalizeAndValidate(request);
        if (normalized.IsFailure)
        {
            return normalized.ToFailure<WarehouseDto>();
        }

        try
        {
            var warehouse = await LoadWarehouseAsync(request.Id, cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure<WarehouseDto>(WmsErrors.NotFound(
                    "warehouse.not_found",
                    "The requested warehouse was not found."));
            }

            var duplicate = await context.Warehouses
                .AsNoTracking()
                .AnyAsync(item => item.Id != request.Id && item.Code == normalized.Value.Code, cancellationToken);
            if (duplicate)
            {
                return Result.Failure<WarehouseDto>(WmsErrors.Conflict(
                    "warehouse.code_conflict",
                    $"Warehouse code '{normalized.Value.Code}' is already in use."));
            }

            var before = ToAuditValues(warehouse, warehouse.NumberSequence);
            warehouse.UpdateProfile(
                normalized.Value.Name,
                normalized.Value.ArabicName,
                normalized.Value.Address,
                normalized.Value.ContactName,
                normalized.Value.ContactPhone,
                normalized.Value.ContactEmail,
                normalized.Value.TimeZone,
                normalized.Value.AllowNegativeStock,
                normalized.Value.RequireLocationForAdjustment,
                normalized.Value.BlockExpiredReceipt,
                normalized.Value.ExpiryWarningDays);

            var sequence = warehouse.NumberSequence ?? new WarehouseNumberSequence(warehouse.Id);
            sequence.SetNextNumbers(
                normalized.Value.NextReceiptNumber,
                normalized.Value.NextOrderNumber,
                normalized.Value.NextWorkNumber,
                normalized.Value.NextShipmentNumber,
                normalized.Value.NextTransferNumber,
                normalized.Value.NextCountNumber);
            if (sequence.Id == 0)
            {
                context.WarehouseNumberSequences.Add(sequence);
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.WarehouseUpdated,
                    WmsAuditEntityTypes.Warehouse,
                    warehouse.Id.ToString(CultureInfo.InvariantCulture),
                    warehouse.Id,
                    Before: before,
                    After: ToAuditValues(warehouse, sequence)),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            return Result.Success(await ToDtoAsync(warehouse, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not update warehouse {WarehouseId}", request.Id);
            return Result.Failure<WarehouseDto>(WmsErrors.FromException(
                exception,
                "warehouse.update_failed",
                "The warehouse could not be updated."));
        }
    }

    public async Task<Result> ConfigureOperationalLocationsAsync(
        int warehouseId,
        IReadOnlyCollection<WarehouseOperationalLocationSelection> selections,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeWarehouseAsync(warehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization;
        }

        var required = Warehouse.RequiredOperationalLocationRoles;
        var duplicateRoles = selections
            .GroupBy(selection => selection.Role)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        var unknownRoles = selections
            .Select(selection => selection.Role)
            .Except(required)
            .Distinct()
            .ToArray();
        var missingRoles = required
            .Except(selections.Select(selection => selection.Role))
            .ToArray();
        if (duplicateRoles.Length > 0 || unknownRoles.Length > 0 || missingRoles.Length > 0)
        {
            return Result.Failure(WmsErrors.Validation(
                "warehouse.operational_locations_invalid",
                $"Exactly one active location is required for each role. Missing: " +
                $"{FormatRoles(missingRoles)}. Invalid: {FormatRoles(unknownRoles)}. " +
                $"Duplicates: {FormatRoles(duplicateRoles)}."));
        }

        if (selections.Any(selection => selection.LocationId <= 0))
        {
            return Result.Failure(WmsErrors.Validation(
                "warehouse.operational_location_required",
                "Every operational role must reference a location."));
        }

        try
        {
            var warehouse = await LoadWarehouseAsync(warehouseId, cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure(WmsErrors.NotFound(
                    "warehouse.not_found",
                    "The requested warehouse was not found."));
            }

            var locationIds = selections.Select(selection => selection.LocationId).Distinct().ToArray();
            var locations = await context.Locations
                .AsNoTracking()
                .Where(location => locationIds.Contains(location.Id))
                .Select(location => new
                {
                    location.Id,
                    location.WarehouseId,
                    location.IsActive,
                    location.Code
                })
                .ToDictionaryAsync(location => location.Id, cancellationToken);
            var invalidLocations = selections
                .Where(selection => !locations.TryGetValue(selection.LocationId, out var location) ||
                                    location.WarehouseId != warehouseId ||
                                    !location.IsActive)
                .Select(selection => selection.LocationId)
                .Distinct()
                .ToArray();
            if (invalidLocations.Length > 0)
            {
                return Result.Failure(WmsErrors.Validation(
                    "warehouse.operational_location_invalid",
                    $"Operational locations must be active locations belonging to warehouse {warehouse.Code}. " +
                    $"Invalid IDs: {string.Join(", ", invalidLocations)}."));
            }

            var existing = await context.WarehouseOperationalLocations
                .Where(reference => reference.WarehouseId == warehouseId)
                .ToListAsync(cancellationToken);
            var before = existing.ToDictionary(
                reference => reference.Role.ToString(),
                reference => (object?)reference.LocationId);
            var selectionMap = selections.ToDictionary(selection => selection.Role);
            foreach (var role in required)
            {
                var selection = selectionMap[role];
                var reference = existing.SingleOrDefault(item => item.Role == role);
                if (reference is null)
                {
                    context.WarehouseOperationalLocations.Add(
                        new WarehouseOperationalLocation(warehouseId, selection.LocationId, role));
                }
                else
                {
                    reference.SetLocation(selection.LocationId);
                }
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.WarehouseConfigurationChanged,
                    WmsAuditEntityTypes.Warehouse,
                    warehouseId.ToString(CultureInfo.InvariantCulture),
                    warehouseId,
                    Before: before,
                    After: selectionMap.ToDictionary(
                        pair => pair.Key.ToString(),
                        pair => (object?)pair.Value.LocationId),
                    Details: "Required operational location references changed."),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not configure operational locations for warehouse {WarehouseId}",
                warehouseId);
            return Result.Failure(WmsErrors.FromException(
                exception,
                "warehouse.operational_locations_save_failed",
                "Operational locations could not be saved."));
        }
    }

    public async Task<Result> EnableWorkflowsAsync(
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeWarehouseAsync(warehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization;
        }

        try
        {
            var warehouse = await LoadWarehouseAsync(warehouseId, cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure(WmsErrors.NotFound(
                    "warehouse.not_found",
                    "The requested warehouse was not found."));
            }

            var references = await context.WarehouseOperationalLocations
                .AsNoTracking()
                .Where(reference => reference.WarehouseId == warehouseId)
                .Join(
                    context.Locations,
                    reference => reference.LocationId,
                    location => location.Id,
                    (reference, location) => new { reference.Role, location.WarehouseId, location.IsActive })
                .ToListAsync(cancellationToken);
            var missing = Warehouse.RequiredOperationalLocationRoles
                .Where(role => !references.Any(reference => reference.Role == role &&
                                                             reference.WarehouseId == warehouseId &&
                                                             reference.IsActive))
                .ToArray();
            if (missing.Length > 0)
            {
                return Result.Failure(WmsErrors.Conflict(
                    "warehouse.operational_locations_incomplete",
                    $"Workflows cannot be enabled until all required active locations are configured. " +
                    $"Missing: {FormatRoles(missing)}."));
            }

            var before = warehouse.WorkflowEnabled;
            warehouse.EnableWorkflows(references.Select(reference => reference.Role));
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.WarehouseConfigurationChanged,
                    WmsAuditEntityTypes.Warehouse,
                    warehouseId.ToString(CultureInfo.InvariantCulture),
                    warehouseId,
                    Before: new Dictionary<string, object?> { ["workflowEnabled"] = before },
                    After: new Dictionary<string, object?> { ["workflowEnabled"] = warehouse.WorkflowEnabled },
                    Details: "Warehouse workflows enabled."),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not enable workflows for warehouse {WarehouseId}", warehouseId);
            return Result.Failure(WmsErrors.FromException(
                exception,
                "warehouse.workflows_enable_failed",
                "Warehouse workflows could not be enabled."));
        }
    }

    public async Task<Result> ActivateAsync(
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeWarehouseAsync(warehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization;
        }

        try
        {
            var warehouse = await LoadWarehouseAsync(warehouseId, cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure(WmsErrors.NotFound(
                    "warehouse.not_found",
                    "The requested warehouse was not found."));
            }

            var before = warehouse.IsActive;
            warehouse.Activate();
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.WarehouseActivated,
                    WmsAuditEntityTypes.Warehouse,
                    warehouseId.ToString(CultureInfo.InvariantCulture),
                    warehouseId,
                    Before: new Dictionary<string, object?> { ["isActive"] = before },
                    After: new Dictionary<string, object?> { ["isActive"] = warehouse.IsActive }),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not activate warehouse {WarehouseId}", warehouseId);
            return Result.Failure(WmsErrors.FromException(
                exception,
                "warehouse.activate_failed",
                "The warehouse could not be activated."));
        }
    }

    public async Task<Result> DeactivateAsync(
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeWarehouseAsync(warehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization;
        }

        try
        {
            var warehouse = await LoadWarehouseAsync(warehouseId, cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure(WmsErrors.NotFound(
                    "warehouse.not_found",
                    "The requested warehouse was not found."));
            }

            if (!warehouse.IsActive)
            {
                return Result.Success();
            }

            var stockQuantities = await context.Stock
                .AsNoTracking()
                .Where(stock => stock.Location.WarehouseId == warehouseId)
                .Select(stock => new
                {
                    Available = stock.QuantityAvailable.Value,
                    Reserved = stock.QuantityReserved.Value
                })
                .ToListAsync(cancellationToken);
            var hasStock = stockQuantities.Any(quantity => quantity.Available > 0 || quantity.Reserved > 0);
            var hasRunningJobs = await context.JobExecutions
                .AsNoTracking()
                .AnyAsync(job => job.WarehouseId == warehouseId && job.Status == "Running", cancellationToken);
            if (hasStock || hasRunningJobs)
            {
                var blockers = new List<string>();
                if (hasStock)
                    blockers.Add("available or reserved inventory");
                if (hasRunningJobs)
                    blockers.Add("running warehouse work");

                return Result.Failure(WmsErrors.Conflict(
                    "warehouse.deactivation_blocked",
                    $"The warehouse cannot be deactivated while it has {string.Join(" and ", blockers)}. " +
                    "Complete or move the open work first."));
            }

            var before = new Dictionary<string, object?>
            {
                ["isActive"] = warehouse.IsActive,
                ["workflowEnabled"] = warehouse.WorkflowEnabled
            };
            warehouse.Deactivate();
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.WarehouseDeactivated,
                    WmsAuditEntityTypes.Warehouse,
                    warehouseId.ToString(CultureInfo.InvariantCulture),
                    warehouseId,
                    Before: before,
                    After: new Dictionary<string, object?>
                    {
                        ["isActive"] = warehouse.IsActive,
                        ["workflowEnabled"] = warehouse.WorkflowEnabled
                    }),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not deactivate warehouse {WarehouseId}", warehouseId);
            return Result.Failure(WmsErrors.FromException(
                exception,
                "warehouse.deactivate_failed",
                "The warehouse could not be deactivated."));
        }
    }

    private async Task<Result> AuthorizePermissionAsync(
        CancellationToken cancellationToken,
        bool requireGlobalScope = false)
    {
        if (!await warehouseAccessService.HasPermissionAsync(
                WmsPermissions.WarehouseManage,
                cancellationToken))
        {
            return Result.Failure(WmsErrors.Forbidden(
                "authorization.permission_denied",
                "You do not have permission to manage warehouses."));
        }

        if (requireGlobalScope)
        {
            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            if (!scope.HasGlobalAccess)
            {
                return Result.Failure(WmsErrors.Forbidden(
                    "authorization.global_warehouse_scope_required",
                    "Creating a warehouse requires global warehouse scope."));
            }
        }

        return Result.Success();
    }

    private async Task<Result> AuthorizeWarehouseAsync(
        int warehouseId,
        CancellationToken cancellationToken)
    {
        if (warehouseId <= 0)
        {
            return Result.Failure(WmsErrors.Validation(
                "warehouse.id_invalid",
                "A valid warehouse is required."));
        }

        var permission = await AuthorizePermissionAsync(cancellationToken);
        if (permission.IsFailure)
        {
            return permission;
        }

        var exists = await context.Warehouses
            .AsNoTracking()
            .AnyAsync(warehouse => warehouse.Id == warehouseId, cancellationToken);
        if (!exists)
        {
            return Result.Failure(WmsErrors.NotFound(
                "warehouse.not_found",
                "The requested warehouse was not found."));
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        if (!scope.HasGlobalAccess && !scope.WarehouseIds.Contains(warehouseId))
        {
            return Result.Failure(WmsErrors.Forbidden(
                "authorization.warehouse_scope_denied",
                "You are not assigned to the requested warehouse."));
        }

        return Result.Success();
    }

    private Task<Warehouse?> LoadWarehouseAsync(int id, CancellationToken cancellationToken) =>
        context.Warehouses
            .Include(warehouse => warehouse.OperationalLocations)
            .ThenInclude(reference => reference.Location)
            .Include(warehouse => warehouse.NumberSequence)
            .SingleOrDefaultAsync(warehouse => warehouse.Id == id, cancellationToken);

    private async Task<WarehouseDto> ToDtoAsync(
        Warehouse warehouse,
        CancellationToken cancellationToken)
    {
        var sequence = warehouse.NumberSequence ?? await context.WarehouseNumberSequences
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.WarehouseId == warehouse.Id, cancellationToken);
        var locationCount = await context.Locations
            .CountAsync(location => location.WarehouseId == warehouse.Id, cancellationToken);
        var activeLocationCount = await context.Locations
            .CountAsync(location => location.WarehouseId == warehouse.Id && location.IsActive, cancellationToken);
        var assignedUserCount = await context.UserWarehouseAssignments
            .CountAsync(assignment => assignment.WarehouseId == warehouse.Id, cancellationToken);
        var operationalLocations = warehouse.OperationalLocations
            .Select(reference => new WarehouseOperationalLocationDto(
                reference.Role,
                reference.LocationId,
                reference.Location?.Code ?? string.Empty,
                reference.Location?.Name ?? string.Empty,
                reference.Location?.IsActive ?? false))
            .OrderBy(reference => reference.Role)
            .ToArray();
        var configuredRoles = operationalLocations.Select(reference => reference.Role).ToHashSet();
        var missing = Warehouse.RequiredOperationalLocationRoles
            .Where(role => !configuredRoles.Contains(role))
            .ToArray();
        var numberSequence = sequence is null
            ? new WarehouseNumberSequenceDto(1, 1, 1, 1, 1, 1, 0)
            : new WarehouseNumberSequenceDto(
                sequence.NextReceiptNumber,
                sequence.NextOrderNumber,
                sequence.NextWorkNumber,
                sequence.NextShipmentNumber,
                sequence.NextTransferNumber,
                sequence.NextCountNumber,
                sequence.Revision);

        return new WarehouseDto(
            warehouse.Id,
            warehouse.Code,
            warehouse.Name,
            warehouse.ArabicName,
            warehouse.Address,
            warehouse.ContactName,
            warehouse.ContactPhone,
            warehouse.ContactEmail,
            warehouse.TimeZone,
            warehouse.IsActive,
            warehouse.WorkflowEnabled,
            warehouse.AllowNegativeStock,
            warehouse.RequireLocationForAdjustment,
            warehouse.BlockExpiredReceipt,
            warehouse.ExpiryWarningDays,
            operationalLocations,
            missing,
            numberSequence,
            locationCount,
            activeLocationCount,
            assignedUserCount,
            warehouse.CreatedAt,
            warehouse.UpdatedAt);
    }

    private static Result<NormalizedWarehouseRequest> NormalizeAndValidate(CreateWarehouseRequest request)
    {
        var normalizedTimeZone = NormalizeTimeZone(request.TimeZone, out var timeZoneError);
        var errors = ValidateCommon(
            request.Code,
            request.Name,
            request.ArabicName,
            request.ContactEmail,
            request.ExpiryWarningDays,
            request.NextReceiptNumber,
            request.NextOrderNumber,
            request.NextWorkNumber,
            request.NextTransferNumber,
            request.NextCountNumber);
        if (timeZoneError is not null)
            errors[nameof(request.TimeZone)] = [timeZoneError];
        if (errors.Count > 0)
            return Result.Failure<NormalizedWarehouseRequest>(WmsErrors.Validation(
                "warehouse.validation_failed",
                "Review the warehouse fields and try again.",
                errors));

        return Result.Success(new NormalizedWarehouseRequest(
            request.Code.Trim().ToUpperInvariant(),
            request.Name.Trim(),
            request.ArabicName?.Trim() ?? string.Empty,
            request.Address?.Trim() ?? string.Empty,
            request.ContactName?.Trim() ?? string.Empty,
            request.ContactPhone?.Trim() ?? string.Empty,
            request.ContactEmail?.Trim() ?? string.Empty,
            normalizedTimeZone!,
            request.AllowNegativeStock,
            request.RequireLocationForAdjustment,
            request.BlockExpiredReceipt,
            request.ExpiryWarningDays,
            request.NextReceiptNumber,
            request.NextOrderNumber,
            request.NextWorkNumber,
            request.NextShipmentNumber,
            request.NextTransferNumber,
            request.NextCountNumber));
    }

    private static Result<NormalizedWarehouseRequest> NormalizeAndValidate(UpdateWarehouseRequest request)
    {
        var normalizedTimeZone = NormalizeTimeZone(request.TimeZone, out var timeZoneError);
        var errors = ValidateCommon(
            string.Empty,
            request.Name,
            request.ArabicName,
            request.ContactEmail,
            request.ExpiryWarningDays,
            request.NextReceiptNumber,
            request.NextOrderNumber,
            request.NextWorkNumber,
            request.NextShipmentNumber,
            request.NextTransferNumber,
            request.NextCountNumber);
        if (timeZoneError is not null)
            errors[nameof(request.TimeZone)] = [timeZoneError];
        if (errors.Count > 0)
            return Result.Failure<NormalizedWarehouseRequest>(WmsErrors.Validation(
                "warehouse.validation_failed",
                "Review the warehouse fields and try again.",
                errors));

        return Result.Success(new NormalizedWarehouseRequest(
            string.Empty,
            request.Name.Trim(),
            request.ArabicName?.Trim() ?? string.Empty,
            request.Address?.Trim() ?? string.Empty,
            request.ContactName?.Trim() ?? string.Empty,
            request.ContactPhone?.Trim() ?? string.Empty,
            request.ContactEmail?.Trim() ?? string.Empty,
            normalizedTimeZone!,
            request.AllowNegativeStock,
            request.RequireLocationForAdjustment,
            request.BlockExpiredReceipt,
            request.ExpiryWarningDays,
            request.NextReceiptNumber,
            request.NextOrderNumber,
            request.NextWorkNumber,
            request.NextShipmentNumber,
            request.NextTransferNumber,
            request.NextCountNumber));
    }

    private static Dictionary<string, string[]> ValidateCommon(
        string code,
        string name,
        string? arabicName,
        string? contactEmail,
        int expiryWarningDays,
        params long[] sequenceValues)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var normalizedCode = code.Trim();
        if (!string.IsNullOrWhiteSpace(code) &&
            (normalizedCode.Length is < 2 or > 20 ||
             normalizedCode.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_')))
        {
            errors[nameof(CreateWarehouseRequest.Code)] =
            [
                "Code must be 2-20 characters and may contain only letters, digits, '-' and '_'."
            ];
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            errors[nameof(CreateWarehouseRequest.Name)] = ["Name is required and cannot exceed 200 characters."];
        if (!string.IsNullOrWhiteSpace(arabicName) && arabicName.Trim().Length > 200)
            errors[nameof(CreateWarehouseRequest.ArabicName)] = ["The localized name cannot exceed 200 characters."];
        if (!string.IsNullOrWhiteSpace(contactEmail) &&
            (!contactEmail.Contains('@', StringComparison.Ordinal) || contactEmail.Trim().Length > 320))
        {
            errors[nameof(CreateWarehouseRequest.ContactEmail)] = ["Contact email must be a valid email address."];
        }

        if (expiryWarningDays is < 0 or > 3_650)
            errors[nameof(CreateWarehouseRequest.ExpiryWarningDays)] = ["Expiry warning days must be between 0 and 3,650."];
        if (sequenceValues.Any(value => value < 1))
            errors["NumberSequence"] = ["All number sequences must start at a positive number."];
        return errors;
    }

    private static string? NormalizeTimeZone(string? value, out string? error)
    {
        if (WmsTimeZoneCatalog.TryNormalize(value ?? string.Empty, out var normalized))
        {
            error = null;
            return normalized;
        }

        error = "Time zone must be a valid installed IANA identifier, for example 'UTC' or 'Africa/Cairo'.";
        return null;
    }

    private static Dictionary<string, object?> ToAuditValues(
        Warehouse warehouse,
        WarehouseNumberSequence? sequence)
    {
        return new Dictionary<string, object?>
        {
            ["code"] = warehouse.Code,
            ["name"] = warehouse.Name,
            ["arabicName"] = warehouse.ArabicName,
            ["address"] = warehouse.Address,
            ["contactName"] = warehouse.ContactName,
            ["contactPhone"] = warehouse.ContactPhone,
            ["contactEmail"] = warehouse.ContactEmail,
            ["timeZone"] = warehouse.TimeZone,
            ["isActive"] = warehouse.IsActive,
            ["workflowEnabled"] = warehouse.WorkflowEnabled,
            ["allowNegativeStock"] = warehouse.AllowNegativeStock,
            ["requireLocationForAdjustment"] = warehouse.RequireLocationForAdjustment,
            ["blockExpiredReceipt"] = warehouse.BlockExpiredReceipt,
            ["expiryWarningDays"] = warehouse.ExpiryWarningDays,
            ["numberSequence"] = sequence is null
                ? null
                : new
                {
                    sequence.NextReceiptNumber,
                    sequence.NextOrderNumber,
                    sequence.NextWorkNumber,
                    sequence.NextShipmentNumber,
                    sequence.NextTransferNumber,
                    sequence.NextCountNumber
                }
        };
    }

    private static string FormatRoles(IEnumerable<WarehouseOperationalLocationRole> roles) =>
        string.Join(", ", roles.Select(role => role.ToString()));

    private sealed record NormalizedWarehouseRequest(
        string Code,
        string Name,
        string ArabicName,
        string Address,
        string ContactName,
        string ContactPhone,
        string ContactEmail,
        string TimeZone,
        bool AllowNegativeStock,
        bool RequireLocationForAdjustment,
        bool BlockExpiredReceipt,
        int ExpiryWarningDays,
        long NextReceiptNumber,
        long NextOrderNumber,
        long NextWorkNumber,
        long NextShipmentNumber,
        long NextTransferNumber,
        long NextCountNumber);
}
