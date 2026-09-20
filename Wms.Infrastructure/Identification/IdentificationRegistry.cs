using Microsoft.EntityFrameworkCore;
using Wms.Application.Common;
using Wms.Application.Identification;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Identification;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Identification;

public sealed class IdentificationRegistry(WmsDbContext context) : IIdentificationRegistry
{
    public async Task<Result> SyncItemAsync(
        Item item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var itemOwner = IdentifierOwnerKeys.Item(item.Sku);
        var desired = new List<PreparedIdentifier>();
        foreach (var barcode in item.Barcodes)
        {
            desired.Add(Prepare(
                barcode.Value,
                IdentificationKind.Item,
                itemOwner,
                item.IsActive));
        }

        foreach (var packaging in item.Packagings)
        {
            var ownerKey = IdentifierOwnerKeys.Packaging(item.Sku, packaging.Code);
            var isActive = item.IsActive && packaging.IsActive;
            if (!string.IsNullOrWhiteSpace(packaging.Barcode))
            {
                desired.Add(Prepare(
                    packaging.Barcode,
                    IdentificationKind.Packaging,
                    ownerKey,
                    isActive));
            }

            if (!string.IsNullOrWhiteSpace(packaging.Gtin))
            {
                desired.Add(PrepareProductCode(
                    packaging.Gtin,
                    IdentificationKind.Packaging,
                    ownerKey,
                    isActive));
            }
        }

        return await SynchronizeAsync(
            desired,
            context.WmsIdentifiers.Where(identifier =>
                identifier.OwnerKey == itemOwner ||
                identifier.OwnerKey.StartsWith($"PACKAGING:{item.Sku.ToUpperInvariant()}|")),
            cancellationToken);
    }

    public async Task<Result> SyncLocationAsync(
        Location location,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        var ownerKey = IdentifierOwnerKeys.Location(location.WarehouseId, location.Code);
        IReadOnlyList<PreparedIdentifier> desired = string.IsNullOrWhiteSpace(location.Barcode)
            ? []
            : [Prepare(
                location.Barcode,
                IdentificationKind.Location,
                ownerKey,
                location.IsActive)];

        return await SynchronizeAsync(
            desired,
            context.WmsIdentifiers.Where(identifier => identifier.OwnerKey == ownerKey),
            cancellationToken);
    }

    public async Task<Result<WmsIdentifier>> RegisterAsync(
        IdentificationRegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        PreparedIdentifier prepared;
        try
        {
            prepared = Prepare(
                request.Value,
                request.Kind,
                request.OwnerKey,
                request.IsActive,
                request.Symbology,
                request.Alias,
                request.ValidFromUtc,
                request.ValidToUtc);
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<WmsIdentifier>(WmsErrors.Validation(
                "identification.invalid",
                exception.Message));
        }

        var conflict = await context.WmsIdentifiers
            .SingleOrDefaultAsync(
                identifier => identifier.NormalizedValue == prepared.NormalizedValue,
                cancellationToken);
        if (conflict is not null)
        {
            if (!string.Equals(conflict.OwnerKey, prepared.OwnerKey, StringComparison.Ordinal))
            {
                return Result.Failure<WmsIdentifier>(WmsErrors.Conflict(
                    "identification.conflict",
                    "The identifier is already assigned to another entity."));
            }

            conflict.UpdateLifecycle(
                prepared.IsActive,
                prepared.ValidFromUtc,
                prepared.ValidToUtc);
            conflict.SetAlias(prepared.Alias);
            return Result.Success(conflict);
        }

        var entity = prepared.CreateEntity();
        context.WmsIdentifiers.Add(entity);
        return Result.Success(entity);
    }

    private async Task<Result> SynchronizeAsync(
        IReadOnlyList<PreparedIdentifier> desired,
        IQueryable<WmsIdentifier> ownerIdentifiers,
        CancellationToken cancellationToken)
    {
        var duplicate = desired
            .GroupBy(identifier => identifier.NormalizedValue, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            return Result.Failure(WmsErrors.Conflict(
                "identification.conflict",
                $"Identifier '{duplicate.Key}' is assigned more than once."));
        }

        var ownerRows = await ownerIdentifiers.ToListAsync(cancellationToken);
        var desiredValues = desired
            .Select(identifier => identifier.NormalizedValue)
            .ToArray();
        var ownerKeys = desired
            .Select(identifier => identifier.OwnerKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var globalConflicts = await context.WmsIdentifiers
            .Where(identifier => desiredValues.Contains(identifier.NormalizedValue) &&
                                 !ownerKeys.Contains(identifier.OwnerKey))
            .Select(identifier => identifier.NormalizedValue)
            .ToListAsync(cancellationToken);
        if (globalConflicts.Count > 0)
        {
            return Result.Failure(WmsErrors.Conflict(
                "identification.conflict",
                "Every active identifier must resolve to one entity only."));
        }

        var localConflicts = context.WmsIdentifiers.Local
            .Where(identifier => desiredValues.Contains(identifier.NormalizedValue, StringComparer.Ordinal) &&
                                 !ownerKeys.Contains(identifier.OwnerKey, StringComparer.Ordinal))
            .ToArray();
        if (localConflicts.Length > 0)
        {
            return Result.Failure(WmsErrors.Conflict(
                "identification.conflict",
                "Every active identifier must resolve to one entity only."));
        }

        var desiredByValue = desired.ToDictionary(
            identifier => identifier.NormalizedValue,
            StringComparer.Ordinal);
        foreach (var row in ownerRows)
        {
            if (!desiredByValue.ContainsKey(row.NormalizedValue))
            {
                row.UpdateLifecycle(false);
            }
        }

        foreach (var identifier in desired)
        {
            var existing = context.WmsIdentifiers.Local
                .FirstOrDefault(row => string.Equals(
                    row.NormalizedValue,
                    identifier.NormalizedValue,
                    StringComparison.Ordinal));
            if (existing is null)
            {
                context.WmsIdentifiers.Add(identifier.CreateEntity());
                continue;
            }

            if (!string.Equals(existing.OwnerKey, identifier.OwnerKey, StringComparison.Ordinal))
            {
                return Result.Failure(WmsErrors.Conflict(
                    "identification.conflict",
                    "Every active identifier must resolve to one entity only."));
            }

            existing.UpdateLifecycle(
                identifier.IsActive,
                identifier.ValidFromUtc,
                identifier.ValidToUtc);
            existing.SetAlias(identifier.Alias);
        }

        return Result.Success();
    }

    private static PreparedIdentifier Prepare(
        string value,
        IdentificationKind kind,
        string ownerKey,
        bool isActive,
        BarcodeSymbology? symbology = null,
        string? alias = null,
        DateTime? validFromUtc = null,
        DateTime? validToUtc = null)
    {
        var parsed = symbology == BarcodeSymbology.Sscc
            ? BarcodeParser.ParseSscc(value)
            : symbology is BarcodeSymbology.Ean8 or
            BarcodeSymbology.UpcA or
            BarcodeSymbology.Ean13 or
            BarcodeSymbology.Gtin14
            ? BarcodeParser.ParseProductCode(value)
            : BarcodeParser.Parse(value);
        return new PreparedIdentifier(
            parsed.OriginalValue,
            parsed.NormalizedValue,
            kind,
            symbology ?? parsed.Symbology,
            ownerKey,
            isActive,
            validFromUtc ?? DateTime.UnixEpoch,
            validToUtc,
            alias);
    }

    private static PreparedIdentifier PrepareProductCode(
        string value,
        IdentificationKind kind,
        string ownerKey,
        bool isActive) =>
        Prepare(value, kind, ownerKey, isActive, BarcodeSymbology.Gtin14);

    private sealed record PreparedIdentifier(
        string OriginalValue,
        string NormalizedValue,
        IdentificationKind Kind,
        BarcodeSymbology Symbology,
        string OwnerKey,
        bool IsActive,
        DateTime ValidFromUtc,
        DateTime? ValidToUtc,
        string? Alias)
    {
        public WmsIdentifier CreateEntity() => new(
            OriginalValue,
            NormalizedValue,
            Kind,
            Symbology,
            OwnerKey,
            IsActive,
            ValidFromUtc,
            ValidToUtc,
            Alias);
    }
}
