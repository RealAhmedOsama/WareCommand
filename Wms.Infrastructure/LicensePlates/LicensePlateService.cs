using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identification;
using Wms.Application.LicensePlates;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Identification;
using Wms.Domain.Inventory;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;

namespace Wms.Infrastructure.LicensePlates;

public sealed class LicensePlateService : ILicensePlateService
{
    private readonly IAuditWriter _auditWriter;
    private readonly IClock _clock;
    private readonly IIdentificationRegistry? _identificationRegistry;
    private readonly ILogger<LicensePlateService> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInventoryLedgerService? _inventoryLedgerService;

    public LicensePlateService(
        IUnitOfWork unitOfWork,
        IAuditWriter auditWriter,
        IClock clock,
        ILogger<LicensePlateService> logger,
        IIdentificationRegistry? identificationRegistry = null,
        IInventoryLedgerService? inventoryLedgerService = null)
    {
        _unitOfWork = unitOfWork;
        _auditWriter = auditWriter;
        _clock = clock;
        _logger = logger;
        _identificationRegistry = identificationRegistry;
        _inventoryLedgerService = inventoryLedgerService;
    }

    public Task<Result<IReadOnlyList<LicensePlateDto>>> SearchAsync(
        LicensePlateSearchQuery query,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            async () =>
            {
                var plates = await _unitOfWork.LicensePlates.SearchAsync(
                    query.WarehouseId,
                    query.Number,
                    query.Status,
                    query.IncludeVoided,
                    query.Page,
                    query.PageSize,
                    cancellationToken);
                return Result.Success<IReadOnlyList<LicensePlateDto>>(
                    plates.Select(ToDto).ToArray());
            },
            "license_plate.search_failed",
            "License plates could not be loaded.");

    public Task<Result<LicensePlateDto>> GetAsync(
        int licensePlateId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            async () =>
            {
                var plate = await _unitOfWork.LicensePlates.GetByIdWithContentsAsync(
                    licensePlateId,
                    cancellationToken);
                return plate is null
                    ? Result.Failure<LicensePlateDto>(WmsErrors.NotFound(
                        "license_plate.not_found",
                        $"License plate {licensePlateId} was not found."))
                    : Result.Success(ToDto(plate));
            },
            "license_plate.get_failed",
            "The license plate could not be loaded.");

    public Task<Result<LicensePlateDto>> CreateAsync(
        LicensePlateCreateInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(
            async () => await CreateInternalAsync(input, userId, cancellationToken),
            "license_plate.create_failed",
            "The license plate could not be created.",
            cancellationToken);

    public Task<Result<LicensePlateDto>> AddContentAsync(
        int licensePlateId,
        LicensePlateContentInput input,
        string userId,
        string? referenceNumber = null,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(
            async () =>
            {
                var plate = await RequirePlateAsync(licensePlateId, cancellationToken);
                await AddContentInternalAsync(plate, input, userId, referenceNumber, cancellationToken);
                return plate;
            },
            "license_plate.content_add_failed",
            "License plate content could not be added.",
            cancellationToken);

    public Task<Result<LicensePlateDto>> MoveContentAsync(
        int sourceLicensePlateId,
        int targetLicensePlateId,
        LicensePlateContentInput input,
        string userId,
        string? referenceNumber = null,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(
            async () =>
            {
                var source = await RequirePlateAsync(sourceLicensePlateId, cancellationToken);
                var target = await RequirePlateAsync(targetLicensePlateId, cancellationToken);
                await MoveContentInternalAsync(
                    source,
                    target,
                    input,
                    userId,
                    referenceNumber,
                    LicensePlateHistoryAction.ContentMoved,
                    cancellationToken);
                return target;
            },
            "license_plate.content_move_failed",
            "License plate content could not be moved.",
            cancellationToken);

    public Task<Result<LicensePlateDto>> SplitContentAsync(
        int sourceLicensePlateId,
        LicensePlateContentInput input,
        LicensePlateCreateInput destination,
        string userId,
        string? referenceNumber = null,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(
            async () =>
            {
                var source = await RequirePlateAsync(sourceLicensePlateId, cancellationToken);
                if (destination.WarehouseId != source.WarehouseId)
                {
                    throw new InvalidOperationException("A split license plate must remain in the same warehouse.");
                }

                var target = await CreateInternalAsync(destination with
                {
                    CurrentLocationId = destination.CurrentLocationId ?? source.CurrentLocationId
                }, userId, cancellationToken);
                await MoveContentInternalAsync(
                    source,
                    target,
                    input,
                    userId,
                    referenceNumber,
                    LicensePlateHistoryAction.ContentSplit,
                    cancellationToken);
                return target;
            },
            "license_plate.content_split_failed",
            "License plate content could not be split.",
            cancellationToken);

    public Task<Result<LicensePlateDto>> MergeAsync(
        int sourceLicensePlateId,
        int targetLicensePlateId,
        string userId,
        string? referenceNumber = null,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(
            async () =>
            {
                var source = await RequirePlateAsync(sourceLicensePlateId, cancellationToken);
                var target = await RequirePlateAsync(targetLicensePlateId, cancellationToken);
                EnsureDifferentPlates(source, target);
                EnsureSameWarehouse(source, target);
                EnsureMutable(source);
                EnsureMutable(target);
                if (source.CurrentLocationId != target.CurrentLocationId)
                {
                    throw new InvalidOperationException("License plates must be in the same location before merging.");
                }

                var contents = (await _unitOfWork.LicensePlates.GetContentsAsync(source.Id, cancellationToken))
                    .Where(content => content.Quantity.Value > 0)
                    .ToArray();
                foreach (var content in contents)
                {
                    await MoveContentInternalAsync(
                        source,
                        target,
                        ToInput(content),
                        userId,
                        referenceNumber,
                        LicensePlateHistoryAction.ContentMerged,
                        cancellationToken);
                }

                var previousStatus = source.Status;
                source.Close();
                await AddHistoryAsync(
                    source,
                    LicensePlateHistoryAction.Closed,
                    userId,
                    referenceNumber: referenceNumber,
                    reason: "merged",
                    cancellationToken: cancellationToken);
                await RecordLifecycleAuditAsync(
                    source,
                    "merged",
                    userId,
                    previousStatus,
                    referenceNumber,
                    "merged",
                    cancellationToken);
                return target;
            },
            "license_plate.merge_failed",
            "License plates could not be merged.",
            cancellationToken);

    public Task<Result<LicensePlateDto>> MoveAsync(
        int licensePlateId,
        int targetLocationId,
        string userId,
        string? referenceNumber = null,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(
            async () =>
            {
                var plate = await RequirePlateAsync(licensePlateId, cancellationToken);
                EnsureMutable(plate);
                if (plate.ParentLicensePlateId.HasValue)
                {
                    throw new InvalidOperationException(
                        "Move the root license plate when moving a nested handling unit.");
                }

                var targetLocation = await _unitOfWork.Locations.GetByIdAsync(
                    targetLocationId,
                    cancellationToken)
                    ?? throw new InvalidOperationException($"Location {targetLocationId} was not found.");
                if (targetLocation.WarehouseId != plate.WarehouseId)
                {
                    throw new InvalidOperationException("A license plate cannot move to another warehouse location.");
                }

                var descendants = await _unitOfWork.LicensePlates.GetDescendantsAsync(
                    plate,
                    cancellationToken);
                var moving = new[] { plate }.Concat(descendants).ToArray();
                await EnsureLocationLicensePlateCapacityAsync(
                    targetLocation,
                    moving.Select(item => item.Id).ToHashSet(),
                    cancellationToken);

                foreach (var current in moving)
                {
                    var fromLocationId = current.CurrentLocationId;
                    current.MoveTo(current.WarehouseId, targetLocation.Id);
                    await AddHistoryAsync(
                        current,
                        LicensePlateHistoryAction.Moved,
                        userId,
                        fromLocationId: fromLocationId,
                        toLocationId: targetLocation.Id,
                        referenceNumber: referenceNumber,
                        cancellationToken: cancellationToken);

                    var stocks = await _unitOfWork.Stock.GetByLicensePlateIdAsync(
                        current.Id,
                        cancellationToken);
                    foreach (var stock in stocks.Where(stock => stock.QuantityAvailable.Value > 0))
                    {
                        var oldLocationId = stock.LocationId;
                        stock.SetLocation(targetLocation.Id);
                        await _unitOfWork.Stock.UpdateAsync(stock, cancellationToken);
                        var movement = Movement.CreateTransfer(
                            stock.ItemId,
                            oldLocationId,
                            targetLocation.Id,
                            stock.QuantityAvailable,
                            userId,
                            stock.LotId,
                            stock.SerialNumber,
                            referenceNumber,
                            "license plate move",
                            _clock.UtcNow.UtcDateTime,
                            stock.SerialNumberId,
                            stock.InventoryStatusId,
                            current.Id,
                            current.Id);
                        await _unitOfWork.Movements.AddAsync(movement, cancellationToken);
                        if (_inventoryLedgerService is not null)
                        {
                            var item = stock.Item ?? await _unitOfWork.Items.GetByIdAsync(
                                stock.ItemId,
                                cancellationToken) ?? throw new InvalidOperationException(
                                $"Item {stock.ItemId} was not found.");
                            var transactionGroupId = $"movement:{Guid.NewGuid():N}";
                            await _inventoryLedgerService.RecordAsync(
                                new[]
                                {
                                    new InventoryLedgerEntryRequest(
                                        InventoryTransactionType.Transfer,
                                        new InventoryBalanceKey(
                                            current.WarehouseId,
                                            oldLocationId,
                                            stock.ItemId,
                                            stock.LotId,
                                            stock.SerialNumberId,
                                            stock.SerialNumber,
                                            current.Id,
                                            stock.InventoryStatusId,
                                            item.UnitOfMeasure),
                                        -stock.QuantityAvailable.Value,
                                        ActorUserId: userId,
                                        ReferenceType: "Movement",
                                        ReferenceId: referenceNumber,
                                        Reason: "license plate move",
                                        OccurredAtUtc: movement.Timestamp,
                                        CorrelationId: transactionGroupId,
                                        IdempotencyKey: $"{transactionGroupId}:1",
                                        TransactionGroupId: transactionGroupId,
                                        EntrySequence: 1,
                                        MovementId: movement.Id > 0 ? movement.Id : null),
                                    new InventoryLedgerEntryRequest(
                                        InventoryTransactionType.Transfer,
                                        new InventoryBalanceKey(
                                            current.WarehouseId,
                                            targetLocation.Id,
                                            stock.ItemId,
                                            stock.LotId,
                                            stock.SerialNumberId,
                                            stock.SerialNumber,
                                            current.Id,
                                            stock.InventoryStatusId,
                                            item.UnitOfMeasure),
                                        stock.QuantityAvailable.Value,
                                        ActorUserId: userId,
                                        ReferenceType: "Movement",
                                        ReferenceId: referenceNumber,
                                        Reason: "license plate move",
                                        OccurredAtUtc: movement.Timestamp,
                                        CorrelationId: transactionGroupId,
                                        IdempotencyKey: $"{transactionGroupId}:2",
                                        TransactionGroupId: transactionGroupId,
                                        EntrySequence: 2,
                                        MovementId: movement.Id > 0 ? movement.Id : null)
                                },
                                cancellationToken);
                        }
                        if (stock.SerialNumberId.HasValue)
                        {
                            var serial = await _unitOfWork.SerialNumbers.GetByIdAsync(
                                stock.SerialNumberId.Value,
                                cancellationToken);
                            serial?.MoveTo(
                                current.WarehouseId,
                                targetLocation.Id,
                                current.Number,
                                _clock.UtcNow.UtcDateTime,
                                current.Id);
                            if (serial is not null)
                            {
                                await _unitOfWork.SerialNumbers.UpdateAsync(serial, cancellationToken);
                            }
                        }
                    }
                }

                await _auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.LicensePlateMoved,
                        WmsAuditEntityTypes.LicensePlate,
                        plate.Id.ToString(CultureInfo.InvariantCulture),
                        plate.WarehouseId,
                        After: new Dictionary<string, object?>
                        {
                            ["locationId"] = targetLocation.Id,
                            ["number"] = plate.Number,
                            ["nestedCount"] = descendants.Count
                        },
                        ActorUserId: userId),
                    cancellationToken);
                return plate;
            },
            "license_plate.move_failed",
            "The license plate could not be moved.",
            cancellationToken);

    public Task<Result<LicensePlateDto>> NestAsync(
        int childLicensePlateId,
        int parentLicensePlateId,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(
            async () =>
            {
                var child = await RequirePlateAsync(childLicensePlateId, cancellationToken);
                var parent = await RequirePlateAsync(parentLicensePlateId, cancellationToken);
                EnsureDifferentPlates(child, parent);
                EnsureSameWarehouse(child, parent);
                EnsureMutable(child);
                EnsureMutable(parent);
                if (child.CurrentLocationId != parent.CurrentLocationId ||
                    !child.CurrentLocationId.HasValue)
                {
                    throw new InvalidOperationException(
                        "Nested license plates must be colocated before nesting.");
                }

                var descendants = await _unitOfWork.LicensePlates.GetDescendantsAsync(
                    child,
                    cancellationToken);
                if (descendants.Any(plate => plate.Id == parent.Id))
                {
                    throw new InvalidOperationException("License plate nesting cannot create a hierarchy cycle.");
                }

                child.SetParent(parent.Id);
                await AddHistoryAsync(
                    child,
                    LicensePlateHistoryAction.Nested,
                    userId,
                    fromLicensePlateId: null,
                    toLicensePlateId: parent.Id,
                    toLocationId: child.CurrentLocationId,
                    cancellationToken: cancellationToken);
                await AddHistoryAsync(
                    parent,
                    LicensePlateHistoryAction.Nested,
                    userId,
                    fromLicensePlateId: child.Id,
                    toLicensePlateId: parent.Id,
                    toLocationId: parent.CurrentLocationId,
                    cancellationToken: cancellationToken);
                await RecordLifecycleAuditAsync(
                    child,
                    "nested",
                    userId,
                    child.Status,
                    referenceNumber: null,
                    reason: $"parent:{parent.Id}",
                    cancellationToken);
                return child;
            },
            "license_plate.nest_failed",
            "The license plate could not be nested.",
            cancellationToken);

    public Task<Result<LicensePlateDto>> UnnestAsync(
        int childLicensePlateId,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(
            async () =>
            {
                var child = await RequirePlateAsync(childLicensePlateId, cancellationToken);
                EnsureMutable(child);
                if (!child.ParentLicensePlateId.HasValue)
                {
                    throw new InvalidOperationException("The license plate is not nested.");
                }

                var parentId = child.ParentLicensePlateId;
                child.SetParent(null);
                await AddHistoryAsync(
                    child,
                    LicensePlateHistoryAction.Unnested,
                    userId,
                    fromLicensePlateId: parentId,
                    cancellationToken: cancellationToken);
                await RecordLifecycleAuditAsync(
                    child,
                    "unnested",
                    userId,
                    child.Status,
                    referenceNumber: null,
                    reason: parentId.HasValue ? $"parent:{parentId.Value}" : null,
                    cancellationToken);
                return child;
            },
            "license_plate.unnest_failed",
            "The license plate could not be unnested.",
            cancellationToken);

    public Task<Result<LicensePlateDto>> PackAsync(
        int licensePlateId,
        string userId,
        string? referenceNumber = null,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(
            async () =>
            {
                var plate = await RequirePlateAsync(licensePlateId, cancellationToken);
                EnsureMutable(plate);
                var previousStatus = plate.Status;
                plate.Close();
                await AddHistoryAsync(
                    plate,
                    LicensePlateHistoryAction.Packed,
                    userId,
                    referenceNumber: referenceNumber,
                    cancellationToken: cancellationToken);
                await RecordLifecycleAuditAsync(
                    plate,
                    "packed",
                    userId,
                    previousStatus,
                    referenceNumber,
                    null,
                    cancellationToken);
                return plate;
            },
            "license_plate.pack_failed",
            "The license plate could not be packed.",
            cancellationToken);

    public Task<Result<LicensePlateDto>> ReopenAsync(
        int licensePlateId,
        string userId,
        string reason,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(
            async () =>
            {
                RequireReason(reason);
                var plate = await RequirePlateAsync(licensePlateId, cancellationToken);
                var previousStatus = plate.Status;
                plate.Reopen();
                await AddHistoryAsync(
                    plate,
                    LicensePlateHistoryAction.Reopened,
                    userId,
                    reason: reason,
                    cancellationToken: cancellationToken);
                await RecordLifecycleAuditAsync(
                    plate,
                    "reopened",
                    userId,
                    previousStatus,
                    referenceNumber: null,
                    reason,
                    cancellationToken);
                return plate;
            },
            "license_plate.reopen_failed",
            "The license plate could not be reopened.",
            cancellationToken);

    public Task<Result<LicensePlateDto>> ShipAsync(
        int licensePlateId,
        string userId,
        string? referenceNumber = null,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(
            async () =>
            {
                var plate = await RequirePlateAsync(licensePlateId, cancellationToken);
                var descendants = await _unitOfWork.LicensePlates.GetDescendantsAsync(
                    plate,
                    cancellationToken);
                var group = new[] { plate }.Concat(descendants).ToArray();
                if (group.Any(current => current.Status != LicensePlateStatus.Closed))
                {
                    throw new InvalidOperationException("Every license plate in a shipment must be packed and closed first.");
                }

                foreach (var current in group)
                {
                    var stocks = await _unitOfWork.Stock.GetByLicensePlateIdAsync(
                        current.Id,
                        cancellationToken);
                    foreach (var stock in stocks.Where(stock => stock.QuantityAvailable.Value > 0))
                    {
                        if (stock.QuantityReserved.Value > 0)
                        {
                            throw new InvalidOperationException(
                                "A license plate with outstanding reservations cannot be shipped before the reservation workflow consumes them.");
                        }

                        var movement = Movement.CreateShip(
                            stock.ItemId,
                            stock.LocationId,
                            stock.QuantityAvailable,
                            userId,
                            stock.LotId,
                            stock.SerialNumber,
                            referenceNumber,
                            "license plate shipment",
                            _clock.UtcNow.UtcDateTime,
                            stock.SerialNumberId,
                            stock.InventoryStatusId,
                            current.Id);
                        await _unitOfWork.Movements.AddAsync(movement, cancellationToken);
                        if (_inventoryLedgerService is not null)
                        {
                            var item = stock.Item ?? await _unitOfWork.Items.GetByIdAsync(
                                stock.ItemId,
                                cancellationToken) ?? throw new InvalidOperationException(
                                $"Item {stock.ItemId} was not found.");
                            var transactionGroupId = $"movement:{Guid.NewGuid():N}";
                            await _inventoryLedgerService.RecordAsync(
                                new[]
                                {
                                    new InventoryLedgerEntryRequest(
                                        InventoryTransactionType.Ship,
                                        new InventoryBalanceKey(
                                            current.WarehouseId,
                                            stock.LocationId,
                                            stock.ItemId,
                                            stock.LotId,
                                            stock.SerialNumberId,
                                            stock.SerialNumber,
                                            current.Id,
                                            stock.InventoryStatusId,
                                            item.UnitOfMeasure),
                                        -stock.QuantityAvailable.Value,
                                        ActorUserId: userId,
                                        ReferenceType: "Movement",
                                        ReferenceId: referenceNumber,
                                        Reason: "license plate shipment",
                                        OccurredAtUtc: movement.Timestamp,
                                        CorrelationId: transactionGroupId,
                                        IdempotencyKey: $"{transactionGroupId}:1",
                                        TransactionGroupId: transactionGroupId,
                                        MovementId: movement.Id > 0 ? movement.Id : null)
                                },
                                cancellationToken);
                        }
                        await _unitOfWork.Stock.DeleteAsync(stock, cancellationToken);
                        if (stock.SerialNumberId.HasValue)
                        {
                            var serial = await _unitOfWork.SerialNumbers.GetByIdAsync(
                                stock.SerialNumberId.Value,
                                cancellationToken);
                            serial?.RecordShipment(referenceNumber, _clock.UtcNow.UtcDateTime);
                            if (serial is not null)
                            {
                                await _unitOfWork.SerialNumbers.UpdateAsync(serial, cancellationToken);
                            }
                        }
                    }

                    current.Ship();
                    await AddHistoryAsync(
                        current,
                        LicensePlateHistoryAction.Shipped,
                        userId,
                        referenceNumber: referenceNumber,
                        cancellationToken: cancellationToken);
                }

                await _auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.LicensePlateShipped,
                        WmsAuditEntityTypes.LicensePlate,
                        plate.Id.ToString(CultureInfo.InvariantCulture),
                        plate.WarehouseId,
                        After: new Dictionary<string, object?>
                        {
                            ["number"] = plate.Number,
                            ["nestedCount"] = descendants.Count
                        },
                        ActorUserId: userId),
                    cancellationToken);
                return plate;
            },
            "license_plate.ship_failed",
            "The license plate could not be shipped.",
            cancellationToken);

    public Task<Result<LicensePlateDto>> ReturnAsync(
        int licensePlateId,
        int targetLocationId,
        string userId,
        string? referenceNumber = null,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(
            async () =>
            {
                var plate = await RequirePlateAsync(licensePlateId, cancellationToken);
                var targetLocation = await _unitOfWork.Locations.GetByIdAsync(
                    targetLocationId,
                    cancellationToken)
                    ?? throw new InvalidOperationException($"Location {targetLocationId} was not found.");
                if (targetLocation.WarehouseId != plate.WarehouseId)
                {
                    throw new InvalidOperationException("A returned license plate must return to its owning warehouse.");
                }

                var descendants = await _unitOfWork.LicensePlates.GetDescendantsAsync(
                    plate,
                    cancellationToken);
                var group = new[] { plate }.Concat(descendants).ToArray();
                var parentByChild = group.ToDictionary(current => current.Id, current => current.ParentLicensePlateId);
                await EnsureLocationLicensePlateCapacityAsync(
                    targetLocation,
                    group.Select(current => current.Id).ToHashSet(),
                    cancellationToken);

                foreach (var current in group)
                {
                    current.ReturnTo(current.WarehouseId, targetLocation.Id);
                    var contents = await _unitOfWork.LicensePlates.GetContentsAsync(
                        current.Id,
                        cancellationToken);
                    foreach (var content in contents)
                    {
                        var existing = await FindStockAsync(current, ToInput(content), cancellationToken);
                        if (existing is not null)
                        {
                            throw new InvalidOperationException(
                                $"Returned license plate content already has an inventory balance for item {content.ItemId}.");
                        }

                        var stock = new Stock(
                            content.ItemId,
                            targetLocation.Id,
                            content.Quantity,
                            content.LotId,
                            content.SerialNumber?.Number,
                            content.SerialNumberId,
                            content.InventoryStatusId,
                            current.Id);
                        await _unitOfWork.Stock.AddAsync(stock, cancellationToken);
                        var movement = Movement.CreateReceipt(
                            content.ItemId,
                            targetLocation.Id,
                            content.Quantity,
                            userId,
                            content.LotId,
                            content.SerialNumber?.Number,
                            referenceNumber,
                            "license plate return",
                            _clock.UtcNow.UtcDateTime,
                            content.SerialNumberId,
                            content.InventoryStatusId,
                            current.Id);
                        await _unitOfWork.Movements.AddAsync(movement, cancellationToken);
                        if (_inventoryLedgerService is not null)
                        {
                            var item = content.Item ?? await _unitOfWork.Items.GetByIdAsync(
                                content.ItemId,
                                cancellationToken) ?? throw new InvalidOperationException(
                                $"Item {content.ItemId} was not found.");
                            var transactionGroupId = $"movement:{Guid.NewGuid():N}";
                            await _inventoryLedgerService.RecordAsync(
                                new[]
                                {
                                    new InventoryLedgerEntryRequest(
                                        InventoryTransactionType.Return,
                                        new InventoryBalanceKey(
                                            current.WarehouseId,
                                            targetLocation.Id,
                                            content.ItemId,
                                            content.LotId,
                                            content.SerialNumberId,
                                            content.SerialNumber?.Number,
                                            current.Id,
                                            content.InventoryStatusId,
                                            item.UnitOfMeasure),
                                        content.Quantity.Value,
                                        ActorUserId: userId,
                                        ReferenceType: "Movement",
                                        ReferenceId: referenceNumber,
                                        Reason: "license plate return",
                                        OccurredAtUtc: movement.Timestamp,
                                        CorrelationId: transactionGroupId,
                                        IdempotencyKey: $"{transactionGroupId}:1",
                                        TransactionGroupId: transactionGroupId,
                                        MovementId: movement.Id > 0 ? movement.Id : null)
                                },
                                cancellationToken);
                        }
                        if (content.SerialNumberId.HasValue)
                        {
                            var serial = await _unitOfWork.SerialNumbers.GetByIdAsync(
                                content.SerialNumberId.Value,
                                cancellationToken);
                            if (serial is not null)
                            {
                                serial.RecordReceipt(
                                    current.WarehouseId,
                                    targetLocation.Id,
                                    content.LotId,
                                    referenceNumber,
                                    quarantine: false,
                                    _clock.UtcNow.UtcDateTime);
                                serial.MoveTo(
                                    current.WarehouseId,
                                    targetLocation.Id,
                                    current.Number,
                                    _clock.UtcNow.UtcDateTime,
                                    current.Id);
                                await _unitOfWork.SerialNumbers.UpdateAsync(serial, cancellationToken);
                            }
                        }
                    }

                    await AddHistoryAsync(
                        current,
                        LicensePlateHistoryAction.Returned,
                        userId,
                        toLocationId: targetLocation.Id,
                        referenceNumber: referenceNumber,
                        cancellationToken: cancellationToken);
                    await RecordLifecycleAuditAsync(
                        current,
                        "returned",
                        userId,
                        LicensePlateStatus.Shipped,
                        referenceNumber,
                        null,
                        cancellationToken);
                }

                foreach (var current in group.Where(current => current.Id != plate.Id))
                {
                    if (parentByChild[current.Id].HasValue)
                    {
                        current.SetParent(parentByChild[current.Id]);
                    }
                }

                return plate;
            },
            "license_plate.return_failed",
            "The license plate could not be returned.",
            cancellationToken);

    public Task<Result<LicensePlateDto>> VoidAsync(
        int licensePlateId,
        string userId,
        string reason,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(
            async () =>
            {
                RequireReason(reason);
                var plate = await RequirePlateAsync(licensePlateId, cancellationToken);
                var previousStatus = plate.Status;
                var descendants = await _unitOfWork.LicensePlates.GetDescendantsAsync(
                    plate,
                    cancellationToken);
                if (descendants.Count > 0)
                {
                    throw new InvalidOperationException("A nested license plate hierarchy must be unnested before voiding.");
                }

                var stocks = await _unitOfWork.Stock.GetByLicensePlateIdAsync(
                    plate.Id,
                    cancellationToken);
                if (stocks.Any(stock => stock.QuantityAvailable.Value > 0 || stock.QuantityReserved.Value > 0) ||
                    plate.Contents.Count > 0)
                {
                    throw new InvalidOperationException("A license plate with content or inventory cannot be voided.");
                }

                plate.Void();
                await AddHistoryAsync(
                    plate,
                    LicensePlateHistoryAction.Voided,
                    userId,
                    reason: reason,
                    cancellationToken: cancellationToken);
                await RecordLifecycleAuditAsync(
                    plate,
                    "voided",
                    userId,
                    previousStatus,
                    referenceNumber: null,
                    reason,
                    cancellationToken);
                return plate;
            },
            "license_plate.void_failed",
            "The license plate could not be voided.",
            cancellationToken);

    public Task<Result<IReadOnlyList<LicensePlateHistoryDto>>> GetHistoryAsync(
        int licensePlateId,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            async () =>
            {
                var plate = await _unitOfWork.LicensePlates.GetByIdAsync(
                    licensePlateId,
                    cancellationToken);
                if (plate is null)
                {
                    return Result.Failure<IReadOnlyList<LicensePlateHistoryDto>>(WmsErrors.NotFound(
                        "license_plate.not_found",
                        $"License plate {licensePlateId} was not found."));
                }

                var history = await _unitOfWork.LicensePlates.GetHistoryAsync(
                    licensePlateId,
                    page,
                    pageSize,
                    cancellationToken);
                return Result.Success<IReadOnlyList<LicensePlateHistoryDto>>(
                    history.Select(ToHistoryDto).ToArray());
            },
            "license_plate.history_failed",
            "License plate history could not be loaded.");

    public Task<Result<LicensePlateNumberingDto>> ConfigureNumberingAsync(
        LicensePlateNumberingInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            async () =>
            {
                RequireUser(userId);
                await _unitOfWork.BeginTransactionAsync(cancellationToken);
                try
                {
                    var sequence = await _unitOfWork.LicensePlates.GetNumberSequenceAsync(
                        input.WarehouseId,
                        cancellationToken);
                    if (sequence is null)
                    {
                        sequence = new LicensePlateNumberSequence(
                            input.WarehouseId,
                            input.Prefix,
                            input.Padding,
                            input.NextNumber);
                        await _unitOfWork.LicensePlates.AddNumberSequenceAsync(
                            sequence,
                            cancellationToken);
                    }
                    else
                    {
                        sequence.Configure(input.Prefix, input.Padding, input.NextNumber);
                    }

                    await _auditWriter.RecordAsync(
                        new AuditRecord(
                            WmsAuditActions.LicensePlateNumberingConfigured,
                            WmsAuditEntityTypes.LicensePlateNumberSequence,
                            input.WarehouseId.ToString(CultureInfo.InvariantCulture),
                            input.WarehouseId,
                            After: new Dictionary<string, object?>
                            {
                                ["prefix"] = input.Prefix,
                                ["padding"] = input.Padding,
                                ["nextNumber"] = input.NextNumber
                            },
                            ActorUserId: userId),
                        cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await _unitOfWork.CommitTransactionAsync(cancellationToken);
                    return Result.Success(ToDto(sequence));
                }
                catch
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    throw;
                }
            },
            "license_plate.numbering_failed",
            "License plate numbering could not be configured.");

    private async Task<LicensePlate> CreateInternalAsync(
        LicensePlateCreateInput input,
        string userId,
        CancellationToken cancellationToken)
    {
        RequireUser(userId);
        if (input.CurrentLocationId.HasValue)
        {
            var location = await _unitOfWork.Locations.GetByIdAsync(
                input.CurrentLocationId.Value,
                cancellationToken)
                ?? throw new InvalidOperationException($"Location {input.CurrentLocationId.Value} was not found.");
            if (location.WarehouseId != input.WarehouseId)
            {
                throw new InvalidOperationException("The license plate location must belong to its warehouse.");
            }

            await EnsureLocationLicensePlateCapacityAsync(
                location,
                movingIds: new HashSet<int>(),
                cancellationToken);
        }

        var number = input.Number;
        LicensePlateNumberSequence? sequence = null;
        if (string.IsNullOrWhiteSpace(number))
        {
            sequence = await _unitOfWork.LicensePlates.GetNumberSequenceAsync(
                input.WarehouseId,
                cancellationToken);
            if (sequence is null)
            {
                sequence = new LicensePlateNumberSequence(
                    input.WarehouseId,
                    $"LPN-{input.WarehouseId}-",
                    padding: 8,
                    nextNumber: 1);
                await _unitOfWork.LicensePlates.AddNumberSequenceAsync(sequence, cancellationToken);
            }

            number = sequence.TakeNextNumber();
        }

        var plate = new LicensePlate(
            number!,
            input.Type,
            input.WarehouseId,
            input.CurrentLocationId,
            input.IsSscc,
            input.GrossWeightKg,
            input.LengthCm,
            input.WidthCm,
            input.HeightCm,
            input.SourceReference,
            input.Notes);
        await _unitOfWork.LicensePlates.AddAsync(plate, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await AddHistoryAsync(
            plate,
            LicensePlateHistoryAction.Created,
            userId,
            toLocationId: plate.CurrentLocationId,
            referenceNumber: input.SourceReference,
            cancellationToken: cancellationToken);

        if (_identificationRegistry is not null)
        {
            var registered = await _identificationRegistry.RegisterAsync(
                new IdentificationRegistrationRequest(
                    plate.Number,
                    IdentificationKind.LicensePlate,
                    IdentifierOwnerKeys.LicensePlate(plate.Number),
                    input.IsSscc ? BarcodeSymbology.Sscc : BarcodeSymbology.Code128),
                cancellationToken);
            if (registered.IsFailure)
            {
                throw new InvalidOperationException(registered.Error);
            }
        }

        await _auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.LicensePlateCreated,
                WmsAuditEntityTypes.LicensePlate,
                plate.Id.ToString(CultureInfo.InvariantCulture),
                plate.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["number"] = plate.Number,
                    ["type"] = plate.Type.ToString(),
                    ["isSscc"] = plate.IsSscc,
                    ["locationId"] = plate.CurrentLocationId
                },
                ActorUserId: userId),
            cancellationToken);
        return plate;
    }

    private async Task AddContentInternalAsync(
        LicensePlate plate,
        LicensePlateContentInput input,
        string userId,
        string? referenceNumber,
        CancellationToken cancellationToken)
    {
        EnsureMutable(plate);
        if (!plate.CurrentLocationId.HasValue)
        {
            throw new InvalidOperationException("An LPN must have a current location before it can receive content.");
        }

        var validation = await ValidateContentAsync(plate, input, cancellationToken);
        await EnsureContentPolicyAsync(plate, input, cancellationToken);
        var existingContent = await _unitOfWork.LicensePlates.FindContentAsync(
            plate.Id,
            input.ItemId,
            input.LotId,
            input.SerialNumberId,
            input.InventoryStatusId,
            input.ItemPackagingId,
            cancellationToken);
        var existingStock = await FindStockAsync(plate, input, cancellationToken);
        if ((existingContent is null) != (existingStock is null))
        {
            throw new InvalidOperationException(
                "License plate content and stock balance are inconsistent; reconcile before adding content.");
        }

        var quantity = new Quantity(input.Quantity);
        if (existingContent is null)
        {
            await _unitOfWork.LicensePlates.AddContentAsync(
                new LicensePlateContent(
                    plate.Id,
                    input.ItemId,
                    quantity,
                    input.LotId,
                    input.SerialNumberId,
                    input.InventoryStatusId,
                    input.ItemPackagingId),
                cancellationToken);
            var stock = new Stock(
                input.ItemId,
                plate.CurrentLocationId.Value,
                quantity,
                input.LotId,
                validation.Serial?.Number,
                input.SerialNumberId,
                input.InventoryStatusId,
                plate.Id);
            await _unitOfWork.Stock.AddAsync(stock, cancellationToken);
        }
        else
        {
            existingContent.AddQuantity(quantity);
            existingStock!.AddQuantity(quantity);
            await _unitOfWork.LicensePlates.UpdateAsync(plate, cancellationToken);
            await _unitOfWork.Stock.UpdateAsync(existingStock, cancellationToken);
        }

        var movement = Movement.CreateReceipt(
            input.ItemId,
            plate.CurrentLocationId.Value,
            quantity,
            userId,
            input.LotId,
            validation.Serial?.Number,
            referenceNumber,
            "license plate content receipt",
            _clock.UtcNow.UtcDateTime,
            input.SerialNumberId,
            input.InventoryStatusId,
            plate.Id);
        await _unitOfWork.Movements.AddAsync(movement, cancellationToken);
        if (_inventoryLedgerService is not null)
        {
            var transactionGroupId = $"movement:{Guid.NewGuid():N}";
            await _inventoryLedgerService.RecordAsync(
                new[]
                {
                    new InventoryLedgerEntryRequest(
                        InventoryTransactionType.Receipt,
                        new InventoryBalanceKey(
                            plate.WarehouseId,
                            plate.CurrentLocationId.Value,
                            input.ItemId,
                            input.LotId,
                            input.SerialNumberId,
                            validation.Serial?.Number,
                            plate.Id,
                            input.InventoryStatusId,
                            validation.Item.UnitOfMeasure),
                        quantity.Value,
                        ActorUserId: userId,
                        ReferenceType: "Movement",
                        ReferenceId: referenceNumber,
                        Reason: "license plate content receipt",
                        OccurredAtUtc: movement.Timestamp,
                        CorrelationId: transactionGroupId,
                        IdempotencyKey: $"{transactionGroupId}:1",
                        TransactionGroupId: transactionGroupId,
                        MovementId: movement.Id > 0 ? movement.Id : null)
                },
                cancellationToken);
        }
        await UpdateSerialLocationAsync(validation.Serial, plate, input.LotId, referenceNumber, cancellationToken);
        await AddHistoryAsync(
            plate,
            LicensePlateHistoryAction.ContentReceived,
            userId,
            itemId: input.ItemId,
            lotId: input.LotId,
            serialNumberId: input.SerialNumberId,
            quantity: input.Quantity,
            toLocationId: plate.CurrentLocationId,
            toLicensePlateId: plate.Id,
            referenceNumber: referenceNumber,
            cancellationToken: cancellationToken);
        await _auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.LicensePlateContentChanged,
                WmsAuditEntityTypes.LicensePlateContent,
                plate.Id.ToString(CultureInfo.InvariantCulture),
                plate.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["itemId"] = input.ItemId,
                    ["quantity"] = input.Quantity,
                    ["serialNumberId"] = input.SerialNumberId
                },
                ActorUserId: userId),
            cancellationToken);
    }

    private async Task MoveContentInternalAsync(
        LicensePlate source,
        LicensePlate target,
        LicensePlateContentInput input,
        string userId,
        string? referenceNumber,
        LicensePlateHistoryAction historyAction,
        CancellationToken cancellationToken)
    {
        EnsureDifferentPlates(source, target);
        EnsureSameWarehouse(source, target);
        EnsureMutable(source);
        EnsureMutable(target);
        if (!source.CurrentLocationId.HasValue || !target.CurrentLocationId.HasValue)
        {
            throw new InvalidOperationException("Both license plates must have current locations before moving content.");
        }

        var validation = await ValidateContentAsync(target, input, cancellationToken, validateSerialPlacement: false);
        var sourceContent = await _unitOfWork.LicensePlates.FindContentAsync(
            source.Id,
            input.ItemId,
            input.LotId,
            input.SerialNumberId,
            input.InventoryStatusId,
            input.ItemPackagingId,
            cancellationToken)
            ?? throw new InvalidOperationException("The source license plate does not contain the requested inventory identity.");
        var sourceStock = await FindStockAsync(source, input, cancellationToken)
            ?? throw new InvalidOperationException("The source license plate content has no matching stock balance.");
        var quantity = new Quantity(input.Quantity);
        if (sourceContent.Quantity.Value < quantity.Value ||
            sourceStock.GetAvailableQuantity().Value < quantity.Value)
        {
            throw new InvalidOperationException("The requested quantity exceeds the movable license plate content.");
        }

        await EnsureContentPolicyAsync(target, input, cancellationToken);
        var targetContent = await _unitOfWork.LicensePlates.FindContentAsync(
            target.Id,
            input.ItemId,
            input.LotId,
            input.SerialNumberId,
            input.InventoryStatusId,
            input.ItemPackagingId,
            cancellationToken);
        var targetStock = await FindStockAsync(target, input, cancellationToken);
        if ((targetContent is null) != (targetStock is null))
        {
            throw new InvalidOperationException(
                "The target license plate content and stock balance are inconsistent.");
        }

        sourceContent.RemoveQuantity(quantity);
        sourceStock.RemoveQuantity(quantity);
        if (sourceContent.Quantity.Value == 0)
        {
            await _unitOfWork.LicensePlates.RemoveContentAsync(sourceContent, cancellationToken);
        }
        else
        {
            await _unitOfWork.LicensePlates.UpdateAsync(source, cancellationToken);
        }

        if (sourceStock.QuantityAvailable.Value == 0 && sourceStock.QuantityReserved.Value == 0)
        {
            await _unitOfWork.Stock.DeleteAsync(sourceStock, cancellationToken);
        }
        else
        {
            await _unitOfWork.Stock.UpdateAsync(sourceStock, cancellationToken);
        }

        if (targetContent is null)
        {
            await _unitOfWork.LicensePlates.AddContentAsync(
                new LicensePlateContent(
                    target.Id,
                    input.ItemId,
                    quantity,
                    input.LotId,
                    input.SerialNumberId,
                    input.InventoryStatusId,
                    input.ItemPackagingId),
                cancellationToken);
            await _unitOfWork.Stock.AddAsync(
                new Stock(
                    input.ItemId,
                    target.CurrentLocationId.Value,
                    quantity,
                    input.LotId,
                    validation.Serial?.Number,
                    input.SerialNumberId,
                    input.InventoryStatusId,
                    target.Id),
                cancellationToken);
        }
        else
        {
            targetContent.AddQuantity(quantity);
            targetStock!.AddQuantity(quantity);
            await _unitOfWork.LicensePlates.UpdateAsync(target, cancellationToken);
            await _unitOfWork.Stock.UpdateAsync(targetStock, cancellationToken);
        }

        var movement = Movement.CreateTransfer(
            input.ItemId,
            source.CurrentLocationId.Value,
            target.CurrentLocationId.Value,
            quantity,
            userId,
            input.LotId,
            validation.Serial?.Number,
            referenceNumber,
            "license plate content move",
            _clock.UtcNow.UtcDateTime,
            input.SerialNumberId,
            input.InventoryStatusId,
            source.Id,
            target.Id);
        await _unitOfWork.Movements.AddAsync(movement, cancellationToken);
        if (_inventoryLedgerService is not null)
        {
            var transactionGroupId = $"movement:{Guid.NewGuid():N}";
            var sourceKey = new InventoryBalanceKey(
                source.WarehouseId,
                source.CurrentLocationId.Value,
                input.ItemId,
                input.LotId,
                input.SerialNumberId,
                validation.Serial?.Number,
                source.Id,
                sourceStock.InventoryStatusId,
                validation.Item.UnitOfMeasure);
            var targetKey = new InventoryBalanceKey(
                target.WarehouseId,
                target.CurrentLocationId.Value,
                input.ItemId,
                input.LotId,
                input.SerialNumberId,
                validation.Serial?.Number,
                target.Id,
                input.InventoryStatusId,
                validation.Item.UnitOfMeasure);
            await _inventoryLedgerService.RecordAsync(
                new[]
                {
                    new InventoryLedgerEntryRequest(
                        InventoryTransactionType.Transfer,
                        sourceKey,
                        -quantity.Value,
                        ActorUserId: userId,
                        ReferenceType: "Movement",
                        ReferenceId: referenceNumber,
                        Reason: "license plate content move",
                        OccurredAtUtc: movement.Timestamp,
                        CorrelationId: transactionGroupId,
                        IdempotencyKey: $"{transactionGroupId}:1",
                        TransactionGroupId: transactionGroupId,
                        EntrySequence: 1,
                        MovementId: movement.Id > 0 ? movement.Id : null),
                    new InventoryLedgerEntryRequest(
                        InventoryTransactionType.Transfer,
                        targetKey,
                        quantity.Value,
                        ActorUserId: userId,
                        ReferenceType: "Movement",
                        ReferenceId: referenceNumber,
                        Reason: "license plate content move",
                        OccurredAtUtc: movement.Timestamp,
                        CorrelationId: transactionGroupId,
                        IdempotencyKey: $"{transactionGroupId}:2",
                        TransactionGroupId: transactionGroupId,
                        EntrySequence: 2,
                        MovementId: movement.Id > 0 ? movement.Id : null)
                },
                cancellationToken);
        }
        if (validation.Serial is not null)
        {
            validation.Serial.MoveTo(
                target.WarehouseId,
                target.CurrentLocationId.Value,
                target.Number,
                _clock.UtcNow.UtcDateTime,
                target.Id);
            await _unitOfWork.SerialNumbers.UpdateAsync(validation.Serial, cancellationToken);
        }

        await AddHistoryAsync(
            source,
            historyAction,
            userId,
            itemId: input.ItemId,
            lotId: input.LotId,
            serialNumberId: input.SerialNumberId,
            quantity: input.Quantity,
            fromLocationId: source.CurrentLocationId,
            toLocationId: target.CurrentLocationId,
            fromLicensePlateId: source.Id,
            toLicensePlateId: target.Id,
            referenceNumber: referenceNumber,
            cancellationToken: cancellationToken);
        await AddHistoryAsync(
            target,
            historyAction,
            userId,
            itemId: input.ItemId,
            lotId: input.LotId,
            serialNumberId: input.SerialNumberId,
            quantity: input.Quantity,
            fromLocationId: source.CurrentLocationId,
            toLocationId: target.CurrentLocationId,
            fromLicensePlateId: source.Id,
            toLicensePlateId: target.Id,
            referenceNumber: referenceNumber,
            cancellationToken: cancellationToken);
    }

    private async Task<ContentValidation> ValidateContentAsync(
        LicensePlate plate,
        LicensePlateContentInput input,
        CancellationToken cancellationToken,
        bool validateSerialPlacement = true)
    {
        if (input.Quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Content quantity must be positive.");
        }

        var item = await _unitOfWork.Items.GetByIdAsync(input.ItemId, cancellationToken)
            ?? throw new InvalidOperationException($"Item {input.ItemId} was not found.");
        if (item.RequiresLot && !input.LotId.HasValue)
        {
            throw new InvalidOperationException($"Item '{item.Sku}' requires a lot identity.");
        }

        if (item.RequiresSerial != input.SerialNumberId.HasValue)
        {
            throw new InvalidOperationException(
                item.RequiresSerial
                    ? $"Item '{item.Sku}' requires a serial identity."
                    : $"Item '{item.Sku}' is not serial controlled and cannot carry a serial identity.");
        }

        Lot? lot = null;
        if (input.LotId.HasValue)
        {
            lot = await _unitOfWork.Lots.GetByIdAsync(input.LotId.Value, cancellationToken)
                ?? throw new InvalidOperationException($"Lot {input.LotId.Value} was not found.");
            if (lot.ItemId != input.ItemId)
            {
                throw new InvalidOperationException("The lot does not belong to the selected item.");
            }
        }

        SerialNumber? serial = null;
        if (input.SerialNumberId.HasValue)
        {
            serial = await _unitOfWork.SerialNumbers.GetByIdAsync(
                input.SerialNumberId.Value,
                cancellationToken)
                ?? throw new InvalidOperationException($"Serial {input.SerialNumberId.Value} was not found.");
            if (serial.ItemId != input.ItemId || serial.LotId != input.LotId)
            {
                throw new InvalidOperationException("The serial identity does not match the selected item and lot.");
            }

            if (serial.Status is SerialStatus.Shipped or SerialStatus.Scrapped or SerialStatus.Corrected)
            {
                throw new InvalidOperationException($"Serial '{serial.Number}' cannot be stored while it is {serial.Status}.");
            }

            if (validateSerialPlacement && serial.CurrentLocationId.HasValue &&
                (serial.CurrentLocationId != plate.CurrentLocationId ||
                 serial.CurrentLicensePlateId != plate.Id))
            {
                throw new InvalidOperationException(
                    $"Serial '{serial.Number}' is already placed outside this license plate.");
            }
        }

        var status = await _unitOfWork.InventoryStatuses.GetByIdAsync(
            input.InventoryStatusId,
            cancellationToken)
            ?? throw new InvalidOperationException($"Inventory status {input.InventoryStatusId} was not found.");
        if (!status.IsActive)
        {
            throw new InvalidOperationException($"Inventory status '{status.Code}' is inactive.");
        }

        if (input.SerialNumberId.HasValue && input.Quantity != 1m)
        {
            throw new InvalidOperationException("Serial-controlled content must have a quantity of exactly one.");
        }

        return new ContentValidation(item, lot, serial);
    }

    private async Task EnsureContentPolicyAsync(
        LicensePlate target,
        LicensePlateContentInput input,
        CancellationToken cancellationToken)
    {
        if (!target.CurrentLocationId.HasValue)
        {
            return;
        }

        var location = await _unitOfWork.Locations.GetByIdAsync(
            target.CurrentLocationId.Value,
            cancellationToken)
            ?? throw new InvalidOperationException($"Location {target.CurrentLocationId.Value} was not found.");
        var contents = await _unitOfWork.LicensePlates.GetContentsAsync(target.Id, cancellationToken);
        if (!location.AllowMixedItems && contents.Any(content => content.ItemId != input.ItemId))
        {
            throw new InvalidOperationException(
                $"Location '{location.Code}' does not allow mixed-item license plate content.");
        }

        if (!location.AllowMixedLots && contents.Any(content =>
                content.ItemId == input.ItemId && content.LotId != input.LotId))
        {
            throw new InvalidOperationException(
                $"Location '{location.Code}' does not allow mixed-lot license plate content.");
        }
    }

    private async Task<Stock?> FindStockAsync(
        LicensePlate plate,
        LicensePlateContentInput input,
        CancellationToken cancellationToken)
    {
        var stocks = await _unitOfWork.Stock.GetByLicensePlateIdAsync(
            plate.Id,
            cancellationToken);
        return stocks.FirstOrDefault(stock =>
            stock.ItemId == input.ItemId &&
            stock.LocationId == plate.CurrentLocationId &&
            stock.LotId == input.LotId &&
            stock.SerialNumberId == input.SerialNumberId &&
            stock.InventoryStatusId == input.InventoryStatusId &&
            stock.LicensePlateId == plate.Id &&
            stock.QuantityAvailable.Value > 0);
    }

    private async Task UpdateSerialLocationAsync(
        SerialNumber? serial,
        LicensePlate plate,
        int? lotId,
        string? referenceNumber,
        CancellationToken cancellationToken)
    {
        if (serial is null || !plate.CurrentLocationId.HasValue)
        {
            return;
        }

        if (serial.CurrentLocationId.HasValue)
        {
            serial.MoveTo(
                plate.WarehouseId,
                plate.CurrentLocationId.Value,
                plate.Number,
                _clock.UtcNow.UtcDateTime,
                plate.Id);
        }
        else
        {
            serial.RecordReceipt(
                plate.WarehouseId,
                plate.CurrentLocationId.Value,
                lotId,
                referenceNumber,
                quarantine: false,
                _clock.UtcNow.UtcDateTime);
            serial.MoveTo(
                plate.WarehouseId,
                plate.CurrentLocationId.Value,
                plate.Number,
                _clock.UtcNow.UtcDateTime,
                plate.Id);
        }

        await _unitOfWork.SerialNumbers.UpdateAsync(serial, cancellationToken);
    }

    private async Task EnsureLocationLicensePlateCapacityAsync(
        Location location,
        HashSet<int> movingIds,
        CancellationToken cancellationToken)
    {
        if (!location.MaxLpns.HasValue)
        {
            return;
        }

        var existing = await _unitOfWork.LicensePlates.CountAtLocationAsync(
            location.Id,
            cancellationToken: cancellationToken);
        if (existing >= location.MaxLpns.Value)
        {
            var candidates = await _unitOfWork.LicensePlates.SearchAsync(
                location.WarehouseId,
                null,
                null,
                includeVoided: false,
                page: 1,
                pageSize: 200,
                cancellationToken);
            existing = candidates.Count(plate =>
                plate.CurrentLocationId == location.Id &&
                !movingIds.Contains(plate.Id) &&
                plate.Status != LicensePlateStatus.Voided);
        }

        if (existing + movingIds.Count > location.MaxLpns.Value)
        {
            throw new InvalidOperationException(
                $"Location '{location.Code}' has reached its LPN capacity of {location.MaxLpns.Value}.");
        }
    }

    private async Task<LicensePlate> RequirePlateAsync(
        int licensePlateId,
        CancellationToken cancellationToken)
    {
        var plate = await _unitOfWork.LicensePlates.GetByIdWithContentsAsync(
            licensePlateId,
            cancellationToken);
        return plate ?? throw new InvalidOperationException($"License plate {licensePlateId} was not found.");
    }

    private async Task AddHistoryAsync(
        LicensePlate plate,
        LicensePlateHistoryAction action,
        string userId,
        int? itemId = null,
        int? lotId = null,
        int? serialNumberId = null,
        decimal? quantity = null,
        int? fromLocationId = null,
        int? toLocationId = null,
        int? fromLicensePlateId = null,
        int? toLicensePlateId = null,
        string? referenceNumber = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        await _unitOfWork.LicensePlates.AddHistoryAsync(
            new LicensePlateHistory(
                plate.Id,
                action,
                userId,
                _clock.UtcNow.UtcDateTime,
                itemId,
                lotId,
                serialNumberId,
                quantity,
                fromLocationId,
                toLocationId,
                fromLicensePlateId,
                toLicensePlateId,
                referenceNumber,
                reason),
            cancellationToken);
    }

    private Task RecordLifecycleAuditAsync(
        LicensePlate plate,
        string transition,
        string userId,
        LicensePlateStatus previousStatus,
        string? referenceNumber,
        string? reason,
        CancellationToken cancellationToken) =>
        _auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.LicensePlateLifecycleChanged,
                WmsAuditEntityTypes.LicensePlate,
                plate.Id.ToString(CultureInfo.InvariantCulture),
                plate.WarehouseId,
                Before: new Dictionary<string, object?>
                {
                    ["number"] = plate.Number,
                    ["status"] = previousStatus.ToString()
                },
                After: new Dictionary<string, object?>
                {
                    ["number"] = plate.Number,
                    ["status"] = plate.Status.ToString(),
                    ["transition"] = transition,
                    ["parentLicensePlateId"] = plate.ParentLicensePlateId,
                    ["locationId"] = plate.CurrentLocationId,
                    ["referenceNumber"] = referenceNumber,
                    ["reason"] = reason
                },
                ActorUserId: userId),
            cancellationToken);

    private async Task<Result<LicensePlateDto>> ExecuteMutationAsync(
        Func<Task<LicensePlate>> mutation,
        string errorCode,
        string safeMessage,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(
            async () =>
            {
                await _unitOfWork.BeginTransactionAsync(cancellationToken);
                try
                {
                    var plate = await mutation();
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await _unitOfWork.CommitTransactionAsync(cancellationToken);
                    var loaded = await _unitOfWork.LicensePlates.GetByIdWithContentsAsync(
                        plate.Id,
                        cancellationToken);
                    return loaded is null
                        ? Result.Failure<LicensePlateDto>(WmsErrors.NotFound(
                            "license_plate.not_found",
                            $"License plate {plate.Id} was not found after the mutation."))
                        : Result.Success(ToDto(loaded));
                }
                catch
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    throw;
                }
            },
            errorCode,
            safeMessage);
    }

    private async Task<Result<T>> ExecuteAsync<T>(
        Func<Task<Result<T>>> operation,
        string errorCode,
        string safeMessage)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<T>(WmsErrors.Validation("license_plate.invalid", exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<T>(WmsErrors.BusinessRule("license_plate.rule_violation", exception.Message));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "License plate operation failed with {ErrorCode}", errorCode);
            return Result.Failure<T>(WmsErrors.FromException(exception, errorCode, safeMessage));
        }
    }

    private static LicensePlateDto ToDto(LicensePlate plate) =>
        new(
            plate.Id,
            plate.Number,
            plate.IsSscc,
            plate.Type,
            plate.WarehouseId,
            plate.Warehouse?.Code,
            plate.CurrentLocationId,
            plate.CurrentLocation?.Code,
            plate.Status,
            plate.IsActive,
            plate.ParentLicensePlateId,
            plate.ParentLicensePlate?.Number,
            plate.GrossWeightKg,
            plate.LengthCm,
            plate.WidthCm,
            plate.HeightCm,
            plate.SourceReference,
            plate.Notes,
            plate.Contents
                .Where(content => content.Quantity.Value > 0)
                .Select(content => new LicensePlateContentDto(
                    content.Id,
                    content.LicensePlateId,
                    content.ItemId,
                    content.Item?.Sku,
                    content.Item?.Name,
                    content.LotId,
                    content.Lot?.Number,
                    content.SerialNumberId,
                    content.SerialNumber?.Number,
                    content.InventoryStatusId,
                    content.InventoryStatus?.Code,
                    content.ItemPackagingId,
                    content.ItemPackaging?.Code,
                    content.Quantity.Value))
                .ToArray());

    private static LicensePlateHistoryDto ToHistoryDto(LicensePlateHistory history) =>
        new(
            history.Id,
            history.LicensePlateId,
            history.Action,
            history.UserId,
            history.OccurredAtUtc,
            history.ItemId,
            history.Item?.Sku,
            history.LotId,
            history.Lot?.Number,
            history.SerialNumberId,
            history.SerialNumber?.Number,
            history.Quantity,
            history.FromLocationId,
            history.ToLocationId,
            history.FromLicensePlateId,
            history.ToLicensePlateId,
            history.ReferenceNumber,
            history.Reason);

    private static LicensePlateContentInput ToInput(LicensePlateContent content) =>
        new(
            content.ItemId,
            content.Quantity.Value,
            content.LotId,
            content.SerialNumberId,
            content.InventoryStatusId,
            content.ItemPackagingId);

    private static LicensePlateNumberingDto ToDto(LicensePlateNumberSequence sequence) =>
        new(
            sequence.WarehouseId,
            sequence.Prefix,
            sequence.Padding,
            sequence.NextNumber,
            sequence.Revision);

    private static void EnsureMutable(LicensePlate plate)
    {
        if (!plate.IsMutable)
        {
            throw new InvalidOperationException(
                $"License plate '{plate.Number}' is {plate.Status} and cannot be mutated.");
        }
    }

    private static void EnsureDifferentPlates(LicensePlate source, LicensePlate target)
    {
        if (source.Id == target.Id)
        {
            throw new InvalidOperationException("Source and target license plates must be different.");
        }
    }

    private static void EnsureSameWarehouse(LicensePlate source, LicensePlate target)
    {
        if (source.WarehouseId != target.WarehouseId)
        {
            throw new InvalidOperationException("License plate operations cannot cross warehouse boundaries.");
        }
    }

    private static void RequireUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID is required.", nameof(userId));
        }
    }

    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required.", nameof(reason));
        }
    }

    private sealed record ContentValidation(Item Item, Lot? Lot, SerialNumber? Serial);
}
