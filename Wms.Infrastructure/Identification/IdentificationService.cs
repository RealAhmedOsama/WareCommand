using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Identification;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Identification;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Identification;

public sealed class IdentificationService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    IClock clock) : IIdentificationService
{
    public async Task<Result<IdentificationResolutionDto>> ResolveAsync(
        IdentificationLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Value))
        {
            return Result.Failure<IdentificationResolutionDto>(WmsErrors.Validation(
                "identification.value_required",
                "A scanned identifier is required."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ItemsRead,
            cancellationToken: cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IdentificationResolutionDto>();
        }

        BarcodeParseResult parsed;
        Gs1Payload? gs1 = null;
        try
        {
            if (Gs1Parser.LooksLikeGs1(request.Value))
            {
                gs1 = Gs1Parser.Parse(request.Value);
                parsed = new BarcodeParseResult(
                    request.Value.Trim(),
                    BarcodeParser.StripScannerSymbologyPrefix(request.Value.Trim()),
                    BarcodeSymbology.Gs1,
                    IsCheckDigitValidated: true);
            }
            else
            {
                parsed = BarcodeParser.Parse(request.Value);
            }
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<IdentificationResolutionDto>(WmsErrors.Validation(
                "identification.format_invalid",
                exception.Message));
        }

        var now = clock.UtcNow.UtcDateTime;
        var direct = await FindDedicatedAsync(parsed, gs1, request.IncludeInactive, now, cancellationToken);
        if (direct.Count > 1)
        {
            return Result.Failure<IdentificationResolutionDto>(WmsErrors.Conflict(
                "identification.ambiguous",
                "The scanned identifier matches more than one active entity."));
        }

        if (direct.Count == 1)
        {
            var legacyMatches = await ResolveLegacyAsync(
                parsed,
                gs1,
                request.WarehouseId,
                request.IncludeInactive,
                cancellationToken);
            if (legacyMatches.Any(match =>
                    !string.Equals(match.OwnerKey, direct[0].OwnerKey, StringComparison.Ordinal)))
            {
                return Result.Failure<IdentificationResolutionDto>(WmsErrors.Conflict(
                    "identification.ambiguous",
                    "The scanned identifier matches more than one active entity."));
            }

            var mapped = await MapDedicatedAsync(
                direct[0],
                gs1,
                request.WarehouseId,
                request.IncludeInactive,
                cancellationToken);
            if (mapped.IsSuccess)
            {
                await RecordResolutionAsync(mapped.Value, cancellationToken);
            }

            return mapped;
        }

        var legacy = await ResolveLegacyAsync(
            parsed,
            gs1,
            request.WarehouseId,
            request.IncludeInactive,
            cancellationToken);
        if (legacy.Count > 1)
        {
            return Result.Failure<IdentificationResolutionDto>(WmsErrors.Conflict(
                "identification.ambiguous",
                "The scanned identifier matches more than one active entity."));
        }

        if (legacy.Count == 0)
        {
            return Result.Failure<IdentificationResolutionDto>(WmsErrors.NotFound(
                "identification.not_found",
                "The scanned identifier was not found."));
        }

        await RecordResolutionAsync(legacy[0], cancellationToken);
        return Result.Success(legacy[0]);
    }

    private async Task<List<WmsIdentifier>> FindDedicatedAsync(
        BarcodeParseResult parsed,
        Gs1Payload? gs1,
        bool includeInactive,
        DateTime now,
        CancellationToken cancellationToken)
    {
        string[] candidateValues = gs1 is null
            ? [parsed.NormalizedValue]
            : gs1.Sscc is not null
                ? [gs1.Sscc]
                : gs1.Gtin is not null ? [gs1.Gtin] : [];
        if (candidateValues.Length == 0)
        {
            return [];
        }

        var query = context.WmsIdentifiers
            .AsNoTracking()
            .Where(identifier => candidateValues.Contains(identifier.NormalizedValue));
        if (!includeInactive)
        {
            query = query.Where(identifier =>
                identifier.IsActive &&
                identifier.ValidFromUtc <= now &&
                (identifier.ValidToUtc == null || identifier.ValidToUtc > now));
        }

        return await query.Take(2).ToListAsync(cancellationToken);
    }

    private async Task<Result<IdentificationResolutionDto>> MapDedicatedAsync(
        WmsIdentifier identifier,
        Gs1Payload? gs1,
        int? requestedWarehouseId,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var owner = identifier.OwnerKey;
        if (owner.StartsWith("ITEM:", StringComparison.Ordinal))
        {
            var sku = owner[5..];
            var item = await context.Items
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Sku == sku && (includeInactive || candidate.IsActive),
                    cancellationToken);
            return item is null
                ? Result.Failure<IdentificationResolutionDto>(WmsErrors.NotFound(
                    "item.not_found",
                    "The scanned item is no longer available."))
                : Result.Success(Map(
                    identifier,
                    gs1,
                    entityId: item.Id,
                    itemSku: item.Sku));
        }

        if (owner.StartsWith("PACKAGING:", StringComparison.Ordinal))
        {
            var separator = owner.IndexOf('|', "PACKAGING:".Length);
            if (separator < 0)
            {
                return InvalidOwner(identifier);
            }

            var sku = owner[10..separator];
            var code = owner[(separator + 1)..];
            var packaging = await context.ItemPackagings
                .AsNoTracking()
                .Include(candidate => candidate.Item)
                .SingleOrDefaultAsync(
                    candidate => candidate.Item.Sku == sku &&
                                 candidate.Code == code &&
                                 (includeInactive && candidate.Item.IsActive ||
                                  candidate.Item.IsActive && candidate.IsActive),
                    cancellationToken);
            return packaging is null
                ? Result.Failure<IdentificationResolutionDto>(WmsErrors.NotFound(
                    "packaging.not_found",
                    "The scanned packaging is no longer available."))
                : Result.Success(Map(
                    identifier,
                    gs1,
                    entityId: packaging.Id,
                    itemSku: packaging.Item.Sku,
                    packagingCode: packaging.Code));
        }

        if (owner.StartsWith("LOCATION:", StringComparison.Ordinal))
        {
            var separator = owner.IndexOf('|', "LOCATION:".Length);
            if (separator < 0 ||
                !int.TryParse(owner[9..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var warehouseId))
            {
                return InvalidOwner(identifier);
            }

            if (requestedWarehouseId.HasValue && requestedWarehouseId.Value != warehouseId)
            {
                return Result.Failure<IdentificationResolutionDto>(WmsErrors.NotFound(
                    "location.not_found",
                    "The scanned location is outside the requested warehouse."));
            }

            var warehouseAuthorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.LocationsRead,
                warehouseId,
                cancellationToken);
            if (warehouseAuthorization.IsFailure)
            {
                return warehouseAuthorization.ToFailure<IdentificationResolutionDto>();
            }

            var code = owner[(separator + 1)..];
            var location = await context.Locations
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.WarehouseId == warehouseId &&
                                 candidate.Code == code &&
                                 (includeInactive || candidate.IsActive),
                    cancellationToken);
            return location is null
                ? Result.Failure<IdentificationResolutionDto>(WmsErrors.NotFound(
                    "location.not_found",
                    "The scanned location is no longer available."))
                : Result.Success(Map(
                    identifier,
                    gs1,
                    entityId: location.Id,
                    locationCode: location.Code,
                    warehouseId: location.WarehouseId));
        }

        if (owner.StartsWith("LOT:", StringComparison.Ordinal))
        {
            var separator = owner.IndexOf('|', "LOT:".Length);
            if (separator < 0 ||
                !int.TryParse(owner[4..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var itemId))
            {
                return InvalidOwner(identifier);
            }

            var number = owner[(separator + 1)..];
            var lot = await context.Lots
                .AsNoTracking()
                .Include(candidate => candidate.Item)
                .SingleOrDefaultAsync(
                    candidate => candidate.ItemId == itemId &&
                                 candidate.Number == number &&
                                 (includeInactive || candidate.IsActive),
                    cancellationToken);
            return lot is null
                ? Result.Failure<IdentificationResolutionDto>(WmsErrors.NotFound(
                    "lot.not_found",
                    "The scanned lot is no longer available."))
                : Result.Success(Map(
                    identifier,
                    gs1,
                    entityId: lot.Id,
                    itemSku: lot.Item.Sku,
                    lotNumber: lot.Number));
        }

        if (owner.StartsWith("SERIAL:", StringComparison.Ordinal))
        {
            var separator = owner.IndexOf('|', "SERIAL:".Length);
            if (separator < 0 ||
                !int.TryParse(owner[7..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var itemId))
            {
                return InvalidOwner(identifier);
            }

            var serial = owner[(separator + 1)..];
            var stock = await context.Stock
                .AsNoTracking()
                .Include(candidate => candidate.Item)
                .SingleOrDefaultAsync(
                    candidate => candidate.ItemId == itemId && candidate.SerialNumber == serial,
                    cancellationToken);
            return stock is null
                ? Result.Failure<IdentificationResolutionDto>(WmsErrors.NotFound(
                    "serial.not_found",
                    "The scanned serial number is no longer available."))
                : Result.Success(Map(
                    identifier,
                    gs1,
                    entityId: stock.Id,
                    itemSku: stock.Item.Sku,
                    serialNumber: stock.SerialNumber));
        }

        if (owner.StartsWith("LPN:", StringComparison.Ordinal))
        {
            return Result.Success(Map(identifier, gs1, licensePlate: owner[4..]));
        }

        if (owner.StartsWith("DOCUMENT:", StringComparison.Ordinal))
        {
            return Result.Success(Map(identifier, gs1));
        }

        return InvalidOwner(identifier);
    }

    private async Task<List<IdentificationResolutionDto>> ResolveLegacyAsync(
        BarcodeParseResult parsed,
        Gs1Payload? gs1,
        int? warehouseId,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        string[] values = gs1 is null
            ? [parsed.NormalizedValue]
            : gs1.Gtin is not null ? [gs1.Gtin] : [parsed.NormalizedValue];
        var candidates = new List<IdentificationResolutionDto>();

        var items = await context.Items
            .AsNoTracking()
            .Include(item => item.Packagings)
            .Where(item => (includeInactive || item.IsActive) &&
                          item.Barcodes.Any(barcode => values.Contains(barcode.Value)))
            .ToListAsync(cancellationToken);
        foreach (var item in items)
        {
            var itemBarcode = item.Barcodes.First(barcode => values.Contains(barcode.Value));
            candidates.Add(MapLegacy(
                itemBarcode.Value,
                IdentificationKind.Item,
                BarcodeSymbology.Internal,
                IdentifierOwnerKeys.Item(item.Sku),
                item.Id,
                itemSku: item.Sku,
                gs1: gs1));
        }

        var packagings = await context.ItemPackagings
            .AsNoTracking()
            .Include(packaging => packaging.Item)
            .Where(packaging =>
                (includeInactive || packaging.IsActive && packaging.Item.IsActive) &&
                ((packaging.Barcode != null && values.Contains(packaging.Barcode)) ||
                 (packaging.Gtin != null && values.Contains(packaging.Gtin))))
            .ToListAsync(cancellationToken);
        foreach (var packaging in packagings)
        {
            var value = packaging.Barcode is not null && values.Contains(packaging.Barcode)
                ? packaging.Barcode
                : packaging.Gtin!;
            candidates.Add(MapLegacy(
                value,
                IdentificationKind.Packaging,
                packaging.Gtin == value ? BarcodeSymbology.Gtin14 : BarcodeSymbology.Internal,
                IdentifierOwnerKeys.Packaging(packaging.Item.Sku, packaging.Code),
                packaging.Id,
                itemSku: packaging.Item.Sku,
                packagingCode: packaging.Code,
                gs1: gs1));
        }

        var locations = await context.Locations
            .AsNoTracking()
            .Where(location =>
                (includeInactive || location.IsActive) &&
                (!warehouseId.HasValue || location.WarehouseId == warehouseId.Value) &&
                location.Barcode != null &&
                values.Contains(location.Barcode))
            .ToListAsync(cancellationToken);
        foreach (var location in locations)
        {
            var authorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.LocationsRead,
                location.WarehouseId,
                cancellationToken);
            if (authorization.IsSuccess)
            {
                candidates.Add(MapLegacy(
                    location.Barcode!,
                    IdentificationKind.Location,
                    BarcodeSymbology.Internal,
                    IdentifierOwnerKeys.Location(location.WarehouseId, location.Code),
                    location.Id,
                    locationCode: location.Code,
                    warehouseId: location.WarehouseId,
                    gs1: gs1));
            }
        }

        var lots = await context.Lots
            .AsNoTracking()
            .Include(lot => lot.Item)
            .Where(lot => (includeInactive || lot.IsActive) && values.Contains(lot.Number))
            .ToListAsync(cancellationToken);
        foreach (var lot in lots)
        {
            candidates.Add(MapLegacy(
                lot.Number,
                IdentificationKind.Lot,
                BarcodeSymbology.Internal,
                IdentifierOwnerKeys.Lot(lot.ItemId, lot.Number),
                lot.Id,
                itemSku: lot.Item.Sku,
                lotNumber: lot.Number,
                gs1: gs1));
        }

        var serials = await context.Stock
            .AsNoTracking()
            .Include(stock => stock.Item)
            .Where(stock => stock.SerialNumber != null && values.Contains(stock.SerialNumber))
            .ToListAsync(cancellationToken);
        foreach (var stock in serials)
        {
            candidates.Add(MapLegacy(
                stock.SerialNumber!,
                IdentificationKind.Serial,
                BarcodeSymbology.Internal,
                IdentifierOwnerKeys.Serial(stock.ItemId, stock.SerialNumber!),
                stock.Id,
                itemSku: stock.Item.Sku,
                serialNumber: stock.SerialNumber,
                gs1: gs1));
        }

        return candidates
            .GroupBy(candidate => candidate.OwnerKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private async Task RecordResolutionAsync(
        IdentificationResolutionDto resolution,
        CancellationToken cancellationToken)
    {
        await auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.IdentifierResolved,
                WmsAuditEntityTypes.Identifier,
                resolution.EntityId?.ToString(CultureInfo.InvariantCulture) ?? resolution.Kind.ToString(),
                resolution.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["kind"] = resolution.Kind.ToString(),
                    ["symbology"] = resolution.Symbology.ToString(),
                    ["normalizedLength"] = resolution.NormalizedValue.Length,
                    ["resolved"] = true
                },
                Details: "Identifier resolved without recording the scanned payload."),
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static Result<IdentificationResolutionDto> InvalidOwner(WmsIdentifier identifier) =>
        Result.Failure<IdentificationResolutionDto>(WmsErrors.Dependency(
            "identification.owner_invalid",
            $"Identifier owner '{identifier.Kind}' is not available."));

    private static IdentificationResolutionDto Map(
        WmsIdentifier identifier,
        Gs1Payload? gs1,
        int? entityId = null,
        string? itemSku = null,
        string? packagingCode = null,
        string? locationCode = null,
        int? warehouseId = null,
        string? lotNumber = null,
        string? serialNumber = null,
        string? licensePlate = null) =>
        new(
            identifier.OriginalValue,
            identifier.NormalizedValue,
            identifier.Kind,
            identifier.Symbology,
            identifier.OwnerKey,
            identifier.IsActive,
            identifier.ValidFromUtc,
            identifier.ValidToUtc,
            entityId,
            itemSku,
            packagingCode,
            locationCode,
            warehouseId,
            lotNumber,
            serialNumber,
            licensePlate,
            gs1 is null ? null : ToGs1Dto(gs1));

    private static IdentificationResolutionDto MapLegacy(
        string value,
        IdentificationKind kind,
        BarcodeSymbology symbology,
        string ownerKey,
        int entityId,
        string? itemSku = null,
        string? packagingCode = null,
        string? locationCode = null,
        int? warehouseId = null,
        string? lotNumber = null,
        string? serialNumber = null,
        Gs1Payload? gs1 = null) =>
        new(
            value,
            value,
            kind,
            symbology,
            ownerKey,
            true,
            DateTime.UnixEpoch,
            null,
            entityId,
            itemSku,
            packagingCode,
            locationCode,
            warehouseId,
            lotNumber,
            serialNumber,
            null,
            gs1 is null ? null : ToGs1Dto(gs1));

    private static IdentificationGs1Dto ToGs1Dto(Gs1Payload payload) =>
        new(
            payload.Gtin,
            payload.Lot,
            payload.Serial,
            payload.ExpiryDate,
            payload.Quantity,
            payload.Sscc,
            payload.ApplicationIdentifiers);
}
