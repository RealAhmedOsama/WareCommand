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
using Wms.Application.Shipping;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Shipping;

public sealed class ShipmentService(
    WmsDbContext context,
    IUnitOfWork unitOfWork,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    IInventoryLedgerService inventoryLedgerService,
    IClock clock,
    ILogger<ShipmentService> logger) : IShipmentService
{
    public Task<Result<CarrierDto>> CreateCarrierAsync(
        CarrierInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(
            async () =>
            {
                var code = input.Code.Trim().ToUpperInvariant();
                if (await context.Carriers.AnyAsync(value => value.Code == code, cancellationToken))
                {
                    throw new InvalidOperationException("A carrier with this code already exists.");
                }

                var carrier = new Carrier(input.Code, input.Name, input.TrackingUrlTemplate);
                context.Carriers.Add(carrier);
                await context.SaveChangesAsync(cancellationToken);
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.SettingsChanged,
                        WmsAuditEntityTypes.Settings,
                        $"carrier:{carrier.Code}",
                        null,
                        After: new Dictionary<string, object?>
                        {
                            ["operation"] = "carrier.created",
                            ["code"] = carrier.Code
                        },
                        ActorUserId: userId),
                    cancellationToken);
                return Map(carrier);
            },
            "shipping.carrier_create_failed",
            "The carrier could not be created.",
            cancellationToken);

    public Task<Result<CarrierServiceDto>> CreateCarrierServiceAsync(
        CarrierServiceInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(
            async () =>
            {
                var carrier = await context.Carriers
                    .Include(value => value.Services)
                    .SingleOrDefaultAsync(value => value.Id == input.CarrierId, cancellationToken)
                    ?? throw new InvalidOperationException("The carrier was not found.");
                if (carrier.Status != CarrierStatus.Active)
                {
                    throw new InvalidOperationException("An inactive carrier cannot receive services.");
                }

                var code = input.Code.Trim().ToUpperInvariant();
                if (carrier.Services.Any(value => value.Code == code) ||
                    await context.CarrierServices.AnyAsync(
                        value => value.CarrierId == input.CarrierId && value.Code == code,
                        cancellationToken))
                {
                    throw new InvalidOperationException("A carrier service with this code already exists.");
                }

                var service = new CarrierService(
                    input.CarrierId,
                    input.Code,
                    input.Name,
                    input.SupportsTracking,
                    input.SupportsLabel);
                carrier.AddService(service);
                context.CarrierServices.Add(service);
                await context.SaveChangesAsync(cancellationToken);
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.SettingsChanged,
                        WmsAuditEntityTypes.Settings,
                        $"carrier-service:{input.CarrierId}:{service.Code}",
                        null,
                        After: new Dictionary<string, object?>
                        {
                            ["operation"] = "carrier_service.created",
                            ["carrierId"] = input.CarrierId,
                            ["code"] = service.Code
                        },
                        ActorUserId: userId),
                    cancellationToken);
                return Map(service);
            },
            "shipping.carrier_service_create_failed",
            "The carrier service could not be created.",
            cancellationToken);

    public async Task<Result<ShipmentDto>> CreateShipmentAsync(
        ShipmentCreateInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ShippingExecute,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ShipmentDto>();
        }

        var existing = await LoadShipmentByNumberAsync(input.ShipmentNumber, cancellationToken);
        if (existing is not null)
        {
            return Result.Success(Map(existing));
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                EnsureIdempotencyKey(input.IdempotencyKey);
                var packageIds = input.ShipmentPackageIds.Distinct().ToArray();
                if (packageIds.Length == 0)
                {
                    throw new ArgumentException("At least one packed package is required.", nameof(input));
                }

                var carrier = await context.Carriers
                    .SingleOrDefaultAsync(value => value.Id == input.CarrierId, cancellationToken)
                    ?? throw new InvalidOperationException("The carrier was not found.");
                var service = await context.CarrierServices
                    .SingleOrDefaultAsync(value => value.Id == input.CarrierServiceId, cancellationToken)
                    ?? throw new InvalidOperationException("The carrier service was not found.");
                if (carrier.Status != CarrierStatus.Active || service.CarrierId != carrier.Id || !service.IsActive)
                {
                    throw new InvalidOperationException("The carrier or carrier service is not active for shipment creation.");
                }

                var packages = await context.ShipmentPackages
                    .Include(value => value.Contents)
                    .Where(value => packageIds.Contains(value.Id))
                    .ToListAsync(cancellationToken);
                if (packages.Count != packageIds.Length)
                {
                    throw new InvalidOperationException("One or more shipment packages were not found.");
                }

                if (packages.Any(package => package.WarehouseId != input.WarehouseId ||
                                            package.Status != ShipmentPackageStatus.Closed))
                {
                    throw new InvalidOperationException(
                        "Only closed packages from the requested warehouse can be shipped.");
                }

                if (await context.ShipmentPackageLinks.AnyAsync(
                        link => packageIds.Contains(link.ShipmentPackageId),
                        cancellationToken))
                {
                    throw new InvalidOperationException("A package is already assigned to another shipment.");
                }

                var orderId = packages.Select(package => package.SalesOrderId).FirstOrDefault(value => value.HasValue);
                var order = orderId.HasValue
                    ? await context.SalesOrders.SingleOrDefaultAsync(value => value.Id == orderId.Value, cancellationToken)
                    : null;
                if (packages.Any(package => package.SalesOrderId.HasValue && package.SalesOrderId != orderId))
                {
                    throw new InvalidOperationException("Packages from different ship-to orders cannot share this shipment.");
                }

                var shipment = new Shipment(
                    input.ShipmentNumber,
                    input.WarehouseId,
                    carrier.Id,
                    service.Id,
                    order?.ShipToRecipientNameSnapshot,
                    order?.ShipToPhoneSnapshot,
                    order?.ShipToCountryCodeSnapshot,
                    order?.ShipToRegionSnapshot,
                    order?.ShipToCitySnapshot,
                    order?.ShipToPostalCodeSnapshot,
                    order?.ShipToAddressLine1Snapshot,
                    order?.ShipToAddressLine2Snapshot,
                    order?.ShipToDeliveryInstructionsSnapshot,
                    input.PlannedShipAtUtc,
                    input.ExternalReference,
                    userId);
                context.Shipments.Add(shipment);
                await context.SaveChangesAsync(cancellationToken);

                foreach (var package in packages)
                {
                    var link = new ShipmentPackageLink(shipment.Id, package.Id);
                    shipment.AddPackage(link);
                    context.ShipmentPackageLinks.Add(link);
                }

                foreach (var group in packages
                    .SelectMany(package => package.Contents)
                    .GroupBy(content => new
                    {
                        content.SalesOrderLineId,
                        content.ItemId,
                        content.BaseUnitOfMeasure
                    }))
                {
                    var line = new ShipmentLine(
                        shipment.Id,
                        group.Key.SalesOrderLineId,
                        group.Key.ItemId,
                        group.Sum(content => content.Quantity),
                        group.Key.BaseUnitOfMeasure);
                    shipment.AddLine(line);
                    context.ShipmentLines.Add(line);
                }

                shipment.MarkReady();
                await context.SaveChangesAsync(cancellationToken);
                context.ShipmentCommands.Add(new ShipmentCommand(
                    shipment.Id,
                    "create",
                    input.IdempotencyKey,
                    Hash(input),
                    userId,
                    clock.UtcNow.UtcDateTime));
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.ShipmentCompleted,
                        WmsAuditEntityTypes.Shipment,
                        shipment.ShipmentNumber,
                        shipment.WarehouseId,
                        After: new Dictionary<string, object?>
                        {
                            ["operation"] = "shipment.created",
                            ["packageCount"] = packages.Count,
                            ["carrierId"] = carrier.Id,
                            ["carrierServiceId"] = service.Id
                        },
                        ActorUserId: userId),
                    cancellationToken);
                return Map(shipment);
            },
            "shipping.shipment_create_failed",
            "The shipment could not be created.",
            cancellationToken);
    }

    public async Task<Result<ShipmentDto>> GetShipmentAsync(
        int shipmentId,
        CancellationToken cancellationToken = default)
    {
        var shipment = await LoadShipmentAsync(shipmentId, cancellationToken);
        if (shipment is null)
        {
            return Result.Failure<ShipmentDto>(WmsErrors.NotFound(
                "shipping.shipment_not_found",
                "The shipment was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ShippingExecute,
            shipment.WarehouseId,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<ShipmentDto>()
            : Result.Success(Map(shipment));
    }

    public async Task<Result<ShipmentDto>> OpenLoadAsync(
        ShipmentLoadInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var shipment = await LoadShipmentAsync(input.ShipmentId, cancellationToken);
        if (shipment is null)
        {
            return Result.Failure<ShipmentDto>(WmsErrors.NotFound(
                "shipping.shipment_not_found",
                "The shipment was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ShippingExecute,
            shipment.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ShipmentDto>();
        }

        var replay = await TryReplayAsync(shipment, "open_load", input.IdempotencyKey, Hash(input), cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var current = await LoadShipmentAsync(input.ShipmentId, cancellationToken)
                    ?? throw new InvalidOperationException("The shipment was not found.");
                if (input.DockLocationId.HasValue)
                {
                    var dock = await context.Locations.SingleOrDefaultAsync(
                        value => value.Id == input.DockLocationId.Value,
                        cancellationToken);
                    if (dock is null || dock.WarehouseId != current.WarehouseId || dock.Type != LocationType.Dock)
                    {
                        throw new InvalidOperationException("The load dock must be a dock location in the shipment warehouse.");
                    }
                }

                if (current.Status == ShipmentStatus.Ready)
                {
                    current.BeginLoading();
                }
                else if (current.Status != ShipmentStatus.Loading)
                {
                    throw new InvalidOperationException($"A shipment in {current.Status} cannot open a load.");
                }

                var load = new ShipmentLoad(
                    current.Id,
                    input.DockLocationId,
                    input.TrailerNumber,
                    input.RouteReference,
                    clock.UtcNow.UtcDateTime);
                current.AddLoad(load);
                context.ShipmentLoads.Add(load);
                await context.SaveChangesAsync(cancellationToken);
                AddCommand(current.Id, "open_load", input.IdempotencyKey, Hash(input), userId);
                await auditWriter.RecordAsync(
                    ShipmentAudit(current, "shipment.load_opened", userId, new Dictionary<string, object?>
                    {
                        ["loadId"] = load.Id,
                        ["dockLocationId"] = input.DockLocationId,
                        ["trailerNumber"] = input.TrailerNumber
                    }),
                    cancellationToken);
                return Map(current);
            },
            "shipping.load_open_failed",
            "The shipment load could not be opened.",
            cancellationToken);
    }

    public async Task<Result<ShipmentDto>> LoadPackageAsync(
        ShipmentPackageScanInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var shipment = await LoadShipmentAsync(input.ShipmentId, cancellationToken);
        if (shipment is null)
        {
            return Result.Failure<ShipmentDto>(WmsErrors.NotFound(
                "shipping.shipment_not_found",
                "The shipment was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ShippingExecute,
            shipment.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ShipmentDto>();
        }

        var replay = await TryReplayAsync(shipment, "load_package", input.IdempotencyKey, Hash(input), cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var current = await LoadShipmentAsync(input.ShipmentId, cancellationToken)
                    ?? throw new InvalidOperationException("The shipment was not found.");
                var link = current.Packages.SingleOrDefault(value => value.ShipmentPackageId == input.ShipmentPackageId)
                    ?? throw new InvalidOperationException("The scanned package does not belong to this shipment.");
                if (current.Status != ShipmentStatus.Loading)
                {
                    throw new InvalidOperationException("The shipment is not accepting load scans.");
                }

                var load = await context.ShipmentLoads.SingleOrDefaultAsync(
                    value => value.Id == input.ShipmentLoadId && value.ShipmentId == current.Id,
                    cancellationToken)
                    ?? throw new InvalidOperationException("The load was not found for this shipment.");
                if (load.Status != ShipmentLoadStatus.Open || link.Status == ShipmentPackageLinkStatus.Loaded)
                {
                    throw new InvalidOperationException("The load or package is no longer available for scanning.");
                }

                var package = link.ShipmentPackage;
                if (package.Status != ShipmentPackageStatus.Closed)
                {
                    throw new InvalidOperationException("Only closed packages can be loaded.");
                }

                var plate = await context.LicensePlates.SingleAsync(
                    value => value.Id == package.TargetLicensePlateId,
                    cancellationToken);
                if (plate.Status != LicensePlateStatus.Closed)
                {
                    throw new InvalidOperationException("The package LPN must be closed before loading.");
                }

                link.MarkLoaded(load.Id, userId, clock.UtcNow.UtcDateTime);
                if (current.Packages.All(value => value.Status == ShipmentPackageLinkStatus.Loaded))
                {
                    current.MarkLoaded();
                }

                AddCommand(current.Id, "load_package", input.IdempotencyKey, Hash(input), userId);
                await auditWriter.RecordAsync(
                    ShipmentAudit(current, "shipment.package_loaded", userId, new Dictionary<string, object?>
                    {
                        ["packageId"] = package.Id,
                        ["packageNumber"] = package.PackageNumber,
                        ["loadId"] = load.Id
                    }),
                    cancellationToken);
                return Map(current);
            },
            "shipping.package_load_failed",
            "The shipment package could not be loaded.",
            cancellationToken);
    }

    public async Task<Result<ShipmentDto>> UnloadPackageAsync(
        ShipmentPackageScanInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var shipment = await LoadShipmentAsync(input.ShipmentId, cancellationToken);
        if (shipment is null)
        {
            return Result.Failure<ShipmentDto>(WmsErrors.NotFound(
                "shipping.shipment_not_found",
                "The shipment was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ShippingExecute,
            shipment.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ShipmentDto>();
        }

        var replay = await TryReplayAsync(shipment, "unload_package", input.IdempotencyKey, Hash(input), cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var current = await LoadShipmentAsync(input.ShipmentId, cancellationToken)
                    ?? throw new InvalidOperationException("The shipment was not found.");
                var link = current.Packages.SingleOrDefault(value => value.ShipmentPackageId == input.ShipmentPackageId)
                    ?? throw new InvalidOperationException("The scanned package does not belong to this shipment.");
                if (link.Status != ShipmentPackageLinkStatus.Loaded || link.ShipmentLoadId != input.ShipmentLoadId)
                {
                    throw new InvalidOperationException("The package is not loaded on the requested load.");
                }

                if (current.Status == ShipmentStatus.Loaded)
                {
                    current.ReopenLoading();
                }

                link.Unload();
                AddCommand(current.Id, "unload_package", input.IdempotencyKey, Hash(input), userId);
                await auditWriter.RecordAsync(
                    ShipmentAudit(current, "shipment.package_unloaded", userId, new Dictionary<string, object?>
                    {
                        ["packageId"] = input.ShipmentPackageId,
                        ["loadId"] = input.ShipmentLoadId
                    }),
                    cancellationToken);
                return Map(current);
            },
            "shipping.package_unload_failed",
            "The shipment package could not be unloaded.",
            cancellationToken);
    }

    public async Task<Result<ShipmentDto>> ConfirmShipmentAsync(
        ShipmentCommandInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var shipment = await LoadShipmentAsync(input.ShipmentId, cancellationToken);
        if (shipment is null)
        {
            return Result.Failure<ShipmentDto>(WmsErrors.NotFound(
                "shipping.shipment_not_found",
                "The shipment was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ShippingExecute,
            shipment.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ShipmentDto>();
        }

        var replay = await TryReplayAsync(shipment, "confirm", input.IdempotencyKey, Hash(input), cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var current = await LoadShipmentAsync(input.ShipmentId, cancellationToken)
                    ?? throw new InvalidOperationException("The shipment was not found.");
                if (current.Status != ShipmentStatus.Loaded ||
                    current.Packages.Any(value => value.Status != ShipmentPackageLinkStatus.Loaded))
                {
                    throw new InvalidOperationException("Every eligible package must be loaded before shipment confirmation.");
                }

                var ledgerEntries = new List<InventoryLedgerEntryRequest>();
                var orderIds = current.Packages
                    .Select(value => value.ShipmentPackage.SalesOrderId)
                    .Where(value => value.HasValue)
                    .Select(value => value!.Value)
                    .Distinct()
                    .ToArray();
                foreach (var link in current.Packages)
                {
                    var package = link.ShipmentPackage;
                    var plate = await context.LicensePlates.SingleAsync(
                        value => value.Id == package.TargetLicensePlateId,
                        cancellationToken);
                    if (plate.Status != LicensePlateStatus.Closed)
                    {
                        throw new InvalidOperationException("A shipment package LPN must be closed before shipment confirmation.");
                    }

                    var stocks = await context.Stock
                        .Where(value => value.LicensePlateId == plate.Id)
                        .ToListAsync(cancellationToken);
                    foreach (var stock in stocks.Where(value => value.QuantityAvailable.Value > 0m))
                    {
                        if (stock.QuantityReserved.Value > 0m)
                        {
                            throw new InvalidOperationException(
                                "A shipment package has outstanding reserved quantity and cannot ship.");
                        }

                        var item = await context.Items.SingleAsync(value => value.Id == stock.ItemId, cancellationToken);
                        var movement = Movement.CreateShip(
                            stock.ItemId,
                            stock.LocationId,
                            stock.QuantityAvailable,
                            userId,
                            stock.LotId,
                            stock.SerialNumber,
                            current.ShipmentNumber,
                            "shipment confirmation",
                            clock.UtcNow.UtcDateTime,
                            stock.SerialNumberId,
                            stock.InventoryStatusId,
                            plate.Id);
                        movement.SetOwnership(
                            stock.OwnerKind,
                            stock.InventoryOwnerId,
                            stock.OwnerCodeSnapshot);
                        context.Movements.Add(movement);
                        ledgerEntries.Add(new InventoryLedgerEntryRequest(
                            InventoryTransactionType.Ship,
                            new InventoryBalanceKey(
                                current.WarehouseId,
                                stock.LocationId,
                                stock.ItemId,
                                stock.LotId,
                                stock.SerialNumberId,
                                stock.SerialNumber,
                                plate.Id,
                                stock.InventoryStatusId,
                                item.UnitOfMeasure,
                                stock.OwnerKind,
                                stock.InventoryOwnerId,
                                stock.OwnerCodeSnapshot),
                            -stock.QuantityAvailable.Value,
                            ActorUserId: userId,
                            ReferenceType: "Shipment",
                            ReferenceId: current.ShipmentNumber,
                            Reason: "shipment confirmation",
                            OccurredAtUtc: movement.Timestamp,
                            IdempotencyKey: $"ship:{current.Id}:{input.IdempotencyKey}:{stock.Id}",
                            TransactionGroupId: $"ship:{current.Id}:{input.IdempotencyKey}"));
                        context.Stock.Remove(stock);

                        if (stock.SerialNumberId.HasValue)
                        {
                            var serial = await context.SerialNumbers.SingleOrDefaultAsync(
                                value => value.Id == stock.SerialNumberId.Value,
                                cancellationToken);
                            serial?.RecordShipment(current.ShipmentNumber, clock.UtcNow.UtcDateTime);
                        }
                    }

                    foreach (var content in package.Contents)
                    {
                        var orderLine = await context.SalesOrderLines.SingleAsync(
                            value => value.Id == content.SalesOrderLineId,
                            cancellationToken);
                        orderLine.RecordShipped(content.Quantity);
                    }

                    plate.Ship();
                    package.MarkShipped(clock.UtcNow.UtcDateTime);
                    link.MarkShipped(clock.UtcNow.UtcDateTime);
                }

                await inventoryLedgerService.RecordAsync(ledgerEntries, cancellationToken);
                foreach (var orderId in orderIds)
                {
                    var order = await context.SalesOrders
                        .Include(value => value.Lines)
                        .SingleAsync(value => value.Id == orderId, cancellationToken);
                    if (order.Lines.All(line => line.RemainingToShipBaseQuantity == 0m))
                    {
                        order.MarkShipped(userId, clock.UtcNow.UtcDateTime);
                    }
                    else
                    {
                        order.MarkPartiallyShipped();
                    }
                }

                current.MarkShipped(userId, clock.UtcNow.UtcDateTime);
                AddCommand(current.Id, "confirm", input.IdempotencyKey, Hash(input), userId);
                await auditWriter.RecordAsync(
                    ShipmentAudit(current, "shipment.confirmed", userId, new Dictionary<string, object?>
                    {
                        ["packageCount"] = current.Packages.Count,
                        ["ledgerEntryCount"] = ledgerEntries.Count
                    }),
                    cancellationToken);
                return Map(current);
            },
            "shipping.confirm_failed",
            "The shipment could not be confirmed.",
            cancellationToken);
    }

    public async Task<Result<ShipmentDto>> CancelShipmentAsync(
        ShipmentCommandInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var shipment = await LoadShipmentAsync(input.ShipmentId, cancellationToken);
        if (shipment is null)
        {
            return Result.Failure<ShipmentDto>(WmsErrors.NotFound(
                "shipping.shipment_not_found",
                "The shipment was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ShippingExecute,
            shipment.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ShipmentDto>();
        }

        var replay = await TryReplayAsync(shipment, "cancel", input.IdempotencyKey, Hash(input), cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var current = await LoadShipmentAsync(input.ShipmentId, cancellationToken)
                    ?? throw new InvalidOperationException("The shipment was not found.");
                if (current.Packages.Any(value => value.Status is ShipmentPackageLinkStatus.Loaded or ShipmentPackageLinkStatus.Shipped))
                {
                    throw new InvalidOperationException("Unload all loaded packages before cancelling the shipment.");
                }

                foreach (var package in current.Packages.Where(value => value.Status != ShipmentPackageLinkStatus.Cancelled))
                {
                    package.Cancel();
                }

                current.Cancel(userId, input.Reason ?? "controlled shipment cancellation", clock.UtcNow.UtcDateTime);
                AddCommand(current.Id, "cancel", input.IdempotencyKey, Hash(input), userId);
                await auditWriter.RecordAsync(
                    ShipmentAudit(current, "shipment.cancelled", userId, new Dictionary<string, object?>
                    {
                        ["reason"] = input.Reason
                    }),
                    cancellationToken);
                return Map(current);
            },
            "shipping.cancel_failed",
            "The shipment could not be cancelled.",
            cancellationToken);
    }

    public async Task<Result<ShipmentDto>> UpdateTrackingAsync(
        ShipmentTrackingUpdateInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var shipment = await LoadShipmentAsync(input.ShipmentId, cancellationToken);
        if (shipment is null)
        {
            return Result.Failure<ShipmentDto>(WmsErrors.NotFound(
                "shipping.shipment_not_found",
                "The shipment was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ShippingExecute,
            shipment.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ShipmentDto>();
        }

        var replay = await TryReplayAsync(shipment, "tracking", input.IdempotencyKey, Hash(input), cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var current = await LoadShipmentAsync(input.ShipmentId, cancellationToken)
                    ?? throw new InvalidOperationException("The shipment was not found.");
                var occurredAt = input.OccurredAtUtc ?? clock.UtcNow.UtcDateTime;
                var trackingEvent = new ShipmentTrackingEvent(
                    current.Id,
                    input.TrackingStatus,
                    input.TrackingNumber,
                    input.ProviderReference,
                    input.Source,
                    occurredAt,
                    input.PayloadReference);
                current.RecordTracking(input.TrackingNumber, input.TrackingStatus, occurredAt);
                current.AddTrackingEvent(trackingEvent);
                context.ShipmentTrackingEvents.Add(trackingEvent);
                AddCommand(current.Id, "tracking", input.IdempotencyKey, Hash(input), userId);
                await auditWriter.RecordAsync(
                    ShipmentAudit(current, "shipment.tracking_updated", userId, new Dictionary<string, object?>
                    {
                        ["trackingStatus"] = input.TrackingStatus,
                        ["source"] = input.Source,
                        ["providerReference"] = input.ProviderReference
                    }),
                    cancellationToken);
                return Map(current);
            },
            "shipping.tracking_update_failed",
            "The shipment tracking update could not be recorded.",
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
            return Result.Failure<T>(WmsErrors.Concurrency("shipping.concurrency_conflict", exception.Message));
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
            logger.LogError(exception, "Shipment operation failed with {ErrorCode}", errorCode);
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

    private async Task<Shipment?> LoadShipmentAsync(int shipmentId, CancellationToken cancellationToken) =>
        await context.Shipments
            .Include(value => value.Lines)
            .Include(value => value.Packages)
                .ThenInclude(value => value.ShipmentPackage)
                    .ThenInclude(value => value.Contents)
            .Include(value => value.Loads)
            .Include(value => value.TrackingEvents)
            .SingleOrDefaultAsync(value => value.Id == shipmentId, cancellationToken);

    private async Task<Shipment?> LoadShipmentByNumberAsync(string shipmentNumber, CancellationToken cancellationToken) =>
        await context.Shipments
            .Include(value => value.Lines)
            .Include(value => value.Packages)
                .ThenInclude(value => value.ShipmentPackage)
                    .ThenInclude(value => value.Contents)
            .Include(value => value.Loads)
            .Include(value => value.TrackingEvents)
            .SingleOrDefaultAsync(value => value.ShipmentNumber == shipmentNumber.Trim(), cancellationToken);

    private async Task<Result<ShipmentDto>?> TryReplayAsync(
        Shipment shipment,
        string operation,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Result.Failure<ShipmentDto>(WmsErrors.Validation(
                "shipping.idempotency_required",
                "Every shipment command requires an idempotency key."));
        }

        var command = await context.ShipmentCommands.SingleOrDefaultAsync(
            value => value.ShipmentId == shipment.Id &&
                     value.Operation == operation &&
                     value.IdempotencyKey == idempotencyKey.Trim(),
            cancellationToken);
        if (command is null)
        {
            return null;
        }

        return command.RequestHash == requestHash
            ? Result.Success(Map(shipment))
            : Result.Failure<ShipmentDto>(WmsErrors.Conflict(
                "shipping.idempotency_reuse",
                "The idempotency key was already used with a different request."));
    }

    private void AddCommand(int shipmentId, string operation, string idempotencyKey, string requestHash, string userId) =>
        context.ShipmentCommands.Add(new ShipmentCommand(
            shipmentId,
            operation,
            idempotencyKey,
            requestHash,
            userId,
            clock.UtcNow.UtcDateTime));

    private static AuditRecord ShipmentAudit(
        Shipment shipment,
        string operation,
        string userId,
        IReadOnlyDictionary<string, object?> details) =>
        new(
            WmsAuditActions.ShipmentCompleted,
            WmsAuditEntityTypes.Shipment,
            shipment.ShipmentNumber,
            shipment.WarehouseId,
            After: new Dictionary<string, object?>(details)
            {
                ["operation"] = operation,
                ["status"] = shipment.Status.ToString()
            },
            ActorUserId: userId);

    private static string Hash<T>(T value)
    {
        var payload = JsonSerializer.Serialize(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static void EnsureIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }
    }

    private static CarrierDto Map(Carrier carrier) => new(
        carrier.Id,
        carrier.Code,
        carrier.Name,
        carrier.Status,
        carrier.TrackingUrlTemplate,
        carrier.Services.Select(Map).ToArray(),
        carrier.Revision);

    private static CarrierServiceDto Map(CarrierService service) => new(
        service.Id,
        service.CarrierId,
        service.Code,
        service.Name,
        service.SupportsTracking,
        service.SupportsLabel,
        service.IsActive,
        service.Revision);

    private static ShipmentDto Map(Shipment shipment) => new(
        shipment.Id,
        shipment.ShipmentNumber,
        shipment.WarehouseId,
        shipment.CarrierId,
        shipment.CarrierServiceId,
        shipment.Status,
        shipment.ShipToRecipientName,
        shipment.ShipToPhone,
        shipment.ShipToCountryCode,
        shipment.ShipToRegion,
        shipment.ShipToCity,
        shipment.ShipToPostalCode,
        shipment.ShipToAddressLine1,
        shipment.ShipToAddressLine2,
        shipment.ShipToDeliveryInstructions,
        shipment.PlannedShipAtUtc,
        shipment.ActualShipAtUtc,
        shipment.ExternalReference,
        shipment.TrackingNumber,
        shipment.TrackingStatus,
        shipment.Lines.Select(line => new ShipmentLineDto(
            line.Id,
            line.SalesOrderLineId,
            line.ItemId,
            line.Quantity,
            line.BaseUnitOfMeasure,
            line.Revision)).ToArray(),
        shipment.Packages.Select(link => new ShipmentPackageLinkDto(
            link.Id,
            link.ShipmentPackageId,
            link.ShipmentPackage.PackageNumber,
            link.ShipmentPackage.TargetLicensePlateId,
            link.ShipmentPackage.Status,
            link.Status,
            link.ShipmentLoadId,
            link.LoadedByUserId,
            link.LoadedAtUtc,
            link.ShippedAtUtc,
            link.Revision)).ToArray(),
        shipment.Loads.Select(load => new ShipmentLoadDto(
            load.Id,
            load.DockLocationId,
            load.TrailerNumber,
            load.RouteReference,
            load.Status,
            load.OpenedAtUtc,
            load.ClosedAtUtc,
            load.Revision)).ToArray(),
        shipment.TrackingEvents.Select(eventRow => new ShipmentTrackingEventDto(
            eventRow.Id,
            eventRow.Status,
            eventRow.TrackingNumber,
            eventRow.ProviderReference,
            eventRow.Source,
            eventRow.OccurredAtUtc,
            eventRow.PayloadReference)).ToArray(),
        shipment.Revision);
}
