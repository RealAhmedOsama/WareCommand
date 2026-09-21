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
using Wms.Application.Packing;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Packing;

public sealed class PackingService(
    WmsDbContext context,
    IUnitOfWork unitOfWork,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    IInventoryLedgerService inventoryLedgerService,
    IClock clock,
    ILogger<PackingService> logger) : IPackingService
{
    public async Task<Result<PackingStationDto>> CreateStationAsync(
        PackingStationInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.PackingExecute,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PackingStationDto>();
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var location = await context.Locations
                    .SingleOrDefaultAsync(value => value.Id == input.LocationId, cancellationToken)
                    ?? throw new InvalidOperationException("The packing station location was not found.");
                if (location.WarehouseId != input.WarehouseId || location.Type != LocationType.Packing)
                {
                    throw new InvalidOperationException(
                        "A packing station must use a packing location in the same warehouse.");
                }

                var normalizedCode = input.Code.Trim().ToUpperInvariant();
                var exists = await context.PackingStations.AnyAsync(
                    station => station.WarehouseId == input.WarehouseId &&
                               station.Code == normalizedCode,
                    cancellationToken);
                if (exists)
                {
                    throw new InvalidOperationException("A packing station with this code already exists.");
                }

                var station = new PackingStation(
                    input.Code,
                    input.Name,
                    input.WarehouseId,
                    input.LocationId,
                    input.SupportedDevices,
                    input.PrinterProfile,
                    input.ScaleProfile,
                    input.AllowedUserIds,
                    input.PermissionProfile);
                context.PackingStations.Add(station);
                await context.SaveChangesAsync(cancellationToken);
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.PackCompleted,
                        WmsAuditEntityTypes.Package,
                        $"station:{input.Code}",
                        input.WarehouseId,
                        After: new Dictionary<string, object?>
                        {
                            ["operation"] = "station.created",
                            ["code"] = station.Code,
                            ["locationId"] = station.LocationId
                        },
                        ActorUserId: userId),
                    cancellationToken);
                return Map(station);
            },
            "packing.station_create_failed",
            "The packing station could not be created.",
            cancellationToken);
    }

    public async Task<Result<PackingStationDto>> SetStationStatusAsync(
        int stationId,
        PackingStationStatus status,
        string idempotencyKey,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var station = await context.PackingStations
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == stationId, cancellationToken);
        if (station is null)
        {
            return Result.Failure<PackingStationDto>(WmsErrors.NotFound(
                "packing.station_not_found",
                "The packing station was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.PackingExecute,
            station.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PackingStationDto>();
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var current = await context.PackingStations
                    .SingleAsync(value => value.Id == stationId, cancellationToken);
                current.SetStatus(status);
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.PackCompleted,
                        WmsAuditEntityTypes.Package,
                        $"station:{stationId.ToString(CultureInfo.InvariantCulture)}",
                        current.WarehouseId,
                        After: new Dictionary<string, object?>
                        {
                            ["operation"] = "station.status",
                            ["status"] = status.ToString(),
                            ["idempotencyKey"] = idempotencyKey
                        },
                        ActorUserId: userId),
                    cancellationToken);
                return Map(current);
            },
            "packing.station_status_failed",
            "The packing station status could not be changed.",
            cancellationToken);
    }

    public async Task<Result<PackingSessionDto>> StartSessionAsync(
        PackingSessionStartInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.PackingExecute,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PackingSessionDto>();
        }

        var existing = await LoadSessionByNumberAsync(input.SessionNumber, cancellationToken);
        if (existing is not null)
        {
            return Result.Success(Map(existing));
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var station = await context.PackingStations
                    .SingleOrDefaultAsync(value => value.Id == input.PackingStationId, cancellationToken)
                    ?? throw new InvalidOperationException("The packing station was not found.");
                EnsureStationAvailable(station, input.WarehouseId, userId);
                var location = await context.Locations
                    .SingleAsync(value => value.Id == station.LocationId, cancellationToken);

                if (input.SourceType == PackingSourceType.SalesOrder && !input.SalesOrderId.HasValue)
                {
                    throw new InvalidOperationException("A sales-order packing session requires an order.");
                }

                if (input.SalesOrderId.HasValue)
                {
                    var order = await context.SalesOrders
                        .SingleOrDefaultAsync(value => value.Id == input.SalesOrderId.Value, cancellationToken)
                        ?? throw new InvalidOperationException("The sales order was not found.");
                    if (order.WarehouseId != input.WarehouseId ||
                        order.Status is SalesOrderStatus.Cancelled or SalesOrderStatus.Closed)
                    {
                        throw new InvalidOperationException(
                            "The sales order cannot be packed in this warehouse or lifecycle state.");
                    }
                }

                if (input.StagingLicensePlateId.HasValue)
                {
                    var stagingPlate = await context.LicensePlates
                        .SingleOrDefaultAsync(
                            value => value.Id == input.StagingLicensePlateId.Value,
                            cancellationToken)
                        ?? throw new InvalidOperationException("The staging license plate was not found.");
                    if (stagingPlate.WarehouseId != input.WarehouseId ||
                        !stagingPlate.CurrentLocationId.HasValue ||
                        stagingPlate.Status is LicensePlateStatus.Shipped or LicensePlateStatus.Voided)
                    {
                        throw new InvalidOperationException(
                            "The staging license plate is not available in the session warehouse.");
                    }
                }

                _ = location;
                var session = new PackingSession(
                    input.SessionNumber,
                    input.WarehouseId,
                    input.PackingStationId,
                    input.SourceType,
                    input.SourceReference,
                    input.SalesOrderId,
                    input.StagingLicensePlateId,
                    userId,
                    clock.UtcNow.UtcDateTime);
                context.PackingSessions.Add(session);
                await context.SaveChangesAsync(cancellationToken);
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.PackCompleted,
                        WmsAuditEntityTypes.Package,
                        session.SessionNumber,
                        session.WarehouseId,
                        After: new Dictionary<string, object?>
                        {
                            ["operation"] = "session.started",
                            ["sourceType"] = session.SourceType.ToString(),
                            ["sourceReference"] = session.SourceReference
                        },
                        ActorUserId: userId),
                    cancellationToken);
                return Map(session);
            },
            "packing.session_start_failed",
            "The packing session could not be started.",
            cancellationToken);
    }

    public async Task<Result<PackingSessionDto>> GetSessionAsync(
        int sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return Result.Failure<PackingSessionDto>(WmsErrors.NotFound(
                "packing.session_not_found",
                "The packing session was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.PackingExecute,
            session.WarehouseId,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<PackingSessionDto>()
            : Result.Success(Map(session));
    }

    public async Task<Result<ShipmentPackageDto>> CreatePackageAsync(
        PackingPackageCreateInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(input.PackingSessionId, cancellationToken);
        if (session is null)
        {
            return Result.Failure<ShipmentPackageDto>(WmsErrors.NotFound(
                "packing.session_not_found",
                "The packing session was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.PackingExecute,
            session.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ShipmentPackageDto>();
        }

        var existing = await context.ShipmentPackages
            .Include(value => value.Contents)
            .SingleOrDefaultAsync(value => value.PackageNumber == input.PackageNumber.Trim(), cancellationToken);
        if (existing is not null)
        {
            return Result.Success(Map(existing));
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var currentSession = await LoadSessionAsync(input.PackingSessionId, cancellationToken)
                    ?? throw new InvalidOperationException("The packing session was not found.");
                if (currentSession.IsTerminal)
                {
                    throw new InvalidOperationException("A terminal packing session cannot receive packages.");
                }

                var station = await context.PackingStations
                    .SingleAsync(value => value.Id == currentSession.PackingStationId, cancellationToken);
                EnsureStationAvailable(station, currentSession.WarehouseId, userId);
                var targetPlate = await context.LicensePlates
                    .SingleOrDefaultAsync(value => value.Id == input.TargetLicensePlateId, cancellationToken)
                    ?? throw new InvalidOperationException("The target package license plate was not found.");
                if (targetPlate.WarehouseId != currentSession.WarehouseId ||
                    targetPlate.CurrentLocationId != station.LocationId ||
                    !targetPlate.IsActive ||
                    targetPlate.Status is not (LicensePlateStatus.Open or LicensePlateStatus.Returned))
                {
                    throw new InvalidOperationException(
                        "The target package license plate must be active, open, and at the packing station.");
                }

                var targetStocks = await context.Stock
                    .Where(stock => stock.LicensePlateId == targetPlate.Id)
                    .ToListAsync(cancellationToken);
                if (targetStocks.Any(stock =>
                        stock.QuantityAvailable.Value > 0m || stock.QuantityReserved.Value > 0m) ||
                    await context.LicensePlateContents.AnyAsync(
                        content => content.LicensePlateId == targetPlate.Id,
                        cancellationToken))
                {
                    throw new InvalidOperationException(
                        "The target package license plate must be empty before a package is opened.");
                }

                var salesOrderId = input.SalesOrderId ?? currentSession.SalesOrderId;
                if (salesOrderId.HasValue && currentSession.SalesOrderId.HasValue &&
                    salesOrderId != currentSession.SalesOrderId && !input.AllowConsolidated)
                {
                    throw new InvalidOperationException(
                        "A package cannot change order ownership without consolidated-pack permission.");
                }

                var package = new ShipmentPackage(
                    input.PackageNumber,
                    currentSession.WarehouseId,
                    currentSession.Id,
                    targetPlate.Id,
                    input.PackageType,
                    salesOrderId,
                    input.AllowConsolidated,
                    input.ExpectedWeightKg,
                    input.WeightToleranceKg,
                    input.WeightTolerancePercent,
                    input.LabelReference);
                currentSession.AddPackage(package);
                context.ShipmentPackages.Add(package);
                await context.SaveChangesAsync(cancellationToken);
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.PackCompleted,
                        WmsAuditEntityTypes.Package,
                        package.PackageNumber,
                        package.WarehouseId,
                        After: new Dictionary<string, object?>
                        {
                            ["operation"] = "package.opened",
                            ["targetLicensePlateId"] = package.TargetLicensePlateId,
                            ["salesOrderId"] = package.SalesOrderId
                        },
                        ActorUserId: userId),
                    cancellationToken);
                return Map(package);
            },
            "packing.package_create_failed",
            "The package could not be opened.",
            cancellationToken);
    }

    public async Task<Result<ShipmentPackageDto>> GetPackageAsync(
        int packageId,
        CancellationToken cancellationToken = default)
    {
        var package = await LoadPackageAsync(packageId, cancellationToken);
        if (package is null)
        {
            return Result.Failure<ShipmentPackageDto>(WmsErrors.NotFound(
                "packing.package_not_found",
                "The package was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.PackingExecute,
            package.WarehouseId,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<ShipmentPackageDto>()
            : Result.Success(Map(package));
    }

    public async Task<Result<ShipmentPackageDto>> PackAsync(
        PackingScanInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var package = await LoadPackageAsync(input.ShipmentPackageId, cancellationToken);
        if (package is null)
        {
            return Result.Failure<ShipmentPackageDto>(WmsErrors.NotFound(
                "packing.package_not_found",
                "The package was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.PackingExecute,
            package.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ShipmentPackageDto>();
        }

        var requestHash = Hash(input);
        var replay = await TryReplayPackageAsync(
            package,
            "pack",
            input.IdempotencyKey,
            requestHash,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var current = await LoadPackageAsync(input.ShipmentPackageId, cancellationToken)
                    ?? throw new InvalidOperationException("The package was not found.");
                var session = current.PackingSession;
                if (session is null || session.Status != PackingSessionStatus.InProgress)
                {
                    throw new InvalidOperationException("The packing session is not available for packing.");
                }

                var station = await context.PackingStations
                    .SingleAsync(value => value.Id == session.PackingStationId, cancellationToken);
                EnsureStationAvailable(station, current.WarehouseId, userId);
                var orderLine = await context.SalesOrderLines
                    .SingleOrDefaultAsync(value => value.Id == input.SalesOrderLineId, cancellationToken)
                    ?? throw new InvalidOperationException("The sales-order line was not found.");
                if ((current.SalesOrderId.HasValue &&
                     current.SalesOrderId != orderLine.SalesOrderId) ||
                    (session.SalesOrderId.HasValue &&
                     session.SalesOrderId != orderLine.SalesOrderId))
                {
                    if (!current.AllowConsolidated)
                    {
                        throw new InvalidOperationException(
                            "The scanned order line does not belong to this package or packing session.");
                    }
                }

                if (current.SalesOrderId is null && !current.AllowConsolidated)
                {
                    throw new InvalidOperationException(
                        "A package without an order owner must explicitly allow consolidated packing.");
                }

                if (orderLine.ItemId != input.ItemId ||
                    orderLine.RemainingToPackBaseQuantity < input.Quantity)
                {
                    throw new InvalidOperationException(
                        "The scanned item or quantity does not match the remaining picked order quantity.");
                }

                var sourceLocation = await context.Locations
                    .SingleOrDefaultAsync(value => value.Id == input.SourceLocationId, cancellationToken)
                    ?? throw new InvalidOperationException("The source packing location was not found.");
                if (sourceLocation.WarehouseId != current.WarehouseId ||
                    sourceLocation.Type is not (LocationType.Staging or LocationType.Packing))
                {
                    throw new InvalidOperationException(
                        "Package verification must scan stock from a staging or packing location in the same warehouse.");
                }

                var stationLocation = await context.Locations
                    .SingleAsync(value => value.Id == station.LocationId, cancellationToken);
                var targetPlate = await context.LicensePlates
                    .SingleAsync(value => value.Id == current.TargetLicensePlateId, cancellationToken);
                if (targetPlate.CurrentLocationId != stationLocation.Id ||
                    targetPlate.Status is not (LicensePlateStatus.Open or LicensePlateStatus.Returned))
                {
                    throw new InvalidOperationException("The target package license plate is not open at the station.");
                }

                if (input.SourceLicensePlateId == targetPlate.Id && sourceLocation.Id != stationLocation.Id)
                {
                    throw new InvalidOperationException(
                        "The source LPN cannot also be the target package LPN across different locations.");
                }

                var sourceStock = await FindStockAsync(
                    input.ItemId,
                    input.SourceLocationId,
                    input.LotId,
                    input.SerialNumberId,
                    input.SerialNumber,
                    input.InventoryStatusId,
                    input.SourceLicensePlateId,
                    input.OwnerKind,
                    input.InventoryOwnerId,
                    input.OwnerCodeSnapshot,
                    cancellationToken);
                if (sourceStock is null || sourceStock.GetAvailableQuantity().Value < input.Quantity)
                {
                    throw new InvalidOperationException(
                        "The scanned source stock is unavailable or no longer has enough quantity.");
                }

                var item = await context.Items
                    .SingleAsync(value => value.Id == input.ItemId, cancellationToken);
                ValidateSerialQuantity(item, input.Quantity, input.SerialNumberId);
                var serial = await ResolveSerialAsync(input, sourceStock, cancellationToken);
                var quantity = new Quantity(input.Quantity);
                var movement = Movement.CreateTransfer(
                    input.ItemId,
                    sourceLocation.Id,
                    stationLocation.Id,
                    quantity,
                    userId,
                    input.LotId,
                    serial?.Number ?? input.SerialNumber,
                    current.PackageNumber,
                    input.ScanReference ?? "packed stock",
                    clock.UtcNow.UtcDateTime,
                    input.SerialNumberId,
                    input.InventoryStatusId,
                    input.SourceLicensePlateId,
                    targetPlate.Id);
                movement.SetOwnership(
                    input.OwnerKind,
                    input.InventoryOwnerId,
                    input.OwnerCodeSnapshot);
                context.Movements.Add(movement);

                sourceStock.RemoveQuantity(quantity);
                if (sourceStock.QuantityAvailable.Value == 0m && sourceStock.QuantityReserved.Value == 0m)
                {
                    context.Stock.Remove(sourceStock);
                }

                var targetStock = await FindStockAsync(
                    input.ItemId,
                    stationLocation.Id,
                    input.LotId,
                    input.SerialNumberId,
                    serial?.Number ?? input.SerialNumber,
                    input.InventoryStatusId,
                    targetPlate.Id,
                    input.OwnerKind,
                    input.InventoryOwnerId,
                    input.OwnerCodeSnapshot,
                    cancellationToken);
                if (targetStock is null)
                {
                    targetStock = new Stock(
                        input.ItemId,
                        stationLocation.Id,
                        quantity,
                        input.LotId,
                        serial?.Number ?? input.SerialNumber,
                        input.SerialNumberId,
                        input.InventoryStatusId,
                        targetPlate.Id,
                        input.OwnerKind,
                        input.InventoryOwnerId,
                        input.OwnerCodeSnapshot);
                    context.Stock.Add(targetStock);
                }
                else
                {
                    targetStock.AddQuantity(quantity);
                }

                var targetContent = await context.LicensePlateContents
                    .SingleOrDefaultAsync(content =>
                        content.LicensePlateId == targetPlate.Id &&
                        content.ItemId == input.ItemId &&
                        content.LotId == input.LotId &&
                        content.SerialNumberId == input.SerialNumberId &&
                        content.InventoryStatusId == input.InventoryStatusId &&
                        content.OwnerKind == input.OwnerKind &&
                        content.InventoryOwnerId == input.InventoryOwnerId &&
                        content.OwnerCodeSnapshot == InventoryOwnershipDimension.NormalizeOwnerCode(
                            input.OwnerKind,
                            input.InventoryOwnerId,
                            input.OwnerCodeSnapshot),
                        cancellationToken);
                if (targetContent is null)
                {
                    context.LicensePlateContents.Add(new LicensePlateContent(
                        targetPlate.Id,
                        input.ItemId,
                        quantity,
                        input.LotId,
                        input.SerialNumberId,
                        input.InventoryStatusId,
                        ownerKind: input.OwnerKind,
                        inventoryOwnerId: input.InventoryOwnerId,
                        ownerCodeSnapshot: input.OwnerCodeSnapshot));
                }
                else
                {
                    targetContent.AddQuantity(quantity);
                }

                if (serial is not null)
                {
                    serial.MoveTo(
                        current.WarehouseId,
                        stationLocation.Id,
                        targetPlate.Number,
                        clock.UtcNow.UtcDateTime,
                        targetPlate.Id);
                }

                await inventoryLedgerService.RecordAsync(
                    [
                        new InventoryLedgerEntryRequest(
                            InventoryTransactionType.Transfer,
                            ToLedgerKey(current.WarehouseId, sourceLocation.Id, sourceStock, item),
                            -input.Quantity,
                            ActorUserId: userId,
                            ReferenceType: "ShipmentPackage",
                            ReferenceId: current.PackageNumber,
                            ReferenceLine: input.SalesOrderLineId,
                            Reason: "packed source stock",
                            OccurredAtUtc: movement.Timestamp,
                            IdempotencyKey: $"pack:{current.Id}:{input.IdempotencyKey}:source",
                            TransactionGroupId: $"pack:{current.Id}:{input.IdempotencyKey}"),
                        new InventoryLedgerEntryRequest(
                            InventoryTransactionType.Transfer,
                            new InventoryBalanceKey(
                                current.WarehouseId,
                                stationLocation.Id,
                                input.ItemId,
                                input.LotId,
                                input.SerialNumberId,
                                serial?.Number ?? input.SerialNumber,
                                targetPlate.Id,
                                input.InventoryStatusId,
                                item.UnitOfMeasure,
                                input.OwnerKind,
                                input.InventoryOwnerId,
                                input.OwnerCodeSnapshot),
                            input.Quantity,
                            ActorUserId: userId,
                            ReferenceType: "ShipmentPackage",
                            ReferenceId: current.PackageNumber,
                            ReferenceLine: input.SalesOrderLineId,
                            Reason: "packed target LPN",
                            OccurredAtUtc: movement.Timestamp,
                            IdempotencyKey: $"pack:{current.Id}:{input.IdempotencyKey}:destination",
                            TransactionGroupId: $"pack:{current.Id}:{input.IdempotencyKey}",
                            EntrySequence: 2)
                    ],
                    cancellationToken);

                var expectedWeight = item.NetWeightKg.HasValue
                    ? (decimal?)(item.NetWeightKg.Value * input.Quantity)
                    : null;
                var content = current.Contents.SingleOrDefault(value =>
                    value.SalesOrderLineId == orderLine.Id &&
                    value.ItemId == input.ItemId &&
                    value.SourceLocationId == input.SourceLocationId &&
                    value.SourceLicensePlateId == input.SourceLicensePlateId &&
                    value.LotId == input.LotId &&
                    value.SerialNumberId == input.SerialNumberId &&
                    value.InventoryStatusId == input.InventoryStatusId &&
                    value.OwnerKind == input.OwnerKind &&
                    value.InventoryOwnerId == input.InventoryOwnerId &&
                    value.OwnerCodeSnapshot == InventoryOwnershipDimension.NormalizeOwnerCode(
                        input.OwnerKind,
                        input.InventoryOwnerId,
                        input.OwnerCodeSnapshot));
                if (content is null)
                {
                    current.AddContent(new ShipmentPackageContent(
                        current.Id,
                        orderLine.Id,
                        input.ItemId,
                        input.Quantity,
                        orderLine.BaseUnitOfMeasure,
                        input.SourceLocationId,
                        input.SourceLicensePlateId,
                        input.LotId,
                        input.SerialNumberId,
                        serial?.Number ?? input.SerialNumber,
                        input.InventoryStatusId,
                        expectedWeight,
                        input.OwnerKind,
                        input.InventoryOwnerId,
                        input.OwnerCodeSnapshot));
                }
                else
                {
                    content.AddQuantity(input.Quantity, expectedWeight);
                }

                orderLine.RecordPacked(input.Quantity);
                context.PackingCommands.Add(new PackingCommand(
                    null,
                    current.Id,
                    "pack",
                    input.IdempotencyKey,
                    requestHash,
                    userId,
                    clock.UtcNow.UtcDateTime));
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.PackCompleted,
                        WmsAuditEntityTypes.Package,
                        current.PackageNumber,
                        current.WarehouseId,
                        After: new Dictionary<string, object?>
                        {
                            ["operation"] = "content.packed",
                            ["salesOrderLineId"] = input.SalesOrderLineId,
                            ["itemId"] = input.ItemId,
                            ["quantity"] = input.Quantity,
                            ["targetLicensePlateId"] = targetPlate.Id
                        },
                        ActorUserId: userId),
                    cancellationToken);
                return Map(current);
            },
            "packing.content_pack_failed",
            "The scanned item could not be packed.",
            cancellationToken);
    }

    public async Task<Result<ShipmentPackageDto>> ClosePackageAsync(
        PackingPackageMeasureInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var package = await LoadPackageAsync(input.ShipmentPackageId, cancellationToken);
        if (package is null)
        {
            return Result.Failure<ShipmentPackageDto>(WmsErrors.NotFound(
                "packing.package_not_found",
                "The package was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.PackingExecute,
            package.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ShipmentPackageDto>();
        }

        var requestHash = Hash(input);
        var replay = await TryReplayPackageAsync(
            package,
            "close",
            input.IdempotencyKey,
            requestHash,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var current = await LoadPackageAsync(input.ShipmentPackageId, cancellationToken)
                    ?? throw new InvalidOperationException("The package was not found.");
                var plate = await context.LicensePlates
                    .SingleAsync(value => value.Id == current.TargetLicensePlateId, cancellationToken);
                current.Close(
                    userId,
                    clock.UtcNow.UtcDateTime,
                    input.ActualWeightKg,
                    input.LengthCm,
                    input.WidthCm,
                    input.HeightCm);
                plate.Close();
                AddCommand(current, "close", input.IdempotencyKey, requestHash, userId);
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.PackCompleted,
                        WmsAuditEntityTypes.Package,
                        current.PackageNumber,
                        current.WarehouseId,
                        After: new Dictionary<string, object?>
                        {
                            ["operation"] = "package.closed",
                            ["actualWeightKg"] = input.ActualWeightKg,
                            ["lengthCm"] = input.LengthCm,
                            ["widthCm"] = input.WidthCm,
                            ["heightCm"] = input.HeightCm
                        },
                        ActorUserId: userId),
                    cancellationToken);
                return Map(current);
            },
            "packing.package_close_failed",
            "The package could not be closed.",
            cancellationToken);
    }

    public async Task<Result<ShipmentPackageDto>> RemoveContentAsync(
        PackingScanInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var package = await LoadPackageAsync(input.ShipmentPackageId, cancellationToken);
        if (package is null)
        {
            return Result.Failure<ShipmentPackageDto>(WmsErrors.NotFound(
                "packing.package_not_found",
                "The package was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.PackingExecute,
            package.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ShipmentPackageDto>();
        }

        var requestHash = Hash(input);
        var replay = await TryReplayPackageAsync(
            package,
            "remove",
            input.IdempotencyKey,
            requestHash,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var current = await LoadPackageAsync(input.ShipmentPackageId, cancellationToken)
                    ?? throw new InvalidOperationException("The package was not found.");
                var content = current.Contents.SingleOrDefault(value =>
                    value.SalesOrderLineId == input.SalesOrderLineId &&
                    value.ItemId == input.ItemId &&
                    value.SourceLocationId == input.SourceLocationId &&
                    value.SourceLicensePlateId == input.SourceLicensePlateId &&
                    value.LotId == input.LotId &&
                    value.SerialNumberId == input.SerialNumberId &&
                    value.InventoryStatusId == input.InventoryStatusId &&
                    value.OwnerKind == input.OwnerKind &&
                    value.InventoryOwnerId == input.InventoryOwnerId &&
                    value.OwnerCodeSnapshot == InventoryOwnershipDimension.NormalizeOwnerCode(
                        input.OwnerKind,
                        input.InventoryOwnerId,
                        input.OwnerCodeSnapshot));
                if (content is null)
                {
                    throw new InvalidOperationException("The package does not contain the scanned identity.");
                }

                var session = await context.PackingSessions
                    .SingleAsync(value => value.Id == current.PackingSessionId, cancellationToken);
                var station = await context.PackingStations
                    .SingleAsync(value => value.Id == session.PackingStationId, cancellationToken);
                var targetPlate = await context.LicensePlates
                    .SingleAsync(value => value.Id == current.TargetLicensePlateId, cancellationToken);
                if (targetPlate.Status != LicensePlateStatus.Open)
                {
                    throw new InvalidOperationException("Reopen the package before removing package content.");
                }

                var targetStock = await FindStockAsync(
                    input.ItemId,
                    station.LocationId,
                    input.LotId,
                    input.SerialNumberId,
                    input.SerialNumber,
                    input.InventoryStatusId,
                    targetPlate.Id,
                    input.OwnerKind,
                    input.InventoryOwnerId,
                    input.OwnerCodeSnapshot,
                    cancellationToken)
                    ?? throw new InvalidOperationException("The package target stock was not found.");
                if (targetStock.GetAvailableQuantity().Value < input.Quantity)
                {
                    throw new InvalidOperationException("The package target stock cannot satisfy the removal.");
                }

                var sourceStock = await FindStockAsync(
                    input.ItemId,
                    input.SourceLocationId,
                    input.LotId,
                    input.SerialNumberId,
                    input.SerialNumber,
                    input.InventoryStatusId,
                    input.SourceLicensePlateId,
                    input.OwnerKind,
                    input.InventoryOwnerId,
                    input.OwnerCodeSnapshot,
                    cancellationToken);
                var sourceLocation = await context.Locations
                    .SingleAsync(value => value.Id == input.SourceLocationId, cancellationToken);
                var item = await context.Items.SingleAsync(value => value.Id == input.ItemId, cancellationToken);
                var quantity = new Quantity(input.Quantity);
                var serial = input.SerialNumberId.HasValue
                    ? await context.SerialNumbers.SingleAsync(
                        value => value.Id == input.SerialNumberId.Value,
                        cancellationToken)
                    : null;
                var movement = Movement.CreateTransfer(
                    input.ItemId,
                    station.LocationId,
                    sourceLocation.Id,
                    quantity,
                    userId,
                    input.LotId,
                    serial?.Number ?? input.SerialNumber,
                    current.PackageNumber,
                    "repacked content removed",
                    clock.UtcNow.UtcDateTime,
                    input.SerialNumberId,
                    input.InventoryStatusId,
                    targetPlate.Id,
                    input.SourceLicensePlateId);
                movement.SetOwnership(
                    input.OwnerKind,
                    input.InventoryOwnerId,
                    input.OwnerCodeSnapshot);
                context.Movements.Add(movement);

                targetStock.RemoveQuantity(quantity);
                if (targetStock.QuantityAvailable.Value == 0m && targetStock.QuantityReserved.Value == 0m)
                {
                    context.Stock.Remove(targetStock);
                }

                if (sourceStock is null)
                {
                    sourceStock = new Stock(
                        input.ItemId,
                        sourceLocation.Id,
                        quantity,
                        input.LotId,
                        serial?.Number ?? input.SerialNumber,
                        input.SerialNumberId,
                        input.InventoryStatusId,
                        input.SourceLicensePlateId,
                        input.OwnerKind,
                        input.InventoryOwnerId,
                        input.OwnerCodeSnapshot);
                    context.Stock.Add(sourceStock);
                }
                else
                {
                    sourceStock.AddQuantity(quantity);
                }

                var targetContent = await context.LicensePlateContents
                    .SingleAsync(contentRow =>
                        contentRow.LicensePlateId == targetPlate.Id &&
                        contentRow.ItemId == input.ItemId &&
                        contentRow.LotId == input.LotId &&
                        contentRow.SerialNumberId == input.SerialNumberId &&
                        contentRow.InventoryStatusId == input.InventoryStatusId &&
                        contentRow.OwnerKind == input.OwnerKind &&
                        contentRow.InventoryOwnerId == input.InventoryOwnerId &&
                        contentRow.OwnerCodeSnapshot == InventoryOwnershipDimension.NormalizeOwnerCode(
                            input.OwnerKind,
                            input.InventoryOwnerId,
                            input.OwnerCodeSnapshot),
                        cancellationToken);
                targetContent.RemoveQuantity(quantity);
                if (targetContent.Quantity.Value == 0m)
                {
                    context.LicensePlateContents.Remove(targetContent);
                }

                if (serial is not null)
                {
                    serial.MoveTo(
                        current.WarehouseId,
                        sourceLocation.Id,
                        input.SourceLicensePlateId.HasValue
                            ? await context.LicensePlates
                                .Where(value => value.Id == input.SourceLicensePlateId.Value)
                                .Select(value => value.Number)
                                .SingleAsync(cancellationToken)
                            : null,
                        clock.UtcNow.UtcDateTime,
                        input.SourceLicensePlateId);
                }

                await inventoryLedgerService.RecordAsync(
                    [
                        new InventoryLedgerEntryRequest(
                            InventoryTransactionType.Transfer,
                            new InventoryBalanceKey(
                                current.WarehouseId,
                                station.LocationId,
                                input.ItemId,
                                input.LotId,
                                input.SerialNumberId,
                                serial?.Number ?? input.SerialNumber,
                                targetPlate.Id,
                                input.InventoryStatusId,
                                item.UnitOfMeasure,
                                input.OwnerKind,
                                input.InventoryOwnerId,
                                input.OwnerCodeSnapshot),
                            -input.Quantity,
                            ActorUserId: userId,
                            ReferenceType: "ShipmentPackage",
                            ReferenceId: current.PackageNumber,
                            ReferenceLine: input.SalesOrderLineId,
                            Reason: "removed packed content",
                            OccurredAtUtc: movement.Timestamp,
                            IdempotencyKey: $"pack:{current.Id}:{input.IdempotencyKey}:source",
                            TransactionGroupId: $"pack:{current.Id}:{input.IdempotencyKey}"),
                        new InventoryLedgerEntryRequest(
                            InventoryTransactionType.Transfer,
                            ToLedgerKey(current.WarehouseId, sourceLocation.Id, sourceStock, item),
                            input.Quantity,
                            ActorUserId: userId,
                            ReferenceType: "ShipmentPackage",
                            ReferenceId: current.PackageNumber,
                            ReferenceLine: input.SalesOrderLineId,
                            Reason: "removed packed content",
                            OccurredAtUtc: movement.Timestamp,
                            IdempotencyKey: $"pack:{current.Id}:{input.IdempotencyKey}:destination",
                            TransactionGroupId: $"pack:{current.Id}:{input.IdempotencyKey}",
                            EntrySequence: 2)
                    ],
                    cancellationToken);

                current.RemoveContent(content, input.Quantity);
                var orderLine = await context.SalesOrderLines
                    .SingleAsync(value => value.Id == input.SalesOrderLineId, cancellationToken);
                orderLine.ReversePacked(input.Quantity);
                AddCommand(current, "remove", input.IdempotencyKey, requestHash, userId);
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.PackCompleted,
                        WmsAuditEntityTypes.Package,
                        current.PackageNumber,
                        current.WarehouseId,
                        After: new Dictionary<string, object?>
                        {
                            ["operation"] = "content.removed",
                            ["salesOrderLineId"] = input.SalesOrderLineId,
                            ["quantity"] = input.Quantity
                        },
                        ActorUserId: userId),
                    cancellationToken);
                return Map(current);
            },
            "packing.content_remove_failed",
            "The package content could not be removed.",
            cancellationToken);
    }

    public async Task<Result<ShipmentPackageDto>> ReopenPackageAsync(
        PackingPackageCommandInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var package = await LoadPackageAsync(input.ShipmentPackageId, cancellationToken);
        if (package is null)
        {
            return Result.Failure<ShipmentPackageDto>(WmsErrors.NotFound(
                "packing.package_not_found",
                "The package was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.PackingExecute,
            package.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ShipmentPackageDto>();
        }

        var requestHash = Hash(input);
        var replay = await TryReplayPackageAsync(
            package,
            "reopen",
            input.IdempotencyKey,
            requestHash,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var current = await LoadPackageAsync(input.ShipmentPackageId, cancellationToken)
                    ?? throw new InvalidOperationException("The package was not found.");
                if (current.Status != ShipmentPackageStatus.Closed)
                {
                    throw new InvalidOperationException("Only a closed package can be reopened.");
                }

                var plate = await context.LicensePlates
                    .SingleAsync(value => value.Id == current.TargetLicensePlateId, cancellationToken);
                plate.Reopen();
                current.Reopen(input.Reason ?? "controlled packing correction");
                AddCommand(current, "reopen", input.IdempotencyKey, requestHash, userId);
                return Map(current);
            },
            "packing.package_reopen_failed",
            "The package could not be reopened.",
            cancellationToken);
    }

    public async Task<Result<ShipmentPackageDto>> VoidPackageAsync(
        PackingPackageCommandInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var package = await LoadPackageAsync(input.ShipmentPackageId, cancellationToken);
        if (package is null)
        {
            return Result.Failure<ShipmentPackageDto>(WmsErrors.NotFound(
                "packing.package_not_found",
                "The package was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.PackingExecute,
            package.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ShipmentPackageDto>();
        }

        var requestHash = Hash(input);
        var replay = await TryReplayPackageAsync(
            package,
            "void",
            input.IdempotencyKey,
            requestHash,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var current = await LoadPackageAsync(input.ShipmentPackageId, cancellationToken)
                    ?? throw new InvalidOperationException("The package was not found.");
                if (current.PackedQuantity > 0m)
                {
                    throw new InvalidOperationException(
                        "Remove all package content before voiding the package.");
                }

                var plate = await context.LicensePlates
                    .SingleAsync(value => value.Id == current.TargetLicensePlateId, cancellationToken);
                if (plate.Status == LicensePlateStatus.Closed)
                {
                    plate.Reopen();
                }

                current.Void(userId, input.Reason ?? "controlled packing void", clock.UtcNow.UtcDateTime);
                plate.Void();
                AddCommand(current, "void", input.IdempotencyKey, requestHash, userId);
                return Map(current);
            },
            "packing.package_void_failed",
            "The package could not be voided.",
            cancellationToken);
    }

    public async Task<Result<PackingSessionDto>> CompleteSessionAsync(
        int sessionId,
        string idempotencyKey,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return Result.Failure<PackingSessionDto>(WmsErrors.NotFound(
                "packing.session_not_found",
                "The packing session was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.PackingExecute,
            session.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PackingSessionDto>();
        }

        var request = new { sessionId, idempotencyKey };
        var requestHash = Hash(request);
        var existingCommand = await context.PackingCommands
            .SingleOrDefaultAsync(command =>
                command.PackingSessionId == sessionId &&
                command.Operation == "complete" &&
                command.IdempotencyKey == idempotencyKey.Trim(),
                cancellationToken);
        if (existingCommand is not null)
        {
            return existingCommand.RequestHash == requestHash
                ? Result.Success(Map(session))
                : Result.Failure<PackingSessionDto>(WmsErrors.Conflict(
                    "packing.idempotency_reuse",
                    "The idempotency key was already used with a different request."));
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var current = await LoadSessionAsync(sessionId, cancellationToken)
                    ?? throw new InvalidOperationException("The packing session was not found.");
                current.Complete(userId, clock.UtcNow.UtcDateTime);
                context.PackingCommands.Add(new PackingCommand(
                    current.Id,
                    null,
                    "complete",
                    idempotencyKey,
                    requestHash,
                    userId,
                    clock.UtcNow.UtcDateTime));
                return Map(current);
            },
            "packing.session_complete_failed",
            "The packing session could not be completed.",
            cancellationToken);
    }

    private async Task<Result<T>> ExecuteMutationAsync<T>(
        Func<Task<T>> mutation,
        string errorCode,
        string safeMessage,
        CancellationToken cancellationToken)
    {
        var transactionStarted = true;
        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);
            var value = await mutation();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionStarted = false;
            return Result.Success(value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            return Result.Failure<T>(WmsErrors.Concurrency(
                "packing.concurrency_conflict",
                exception.Message));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<T>(WmsErrors.Validation(errorCode, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<T>(WmsErrors.BusinessRule(errorCode, exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Packing operation failed with {ErrorCode}", errorCode);
            return Result.Failure<T>(WmsErrors.FromException(exception, errorCode, safeMessage));
        }
        finally
        {
            if (transactionStarted)
            {
                await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }
        }
    }

    private async Task<ShipmentPackage?> LoadPackageAsync(
        int packageId,
        CancellationToken cancellationToken) =>
        await context.ShipmentPackages
            .Include(package => package.Contents)
            .Include(package => package.PackingSession)
            .SingleOrDefaultAsync(package => package.Id == packageId, cancellationToken);

    private async Task<PackingSession?> LoadSessionAsync(
        int sessionId,
        CancellationToken cancellationToken) =>
        await context.PackingSessions
            .Include(session => session.Packages)
            .ThenInclude(package => package.Contents)
            .SingleOrDefaultAsync(session => session.Id == sessionId, cancellationToken);

    private async Task<PackingSession?> LoadSessionByNumberAsync(
        string sessionNumber,
        CancellationToken cancellationToken) =>
        await context.PackingSessions
            .Include(session => session.Packages)
            .ThenInclude(package => package.Contents)
            .SingleOrDefaultAsync(
                session => session.SessionNumber == sessionNumber.Trim(),
                cancellationToken);

    private async Task<Result<ShipmentPackageDto>?> TryReplayPackageAsync(
        ShipmentPackage package,
        string operation,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Result.Failure<ShipmentPackageDto>(WmsErrors.Validation(
                "packing.idempotency_required",
                "Every package command requires an idempotency key."));
        }

        var command = await context.PackingCommands
            .SingleOrDefaultAsync(value =>
                value.ShipmentPackageId == package.Id &&
                value.Operation == operation &&
                value.IdempotencyKey == idempotencyKey.Trim(),
                cancellationToken);
        if (command is null)
        {
            return null;
        }

        return command.RequestHash == requestHash
            ? Result.Success(Map(package))
            : Result.Failure<ShipmentPackageDto>(WmsErrors.Conflict(
                "packing.idempotency_reuse",
                "The idempotency key was already used with a different request."));
    }

    private void AddCommand(
        ShipmentPackage package,
        string operation,
        string idempotencyKey,
        string requestHash,
        string userId) =>
        context.PackingCommands.Add(new PackingCommand(
            null,
            package.Id,
            operation,
            idempotencyKey,
            requestHash,
            userId,
            clock.UtcNow.UtcDateTime));

    private async Task<Stock?> FindStockAsync(
        int itemId,
        int locationId,
        int? lotId,
        int? serialNumberId,
        string? serialNumber,
        int inventoryStatusId,
        int? licensePlateId,
        InventoryOwnerKind ownerKind,
        int? inventoryOwnerId,
        string? ownerCodeSnapshot,
        CancellationToken cancellationToken) =>
        await context.Stock.SingleOrDefaultAsync(stock =>
            stock.ItemId == itemId &&
            stock.LocationId == locationId &&
            stock.LotId == lotId &&
            stock.SerialNumberId == serialNumberId &&
            stock.SerialNumber == serialNumber &&
            stock.InventoryStatusId == inventoryStatusId &&
            stock.LicensePlateId == licensePlateId &&
            stock.OwnerKind == ownerKind &&
            stock.InventoryOwnerId == inventoryOwnerId &&
            stock.OwnerCodeSnapshot == InventoryOwnershipDimension.NormalizeOwnerCode(
                ownerKind,
                inventoryOwnerId,
                ownerCodeSnapshot),
            cancellationToken);

    private async Task<SerialNumber?> ResolveSerialAsync(
        PackingScanInput input,
        Stock sourceStock,
        CancellationToken cancellationToken)
    {
        if (!input.SerialNumberId.HasValue)
        {
            return null;
        }

        var serial = await context.SerialNumbers
            .SingleOrDefaultAsync(value => value.Id == input.SerialNumberId.Value, cancellationToken)
            ?? throw new InvalidOperationException("The scanned serial identity was not found.");
        if (serial.ItemId != input.ItemId ||
            serial.LotId != input.LotId ||
            serial.CurrentLocationId != input.SourceLocationId ||
            serial.CurrentLicensePlateId != input.SourceLicensePlateId ||
            !string.Equals(serial.Number, input.SerialNumber, StringComparison.OrdinalIgnoreCase) ||
            sourceStock.SerialNumberId != serial.Id)
        {
            throw new InvalidOperationException("The scanned serial does not match the source stock identity.");
        }

        return serial;
    }

    private static void ValidateSerialQuantity(Item item, decimal quantity, int? serialNumberId)
    {
        if (item.RequiresSerial != serialNumberId.HasValue)
        {
            throw new InvalidOperationException(
                item.RequiresSerial
                    ? "The scanned item requires a serial identity."
                    : "A serial identity was supplied for a non-serial item.");
        }

        if (serialNumberId.HasValue && quantity != 1m)
        {
            throw new InvalidOperationException("A serial-controlled pack scan must contain exactly one unit.");
        }
    }

    private static InventoryBalanceKey ToLedgerKey(
        int warehouseId,
        int locationId,
        Stock stock,
        Item item) => new(
            warehouseId,
            locationId,
            stock.ItemId,
            stock.LotId,
            stock.SerialNumberId,
            stock.SerialNumber,
            stock.LicensePlateId,
            stock.InventoryStatusId,
            item.UnitOfMeasure,
            stock.OwnerKind,
            stock.InventoryOwnerId,
            stock.OwnerCodeSnapshot);

    private static void EnsureStationAvailable(
        PackingStation station,
        int warehouseId,
        string userId)
    {
        if (station.WarehouseId != warehouseId || !station.IsAvailable)
        {
            throw new InvalidOperationException("The packing station is not available for this warehouse.");
        }

        if (!string.IsNullOrWhiteSpace(station.AllowedUserIds))
        {
            var allowed = station.AllowedUserIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!allowed.Contains(userId, StringComparer.Ordinal))
            {
                throw new UnauthorizedAccessException("The user is not permitted to operate this packing station.");
            }
        }
    }

    private static string Hash<T>(T value)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static PackingStationDto Map(PackingStation station) => new(
        station.Id,
        station.Code,
        station.Name,
        station.WarehouseId,
        station.LocationId,
        station.Status,
        station.SupportedDevices,
        station.PrinterProfile,
        station.ScaleProfile,
        station.AllowedUserIds,
        station.PermissionProfile,
        station.Revision);

    private static PackingSessionDto Map(PackingSession session) => new(
        session.Id,
        session.SessionNumber,
        session.WarehouseId,
        session.PackingStationId,
        session.SourceType,
        session.SourceReference,
        session.SalesOrderId,
        session.StagingLicensePlateId,
        session.Status,
        session.UserId,
        session.StartedAtUtc,
        session.CompletedAtUtc,
        session.Packages.OrderBy(package => package.Id).Select(Map).ToArray(),
        session.Revision);

    private static ShipmentPackageDto Map(ShipmentPackage package) => new(
        package.Id,
        package.PackageNumber,
        package.WarehouseId,
        package.PackingSessionId,
        package.TargetLicensePlateId,
        package.PackageType,
        package.SalesOrderId,
        package.AllowConsolidated,
        package.Status,
        package.PackedQuantity,
        package.ExpectedWeightKg,
        package.ActualWeightKg,
        package.WeightToleranceKg,
        package.WeightTolerancePercent,
        package.LengthCm,
        package.WidthCm,
        package.HeightCm,
        package.VolumeCubicMeters,
        package.LabelReference,
        package.ClosedByUserId,
        package.ClosedAtUtc,
        package.Contents.OrderBy(content => content.Id).Select(Map).ToArray(),
        package.Revision);

    private static ShipmentPackageContentDto Map(ShipmentPackageContent content) => new(
        content.Id,
        content.ShipmentPackageId,
        content.SalesOrderLineId,
        content.ItemId,
        content.Quantity,
        content.BaseUnitOfMeasure,
        content.SourceLocationId,
        content.SourceLicensePlateId,
        content.LotId,
        content.SerialNumberId,
        content.SerialNumber,
        content.InventoryStatusId,
        content.ExpectedWeightKg,
        content.Revision,
        content.OwnerKind,
        content.InventoryOwnerId,
        content.OwnerCodeSnapshot);
}
