using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.ValueAddedServices;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.ValueAddedServices;

/// <summary>
/// Reservation-backed warehouse assembly and value-added-service execution.
/// The service deliberately stays inside the warehouse execution boundary: it
/// creates a normal warehouse-work item, consumes reserved component stock,
/// produces normal inventory balances, and keeps immutable genealogy links.
/// </summary>
public sealed class ValueAddedService : IValueAddedService
{
    private const string DemandType = "ValueAddedService";
    private const int MaximumPageSize = 200;

    private readonly WmsDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWarehouseAccessService _warehouseAccessService;
    private readonly IInventoryReservationService _reservationService;
    private readonly IInventoryLedgerService _ledgerService;
    private readonly IAuditWriter _auditWriter;
    private readonly IClock _clock;
    private readonly ILogger<ValueAddedService> _logger;

    public ValueAddedService(
        WmsDbContext context,
        IUnitOfWork unitOfWork,
        IWarehouseAccessService warehouseAccessService,
        IInventoryReservationService reservationService,
        IInventoryLedgerService ledgerService,
        IAuditWriter auditWriter,
        IClock clock,
        ILogger<ValueAddedService> logger)
    {
        _context = context;
        _unitOfWork = unitOfWork;
        _warehouseAccessService = warehouseAccessService;
        _reservationService = reservationService;
        _ledgerService = ledgerService;
        _auditWriter = auditWriter;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<KitDefinitionDto>> CreateKitDefinitionAsync(
        KitDefinitionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ValueAddedServiceManage,
            cancellationToken: cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<KitDefinitionDto>();
        }

        try
        {
            var lines = input.Lines ?? [];
            if (lines.Count == 0)
            {
                return Result.Failure<KitDefinitionDto>(WmsErrors.Validation(
                    "vas.kit_lines_required",
                    "A kit definition must contain at least one component line."));
            }

            if (lines.GroupBy(line => line.Sequence).Any(group => group.Count() > 1))
            {
                return Result.Failure<KitDefinitionDto>(WmsErrors.Validation(
                    "vas.kit_line_sequence_duplicate",
                    "Kit component sequence numbers must be unique."));
            }

            var outputItem = await _context.Items
                .SingleOrDefaultAsync(item => item.Id == input.OutputItemId, cancellationToken);
            if (outputItem is null || !outputItem.IsActive)
            {
                return Result.Failure<KitDefinitionDto>(WmsErrors.Validation(
                    "vas.kit_output_item_invalid",
                    "The kit output item does not exist or is inactive."));
            }

            if (!string.Equals(
                    outputItem.UnitOfMeasure,
                    input.OutputUnitOfMeasure.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<KitDefinitionDto>(WmsErrors.Validation(
                    "vas.kit_output_uom_mismatch",
                    "The kit output unit of measure must match the output item base unit."));
            }

            var componentIds = lines.Select(line => line.ComponentItemId).Distinct().ToArray();
            var components = await _context.Items
                .Where(item => componentIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, cancellationToken);
            if (components.Count != componentIds.Length || components.Values.Any(item => !item.IsActive))
            {
                return Result.Failure<KitDefinitionDto>(WmsErrors.Validation(
                    "vas.kit_component_item_invalid",
                    "Every kit component item must exist and be active."));
            }

            foreach (var line in lines)
            {
                if (!string.Equals(
                        components[line.ComponentItemId].UnitOfMeasure,
                        line.ComponentUnitOfMeasure.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Failure<KitDefinitionDto>(WmsErrors.Validation(
                        "vas.kit_component_uom_mismatch",
                        $"Component item {line.ComponentItemId} does not use the supplied base unit."));
                }
            }

            var normalizedCode = input.Code.Trim().ToUpperInvariant();
            if (await _context.KitDefinitions.AnyAsync(
                    definition => definition.Code == normalizedCode && definition.Version == input.Version,
                    cancellationToken))
            {
                return Result.Failure<KitDefinitionDto>(WmsErrors.Conflict(
                    "vas.kit_version_conflict",
                    $"Kit definition {normalizedCode} version {input.Version} already exists."));
            }

            var definition = new KitDefinition(
                normalizedCode,
                input.Version,
                input.OutputItemId,
                input.OutputUnitOfMeasure,
                input.EffectiveFromUtc,
                input.EffectiveToUtc,
                input.Instructions,
                input.LocalizedInstructions);
            foreach (var line in lines.OrderBy(value => value.Sequence))
            {
                definition.AddLine(new KitDefinitionLine(
                    line.Sequence,
                    line.ComponentItemId,
                    line.QuantityPerOutput,
                    line.ComponentUnitOfMeasure,
                    line.SubstitutionPolicy,
                    line.ApprovedSubstitutionItemIdsJson,
                    line.Notes));
            }

            _context.KitDefinitions.Add(definition);
            await _auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.KitDefinitionCreated,
                    WmsAuditEntityTypes.KitDefinition,
                    normalizedCode,
                    After: new Dictionary<string, object?>
                    {
                        ["version"] = input.Version,
                        ["outputItemId"] = input.OutputItemId,
                        ["componentCount"] = lines.Count
                    },
                    ActorUserId: userId),
                cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            var saved = await LoadKitAsync(definition.Id, cancellationToken);
            return saved is null
                ? Result.Failure<KitDefinitionDto>(WmsErrors.Dependency(
                    "vas.kit_reload_failed",
                    "The kit definition was saved but could not be reloaded.",
                    isRetryable: false))
                : Result.Success(MapKit(saved));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DbUpdateException exception)
        {
            return Result.Failure<KitDefinitionDto>(WmsErrors.FromException(
                exception,
                "vas.kit_create_failed",
                "The kit definition could not be created."));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<KitDefinitionDto>(WmsErrors.Validation(
                "vas.kit_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<KitDefinitionDto>(WmsErrors.BusinessRule(
                "vas.kit_invalid",
                exception.Message));
        }
    }

    public async Task<Result<KitDefinitionDto>> GetKitDefinitionAsync(
        int definitionId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ValueAddedServiceRead,
            cancellationToken: cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<KitDefinitionDto>();
        }

        var definition = await LoadKitAsync(definitionId, cancellationToken);
        return definition is null
            ? Result.Failure<KitDefinitionDto>(WmsErrors.NotFound(
                "vas.kit_not_found",
                "The kit definition was not found."))
            : Result.Success(MapKit(definition));
    }

    public async Task<Result<ValueAddedServiceOrderDto>> CreateOrderAsync(
        ValueAddedServiceOrderInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ValueAddedServiceManage,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ValueAddedServiceOrderDto>();
        }

        try
        {
            if (!Enum.IsDefined(input.Type) || string.IsNullOrWhiteSpace(input.IdempotencyKey))
            {
                return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Validation(
                    "vas.order_input_invalid",
                    "A valid VAS type and idempotency key are required."));
            }

            var existing = await LoadOrderByIdempotencyAsync(input.IdempotencyKey, cancellationToken);
            if (existing is not null)
            {
                return MatchesOrder(existing, input)
                    ? Result.Success(MapOrder(existing))
                    : Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Conflict(
                        "vas.order_idempotency_reuse",
                        "The VAS idempotency key was already used with a different order."));
            }

            var warehouse = await _context.Warehouses
                .SingleOrDefaultAsync(value => value.Id == input.WarehouseId && value.IsActive, cancellationToken);
            var source = await _context.Locations
                .SingleOrDefaultAsync(value => value.Id == input.SourceLocationId &&
                                               value.WarehouseId == input.WarehouseId && value.IsActive,
                    cancellationToken);
            var destination = await _context.Locations
                .SingleOrDefaultAsync(value => value.Id == input.DestinationLocationId &&
                                               value.WarehouseId == input.WarehouseId && value.IsActive,
                    cancellationToken);
            var outputItem = await _context.Items
                .SingleOrDefaultAsync(value => value.Id == input.OutputItemId && value.IsActive, cancellationToken);
            if (warehouse is null || source is null || destination is null || outputItem is null)
            {
                return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Validation(
                    "vas.order_dimension_invalid",
                    "The warehouse, source location, destination location, or output item is invalid."));
            }

            var inputLocationId = input.InputSelector?.LocationId ?? input.SourceLocationId;
            var inputLocation = await _context.Locations
                .SingleOrDefaultAsync(value => value.Id == inputLocationId &&
                                               value.WarehouseId == input.WarehouseId && value.IsActive,
                    cancellationToken);
            if (inputLocation is null)
            {
                return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Validation(
                    "vas.input_location_invalid",
                    "The selected input location is invalid for the requested warehouse."));
            }

            if (input.RequestedOutputQuantity <= 0m)
            {
                return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Validation(
                    "vas.order_quantity_invalid",
                    "The requested VAS quantity must be positive."));
            }

            var kit = input.KitDefinitionId.HasValue
                ? await LoadKitAsync(input.KitDefinitionId.Value, cancellationToken)
                : null;
            if (input.KitDefinitionId.HasValue && kit is null)
            {
                return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.NotFound(
                    "vas.kit_not_found",
                    "The requested kit definition was not found."));
            }

            var now = _clock.UtcNow.UtcDateTime;
            if (kit is not null &&
                (!kit.IsActive || kit.EffectiveFromUtc > now ||
                 (kit.EffectiveToUtc.HasValue && kit.EffectiveToUtc.Value <= now)))
            {
                return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.BusinessRule(
                    "vas.kit_not_effective",
                    "The kit definition is not active for the current effective date."));
            }

            if (kit is not null && kit.OutputItemId != input.OutputItemId)
            {
                return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Validation(
                    "vas.kit_output_mismatch",
                    "The order output item must match the selected kit definition."));
            }

            if (input.QualityProfileId.HasValue)
            {
                var qualityProfile = await _context.QualityProfiles
                    .SingleOrDefaultAsync(value => value.Id == input.QualityProfileId.Value, cancellationToken);
                if (qualityProfile is null || !qualityProfile.IsActive)
                {
                    return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Validation(
                        "vas.quality_profile_invalid",
                        "The selected quality profile does not exist or is inactive."));
                }

                if ((qualityProfile.WarehouseId.HasValue && qualityProfile.WarehouseId != input.WarehouseId) ||
                    (qualityProfile.ItemId.HasValue && qualityProfile.ItemId != input.OutputItemId))
                {
                    return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Validation(
                        "vas.quality_profile_scope_invalid",
                        "The selected quality profile is not scoped to this warehouse and output item."));
                }
            }

            var inputSelector = input.InputSelector;
            var inputItemId = input.InputItemId ?? (kit is null ? input.OutputItemId : kit.OutputItemId);
            var inputItem = await _context.Items
                .SingleOrDefaultAsync(value => value.Id == inputItemId && value.IsActive, cancellationToken);
            if (inputItem is null)
            {
                return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Validation(
                    "vas.input_item_invalid",
                    "The input item does not exist or is inactive."));
            }

            var ownerValidation = await ValidateOwnerAsync(
                input.OutputOwnerKind,
                input.OutputInventoryOwnerId,
                input.OutputOwnerCodeSnapshot,
                cancellationToken);
            if (ownerValidation.IsFailure)
            {
                return ownerValidation.ToFailure<ValueAddedServiceOrderDto>();
            }

            var inputOwnerCode = InventoryOwnershipDimension.NormalizeOwnerCode(
                inputSelector?.OwnerKind ?? InventoryOwnerKind.CompanyOwned,
                inputSelector?.InventoryOwnerId,
                inputSelector?.OwnerCodeSnapshot);
            ownerValidation = await ValidateOwnerAsync(
                inputSelector?.OwnerKind ?? InventoryOwnerKind.CompanyOwned,
                inputSelector?.InventoryOwnerId,
                inputOwnerCode,
                cancellationToken);
            if (ownerValidation.IsFailure)
            {
                return ownerValidation.ToFailure<ValueAddedServiceOrderDto>();
            }

            var inputStatusId = inputSelector?.InventoryStatusId ?? InventoryStatusSystemIds.Available;
            if (!await _context.InventoryStatuses.AnyAsync(
                    status => status.Id == inputStatusId && status.IsActive,
                    cancellationToken))
            {
                return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Validation(
                    "vas.input_status_invalid",
                    "The input inventory status is missing or inactive."));
            }

            var order = new ValueAddedServiceOrder(
                $"VAS-{input.WarehouseId.ToString(CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}",
                input.IdempotencyKey,
                input.Type,
                input.WarehouseId,
                input.SourceLocationId,
                input.DestinationLocationId,
                input.OutputItemId,
                input.RequestedOutputQuantity,
                outputItem.UnitOfMeasure,
                userId,
                now,
                kit?.Id,
                kit?.Version,
                kit?.Instructions,
                kit?.LocalizedInstructions,
                input.StationCode,
                input.LabelTemplateCode,
                input.QualityProfileId,
                input.OutputOwnerKind,
                input.OutputInventoryOwnerId,
                input.OutputOwnerCodeSnapshot,
                inputSelector?.OwnerKind ?? InventoryOwnerKind.CompanyOwned,
                inputSelector?.InventoryOwnerId,
                inputOwnerCode,
                inputStatusId,
                inputSelector?.LotId,
                inputSelector?.SerialNumberId,
                inputSelector?.SerialNumber,
                inputSelector?.LicensePlateId);

            var nextLine = 1;
            if (kit is not null && input.Type is ValueAddedServiceType.Disassemble or ValueAddedServiceType.Unbundle)
            {
                order.AddLine(CreateInputLine(
                    nextLine++,
                    inputItem,
                    input.RequestedOutputQuantity,
                    inputLocationId,
                    inputStatusId,
                    inputSelector,
                    inputOwnerCode,
                    notes: "Kit parent input"));
                foreach (var kitLine in kit.Lines.OrderBy(value => value.Sequence))
                {
                    order.AddLine(new ValueAddedServiceOrderLine(
                        nextLine++,
                        ValueAddedServiceLineKind.Output,
                        kitLine.ComponentItemId,
                        kitLine.QuantityPerOutput * input.RequestedOutputQuantity,
                        kitLine.ComponentUnitOfMeasure,
                        InventoryStatusSystemIds.Available,
                        destinationLocationId: input.DestinationLocationId,
                        kitDefinitionLineId: kitLine.Id,
                        notes: kitLine.Notes));
                }
            }
            else if (kit is not null)
            {
                foreach (var kitLine in kit.Lines.OrderBy(value => value.Sequence))
                {
                    var component = kitLine.ComponentItem;
                    order.AddLine(new ValueAddedServiceOrderLine(
                        nextLine++,
                        ValueAddedServiceLineKind.Input,
                        component.Id,
                        kitLine.QuantityPerOutput * input.RequestedOutputQuantity,
                        component.UnitOfMeasure,
                        inputStatusId,
                        sourceLocationId: inputLocationId,
                        lotId: inputSelector?.LotId,
                        serialNumberId: inputSelector?.SerialNumberId,
                        serialNumber: inputSelector?.SerialNumber,
                        licensePlateId: inputSelector?.LicensePlateId,
                        ownerKind: inputSelector?.OwnerKind ?? InventoryOwnerKind.CompanyOwned,
                        inventoryOwnerId: inputSelector?.InventoryOwnerId,
                        ownerCodeSnapshot: inputOwnerCode,
                        kitDefinitionLineId: kitLine.Id,
                        notes: kitLine.Notes));
                }

                order.AddLine(new ValueAddedServiceOrderLine(
                    nextLine,
                    ValueAddedServiceLineKind.Output,
                    input.OutputItemId,
                    input.RequestedOutputQuantity,
                    outputItem.UnitOfMeasure,
                    InventoryStatusSystemIds.Available,
                    destinationLocationId: input.DestinationLocationId));
            }
            else
            {
                order.AddLine(CreateInputLine(
                    nextLine++,
                    inputItem,
                    input.RequestedOutputQuantity,
                    inputLocationId,
                    inputStatusId,
                    inputSelector,
                    inputOwnerCode,
                    notes: "VAS input"));
                order.AddLine(new ValueAddedServiceOrderLine(
                    nextLine,
                    ValueAddedServiceLineKind.Output,
                    input.OutputItemId,
                    input.RequestedOutputQuantity,
                    outputItem.UnitOfMeasure,
                    InventoryStatusSystemIds.Available,
                    destinationLocationId: input.DestinationLocationId));
            }

            _context.ValueAddedServiceOrders.Add(order);
            await _auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.ValueAddedServiceOrderCreated,
                    WmsAuditEntityTypes.ValueAddedServiceOrder,
                    order.OrderNumber,
                    order.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["type"] = order.Type.ToString(),
                        ["requestedOutputQuantity"] = order.RequestedOutputQuantity,
                        ["kitDefinitionId"] = order.KitDefinitionId,
                        ["kitVersionSnapshot"] = order.KitVersionSnapshot
                    },
                    ActorUserId: userId),
                cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            var saved = await LoadOrderAsync(order.Id, cancellationToken);
            return saved is null
                ? Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Dependency(
                    "vas.order_reload_failed",
                    "The VAS order was saved but could not be reloaded.",
                    isRetryable: false))
                : Result.Success(MapOrder(saved));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DbUpdateException exception)
        {
            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.FromException(
                exception,
                "vas.order_create_failed",
                "The VAS order could not be created."));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Validation(
                "vas.order_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.BusinessRule(
                "vas.order_invalid",
                exception.Message));
        }
    }

    public async Task<Result<ValueAddedServiceOrderDto>> ReleaseAsync(
        int orderId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.NotFound(
                "vas.order_not_found",
                "The VAS order was not found."));
        }

        var authorization = await _warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ValueAddedServiceManage,
            order.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ValueAddedServiceOrderDto>();
        }

        if (order.Status != ValueAddedServiceOrderStatus.Draft)
        {
            return Result.Success(MapOrder(order));
        }

        var reservationIds = new List<int>();
        var transactionStarted = false;
        try
        {
            foreach (var inputLine in order.Lines
                         .Where(line => line.Kind is ValueAddedServiceLineKind.Input or ValueAddedServiceLineKind.AdditionalInput)
                         .OrderBy(line => line.LineNumber)
                         .ToArray())
            {
                var reservation = await ReserveInputLineAsync(order, inputLine, userId, cancellationToken);
                reservationIds.Add(reservation.ReservationId);
                AttachReservationAllocations(order, inputLine, reservation);
            }

            var now = _clock.UtcNow.UtcDateTime;
            if (_context.Database.CurrentTransaction is null)
            {
                await _unitOfWork.BeginTransactionAsync(cancellationToken);
                transactionStarted = true;
            }

            var work = new WarehouseWorkEntity(
                $"WORK-{order.WarehouseId.ToString(CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}",
                $"vas:{order.Id}",
                WarehouseWorkType.ValueAddedService,
                order.WarehouseId,
                WmsAuditEntityTypes.ValueAddedServiceOrder,
                order.Id.ToString(CultureInfo.InvariantCulture),
                queueCode: $"VAS_{order.Type}",
                teamCode: order.StationCode,
                notes: order.InstructionSnapshot);
            foreach (var line in order.Lines.OrderBy(value => value.LineNumber))
            {
                work.AddLine(new WarehouseWorkLine(
                    line.LineNumber,
                    order.WarehouseId,
                    line.ItemId,
                    line.PlannedQuantity,
                    line.BaseUnitOfMeasure,
                    sourceLocationId: line.Kind is ValueAddedServiceLineKind.Input or ValueAddedServiceLineKind.AdditionalInput
                        ? line.SourceLocationId
                        : null,
                    destinationLocationId: line.Kind == ValueAddedServiceLineKind.Output
                        ? order.DestinationLocationId
                        : null,
                    lotId: line.LotId,
                    serialNumberId: line.SerialNumberId,
                    serialNumber: line.SerialNumber,
                    licensePlateId: line.LicensePlateId,
                    inventoryStatusId: line.InventoryStatusId,
                    sourceReference: order.OrderNumber,
                    dimensionsSnapshot: BuildDimensionSnapshot(line),
                    reservationId: line.ReservationId,
                    reservationAllocationId: line.ReservationAllocationId,
                    ownerKind: line.OwnerKind,
                    inventoryOwnerId: line.InventoryOwnerId,
                    ownerCodeSnapshot: line.OwnerCodeSnapshot));
            }

            work.MakeAvailable(now);
            _context.WarehouseWorks.Add(work);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            order.SetWarehouseWork(work.Id);
            order.Release(now);
            await _auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.ValueAddedServiceOrderReleased,
                    WmsAuditEntityTypes.ValueAddedServiceOrder,
                    order.OrderNumber,
                    order.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["warehouseWorkId"] = work.Id,
                        ["inputLineCount"] = order.Lines.Count(line =>
                            line.Kind is ValueAddedServiceLineKind.Input or ValueAddedServiceLineKind.AdditionalInput),
                        ["kitVersionSnapshot"] = order.KitVersionSnapshot
                    },
                    ActorUserId: userId),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            if (transactionStarted)
            {
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                transactionStarted = false;
            }

            var saved = await LoadOrderAsync(order.Id, cancellationToken);
            return saved is null
                ? Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Dependency(
                    "vas.order_reload_failed",
                    "The VAS order was released but could not be reloaded.",
                    isRetryable: false))
                : Result.Success(MapOrder(saved));
        }
        catch (OperationCanceledException)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            await ReleaseReservationsBestEffortAsync(reservationIds, userId, CancellationToken.None);
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            await ReleaseReservationsBestEffortAsync(reservationIds, userId, CancellationToken.None);
            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Concurrency(
                "vas.release_concurrency",
                exception.Message));
        }
        catch (DbUpdateException exception)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            await ReleaseReservationsBestEffortAsync(reservationIds, userId, CancellationToken.None);
            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.FromException(
                exception,
                "vas.release_failed",
                "The VAS order could not be released."));
        }
        catch (ArgumentException exception)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            await ReleaseReservationsBestEffortAsync(reservationIds, userId, CancellationToken.None);
            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Validation(
                "vas.release_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            await ReleaseReservationsBestEffortAsync(reservationIds, userId, CancellationToken.None);
            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.BusinessRule(
                "vas.release_invalid",
                exception.Message));
        }
    }

    public async Task<Result<ValueAddedServiceOrderDto>> CompleteAsync(
        int orderId,
        ValueAddedServiceCompletionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.NotFound(
                "vas.order_not_found",
                "The VAS order was not found."));
        }

        var authorization = await _warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ValueAddedServiceManage,
            order.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ValueAddedServiceOrderDto>();
        }

        var requestHash = Hash(input);
        var replay = await FindCommandAsync(order.Id, "complete", input.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return replay.RequestHash == requestHash
                ? Result.Success(MapOrder(order))
                : Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Conflict(
                    "vas.idempotency_reuse",
                    "The completion idempotency key was already used with a different request."));
        }

        if (order.Status is not (ValueAddedServiceOrderStatus.Released or
            ValueAddedServiceOrderStatus.InProgress or
            ValueAddedServiceOrderStatus.PartiallyCompleted))
        {
            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.BusinessRule(
                "vas.complete_status_invalid",
                $"The VAS order cannot be completed from {order.Status}."));
        }

        if (input.OutputQuantity < 0m || input.OutputQuantity > order.RemainingOutputQuantity)
        {
            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Validation(
                "vas.complete_quantity_invalid",
                "The completion quantity must be non-negative and cannot exceed the remaining order quantity."));
        }

        var outputLines = order.Lines
            .Where(line => line.Kind == ValueAddedServiceLineKind.Output)
            .OrderBy(line => line.LineNumber)
            .ToArray();
        var expectedOutputMaterial = outputLines.Sum(line => line.PlannedQuantity) /
                                      order.RequestedOutputQuantity * input.OutputQuantity;
        var suppliedOutputs = input.Outputs ?? [];
        if (Math.Abs(suppliedOutputs.Sum(value => value.Quantity) - expectedOutputMaterial) > 0.0000001m)
        {
            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Validation(
                "vas.complete_outputs_unbalanced",
                $"Output quantities must total {expectedOutputMaterial.ToString(CultureInfo.InvariantCulture)} for this completion."));
        }

        var componentPlan = BuildComponentPlan(order, input);
        if (componentPlan.IsFailure)
        {
            return componentPlan.ToFailure<ValueAddedServiceOrderDto>();
        }

        var transactionStarted = _context.Database.CurrentTransaction is null;
        try
        {
            if (transactionStarted)
            {
                await _unitOfWork.BeginTransactionAsync(cancellationToken);
            }

            var now = _clock.UtcNow.UtcDateTime;
            if (order.Status == ValueAddedServiceOrderStatus.Released)
            {
                order.Start(now);
            }

            foreach (var plan in componentPlan.Value)
            {
                var total = plan.ConsumedQuantity + plan.ScrapQuantity;
                if (total <= 0m)
                {
                    continue;
                }

                await ApplyLegacyConsumptionAsync(plan.Line, total, cancellationToken);
                if (plan.ConsumedQuantity > 0m)
                {
                    await _reservationService.ConsumeAsync(
                        new InventoryReservationMutationRequest(
                            plan.Line.ReservationId!.Value,
                            plan.ConsumedQuantity,
                            userId,
                            CorrelationId: $"vas:{order.Id}:{input.IdempotencyKey}",
                            Reason: input.Reason ?? "VAS component consumed",
                            AllocationId: plan.Line.ReservationAllocationId,
                            LedgerType: InventoryTransactionType.ValueAddedConsumption),
                        cancellationToken);
                    plan.Line.RecordConsumed(plan.ConsumedQuantity);
                }

                if (plan.ScrapQuantity > 0m)
                {
                    await _reservationService.ConsumeAsync(
                        new InventoryReservationMutationRequest(
                            plan.Line.ReservationId!.Value,
                            plan.ScrapQuantity,
                            userId,
                            CorrelationId: $"vas:{order.Id}:{input.IdempotencyKey}",
                            Reason: plan.Reason ?? input.Reason ?? "VAS component scrap",
                            AllocationId: plan.Line.ReservationAllocationId,
                            LedgerType: InventoryTransactionType.ValueAddedScrap),
                        cancellationToken);
                    plan.Line.RecordScrap(plan.ScrapQuantity);
                }

                await RecordInputSerialPickAsync(plan.Line, now, cancellationToken);
            }

            var productionEntries = new List<InventoryLedgerEntryRequest>();
            var outputMutations = new List<(ValueAddedServiceOrderLine Line, ValueAddedServiceOutputInput Input, decimal Quantity)>();
            var outputSequence = 1;
            foreach (var output in suppliedOutputs.Where(value => value.Quantity > 0m))
            {
                var line = outputLines.SingleOrDefault(value => value.Id == output.OrderLineId);
                if (line is null)
                {
                    throw new ArgumentException($"Output line '{output.OrderLineId}' does not belong to the VAS order.");
                }

                if (output.Quantity > line.RemainingQuantity)
                {
                    throw new InvalidOperationException(
                        $"Output quantity exceeds the remaining quantity for VAS line {line.LineNumber}.");
                }

                var effectiveOutput = output with
                {
                    LotId = output.LotId ?? (line.ProducedQuantity > 0m ? line.LotId : null),
                    SerialNumberId = output.SerialNumberId ?? (line.ProducedQuantity > 0m ? line.SerialNumberId : null),
                    SerialNumber = output.SerialNumber ?? (line.ProducedQuantity > 0m ? line.SerialNumber : null),
                    LicensePlateId = output.LicensePlateId ?? (line.ProducedQuantity > 0m ? line.LicensePlateId : null),
                    InventoryStatusId = output.InventoryStatusId ??
                        (line.ProducedQuantity > 0m ? line.InventoryStatusId : null),
                    OwnerKind = output.OwnerKind ?? (line.ProducedQuantity > 0m ? line.OwnerKind : null),
                    InventoryOwnerId = output.OwnerKind.HasValue
                        ? output.InventoryOwnerId
                        : (line.ProducedQuantity > 0m ? line.InventoryOwnerId : null),
                    OwnerCodeSnapshot = output.OwnerCodeSnapshot ??
                        (line.ProducedQuantity > 0m ? line.OwnerCodeSnapshot : null)
                };
                var ownerKind = effectiveOutput.OwnerKind ?? order.OutputOwnerKind;
                var ownerId = effectiveOutput.OwnerKind.HasValue
                    ? effectiveOutput.InventoryOwnerId
                    : order.OutputInventoryOwnerId;
                var ownerCode = InventoryOwnershipDimension.NormalizeOwnerCode(
                    ownerKind,
                    ownerId,
                    effectiveOutput.OwnerCodeSnapshot ?? (effectiveOutput.OwnerKind.HasValue
                        ? null
                        : order.OutputOwnerCodeSnapshot));
                var dimension = await ValidateOutputDimensionAsync(
                    order,
                    line,
                    effectiveOutput,
                    ownerKind,
                    ownerId,
                    ownerCode,
                    cancellationToken);
                if (line.ProducedQuantity > 0m && !SameDimension(line, dimension))
                {
                    throw new InvalidOperationException(
                        $"VAS output line {line.LineNumber} cannot change its lot, serial, license plate, status, or owner after production has started.");
                }

                line.SetProducedDimension(
                    dimension.LotId,
                    dimension.SerialNumberId,
                    dimension.SerialNumber,
                    dimension.LicensePlateId,
                    dimension.InventoryStatusId,
                    ownerKind,
                    ownerId,
                    ownerCode);

                var movement = new Movement(
                    MovementType.ValueAddedService,
                    line.ItemId,
                    new Quantity(output.Quantity),
                    userId,
                    toLocationId: order.DestinationLocationId,
                    lotId: dimension.LotId,
                    serialNumber: dimension.SerialNumber,
                    referenceNumber: input.CompletionReference ?? order.OrderNumber,
                    notes: input.Reason ?? "VAS production",
                    timestampUtc: now,
                    serialNumberId: dimension.SerialNumberId,
                    inventoryStatusId: dimension.InventoryStatusId,
                    licensePlateId: dimension.LicensePlateId,
                    toLicensePlateId: dimension.LicensePlateId,
                    ownerKind: ownerKind,
                    inventoryOwnerId: ownerId,
                    ownerCodeSnapshot: ownerCode);
                _context.Movements.Add(movement);

                var transactionGroupId = $"vas-complete:{order.Id}:{input.IdempotencyKey}";
                productionEntries.Add(new InventoryLedgerEntryRequest(
                    InventoryTransactionType.ValueAddedProduction,
                    new InventoryBalanceKey(
                        order.WarehouseId,
                        order.DestinationLocationId,
                        line.ItemId,
                        dimension.LotId,
                        dimension.SerialNumberId,
                        dimension.SerialNumber,
                        dimension.LicensePlateId,
                        dimension.InventoryStatusId,
                        line.BaseUnitOfMeasure,
                        ownerKind,
                        ownerId,
                        ownerCode),
                    output.Quantity,
                    ActorUserId: userId,
                    ReferenceType: WmsAuditEntityTypes.ValueAddedServiceOrder,
                    ReferenceId: order.OrderNumber,
                    ReferenceLine: line.LineNumber,
                    Reason: input.Reason ?? "VAS production",
                    OccurredAtUtc: now,
                    IdempotencyKey: $"{transactionGroupId}:output:{line.Id}:{outputSequence}",
                    TransactionGroupId: transactionGroupId,
                    MovementId: movement.Id > 0 ? movement.Id : null));
                outputMutations.Add((line, output, output.Quantity));
                outputSequence++;
            }

            if (productionEntries.Count > 0)
            {
                await _ledgerService.RecordAsync(productionEntries, cancellationToken);
                foreach (var mutation in outputMutations)
                {
                    await AddLegacyStockAsync(
                        order.WarehouseId,
                        order.DestinationLocationId,
                        mutation.Line,
                        mutation.Quantity,
                        cancellationToken);
                    mutation.Line.RecordProduced(mutation.Quantity);
                    await RecordOutputSerialReceiptAsync(order, mutation.Line, now, cancellationToken);
                }
            }

            var scrapQuantity = componentPlan.Value.Sum(plan => plan.ScrapQuantity);
            RecordTraceLinks(order, componentPlan.Value, outputMutations, input.IdempotencyKey, now);
            order.RecordCompletion(input.OutputQuantity, scrapQuantity, now);
            await UpdateWarehouseWorkAsync(order, userId, now, cancellationToken);

            _context.ValueAddedServiceCommands.Add(new ValueAddedServiceCommand(
                order.Id,
                "complete",
                input.IdempotencyKey,
                requestHash,
                userId,
                now));
            await _auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.ValueAddedServiceCompleted,
                    WmsAuditEntityTypes.ValueAddedServiceOrder,
                    order.OrderNumber,
                    order.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["outputQuantity"] = input.OutputQuantity,
                        ["scrapQuantity"] = scrapQuantity,
                        ["status"] = order.Status.ToString(),
                        ["idempotencyKey"] = input.IdempotencyKey
                    },
                    ActorUserId: userId),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            if (transactionStarted)
            {
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                transactionStarted = false;
            }

            var saved = await LoadOrderAsync(order.Id, cancellationToken);
            return saved is null
                ? Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Dependency(
                    "vas.order_reload_failed",
                    "The VAS completion committed but could not be reloaded.",
                    isRetryable: false))
                : Result.Success(MapOrder(saved));
        }
        catch (OperationCanceledException)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Concurrency(
                "vas.complete_concurrency",
                exception.Message));
        }
        catch (ArgumentException exception)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Validation(
                "vas.complete_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.BusinessRule(
                "vas.complete_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            _logger.LogError(exception, "VAS completion failed for order {OrderId}", orderId);
            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.FromException(
                exception,
                "vas.complete_failed",
                "The VAS completion could not be committed."));
        }
    }

    public async Task<Result<ValueAddedServiceOrderDto>> CancelAsync(
        int orderId,
        string reason,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.NotFound(
                "vas.order_not_found",
                "The VAS order was not found."));
        }

        var authorization = await _warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ValueAddedServiceManage,
            order.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ValueAddedServiceOrderDto>();
        }

        if (order.Status == ValueAddedServiceOrderStatus.Cancelled)
        {
            return Result.Success(MapOrder(order));
        }

        var transactionStarted = _context.Database.CurrentTransaction is null;
        try
        {
            if (transactionStarted)
            {
                await _unitOfWork.BeginTransactionAsync(cancellationToken);
            }

            foreach (var reservationId in order.Lines
                         .Where(line => line.ReservationId.HasValue)
                         .Select(line => line.ReservationId!.Value)
                         .Distinct())
            {
                await _reservationService.ReleaseAsync(
                    new InventoryReservationMutationRequest(
                        reservationId,
                        null,
                        userId,
                        CorrelationId: $"vas-cancel:{order.Id}",
                        Reason: reason),
                    cancellationToken);
            }

            var work = order.WarehouseWorkId.HasValue
                ? await _context.WarehouseWorks.SingleOrDefaultAsync(
                    value => value.Id == order.WarehouseWorkId.Value,
                    cancellationToken)
                : null;
            var now = _clock.UtcNow.UtcDateTime;
            if (work is not null && !work.IsTerminal)
            {
                work.Cancel(userId, reason, now);
            }

            order.Cancel(userId, reason, now);
            await _auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.ValueAddedServiceCancelled,
                    WmsAuditEntityTypes.ValueAddedServiceOrder,
                    order.OrderNumber,
                    order.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = order.Status.ToString(),
                        ["reason"] = reason
                    },
                    ActorUserId: userId),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            if (transactionStarted)
            {
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                transactionStarted = false;
            }

            return Result.Success(MapOrder(order));
        }
        catch (OperationCanceledException)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            throw;
        }
        catch (ArgumentException exception)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Validation(
                "vas.cancel_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.BusinessRule(
                "vas.cancel_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            _logger.LogError(exception, "VAS cancellation failed for order {OrderId}", orderId);
            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.FromException(
                exception,
                "vas.cancel_failed",
                "The VAS order could not be cancelled."));
        }
    }

    public async Task<Result<ValueAddedServiceOrderDto>> ReverseAsync(
        int orderId,
        ValueAddedServiceReversalInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.NotFound(
                "vas.order_not_found",
                "The VAS order was not found."));
        }

        var authorization = await _warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ValueAddedServiceManage,
            order.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ValueAddedServiceOrderDto>();
        }

        var requestHash = Hash(input);
        var replay = await FindCommandAsync(order.Id, "reverse", input.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return replay.RequestHash == requestHash
                ? Result.Success(MapOrder(order))
                : Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Conflict(
                    "vas.idempotency_reuse",
                    "The reversal idempotency key was already used with a different request."));
        }

        if (string.IsNullOrWhiteSpace(input.Reason))
        {
            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Validation(
                "vas.reversal_reason_required",
                "A reversal reason is required."));
        }

        if (order.Status is not (ValueAddedServiceOrderStatus.Completed or ValueAddedServiceOrderStatus.PartiallyCompleted))
        {
            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.BusinessRule(
                "vas.reverse_status_invalid",
                $"The VAS order cannot be reversed from {order.Status}."));
        }

        var transactionStarted = _context.Database.CurrentTransaction is null;
        try
        {
            if (transactionStarted)
            {
                await _unitOfWork.BeginTransactionAsync(cancellationToken);
            }

            var now = _clock.UtcNow.UtcDateTime;
            var entries = new List<InventoryLedgerEntryRequest>();
            var inputRestorations = new List<(ValueAddedServiceOrderLine Line, decimal Quantity)>();
            var sequence = 1;
            var transactionGroupId = $"vas-reversal:{order.Id}:{input.IdempotencyKey}";
            foreach (var line in order.Lines.Where(value => value.Kind == ValueAddedServiceLineKind.Output))
            {
                var quantity = line.ProducedQuantity - line.ReversedQuantity;
                if (quantity <= 0m)
                {
                    continue;
                }

                await RemoveLegacyStockAsync(order.DestinationLocationId, line, quantity, cancellationToken);
                entries.Add(new InventoryLedgerEntryRequest(
                    InventoryTransactionType.ValueAddedReversal,
                    new InventoryBalanceKey(
                        order.WarehouseId,
                        order.DestinationLocationId,
                        line.ItemId,
                        line.LotId,
                        line.SerialNumberId,
                        line.SerialNumber,
                        line.LicensePlateId,
                        line.InventoryStatusId,
                        line.BaseUnitOfMeasure,
                        line.OwnerKind,
                        line.InventoryOwnerId,
                        line.OwnerCodeSnapshot),
                    -quantity,
                    ActorUserId: userId,
                    ReferenceType: WmsAuditEntityTypes.ValueAddedServiceOrder,
                    ReferenceId: order.OrderNumber,
                    ReferenceLine: line.LineNumber,
                    Reason: input.Reason,
                    OccurredAtUtc: now,
                    IdempotencyKey: $"{transactionGroupId}:output:{line.Id}",
                    TransactionGroupId: transactionGroupId,
                    EntrySequence: sequence++));
                _context.Movements.Add(new Movement(
                    MovementType.ValueAddedService,
                    line.ItemId,
                    new Quantity(quantity),
                    userId,
                    fromLocationId: order.DestinationLocationId,
                    lotId: line.LotId,
                    serialNumber: line.SerialNumber,
                    referenceNumber: input.ReversalReference ?? order.OrderNumber,
                    notes: input.Reason,
                    timestampUtc: now,
                    serialNumberId: line.SerialNumberId,
                    inventoryStatusId: line.InventoryStatusId,
                    licensePlateId: line.LicensePlateId,
                    fromLicensePlateId: line.LicensePlateId,
                    ownerKind: line.OwnerKind,
                    inventoryOwnerId: line.InventoryOwnerId,
                    ownerCodeSnapshot: line.OwnerCodeSnapshot));
                line.RecordReversed(quantity);
                await RecordOutputSerialCorrectionAsync(line, input.Reason, now, cancellationToken);
            }

            foreach (var line in order.Lines.Where(value =>
                         value.Kind is ValueAddedServiceLineKind.Input or ValueAddedServiceLineKind.AdditionalInput))
            {
                var quantity = line.ConsumedQuantity + line.ScrapQuantity - line.ReversedQuantity;
                if (quantity <= 0m)
                {
                    continue;
                }

                entries.Add(new InventoryLedgerEntryRequest(
                    InventoryTransactionType.ValueAddedReversal,
                    new InventoryBalanceKey(
                        order.WarehouseId,
                        line.SourceLocationId ?? order.SourceLocationId,
                        line.ItemId,
                        line.LotId,
                        line.SerialNumberId,
                        line.SerialNumber,
                        line.LicensePlateId,
                        line.InventoryStatusId,
                        line.BaseUnitOfMeasure,
                        line.OwnerKind,
                        line.InventoryOwnerId,
                        line.OwnerCodeSnapshot),
                    quantity,
                    ActorUserId: userId,
                    ReferenceType: WmsAuditEntityTypes.ValueAddedServiceOrder,
                    ReferenceId: order.OrderNumber,
                    ReferenceLine: line.LineNumber,
                    Reason: input.Reason,
                    OccurredAtUtc: now,
                    IdempotencyKey: $"{transactionGroupId}:input:{line.Id}",
                    TransactionGroupId: transactionGroupId,
                    EntrySequence: sequence++));
                inputRestorations.Add((line, quantity));
                line.RecordReversed(quantity);
                await RecordInputSerialReceiptAsync(order, line, now, cancellationToken);
            }

            if (entries.Count > 0)
            {
                await _ledgerService.RecordAsync(entries, cancellationToken);
                foreach (var restoration in inputRestorations)
                {
                    await AddLegacyStockAsync(
                        order.WarehouseId,
                        restoration.Line.SourceLocationId ?? order.SourceLocationId,
                        restoration.Line,
                        restoration.Quantity,
                        cancellationToken);
                }
            }

            foreach (var trace in order.TraceLinks.OrderBy(value => value.Id))
            {
                var remaining = trace.Quantity - trace.ReversedQuantity;
                if (remaining > 0m)
                {
                    trace.RecordReversal(remaining);
                }
            }

            order.MarkReversed(userId, input.Reason, now);
            var work = order.WarehouseWorkId.HasValue
                ? await _context.WarehouseWorks.SingleOrDefaultAsync(
                    value => value.Id == order.WarehouseWorkId.Value,
                    cancellationToken)
                : null;
            if (work is not null && !work.IsTerminal)
            {
                work.Cancel(userId, $"VAS reversal: {input.Reason}", now);
            }

            _context.ValueAddedServiceCommands.Add(new ValueAddedServiceCommand(
                order.Id,
                "reverse",
                input.IdempotencyKey,
                requestHash,
                userId,
                now));
            await _auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.ValueAddedServiceReversed,
                    WmsAuditEntityTypes.ValueAddedServiceOrder,
                    order.OrderNumber,
                    order.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["reversedOutputQuantity"] = order.ReversedOutputQuantity,
                        ["reason"] = input.Reason
                    },
                    ActorUserId: userId),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            if (transactionStarted)
            {
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                transactionStarted = false;
            }

            return Result.Success(MapOrder(order));
        }
        catch (OperationCanceledException)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Concurrency(
                "vas.reverse_concurrency",
                exception.Message));
        }
        catch (ArgumentException exception)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.Validation(
                "vas.reverse_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.BusinessRule(
                "vas.reverse_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            _logger.LogError(exception, "VAS reversal failed for order {OrderId}", orderId);
            return Result.Failure<ValueAddedServiceOrderDto>(WmsErrors.FromException(
                exception,
                "vas.reverse_failed",
                "The VAS order could not be reversed."));
        }
    }

    public async Task<Result<ValueAddedServicePageDto>> ListOrdersAsync(
        ValueAddedServiceOrderQuery query,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ValueAddedServiceRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ValueAddedServicePageDto>();
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        var orders = _context.ValueAddedServiceOrders
            .AsNoTracking()
            .Include(order => order.OutputItem)
            .Include(order => order.Lines)
            .ThenInclude(line => line.Item)
            .AsQueryable();
        if (query.WarehouseId.HasValue)
        {
            orders = orders.Where(order => order.WarehouseId == query.WarehouseId.Value);
        }

        if (query.Status.HasValue)
        {
            orders = orders.Where(order => order.Status == query.Status.Value);
        }

        if (query.Type.HasValue)
        {
            orders = orders.Where(order => order.Type == query.Type.Value);
        }

        var total = await orders.CountAsync(cancellationToken);
        var rows = await orders
            .OrderByDescending(order => order.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Result.Success(new ValueAddedServicePageDto(
            rows.Select(MapOrder).ToArray(),
            page,
            pageSize,
            total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)));
    }

    public async Task<Result<IReadOnlyList<ValueAddedServiceGenealogyRow>>> GetGenealogyAsync(
        int orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.ValueAddedServiceOrders
            .AsNoTracking()
            .Include(value => value.OutputItem)
            .Include(value => value.Lines)
            .ThenInclude(value => value.Item)
            .Include(value => value.TraceLinks)
            .ThenInclude(value => value.InputLine)
            .ThenInclude(value => value.Item)
            .Include(value => value.TraceLinks)
            .ThenInclude(value => value.OutputLine)
            .ThenInclude(value => value!.Item)
            .SingleOrDefaultAsync(value => value.Id == orderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<IReadOnlyList<ValueAddedServiceGenealogyRow>>(WmsErrors.NotFound(
                "vas.order_not_found",
                "The VAS order was not found."));
        }

        var authorization = await _warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ValueAddedServiceRead,
            order.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<ValueAddedServiceGenealogyRow>>();
        }

        return Result.Success<IReadOnlyList<ValueAddedServiceGenealogyRow>>(
            order.TraceLinks
                .OrderBy(value => value.OccurredAtUtc)
                .ThenBy(value => value.Id)
                .Select(value => new ValueAddedServiceGenealogyRow(
                    value.Id,
                    order.Id,
                    order.OrderNumber,
                    value.Kind,
                    value.Quantity,
                    value.ReversedQuantity,
                    MapLine(value.InputLine),
                    value.OutputLine is null ? null : MapLine(value.OutputLine),
                    value.OccurredAtUtc,
                    value.IdempotencyKey))
                .ToArray());
    }

    private async Task<InventoryReservationResult> ReserveInputLineAsync(
        ValueAddedServiceOrder order,
        ValueAddedServiceOrderLine line,
        string userId,
        CancellationToken cancellationToken)
    {
        var selector = new InventoryReservationSelector(
            line.SourceLocationId ?? order.SourceLocationId,
            line.LotId,
            line.SerialNumberId,
            line.SerialNumber,
            line.LicensePlateId,
            line.InventoryStatusId,
            line.BaseUnitOfMeasure,
            line.OwnerKind,
            line.InventoryOwnerId,
            line.OwnerCodeSnapshot);
        var request = new InventoryReservationRequest(
            DemandType,
            order.OrderNumber,
            line.LineNumber,
            order.WarehouseId,
            line.ItemId,
            line.PlannedQuantity,
            Selector: selector,
            ActorUserId: userId,
            CorrelationId: $"vas-release:{order.Id}",
            Reason: line.IsSubstitution ? "VAS approved component substitution" : "VAS component reservation");
        var simulation = await _reservationService.SimulateAsync(request, cancellationToken);
        if (simulation.BackorderQuantity > 0m)
        {
            var substituted = await TryApplySubstitutionAsync(order, line, userId, cancellationToken);
            if (substituted)
            {
                selector = new InventoryReservationSelector(
                    line.SourceLocationId ?? order.SourceLocationId,
                    line.LotId,
                    line.SerialNumberId,
                    line.SerialNumber,
                    line.LicensePlateId,
                    line.InventoryStatusId,
                    line.BaseUnitOfMeasure,
                    line.OwnerKind,
                    line.InventoryOwnerId,
                    line.OwnerCodeSnapshot);
                request = request with
                {
                    ItemId = line.ItemId,
                    Selector = selector,
                    Reason = "VAS approved component substitution"
                };
                simulation = await _reservationService.SimulateAsync(request, cancellationToken);
            }

            if (simulation.BackorderQuantity > 0m)
            {
                throw new InvalidOperationException(
                    $"VAS input line {line.LineNumber} has a shortage of {simulation.BackorderQuantity.ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        var reservation = await _reservationService.ReserveAsync(request, cancellationToken);
        if (reservation.ActiveQuantity < line.PlannedQuantity)
        {
            throw new InvalidOperationException(
                $"VAS input line {line.LineNumber} could not be fully reserved.");
        }

        return reservation;
    }

    private async Task<bool> TryApplySubstitutionAsync(
        ValueAddedServiceOrder order,
        ValueAddedServiceOrderLine line,
        string userId,
        CancellationToken cancellationToken)
    {
        if (!line.KitDefinitionLineId.HasValue)
        {
            return false;
        }

        var kitLine = await _context.KitDefinitionLines
            .SingleOrDefaultAsync(value => value.Id == line.KitDefinitionLineId.Value, cancellationToken);
        if (kitLine is null || kitLine.SubstitutionPolicy != KitSubstitutionPolicy.ApprovedItems ||
            string.IsNullOrWhiteSpace(kitLine.ApprovedSubstitutionItemIdsJson))
        {
            return false;
        }

        int[] substitutions;
        try
        {
            substitutions = JsonSerializer.Deserialize<int[]>(kitLine.ApprovedSubstitutionItemIdsJson) ?? [];
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"Kit line {kitLine.Sequence} contains invalid approved substitution data.");
        }

        foreach (var itemId in substitutions.Where(value => value > 0).Distinct())
        {
            var item = await _context.Items
                .SingleOrDefaultAsync(value => value.Id == itemId && value.IsActive, cancellationToken);
            if (item is null || !string.Equals(item.UnitOfMeasure, line.BaseUnitOfMeasure, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var selector = new InventoryReservationSelector(
                line.SourceLocationId ?? order.SourceLocationId,
                null,
                null,
                null,
                null,
                line.InventoryStatusId,
                item.UnitOfMeasure,
                line.OwnerKind,
                line.InventoryOwnerId,
                line.OwnerCodeSnapshot);
            var simulation = await _reservationService.SimulateAsync(
                new InventoryReservationRequest(
                    DemandType,
                    order.OrderNumber,
                    line.LineNumber,
                    order.WarehouseId,
                    item.Id,
                    line.PlannedQuantity,
                    Selector: selector,
                    ActorUserId: userId,
                    CorrelationId: $"vas-release:{order.Id}",
                    Reason: "VAS approved component substitution"),
                cancellationToken);
            if (simulation.BackorderQuantity == 0m)
            {
                line.ReplaceWithSubstitution(
                    item.Id,
                    line.PlannedQuantity,
                    item.UnitOfMeasure,
                    kitLine.ComponentItemId);
                return true;
            }
        }

        return false;
    }

    private static void AttachReservationAllocations(
        ValueAddedServiceOrder order,
        ValueAddedServiceOrderLine line,
        InventoryReservationResult reservation)
    {
        var allocations = reservation.Allocations
            .Where(value => value.AllocatedQuantity > 0m)
            .OrderBy(value => value.AllocationId)
            .ToArray();
        if (allocations.Length == 0)
        {
            throw new InvalidOperationException("The VAS reservation returned no allocations.");
        }

        line.AttachReservation(reservation.ReservationId, allocations[0]);
        var nextLine = order.Lines.Max(value => value.LineNumber) + 1;
        foreach (var allocation in allocations.Skip(1))
        {
            var split = new ValueAddedServiceOrderLine(
                nextLine++,
                line.Kind,
                allocation.ItemId,
                allocation.AllocatedQuantity,
                allocation.BaseUnitOfMeasure,
                allocation.InventoryStatusId,
                sourceLocationId: allocation.LocationId,
                lotId: allocation.LotId,
                serialNumberId: allocation.SerialNumberId,
                serialNumber: allocation.SerialNumber,
                licensePlateId: allocation.LicensePlateId,
                ownerKind: allocation.OwnerKind,
                inventoryOwnerId: allocation.InventoryOwnerId,
                ownerCodeSnapshot: allocation.OwnerCodeSnapshot,
                kitDefinitionLineId: line.KitDefinitionLineId,
                reservationId: reservation.ReservationId,
                reservationAllocationId: allocation.AllocationId,
                isSubstitution: line.IsSubstitution,
                substitutedForItemId: line.SubstitutedForItemId,
                notes: line.Notes);
            order.AddLine(split);
        }
    }

    private static Result<IReadOnlyList<ComponentPlan>> BuildComponentPlan(
        ValueAddedServiceOrder order,
        ValueAddedServiceCompletionInput input)
    {
        var inputLines = order.Lines
            .Where(line => line.Kind is ValueAddedServiceLineKind.Input or ValueAddedServiceLineKind.AdditionalInput)
            .ToDictionary(line => line.Id);
        var explicitRows = input.Components ?? [];
        if (explicitRows.GroupBy(value => value.OrderLineId).Any(group => group.Count() > 1))
        {
            return Result.Failure<IReadOnlyList<ComponentPlan>>(WmsErrors.Validation(
                "vas.complete_component_duplicate",
                "Each VAS input line may appear only once in a completion."));
        }

        var plans = new List<ComponentPlan>(inputLines.Count);
        foreach (var line in inputLines.Values.OrderBy(value => value.LineNumber))
        {
            var explicitRow = explicitRows.SingleOrDefault(value => value.OrderLineId == line.Id);
            if (explicitRow is null && explicitRows.Any(value => value.OrderLineId == line.Id))
            {
                continue;
            }

            if (explicitRow is not null &&
                (explicitRow.ConsumedQuantity < 0m || explicitRow.ScrapQuantity < 0m))
            {
                return Result.Failure<IReadOnlyList<ComponentPlan>>(WmsErrors.Validation(
                    "vas.complete_component_quantity_invalid",
                    "Component consumption and scrap quantities cannot be negative."));
            }

            var expected = line.PlannedQuantity * input.OutputQuantity / order.RequestedOutputQuantity;
            var consumed = explicitRow?.ConsumedQuantity ?? expected;
            var scrap = explicitRow?.ScrapQuantity ?? 0m;
            if (consumed + scrap > line.RemainingQuantity)
            {
                return Result.Failure<IReadOnlyList<ComponentPlan>>(WmsErrors.Validation(
                    "vas.complete_component_exceeds_plan",
                    $"Component line {line.LineNumber} exceeds its remaining planned quantity."));
            }

            if (consumed + scrap + 0.0000001m < expected)
            {
                return Result.Failure<IReadOnlyList<ComponentPlan>>(WmsErrors.Validation(
                    "vas.complete_component_underreported",
                    $"Component line {line.LineNumber} must account for at least {expected.ToString(CultureInfo.InvariantCulture)} units for this output quantity."));
            }

            plans.Add(new ComponentPlan(line, consumed, scrap, explicitRow?.Reason));
        }

        foreach (var row in explicitRows.Where(value => !inputLines.ContainsKey(value.OrderLineId)))
        {
            return Result.Failure<IReadOnlyList<ComponentPlan>>(WmsErrors.Validation(
                "vas.complete_component_line_invalid",
                $"Component line {row.OrderLineId} does not belong to the VAS order."));
        }

        return Result.Success<IReadOnlyList<ComponentPlan>>(plans);
    }

    private async Task<OutputDimension> ValidateOutputDimensionAsync(
        ValueAddedServiceOrder order,
        ValueAddedServiceOrderLine line,
        ValueAddedServiceOutputInput output,
        InventoryOwnerKind ownerKind,
        int? ownerId,
        string ownerCode,
        CancellationToken cancellationToken)
    {
        var statusId = output.InventoryStatusId ??
                       (line.ProducedQuantity > 0m ? line.InventoryStatusId : InventoryStatusSystemIds.Available);
        var status = await _context.InventoryStatuses
            .SingleOrDefaultAsync(value => value.Id == statusId && value.IsActive, cancellationToken);
        if (status is null || !status.IsAvailable)
        {
            throw new InvalidOperationException("Finished VAS stock requires an active available inventory status.");
        }

        var lot = output.LotId.HasValue
            ? await _context.Lots.SingleOrDefaultAsync(value => value.Id == output.LotId.Value, cancellationToken)
            : null;
        if (output.LotId.HasValue && (lot is null || !lot.IsActive || lot.ItemId != line.ItemId))
        {
            throw new InvalidOperationException("The output lot is missing, inactive, or belongs to another item.");
        }

        var serial = output.SerialNumberId.HasValue
            ? await _context.SerialNumbers.SingleOrDefaultAsync(
                value => value.Id == output.SerialNumberId.Value,
                cancellationToken)
            : null;
        if (output.SerialNumberId.HasValue &&
            (serial is null || serial.ItemId != line.ItemId || serial.LotId != output.LotId))
        {
            throw new InvalidOperationException("The output serial does not match the output item and lot.");
        }

        var serialNumber = serial?.Number ?? NormalizeOptional(output.SerialNumber);
        if (serial is not null && !string.IsNullOrWhiteSpace(output.SerialNumber) &&
            !string.Equals(serial.Number, output.SerialNumber.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The output serial number text does not match the selected serial.");
        }

        var licensePlate = output.LicensePlateId.HasValue
            ? await _context.LicensePlates.SingleOrDefaultAsync(
                value => value.Id == output.LicensePlateId.Value,
                cancellationToken)
            : null;
        if (licensePlate is not null &&
            (!licensePlate.IsActive || licensePlate.WarehouseId != order.WarehouseId ||
             (licensePlate.CurrentLocationId.HasValue && licensePlate.CurrentLocationId != order.DestinationLocationId)))
        {
            throw new InvalidOperationException("The output license plate is not active at the destination warehouse/location.");
        }

        await EnsureOwnerExistsAsync(ownerKind, ownerId, ownerCode, cancellationToken);
        return new OutputDimension(
            output.LotId,
            output.SerialNumberId,
            serialNumber,
            output.LicensePlateId,
            statusId,
            ownerKind,
            ownerId,
            ownerCode);
    }

    private async Task ApplyLegacyConsumptionAsync(
        ValueAddedServiceOrderLine line,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        var sourceLocationId = line.SourceLocationId;
        if (!sourceLocationId.HasValue)
        {
            throw new InvalidOperationException($"VAS input line {line.LineNumber} has no allocated source location.");
        }

        var stock = await _context.Stock.SingleOrDefaultAsync(value =>
            value.ItemId == line.ItemId &&
            value.LocationId == sourceLocationId.Value &&
            value.LotId == line.LotId &&
            value.SerialNumberId == line.SerialNumberId &&
            value.SerialNumber == line.SerialNumber &&
            value.InventoryStatusId == line.InventoryStatusId &&
            value.LicensePlateId == line.LicensePlateId &&
            value.OwnerKind == line.OwnerKind &&
            value.InventoryOwnerId == line.InventoryOwnerId &&
            value.OwnerCodeSnapshot == line.OwnerCodeSnapshot,
            cancellationToken);
        if (stock is null)
        {
            return;
        }

        if (stock.QuantityAvailable.Value < quantity)
        {
            throw new InvalidOperationException(
                $"VAS input line {line.LineNumber} no longer has enough physical stock.");
        }

        stock.RemoveQuantity(new Quantity(quantity));
        if (stock.QuantityReserved.Value >= quantity)
        {
            stock.ReleaseReservation(new Quantity(quantity));
        }
    }

    private async Task AddLegacyStockAsync(
        int warehouseId,
        int locationId,
        ValueAddedServiceOrderLine line,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        var stock = await _context.Stock.SingleOrDefaultAsync(value =>
            value.ItemId == line.ItemId &&
            value.LocationId == locationId &&
            value.LotId == line.LotId &&
            value.SerialNumberId == line.SerialNumberId &&
            value.SerialNumber == line.SerialNumber &&
            value.InventoryStatusId == line.InventoryStatusId &&
            value.LicensePlateId == line.LicensePlateId &&
            value.OwnerKind == line.OwnerKind &&
            value.InventoryOwnerId == line.InventoryOwnerId &&
            value.OwnerCodeSnapshot == line.OwnerCodeSnapshot,
            cancellationToken);
        if (stock is null)
        {
            _context.Stock.Add(new Stock(
                line.ItemId,
                locationId,
                new Quantity(quantity),
                line.LotId,
                line.SerialNumber,
                line.SerialNumberId,
                line.InventoryStatusId,
                line.LicensePlateId,
                line.OwnerKind,
                line.InventoryOwnerId,
                line.OwnerCodeSnapshot));
        }
        else
        {
            stock.AddQuantity(new Quantity(quantity));
        }
    }

    private async Task RemoveLegacyStockAsync(
        int locationId,
        ValueAddedServiceOrderLine line,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        var stock = await _context.Stock.SingleOrDefaultAsync(value =>
            value.ItemId == line.ItemId &&
            value.LocationId == locationId &&
            value.LotId == line.LotId &&
            value.SerialNumberId == line.SerialNumberId &&
            value.SerialNumber == line.SerialNumber &&
            value.InventoryStatusId == line.InventoryStatusId &&
            value.LicensePlateId == line.LicensePlateId &&
            value.OwnerKind == line.OwnerKind &&
            value.InventoryOwnerId == line.InventoryOwnerId &&
            value.OwnerCodeSnapshot == line.OwnerCodeSnapshot,
            cancellationToken);
        if (stock is null || stock.QuantityAvailable.Value < quantity)
        {
            throw new InvalidOperationException(
                $"The produced VAS stock for line {line.LineNumber} is no longer available for reversal.");
        }

        stock.RemoveQuantity(new Quantity(quantity));
    }

    private async Task RecordInputSerialPickAsync(
        ValueAddedServiceOrderLine line,
        DateTime timestampUtc,
        CancellationToken cancellationToken)
    {
        if (!line.SerialNumberId.HasValue)
        {
            return;
        }

        var serial = await _context.SerialNumbers
            .SingleOrDefaultAsync(value => value.Id == line.SerialNumberId.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Input serial '{line.SerialNumberId}' was not found.");
        serial.RecordPick(timestampUtc);
    }

    private async Task RecordOutputSerialReceiptAsync(
        ValueAddedServiceOrder order,
        ValueAddedServiceOrderLine line,
        DateTime timestampUtc,
        CancellationToken cancellationToken)
    {
        if (!line.SerialNumberId.HasValue)
        {
            return;
        }

        var serial = await _context.SerialNumbers
            .SingleOrDefaultAsync(value => value.Id == line.SerialNumberId.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Output serial '{line.SerialNumberId}' was not found.");
        if (!serial.CurrentLocationId.HasValue)
        {
            serial.RecordReceipt(
                order.WarehouseId,
                order.DestinationLocationId,
                line.LotId,
                order.OrderNumber,
                quarantine: false,
                timestampUtc,
                line.LicensePlateId);
        }
        else if (serial.CurrentLocationId != order.DestinationLocationId)
        {
            throw new InvalidOperationException("The output serial is already located elsewhere.");
        }
    }

    private async Task RecordOutputSerialCorrectionAsync(
        ValueAddedServiceOrderLine line,
        string reason,
        DateTime timestampUtc,
        CancellationToken cancellationToken)
    {
        if (!line.SerialNumberId.HasValue)
        {
            return;
        }

        var serial = await _context.SerialNumbers
            .SingleOrDefaultAsync(value => value.Id == line.SerialNumberId.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Output serial '{line.SerialNumberId}' was not found.");
        if (serial.CurrentLocationId.HasValue)
        {
            serial.RecordCorrection(reason, timestampUtc);
        }
    }

    private async Task RecordInputSerialReceiptAsync(
        ValueAddedServiceOrder order,
        ValueAddedServiceOrderLine line,
        DateTime timestampUtc,
        CancellationToken cancellationToken)
    {
        if (!line.SerialNumberId.HasValue)
        {
            return;
        }

        var serial = await _context.SerialNumbers
            .SingleOrDefaultAsync(value => value.Id == line.SerialNumberId.Value, cancellationToken)
            ?? throw new InvalidOperationException($"Input serial '{line.SerialNumberId}' was not found.");
        if (!serial.CurrentLocationId.HasValue)
        {
            serial.RecordReceipt(
                order.WarehouseId,
                line.SourceLocationId ?? order.SourceLocationId,
                line.LotId,
                order.OrderNumber,
                quarantine: false,
                timestampUtc,
                line.LicensePlateId);
        }
    }

    private async Task UpdateWarehouseWorkAsync(
        ValueAddedServiceOrder order,
        string userId,
        DateTime timestampUtc,
        CancellationToken cancellationToken)
    {
        if (!order.WarehouseWorkId.HasValue)
        {
            return;
        }

        var work = await _context.WarehouseWorks
            .Include(value => value.Lines)
            .SingleOrDefaultAsync(value => value.Id == order.WarehouseWorkId.Value, cancellationToken)
            ?? throw new InvalidOperationException("The VAS warehouse-work item was not found.");
        if (work.Status == WarehouseWorkStatus.Available)
        {
            work.Assign(userId, work.TeamCode, userId, timestampUtc);
        }

        if (work.Status == WarehouseWorkStatus.Assigned)
        {
            work.Start(userId, timestampUtc);
        }

        foreach (var workLine in work.Lines)
        {
            var orderLine = order.Lines.SingleOrDefault(value => value.LineNumber == workLine.Sequence)
                ?? throw new InvalidOperationException("A VAS warehouse-work line is missing its order line.");
            var actual = orderLine.Kind == ValueAddedServiceLineKind.Output
                ? orderLine.ProducedQuantity
                : orderLine.ConsumedQuantity + orderLine.ScrapQuantity;
            workLine.RecordActualQuantity(actual);
        }

        if (order.Status == ValueAddedServiceOrderStatus.Completed)
        {
            if (work.Status != WarehouseWorkStatus.InProgress)
            {
                work.Start(userId, timestampUtc);
            }

            work.Complete(userId, timestampUtc);
        }
    }

    private static void RecordTraceLinks(
        ValueAddedServiceOrder order,
        IReadOnlyList<ComponentPlan> componentPlans,
        List<(ValueAddedServiceOrderLine Line, ValueAddedServiceOutputInput Input, decimal Quantity)> outputs,
        string idempotencyKey,
        DateTime occurredAtUtc)
    {
        var outputTotal = outputs.Sum(value => value.Quantity);
        var sequence = 1;
        foreach (var plan in componentPlans.Where(value => value.ConsumedQuantity > 0m))
        {
            var remaining = plan.ConsumedQuantity;
            foreach (var output in outputs.Where(value => value.Quantity > 0m))
            {
                var quantity = outputTotal == 0m
                    ? 0m
                    : Math.Min(remaining, plan.ConsumedQuantity * output.Quantity / outputTotal);
                if (quantity <= 0m)
                {
                    continue;
                }

                order.TraceLinks.Add(new ValueAddedServiceTraceLink(
                    order.Id,
                    plan.Line.Id,
                    output.Line.Id,
                    ValueAddedServiceTraceKind.MaterialToOutput,
                    quantity,
                    $"{idempotencyKey}:output:{plan.Line.Id}:{output.Line.Id}:{sequence++}",
                    occurredAtUtc));
                remaining -= quantity;
            }

            if (remaining > 0.0000001m && outputs.Count > 0)
            {
                var output = outputs[0];
                order.TraceLinks.Add(new ValueAddedServiceTraceLink(
                    order.Id,
                    plan.Line.Id,
                    output.Line.Id,
                    ValueAddedServiceTraceKind.MaterialToOutput,
                    remaining,
                    $"{idempotencyKey}:output-remainder:{plan.Line.Id}:{sequence++}",
                    occurredAtUtc));
            }
        }

        foreach (var plan in componentPlans.Where(value => value.ScrapQuantity > 0m))
        {
            order.TraceLinks.Add(new ValueAddedServiceTraceLink(
                order.Id,
                plan.Line.Id,
                null,
                ValueAddedServiceTraceKind.MaterialToScrap,
                plan.ScrapQuantity,
                $"{idempotencyKey}:scrap:{plan.Line.Id}:{sequence++}",
                occurredAtUtc));
        }
    }

    private async Task<Result> ValidateOwnerAsync(
        InventoryOwnerKind kind,
        int? ownerId,
        string? ownerCode,
        CancellationToken cancellationToken)
    {
        try
        {
            var normalized = InventoryOwnershipDimension.NormalizeOwnerCode(kind, ownerId, ownerCode);
            await EnsureOwnerExistsAsync(kind, ownerId, normalized, cancellationToken);
            return Result.Success();
        }
        catch (ArgumentException exception)
        {
            return Result.Failure(WmsErrors.Validation(
                "vas.owner_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure(WmsErrors.Validation(
                "vas.owner_invalid",
                exception.Message));
        }
    }

    private async Task EnsureOwnerExistsAsync(
        InventoryOwnerKind kind,
        int? ownerId,
        string ownerCode,
        CancellationToken cancellationToken)
    {
        if (kind == InventoryOwnerKind.CompanyOwned)
        {
            return;
        }

        var owner = await _context.InventoryOwners
            .SingleOrDefaultAsync(value => value.Id == ownerId, cancellationToken);
        if (owner is null || !owner.IsActive || owner.Kind != kind || owner.OwnerCode != ownerCode)
        {
            throw new InvalidOperationException(
                "The VAS inventory owner is missing, inactive, or does not match its owner-code snapshot.");
        }
    }

    private async Task ReleaseReservationsBestEffortAsync(
        IEnumerable<int> reservationIds,
        string userId,
        CancellationToken cancellationToken)
    {
        foreach (var reservationId in reservationIds.Distinct())
        {
            try
            {
                await _reservationService.ReleaseAsync(
                    new InventoryReservationMutationRequest(
                        reservationId,
                        null,
                        userId,
                        CorrelationId: "vas-release-cleanup",
                        Reason: "VAS release compensation"),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "VAS release compensation failed for reservation {ReservationId}", reservationId);
            }
        }
    }

    private async Task<KitDefinition?> LoadKitAsync(int definitionId, CancellationToken cancellationToken) =>
        await _context.KitDefinitions
            .AsNoTracking()
            .Include(value => value.OutputItem)
            .Include(value => value.Lines)
            .ThenInclude(value => value.ComponentItem)
            .SingleOrDefaultAsync(value => value.Id == definitionId, cancellationToken);

    private async Task<ValueAddedServiceOrder?> LoadOrderAsync(
        int orderId,
        CancellationToken cancellationToken,
        bool tracking = true)
    {
        var query = _context.ValueAddedServiceOrders
            .Include(value => value.OutputItem)
            .Include(value => value.Lines)
            .ThenInclude(value => value.Item)
            .Include(value => value.TraceLinks)
            .AsQueryable();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(value => value.Id == orderId, cancellationToken);
    }

    private async Task<ValueAddedServiceOrder?> LoadOrderByIdempotencyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await _context.ValueAddedServiceOrders
            .AsNoTracking()
            .Include(value => value.OutputItem)
            .Include(value => value.Lines)
            .ThenInclude(value => value.Item)
            .SingleOrDefaultAsync(value => value.IdempotencyKey == idempotencyKey.Trim(), cancellationToken);

    private async Task<ValueAddedServiceCommand?> FindCommandAsync(
        int orderId,
        string operation,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await _context.ValueAddedServiceCommands
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.ValueAddedServiceOrderId == orderId &&
                                           value.Operation == operation &&
                                           value.IdempotencyKey == idempotencyKey.Trim(),
                cancellationToken);

    private static bool MatchesOrder(
        ValueAddedServiceOrder order,
        ValueAddedServiceOrderInput input) =>
        order.Type == input.Type &&
        order.WarehouseId == input.WarehouseId &&
        order.SourceLocationId == input.SourceLocationId &&
        order.DestinationLocationId == input.DestinationLocationId &&
        order.OutputItemId == input.OutputItemId &&
        order.RequestedOutputQuantity == input.RequestedOutputQuantity &&
        order.KitDefinitionId == input.KitDefinitionId;

    private static ValueAddedServiceOrderLine CreateInputLine(
        int lineNumber,
        Item item,
        decimal quantity,
        int sourceLocationId,
        int inventoryStatusId,
        ValueAddedServiceInputSelector? selector,
        string ownerCode,
        string notes) =>
        new(
            lineNumber,
            ValueAddedServiceLineKind.Input,
            item.Id,
            quantity,
            item.UnitOfMeasure,
            inventoryStatusId,
            sourceLocationId: sourceLocationId,
            lotId: selector?.LotId,
            serialNumberId: selector?.SerialNumberId,
            serialNumber: selector?.SerialNumber,
            licensePlateId: selector?.LicensePlateId,
            ownerKind: selector?.OwnerKind ?? InventoryOwnerKind.CompanyOwned,
            inventoryOwnerId: selector?.InventoryOwnerId,
            ownerCodeSnapshot: ownerCode,
            notes: notes);

    private static string BuildDimensionSnapshot(ValueAddedServiceOrderLine line) =>
        JsonSerializer.Serialize(new
        {
            line.LotId,
            line.SerialNumberId,
            line.SerialNumber,
            line.LicensePlateId,
            line.InventoryStatusId,
            line.OwnerKind,
            line.InventoryOwnerId,
            line.OwnerCodeSnapshot
        });

    private static bool SameDimension(
        ValueAddedServiceOrderLine line,
        OutputDimension dimension) =>
        line.LotId == dimension.LotId &&
        line.SerialNumberId == dimension.SerialNumberId &&
        string.Equals(line.SerialNumber, dimension.SerialNumber, StringComparison.OrdinalIgnoreCase) &&
        line.LicensePlateId == dimension.LicensePlateId &&
        line.InventoryStatusId == dimension.InventoryStatusId &&
        line.OwnerKind == dimension.OwnerKind &&
        line.InventoryOwnerId == dimension.OwnerId &&
        line.OwnerCodeSnapshot == dimension.OwnerCode;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string Hash<T>(T value)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static KitDefinitionDto MapKit(KitDefinition definition) =>
        new(
            definition.Id,
            definition.Code,
            definition.Version,
            definition.OutputItemId,
            definition.OutputItem?.Sku ?? string.Empty,
            definition.OutputItem?.Name ?? string.Empty,
            definition.OutputUnitOfMeasure,
            definition.EffectiveFromUtc,
            definition.EffectiveToUtc,
            definition.Instructions,
            definition.LocalizedInstructions,
            definition.IsActive,
            definition.Revision,
            definition.Lines
                .OrderBy(value => value.Sequence)
                .Select(value => new KitDefinitionLineDto(
                    value.Id,
                    value.Sequence,
                    value.ComponentItemId,
                    value.ComponentItem?.Sku ?? string.Empty,
                    value.ComponentItem?.Name ?? string.Empty,
                    value.QuantityPerOutput,
                    value.ComponentUnitOfMeasure,
                    value.SubstitutionPolicy,
                    value.ApprovedSubstitutionItemIdsJson,
                    value.Notes,
                    value.Revision))
                .ToArray());

    private static ValueAddedServiceOrderDto MapOrder(ValueAddedServiceOrder order) =>
        new(
            order.Id,
            order.OrderNumber,
            order.IdempotencyKey,
            order.Type,
            order.Status,
            order.WarehouseId,
            order.SourceLocationId,
            order.DestinationLocationId,
            order.OutputItemId,
            order.OutputItem?.Sku ?? string.Empty,
            order.OutputItem?.Name ?? string.Empty,
            order.RequestedOutputQuantity,
            order.CompletedOutputQuantity,
            order.ScrapQuantity,
            order.ReversedOutputQuantity,
            order.OutputUnitOfMeasure,
            order.KitDefinitionId,
            order.KitVersionSnapshot,
            order.InstructionSnapshot,
            order.LocalizedInstructionSnapshot,
            order.StationCode,
            order.LabelTemplateCode,
            order.QualityProfileId,
            order.OutputOwnerKind,
            order.OutputInventoryOwnerId,
            order.OutputOwnerCodeSnapshot,
            order.WarehouseWorkId,
            order.CreatedByUserId,
            order.CreatedAtUtc,
            order.ReleasedAtUtc,
            order.CompletedAtUtc,
            order.CancelledAtUtc,
            order.CancellationReason,
            order.ReversedAtUtc,
            order.ReversalReason,
            order.ExceptionReason,
            order.Revision,
            order.Lines.OrderBy(value => value.LineNumber).Select(MapLine).ToArray());

    private static ValueAddedServiceOrderLineDto MapLine(ValueAddedServiceOrderLine line) =>
        new(
            line.Id,
            line.LineNumber,
            line.Kind,
            line.ItemId,
            line.Item?.Sku ?? string.Empty,
            line.Item?.Name ?? string.Empty,
            line.PlannedQuantity,
            line.ConsumedQuantity,
            line.ProducedQuantity,
            line.ScrapQuantity,
            line.ReversedQuantity,
            line.RemainingQuantity,
            line.BaseUnitOfMeasure,
            line.InventoryStatusId,
            line.SourceLocationId,
            line.DestinationLocationId,
            line.LotId,
            line.SerialNumberId,
            line.SerialNumber,
            line.LicensePlateId,
            line.OwnerKind,
            line.InventoryOwnerId,
            line.OwnerCodeSnapshot,
            line.KitDefinitionLineId,
            line.ReservationId,
            line.ReservationAllocationId,
            line.IsSubstitution,
            line.SubstitutedForItemId,
            line.Notes,
            line.Revision);

    private sealed record ComponentPlan(
        ValueAddedServiceOrderLine Line,
        decimal ConsumedQuantity,
        decimal ScrapQuantity,
        string? Reason);

    private sealed record OutputDimension(
        int? LotId,
        int? SerialNumberId,
        string? SerialNumber,
        int? LicensePlateId,
        int InventoryStatusId,
        InventoryOwnerKind OwnerKind,
        int? OwnerId,
        string OwnerCode);
}
