using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.InventoryStatuses;
using Wms.Application.Quality;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Quality;

public sealed class QualityInspectionService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IInventoryStatusService inventoryStatusService,
    Lazy<IWarehouseWorkService> warehouseWorkService,
    IAuditWriter auditWriter,
    IClock clock,
    ILogger<QualityInspectionService> logger) : IQualityInspectionService
{
    public async Task<bool> RequiresInboundInspectionAsync(
        int warehouseId,
        int itemId,
        CancellationToken cancellationToken = default)
    {
        var item = await context.Items
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == itemId, cancellationToken);
        if (item is null || item.QualityInspectionRequired)
        {
            return item?.QualityInspectionRequired == true;
        }

        return await context.QualityProfiles
            .AsNoTracking()
            .AnyAsync(profile => profile.IsActive && profile.IsRequired &&
                                 (!profile.WarehouseId.HasValue || profile.WarehouseId == warehouseId) &&
                                 (!profile.ItemId.HasValue || profile.ItemId == itemId) &&
                                 (profile.ItemCategory == null || profile.ItemCategory == item.Category) &&
                                 profile.ReceiptSourceType == null,
                cancellationToken);
    }

    public async Task<Result<QualityProfilePageDto>> ListProfilesAsync(
        bool includeInactive = false,
        int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.QualityRead,
            warehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<QualityProfilePageDto>();
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = context.QualityProfiles
            .AsNoTracking()
            .Include(profile => profile.Tests)
            .AsQueryable();
        if (!scope.HasGlobalAccess)
        {
            query = query.Where(profile => !profile.WarehouseId.HasValue ||
                                           scope.WarehouseIds.Contains(profile.WarehouseId.Value));
        }

        if (warehouseId.HasValue)
        {
            query = query.Where(profile => !profile.WarehouseId.HasValue ||
                                           profile.WarehouseId == warehouseId.Value);
        }

        if (!includeInactive)
        {
            query = query.Where(profile => profile.IsActive);
        }

        var profiles = await query
            .OrderBy(profile => profile.Code)
            .ToListAsync(cancellationToken);
        return Result.Success(new QualityProfilePageDto(
            profiles.Select(Map).ToArray(),
            profiles.Count));
    }

    public async Task<Result<QualityProfileDto>> CreateProfileAsync(
        QualityProfileInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.QualityManage,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<QualityProfileDto>();
        }

        try
        {
            var normalizedCode = input.Code.Trim().ToUpperInvariant();
            if (await context.QualityProfiles.AnyAsync(profile => profile.Code == normalizedCode, cancellationToken))
            {
                return Result.Failure<QualityProfileDto>(WmsErrors.Conflict(
                    "quality.profile_code_exists",
                    "A quality profile with this code already exists."));
            }

            await ValidateScopeReferencesAsync(input, cancellationToken);
            var profile = new QualityProfile(
                input.Code,
                input.Name,
                input.LocalizedName,
                input.RiskLevel,
                input.SamplingMethod,
                input.SamplingValue,
                input.IsRequired,
                input.WarehouseId,
                input.SupplierId,
                input.ItemId,
                input.ItemCategory,
                input.ReceiptSourceType);
            foreach (var testInput in input.Tests ?? [])
            {
                profile.AddTest(new QualityProfileTest(
                    testInput.Sequence,
                    testInput.Code,
                    testInput.Name,
                    testInput.LocalizedName,
                    testInput.MeasurementType,
                    testInput.IsRequired,
                    testInput.MinimumValue,
                    testInput.MaximumValue,
                    testInput.AllowedValues));
            }

            context.QualityProfiles.Add(profile);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.QualityProfileCreated,
                    WmsAuditEntityTypes.QualityProfile,
                    profile.Code,
                    profile.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["code"] = profile.Code,
                        ["samplingMethod"] = profile.SamplingMethod.ToString(),
                        ["samplingValue"] = profile.SamplingValue,
                        ["testCount"] = profile.Tests.Count
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(profile));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<QualityProfileDto>(WmsErrors.Validation(
                "quality.profile_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<QualityProfileDto>(WmsErrors.BusinessRule(
                "quality.profile_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Quality profile creation failed for {Code}", input.Code);
            return Result.Failure<QualityProfileDto>(WmsErrors.FromException(
                exception,
                "quality.profile_create_failed",
                "The quality profile could not be created."));
        }
    }

    public async Task<Result<QualityProfileDto>> SetProfileActiveAsync(
        int profileId,
        bool isActive,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var profile = await context.QualityProfiles
            .Include(value => value.Tests)
            .SingleOrDefaultAsync(value => value.Id == profileId, cancellationToken);
        if (profile is null)
        {
            return Result.Failure<QualityProfileDto>(WmsErrors.NotFound(
                "quality.profile_not_found",
                "The quality profile was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.QualityManage,
            profile.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<QualityProfileDto>();
        }

        try
        {
            if (isActive)
            {
                profile.Activate();
            }
            else
            {
                profile.Deactivate();
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.QualityProfileChanged,
                    WmsAuditEntityTypes.QualityProfile,
                    profile.Code,
                    profile.WarehouseId,
                    After: new Dictionary<string, object?> { ["isActive"] = isActive },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(profile));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<QualityProfileDto>(WmsErrors.BusinessRule(
                "quality.profile_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Quality profile activation failed for {ProfileId}", profileId);
            return Result.Failure<QualityProfileDto>(WmsErrors.FromException(
                exception,
                "quality.profile_activation_failed",
                "The quality profile activation could not be changed."));
        }
    }

    public async Task<Result<QualityInspectionPageDto>> ListInspectionsAsync(
        QualityInspectionQuery query,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.QualityRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<QualityInspectionPageDto>();
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var inspectionQuery = InspectionQuery();
        if (!scope.HasGlobalAccess)
        {
            inspectionQuery = inspectionQuery.Where(inspection => scope.WarehouseIds.Contains(inspection.WarehouseId));
        }

        if (query.WarehouseId.HasValue)
        {
            inspectionQuery = inspectionQuery.Where(inspection => inspection.WarehouseId == query.WarehouseId.Value);
        }

        if (query.Status.HasValue)
        {
            inspectionQuery = inspectionQuery.Where(inspection => inspection.Status == query.Status.Value);
        }
        else if (!query.IncludeClosed)
        {
            inspectionQuery = inspectionQuery.Where(inspection => inspection.Status != QualityInspectionStatus.Closed &&
                                                                  inspection.Status != QualityInspectionStatus.Cancelled);
        }

        if (query.ReceiptId.HasValue)
        {
            inspectionQuery = inspectionQuery.Where(inspection => inspection.ReceiptId == query.ReceiptId.Value);
        }

        if (query.ItemId.HasValue)
        {
            inspectionQuery = inspectionQuery.Where(inspection => inspection.ItemId == query.ItemId.Value);
        }

        if (query.SupplierId.HasValue)
        {
            inspectionQuery = inspectionQuery.Where(inspection => inspection.SupplierId == query.SupplierId.Value);
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var totalCount = await inspectionQuery.CountAsync(cancellationToken);
        var inspections = await inspectionQuery
            .OrderByDescending(inspection => inspection.CreatedAt)
            .ThenByDescending(inspection => inspection.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Result.Success(new QualityInspectionPageDto(
            inspections.Select(Map).ToArray(),
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    public async Task<Result<QualityInspectionDto>> GetInspectionAsync(
        int inspectionId,
        CancellationToken cancellationToken = default)
    {
        var inspection = await InspectionQuery()
            .SingleOrDefaultAsync(value => value.Id == inspectionId, cancellationToken);
        if (inspection is null)
        {
            return Result.Failure<QualityInspectionDto>(WmsErrors.NotFound(
                "quality.inspection_not_found",
                "The quality inspection was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.QualityRead,
            inspection.WarehouseId,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<QualityInspectionDto>()
            : Result.Success(Map(inspection));
    }

    public async Task<Result<QualityInspectionDto?>> EnsureForReceiptAsync(
        int receiptId,
        int receiptLineId,
        Movement movement,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movement);
        if (movement.ReceiptId != receiptId || movement.ReceiptLineId != receiptLineId ||
            movement.Type != MovementType.Receipt)
        {
            return Result.Failure<QualityInspectionDto?>(WmsErrors.Validation(
                "quality.receipt_movement_mismatch",
                "The quality inspection source movement does not match the receipt line."));
        }

        var line = await context.ReceiptLines
            .Include(value => value.Receipt)
                .ThenInclude(receipt => receipt.Warehouse)
            .Include(value => value.Receipt)
                .ThenInclude(receipt => receipt.Supplier)
            .Include(value => value.Item)
            .Include(value => value.LicensePlate)
            .SingleOrDefaultAsync(value => value.Id == receiptLineId && value.ReceiptId == receiptId, cancellationToken);
        if (line is null)
        {
            return Result.Failure<QualityInspectionDto?>(WmsErrors.NotFound(
                "quality.receipt_line_not_found",
                "The receipt line for the quality inspection was not found."));
        }

        var existing = context.QualityInspections.Local
            .FirstOrDefault(value => value.ReceiptLineId == receiptLineId &&
                                     value.LicensePlateId == movement.ToLicensePlateId);
        existing ??= await InspectionQuery()
            .SingleOrDefaultAsync(value => value.ReceiptLineId == receiptLineId &&
                                           value.LicensePlateId == movement.ToLicensePlateId,
                cancellationToken);
        if (existing is not null)
        {
            return Result.Success<QualityInspectionDto?>(Map(existing));
        }

        var profile = await SelectProfileAsync(line, cancellationToken);
        var required = line.Item.QualityInspectionRequired || profile?.IsRequired == true;
        if (!required)
        {
            return Result.Success<QualityInspectionDto?>(null);
        }

        var sample = profile?.CalculateSampleQuantity(
            movement.Quantity.Value,
            movement.ToLicensePlateId.HasValue,
            movement.ToLicensePlateId) ?? movement.Quantity.Value;
        if (sample <= 0m)
        {
            return Result.Success<QualityInspectionDto?>(null);
        }

        var inspection = new QualityInspection(
            $"QI-{line.Receipt.DocumentNumber}-{line.LineNumber}-{movement.ToLicensePlateId?.ToString(CultureInfo.InvariantCulture) ?? "ALL"}",
            receiptId,
            receiptLineId,
            line.WarehouseId,
            line.ItemId,
            line.ItemSkuSnapshot,
            line.ItemNameSnapshot,
            movement.Quantity.Value,
            sample,
            movement.InventoryStatusId,
            line.Receipt.SourceType,
            line.Receipt.SupplierId,
            line.Receipt.SupplierCodeSnapshot,
            profile?.Id,
            profile?.Code,
            profile?.RiskLevel ?? QualityRiskLevel.Medium,
            movement.LotId,
            line.LotNumberSnapshot,
            line.ExpiryDateSnapshot,
            movement.SerialNumber,
            movement.ToLicensePlateId,
            line.LicensePlateNumberSnapshot,
            line.LicensePlateIsSscc,
            userId);
        context.QualityInspections.Add(inspection);
        await auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.QualityInspectionGenerated,
                WmsAuditEntityTypes.QualityInspection,
                inspection.InspectionNumber,
                inspection.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["receiptId"] = receiptId,
                    ["receiptLineId"] = receiptLineId,
                    ["sampleBaseQuantity"] = sample,
                    ["profileCode"] = profile?.Code,
                    ["inventoryStatusId"] = movement.InventoryStatusId
                },
                ActorUserId: userId),
            cancellationToken);
        return Result.Success<QualityInspectionDto?>(Map(inspection));
    }

    public async Task<Result<QualityInspectionDto>> RecordTestResultAsync(
        int inspectionId,
        QualityInspectionTestResultInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadForMutationAsync(inspectionId, WmsPermissions.QualityInspect, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.ToFailure<QualityInspectionDto>();
        }

        try
        {
            var inspection = loaded.Value;
            var test = inspection.QualityProfile?.Tests.SingleOrDefault(value => value.Id == input.QualityProfileTestId);
            if (test is null)
            {
                return Result.Failure<QualityInspectionDto>(WmsErrors.Validation(
                    "quality.test_not_in_profile",
                    "The selected quality test is not part of the inspection profile."));
            }

            if (input.InspectedQuantity <= 0m)
            {
                return Result.Failure<QualityInspectionDto>(WmsErrors.Validation(
                    "quality.inspected_quantity_invalid",
                    "Inspected quantity must be greater than zero."));
            }

            var evaluated = EvaluateTest(test, input);
            if (evaluated != input.Passed)
            {
                return Result.Failure<QualityInspectionDto>(WmsErrors.Validation(
                    "quality.result_mismatch",
                    "The supplied pass/fail value does not match the test criteria."));
            }

            var result = new QualityInspectionTestResult(
                test.Id,
                test.Code,
                test.Name,
                test.MeasurementType,
                input.InspectedQuantity,
                input.RecordedValue,
                input.NumericValue,
                input.BooleanValue,
                evaluated,
                input.Notes,
                input.AttachmentReferences,
                userId,
                clock.UtcNow.UtcDateTime);
            inspection.AddResult(result, input.InspectedQuantity, userId, clock.UtcNow.UtcDateTime);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.QualityInspectionResultRecorded,
                    WmsAuditEntityTypes.QualityInspectionTestResult,
                    inspection.InspectionNumber,
                    inspection.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["testCode"] = test.Code,
                        ["passed"] = evaluated,
                        ["inspectedQuantity"] = input.InspectedQuantity
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(inspection));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<QualityInspectionDto>(WmsErrors.Validation(
                "quality.result_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<QualityInspectionDto>(WmsErrors.BusinessRule(
                "quality.result_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Quality result recording failed for {InspectionId}", inspectionId);
            return Result.Failure<QualityInspectionDto>(WmsErrors.FromException(
                exception,
                "quality.result_failed",
                "The quality result could not be recorded."));
        }
    }

    public async Task<Result<QualityInspectionDto>> AddDispositionAsync(
        int inspectionId,
        QualityDispositionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadForMutationAsync(inspectionId, WmsPermissions.QualityInspect, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.ToFailure<QualityInspectionDto>();
        }

        if (input.SupervisorOverride)
        {
            var overrideAuthorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.QualityOverride,
                loaded.Value.WarehouseId,
                cancellationToken);
            if (overrideAuthorization.IsFailure)
            {
                return overrideAuthorization.ToFailure<QualityInspectionDto>();
            }
        }

        try
        {
            var inspection = loaded.Value;
            if (inspection.IsClosed)
            {
                return Result.Failure<QualityInspectionDto>(WmsErrors.BusinessRule(
                    "quality.inspection_closed",
                    "A closed quality inspection cannot be edited without a controlled correction."));
            }

            if (input.Quantity <= 0m)
            {
                return Result.Failure<QualityInspectionDto>(WmsErrors.Validation(
                    "quality.quantity_invalid",
                    "Disposition quantity must be greater than zero."));
            }

            var remainingDisposition = inspection.SampleBaseQuantity -
                                       inspection.Dispositions.Sum(disposition => disposition.Quantity);
            if (input.Quantity > remainingDisposition)
            {
                return Result.Failure<QualityInspectionDto>(WmsErrors.BusinessRule(
                    "quality.disposition_exceeds_sample",
                    "Quality dispositions cannot exceed the remaining selected sample."));
            }

            if (string.IsNullOrWhiteSpace(input.Reason))
            {
                return Result.Failure<QualityInspectionDto>(WmsErrors.Validation(
                    "quality.reason_required",
                    "A disposition reason is required."));
            }

            if (input.SupervisorOverride && string.IsNullOrWhiteSpace(input.OverrideReason))
            {
                return Result.Failure<QualityInspectionDto>(WmsErrors.Validation(
                    "quality.override_reason_required",
                    "A supervisor override reason is required."));
            }

            var targetStatusId = TargetStatusFor(input.Type);
            var statusMovementReference = string.IsNullOrWhiteSpace(input.ReferenceNumber)
                ? $"QI-DISP-{inspection.Id.ToString(CultureInfo.InvariantCulture)}-{inspection.Revision.ToString(CultureInfo.InvariantCulture)}"
                : input.ReferenceNumber.Trim();
            if (targetStatusId == InventoryStatusSystemIds.Available)
            {
                var workAuthorization = await warehouseAccessService.AuthorizeAsync(
                    WmsPermissions.WorkExecute,
                    inspection.WarehouseId,
                    cancellationToken);
                if (workAuthorization.IsFailure)
                {
                    return workAuthorization.ToFailure<QualityInspectionDto>();
                }
            }

            var statusChange = await MoveStockToDispositionStatusAsync(
                inspection,
                input.Quantity,
                targetStatusId,
                input.Reason,
                statusMovementReference,
                userId,
                cancellationToken);
            if (statusChange.IsFailure)
            {
                return statusChange.ToFailure<QualityInspectionDto>();
            }

            if (targetStatusId == InventoryStatusSystemIds.Available)
            {
                var sourceMovement = statusChange.Value.HasValue
                    ? await context.Movements.AsNoTracking()
                        .SingleOrDefaultAsync(
                            movement => movement.Id == statusChange.Value.Value,
                            cancellationToken)
                    : await context.Movements.AsNoTracking()
                        .Where(movement => movement.ReceiptLineId == inspection.ReceiptLineId &&
                                           movement.ReceiptMovementKind == ReceiptMovementKind.Receipt)
                        .OrderBy(movement => movement.Id)
                        .FirstOrDefaultAsync(cancellationToken);
                if (sourceMovement is null)
                {
                    return Result.Failure<QualityInspectionDto>(WmsErrors.Dependency(
                        "quality.putaway_source_missing",
                        "The accepted inspection stock has no source receipt movement for putaway.",
                        isRetryable: false));
                }

                var movementKey = statusChange.Value.HasValue
                    ? $"id:{statusChange.Value.Value.ToString(CultureInfo.InvariantCulture)}"
                    : $"id:{sourceMovement.Id.ToString(CultureInfo.InvariantCulture)}:quality-disposition:{inspection.Revision.ToString(CultureInfo.InvariantCulture)}";
                var putawayResult = await warehouseWorkService.Value.EnsurePutawayForReceiptAsync(
                    new PutawayWorkGenerationInput(
                        inspection.ReceiptId,
                        inspection.ReceiptLineId,
                        inspection.WarehouseId,
                        inspection.ItemId,
                        input.Quantity,
                        inspection.ReceiptLine.BaseUnitOfMeasure,
                        inspection.ReceiptLine.ReceivingLocationId!.Value,
                        sourceMovement.ToLicensePlateId ?? sourceMovement.LicensePlateId,
                        sourceMovement.LotId,
                        sourceMovement.SerialNumberId,
                        sourceMovement.SerialNumber,
                        targetStatusId,
                        sourceMovement.ReferenceNumber ?? inspection.InspectionNumber,
                        movementKey,
                        QualityInspectionPending: false,
                        OwnerKind: sourceMovement.OwnerKind,
                        InventoryOwnerId: sourceMovement.InventoryOwnerId,
                        OwnerCodeSnapshot: sourceMovement.OwnerCodeSnapshot),
                    userId,
                    cancellationToken);
                if (putawayResult.IsFailure)
                {
                    return putawayResult.ToFailure<QualityInspectionDto>();
                }
            }

            var now = clock.UtcNow.UtcDateTime;
            var disposition = new QualityInspectionDisposition(
                input.Type,
                input.Quantity,
                targetStatusId,
                input.Reason,
                userId,
                now,
                input.ReferenceNumber,
                statusChange.Value,
                input.TargetLocationId,
                input.SupervisorOverride,
                input.OverrideReason);
            inspection.AddDisposition(disposition, now);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.QualityDispositionRecorded,
                    WmsAuditEntityTypes.QualityInspectionDisposition,
                    inspection.InspectionNumber,
                    inspection.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["type"] = input.Type.ToString(),
                        ["quantity"] = input.Quantity,
                        ["targetStatusId"] = targetStatusId,
                        ["movementId"] = statusChange.Value,
                        ["supervisorOverride"] = input.SupervisorOverride
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(inspection));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<QualityInspectionDto>(WmsErrors.Validation(
                "quality.disposition_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<QualityInspectionDto>(WmsErrors.BusinessRule(
                "quality.disposition_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Quality disposition failed for {InspectionId}", inspectionId);
            return Result.Failure<QualityInspectionDto>(WmsErrors.FromException(
                exception,
                "quality.disposition_failed",
                "The quality disposition could not be recorded."));
        }
    }

    public async Task<Result<QualityInspectionDto>> CloseAsync(
        int inspectionId,
        string userId,
        string? reason = null,
        bool supervisorOverride = false,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadForMutationAsync(inspectionId, WmsPermissions.QualityInspect, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.ToFailure<QualityInspectionDto>();
        }

        if (supervisorOverride)
        {
            var overrideAuthorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.QualityOverride,
                loaded.Value.WarehouseId,
                cancellationToken);
            if (overrideAuthorization.IsFailure)
            {
                return overrideAuthorization.ToFailure<QualityInspectionDto>();
            }
        }

        try
        {
            var now = clock.UtcNow.UtcDateTime;
            loaded.Value.Close(userId, now, reason, supervisorOverride);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.QualityInspectionClosed,
                    WmsAuditEntityTypes.QualityInspection,
                    loaded.Value.InspectionNumber,
                    loaded.Value.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = loaded.Value.Status.ToString(),
                        ["reason"] = reason,
                        ["supervisorOverride"] = supervisorOverride
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(loaded.Value));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<QualityInspectionDto>(WmsErrors.Validation(
                "quality.close_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<QualityInspectionDto>(WmsErrors.BusinessRule(
                "quality.close_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Quality inspection closure failed for {InspectionId}", inspectionId);
            return Result.Failure<QualityInspectionDto>(WmsErrors.FromException(
                exception,
                "quality.close_failed",
                "The quality inspection could not be closed."));
        }
    }

    private IQueryable<QualityInspection> InspectionQuery() =>
        context.QualityInspections
            .Include(inspection => inspection.QualityProfile)
                .ThenInclude(profile => profile!.Tests)
            .Include(inspection => inspection.ReceiptLine)
            .Include(inspection => inspection.Results)
            .Include(inspection => inspection.Dispositions)
            .AsSplitQuery();

    private async Task<Result<QualityInspection>> LoadForMutationAsync(
        int inspectionId,
        string permission,
        CancellationToken cancellationToken)
    {
        var inspection = await InspectionQuery()
            .SingleOrDefaultAsync(value => value.Id == inspectionId, cancellationToken);
        if (inspection is null)
        {
            return Result.Failure<QualityInspection>(WmsErrors.NotFound(
                "quality.inspection_not_found",
                "The quality inspection was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            permission,
            inspection.WarehouseId,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<QualityInspection>()
            : Result.Success(inspection);
    }

    private async Task<QualityProfile?> SelectProfileAsync(
        ReceiptLine line,
        CancellationToken cancellationToken)
    {
        var profiles = await context.QualityProfiles
            .Include(profile => profile.Tests)
            .Where(profile => profile.IsActive)
            .ToListAsync(cancellationToken);
        return profiles
            .Where(profile =>
                (!profile.WarehouseId.HasValue || profile.WarehouseId == line.WarehouseId) &&
                (!profile.SupplierId.HasValue || profile.SupplierId == line.Receipt.SupplierId) &&
                (!profile.ItemId.HasValue || profile.ItemId == line.ItemId) &&
                (profile.ItemCategory == null || string.Equals(profile.ItemCategory, line.Item.Category, StringComparison.OrdinalIgnoreCase)) &&
                (profile.ReceiptSourceType == null || string.Equals(profile.ReceiptSourceType, line.Receipt.SourceType, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(profile => profile.ItemId.HasValue)
            .ThenByDescending(profile => profile.SupplierId.HasValue)
            .ThenByDescending(profile => profile.WarehouseId.HasValue)
            .ThenByDescending(profile => profile.ItemCategory != null)
            .ThenByDescending(profile => profile.ReceiptSourceType != null)
            .ThenByDescending(profile => profile.RiskLevel)
            .ThenBy(profile => profile.Id)
            .FirstOrDefault();
    }

    private async Task ValidateScopeReferencesAsync(
        QualityProfileInput input,
        CancellationToken cancellationToken)
    {
        if (input.WarehouseId.HasValue &&
            !await context.Warehouses.AnyAsync(value => value.Id == input.WarehouseId.Value, cancellationToken))
        {
            throw new InvalidOperationException("The quality profile warehouse was not found.");
        }

        if (input.SupplierId.HasValue &&
            !await context.Suppliers.AnyAsync(value => value.Id == input.SupplierId.Value, cancellationToken))
        {
            throw new InvalidOperationException("The quality profile supplier was not found.");
        }

        if (input.ItemId.HasValue &&
            !await context.Items.AnyAsync(value => value.Id == input.ItemId.Value, cancellationToken))
        {
            throw new InvalidOperationException("The quality profile item was not found.");
        }
    }

    private async Task<Result<int?>> MoveStockToDispositionStatusAsync(
        QualityInspection inspection,
        decimal quantity,
        int targetStatusId,
        string reason,
        string? referenceNumber,
        string userId,
        CancellationToken cancellationToken)
    {
        if (quantity <= 0m)
        {
            return Result.Failure<int?>(WmsErrors.Validation(
                "quality.quantity_invalid",
                "Disposition quantity must be greater than zero."));
        }

        var locationId = inspection.ReceiptLine.ReceivingLocationId;
        if (!locationId.HasValue)
        {
            return Result.Failure<int?>(WmsErrors.Dependency(
                "quality.location_missing",
                "The receipt line has no receiving location.",
                isRetryable: false));
        }

        var existingInboundMovements = await context.Movements.AsNoTracking()
            .Where(movement => movement.Type == MovementType.StatusChange &&
                               movement.StatusChangeLeg == InventoryStatusMovementLeg.Inbound &&
                               movement.ItemId == inspection.ItemId &&
                               movement.ToLocationId == locationId.Value &&
                               movement.ToInventoryStatusId == targetStatusId &&
                               movement.ReferenceNumber == referenceNumber &&
                               movement.LotId == inspection.LotId &&
                               movement.LicensePlateId == inspection.LicensePlateId &&
                               movement.OwnerKind == inspection.ReceiptLine.OwnerKind &&
                               movement.InventoryOwnerId == inspection.ReceiptLine.InventoryOwnerId &&
                               movement.OwnerCodeSnapshot == inspection.ReceiptLine.OwnerCodeSnapshot &&
                               (inspection.SerialNumberSnapshot == null
                                   ? movement.SerialNumber == null
                                   : movement.SerialNumber == inspection.SerialNumberSnapshot))
            .ToArrayAsync(cancellationToken);
        var existingInboundMovementId = existingInboundMovements
            .Where(movement => movement.Quantity.Value == quantity)
            .Select(movement => (int?)movement.Id)
            .SingleOrDefault();
        if (existingInboundMovementId.HasValue)
        {
            return Result.Success(existingInboundMovementId);
        }

        var candidates = context.Stock.Local
            .Where(stock => stock.ItemId == inspection.ItemId &&
                            stock.LocationId == locationId.Value &&
                            stock.LotId == inspection.LotId &&
                            stock.InventoryStatusId == inspection.InventoryStatusId &&
                            stock.LicensePlateId == inspection.LicensePlateId &&
                            stock.OwnerKind == inspection.ReceiptLine.OwnerKind &&
                            stock.InventoryOwnerId == inspection.ReceiptLine.InventoryOwnerId &&
                            stock.OwnerCodeSnapshot == inspection.ReceiptLine.OwnerCodeSnapshot &&
                            (inspection.SerialNumberSnapshot == null
                                ? stock.SerialNumber == null
                                : stock.SerialNumber == inspection.SerialNumberSnapshot))
            .ToArray();
        var stock = candidates.FirstOrDefault(value => value.GetAvailableQuantity().Value >= quantity);
        if (stock is null)
        {
            var persistedCandidates = await context.Stock
                .Include(value => value.Location)
                .Include(value => value.InventoryStatus)
                .Include(value => value.Item)
                .Where(value => value.ItemId == inspection.ItemId &&
                                value.LocationId == locationId.Value &&
                                value.LotId == inspection.LotId &&
                                value.InventoryStatusId == inspection.InventoryStatusId &&
                                value.LicensePlateId == inspection.LicensePlateId &&
                                value.OwnerKind == inspection.ReceiptLine.OwnerKind &&
                                value.InventoryOwnerId == inspection.ReceiptLine.InventoryOwnerId &&
                                value.OwnerCodeSnapshot == inspection.ReceiptLine.OwnerCodeSnapshot &&
                                (inspection.SerialNumberSnapshot == null
                                    ? value.SerialNumber == null
                                    : value.SerialNumber == inspection.SerialNumberSnapshot))
                .ToArrayAsync(cancellationToken);
            stock = persistedCandidates
                .OrderByDescending(value => value.GetAvailableQuantity().Value)
                .FirstOrDefault();
        }
        if (stock is null)
        {
            return Result.Failure<int?>(WmsErrors.NotFound(
                "quality.stock_not_found",
                "The inspected stock record was not found in the source inventory status."));
        }

        if (stock.GetAvailableQuantity().Value < quantity)
        {
            return Result.Failure<int?>(WmsErrors.BusinessRule(
                "quality.stock_insufficient",
                "The requested disposition quantity exceeds unreserved inspected stock."));
        }

        if (stock.InventoryStatusId == targetStatusId)
        {
            return Result.Success<int?>(null);
        }

        var result = await inventoryStatusService.ChangeStockStatusAsync(
            stock.Id,
            quantity,
            targetStatusId,
            reason,
            referenceNumber,
            userId,
            cancellationToken);
        return result.IsFailure
            ? result.ToFailure<int?>()
            : Result.Success<int?>(result.Value.InboundMovementId);
    }

    private static int TargetStatusFor(QualityDispositionType type) => type switch
    {
        QualityDispositionType.Pass or QualityDispositionType.PartialPass => InventoryStatusSystemIds.Available,
        QualityDispositionType.Fail => InventoryStatusSystemIds.Quarantine,
        QualityDispositionType.Retest => InventoryStatusSystemIds.QualityPending,
        QualityDispositionType.Hold or QualityDispositionType.Rework => InventoryStatusSystemIds.Hold,
        QualityDispositionType.ReturnToVendor => InventoryStatusSystemIds.ReturnPending,
        QualityDispositionType.Damage => InventoryStatusSystemIds.Damaged,
        QualityDispositionType.Scrap => InventoryStatusSystemIds.ScrapPending,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static bool EvaluateTest(QualityProfileTest test, QualityInspectionTestResultInput input)
    {
        return test.MeasurementType switch
        {
            QualityMeasurementType.Numeric => input.NumericValue.HasValue &&
                (!test.MinimumValue.HasValue || input.NumericValue.Value >= test.MinimumValue.Value) &&
                (!test.MaximumValue.HasValue || input.NumericValue.Value <= test.MaximumValue.Value),
            QualityMeasurementType.Boolean => input.BooleanValue == true,
            QualityMeasurementType.Choice => IsAllowedChoice(test.AllowedValues, input.RecordedValue),
            QualityMeasurementType.Text => !string.IsNullOrWhiteSpace(input.RecordedValue) && input.Passed,
            _ => false
        };
    }

    private static bool IsAllowedChoice(string? allowedValues, string? recordedValue)
    {
        if (string.IsNullOrWhiteSpace(allowedValues) || string.IsNullOrWhiteSpace(recordedValue))
        {
            return false;
        }

        var values = allowedValues.TrimStart().StartsWith('[')
            ? JsonSerializer.Deserialize<string[]>(allowedValues) ?? []
            : allowedValues.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return values.Contains(recordedValue.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static QualityProfileDto Map(QualityProfile profile) => new(
        profile.Id,
        profile.Code,
        profile.Name,
        profile.LocalizedName,
        profile.RiskLevel,
        profile.SamplingMethod,
        profile.SamplingValue,
        profile.IsRequired,
        profile.WarehouseId,
        profile.SupplierId,
        profile.ItemId,
        profile.ItemCategory,
        profile.ReceiptSourceType,
        profile.IsActive,
        profile.Revision,
        profile.Tests
            .OrderBy(test => test.Sequence)
            .Select(test => new QualityProfileTestDto(
                test.Id,
                test.Sequence,
                test.Code,
                test.Name,
                test.LocalizedName,
                test.MeasurementType,
                test.IsRequired,
                test.MinimumValue,
                test.MaximumValue,
                test.AllowedValues))
            .ToArray());

    private static QualityInspectionDto Map(QualityInspection inspection) => new(
        inspection.Id,
        inspection.InspectionNumber,
        inspection.ReceiptId,
        inspection.ReceiptLineId,
        inspection.WarehouseId,
        inspection.ItemId,
        inspection.ItemSkuSnapshot,
        inspection.ItemNameSnapshot,
        inspection.ReceivedBaseQuantity,
        inspection.SampleBaseQuantity,
        inspection.InspectedBaseQuantity,
        inspection.AcceptedBaseQuantity,
        inspection.RejectedBaseQuantity,
        inspection.RemainingSampleQuantity,
        inspection.InventoryStatusId,
        inspection.SourceType,
        inspection.SupplierId,
        inspection.SupplierCodeSnapshot,
        inspection.QualityProfileId,
        inspection.QualityProfileCodeSnapshot,
        inspection.RiskLevel,
        inspection.LotNumberSnapshot,
        inspection.ExpiryDateSnapshot,
        inspection.SerialNumberSnapshot,
        inspection.LicensePlateId,
        inspection.LicensePlateNumberSnapshot,
        inspection.Status,
        inspection.CreatedByUserId,
        inspection.InspectorUserId,
        inspection.CreatedAt,
        inspection.StartedAtUtc,
        inspection.ClosedAtUtc,
        inspection.ClosedByUserId,
        inspection.ClosureReason,
        inspection.Revision,
        inspection.Results.Select(result => new QualityInspectionTestResultDto(
            result.Id,
            result.QualityProfileTestId,
            result.TestCodeSnapshot,
            result.TestNameSnapshot,
            result.MeasurementType,
            result.NumericValue,
            result.BooleanValue,
            result.RecordedValue,
            result.Passed,
            result.InspectedQuantity,
            result.Notes,
            result.AttachmentReferences,
            result.RecordedByUserId,
            result.RecordedAtUtc)).ToArray(),
        inspection.Dispositions.Select(disposition => new QualityInspectionDispositionDto(
            disposition.Id,
            disposition.Type,
            disposition.Quantity,
            disposition.TargetInventoryStatusId,
            disposition.Reason,
            disposition.RecordedByUserId,
            disposition.RecordedAtUtc,
            disposition.ReferenceNumber,
            disposition.MovementId,
            disposition.TargetLocationId,
            disposition.SupervisorOverride,
            disposition.OverrideReason)).ToArray(),
        inspection.IsClosed);
}
