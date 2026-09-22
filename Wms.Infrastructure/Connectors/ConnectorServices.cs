using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.Connectors;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Connectors;

public sealed class ConnectorService(
    WmsDbContext context,
    IEnumerable<IConnectorAdapter> adapters,
    IWarehouseAccessService warehouseAccessService,
    IClock clock,
    IRequestContext requestContext,
    ILogger<ConnectorService> logger) : IConnectorService
{
    private const string ConnectorPermission = WmsPermissions.SettingsManage;
    private const int MaximumBatchSize = 1_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<ConnectorMappingProfileDto>> SaveMappingProfileAsync(
        ConnectorMappingProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateMappingProfile(request);
        if (validation is not null)
        {
            return Result.Failure<ConnectorMappingProfileDto>(validation);
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            ConnectorPermission,
            cancellationToken: cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ConnectorMappingProfileDto>();
        }

        var connectorType = request.ConnectorType.Trim();
        var name = request.Name.Trim();
        var existing = await context.ConnectorMappingProfiles.SingleOrDefaultAsync(
            profile => profile.ConnectorType == connectorType &&
                       profile.Name == name &&
                       profile.Version == request.Version,
            cancellationToken);
        if (existing is not null)
        {
            return Result.Failure<ConnectorMappingProfileDto>(WmsErrors.Conflict(
                "connector.mapping_version_exists",
                "The connector mapping version already exists and cannot be overwritten."));
        }

        var now = clock.UtcNow;
        var entity = new WmsConnectorMappingProfileEntity
        {
            ConnectorType = connectorType,
            Name = name,
            Version = request.Version,
            ExternalIdField = request.ExternalIdField.Trim(),
            RulesJson = ConnectorMappingEngine.SerializeRules(request.Rules),
            CultureName = request.CultureName.Trim(),
            ConflictPolicy = request.ConflictPolicy.Trim(),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.ConnectorMappingProfiles.Add(entity);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(ToDto(entity));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not save connector mapping profile {ConnectorType}/{MappingName}/{Version}",
                connectorType,
                name,
                request.Version);
            return Result.Failure<ConnectorMappingProfileDto>(WmsErrors.FromException(
                exception,
                "connector.mapping_save_failed",
                "The connector mapping profile could not be saved."));
        }
    }

    public async Task<Result<ConnectorInstanceDto>> CreateAsync(
        ConnectorInstanceCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateInstance(request);
        if (validation is not null)
        {
            return Result.Failure<ConnectorInstanceDto>(validation);
        }

        var connectorType = request.ConnectorType.Trim();
        var name = request.Name.Trim();
        var warehouseIds = request.AllowedWarehouseIds.Distinct().Order().ToArray();
        var authorization = await AuthorizeWarehousesAsync(warehouseIds, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ConnectorInstanceDto>();
        }

        var mapping = await context.ConnectorMappingProfiles.SingleOrDefaultAsync(
            profile => profile.ConnectorType == connectorType &&
                       profile.Name == request.MappingProfileName.Trim() &&
                       profile.Version == request.MappingProfileVersion &&
                       profile.IsActive,
            cancellationToken);
        if (mapping is null)
        {
            return Result.Failure<ConnectorInstanceDto>(WmsErrors.NotFound(
                "connector.mapping_not_found",
                "The selected connector mapping profile is not active or does not exist."));
        }

        if (adapters.All(adapter => !string.Equals(adapter.ConnectorType, connectorType, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<ConnectorInstanceDto>(WmsErrors.Dependency(
                "connector.adapter_not_registered",
                "No connector adapter is registered for the selected connector type.",
                isRetryable: false));
        }

        string credentialReference;
        try
        {
            credentialReference = ConnectorSecurityPolicy.NormalizeCredentialReference(request.CredentialReference);
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<ConnectorInstanceDto>(WmsErrors.Validation(
                "connector.credential_reference_invalid",
                exception.Message));
        }

        var now = clock.UtcNow;
        var entity = new WmsConnectorInstanceEntity
        {
            ConnectorType = connectorType,
            Name = name,
            AllowedWarehouseIdsJson = Serialize(warehouseIds),
            CredentialReference = credentialReference,
            CredentialVersion = 1,
            ModesJson = Serialize(request.Modes.Select(mode => mode.Trim().ToLowerInvariant()).Distinct().Order(StringComparer.Ordinal).ToArray()),
            Schedule = NormalizeSchedule(request.Schedule),
            MappingProfileName = mapping.Name,
            MappingProfileVersion = mapping.Version,
            Status = request.Activate ? WmsConnectorStatuses.Active : WmsConnectorStatuses.Draft,
            Cursor = string.Empty,
            HealthStatus = WmsConnectorHealthStatuses.Unknown,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.ConnectorInstances.Add(entity);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(ToDto(entity));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not create connector {ConnectorType}/{ConnectorName}", connectorType, name);
            return Result.Failure<ConnectorInstanceDto>(WmsErrors.FromException(
                exception,
                "connector.create_failed",
                "The connector instance could not be created."));
        }
    }

    public async Task<Result<IReadOnlyList<ConnectorInstanceDto>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            ConnectorPermission,
            cancellationToken: cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<ConnectorInstanceDto>>();
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var entities = await context.ConnectorInstances
            .AsNoTracking()
            .OrderBy(instance => instance.Name)
            .ToListAsync(cancellationToken);
        var visible = entities
            .Where(instance => scope.HasGlobalAccess || DeserializeInts(instance.AllowedWarehouseIdsJson).Any(scope.WarehouseIds.Contains))
            .Select(ToDto)
            .ToArray();
        return Result.Success<IReadOnlyList<ConnectorInstanceDto>>(visible);
    }

    public async Task<Result<ConnectorInstanceDto>> RotateCredentialAsync(
        long connectorId,
        ConnectorCredentialRotationRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.ConnectorInstances.SingleOrDefaultAsync(
            connector => connector.Id == connectorId,
            cancellationToken);
        if (entity is null)
        {
            return Result.Failure<ConnectorInstanceDto>(WmsErrors.NotFound(
                "connector.not_found",
                "The connector instance was not found."));
        }

        var authorization = await AuthorizeWarehousesAsync(
            DeserializeInts(entity.AllowedWarehouseIdsJson),
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ConnectorInstanceDto>();
        }

        string reference;
        try
        {
            reference = ConnectorSecurityPolicy.NormalizeCredentialReference(request.CredentialReference);
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<ConnectorInstanceDto>(WmsErrors.Validation(
                "connector.credential_reference_invalid",
                exception.Message));
        }

        if (string.Equals(reference, entity.CredentialReference, StringComparison.Ordinal))
        {
            return Result.Failure<ConnectorInstanceDto>(WmsErrors.Conflict(
                "connector.credential_not_rotated",
                "Credential rotation requires a new deployment-managed reference."));
        }

        entity.CredentialReference = reference;
        entity.CredentialVersion++;
        entity.UpdatedAtUtc = clock.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(entity));
    }

    public async Task<Result<ConnectorRunDto>> TestConnectionAsync(
        long connectorId,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.ConnectorInstances.SingleOrDefaultAsync(
            connector => connector.Id == connectorId,
            cancellationToken);
        if (entity is null)
        {
            return Result.Failure<ConnectorRunDto>(WmsErrors.NotFound(
                "connector.not_found",
                "The connector instance was not found."));
        }

        var authorization = await AuthorizeWarehousesAsync(
            DeserializeInts(entity.AllowedWarehouseIdsJson),
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ConnectorRunDto>();
        }

        var adapter = FindAdapter(entity.ConnectorType);
        if (adapter is null)
        {
            return Result.Failure<ConnectorRunDto>(WmsErrors.Dependency(
                "connector.adapter_not_registered",
                "No connector adapter is registered for this connector instance.",
                isRetryable: false));
        }

        var now = clock.UtcNow;
        var run = new WmsConnectorRunEntity
        {
            ConnectorInstanceId = entity.Id,
            Operation = "connection-test",
            Mode = WmsConnectorModes.Pull,
            Status = WmsConnectorRunStatuses.Running,
            IdempotencyKey = $"connector-health:{entity.Id}:{now:yyyyMMddHHmmssfff}",
            CorrelationId = WmsExecutionIdentifiers.Normalize(requestContext.CorrelationId),
            AttemptCount = 1,
            CreatedAtUtc = now,
            StartedAtUtc = now
        };
        context.ConnectorRuns.Add(run);
        entity.LastHealthCheckAtUtc = now;
        entity.LastRunAtUtc = now;
        await context.SaveChangesAsync(cancellationToken);

        try
        {
            var health = await adapter.TestConnectionAsync(ToContext(entity), cancellationToken);
            run.Status = health.Succeeded ? WmsConnectorRunStatuses.Succeeded : WmsConnectorRunStatuses.Failed;
            run.ErrorCode = health.Succeeded ? null : "connector.connection_test_failed";
            run.ErrorMessage = health.Succeeded ? null : ConnectorSecurityPolicy.Redact(health.Summary);
            run.CompletedAtUtc = clock.UtcNow;
            entity.HealthStatus = health.HealthStatus;
            entity.HealthSummary = Limit(ConnectorSecurityPolicy.Redact(health.Summary), 2_000);
            entity.ConsecutiveFailureCount = health.Succeeded ? 0 : entity.ConsecutiveFailureCount + 1;
            entity.UpdatedAtUtc = clock.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(ToDto(run));
        }
        catch (OperationCanceledException)
        {
            run.Status = WmsConnectorRunStatuses.Canceled;
            run.CompletedAtUtc = clock.UtcNow;
            await context.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Connector connection test failed for {ConnectorId}", entity.Id);
            return await FailRunAsync(entity, run, "connector.connection_test_failed", cancellationToken);
        }
    }

    public async Task<Result<ConnectorRunDto>> RunAsync(
        ConnectorRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateRun(request);
        if (validation is not null)
        {
            return Result.Failure<ConnectorRunDto>(validation);
        }

        var entity = await context.ConnectorInstances.SingleOrDefaultAsync(
            connector => connector.Id == request.ConnectorId,
            cancellationToken);
        if (entity is null)
        {
            return Result.Failure<ConnectorRunDto>(WmsErrors.NotFound(
                "connector.not_found",
                "The connector instance was not found."));
        }

        var allowedWarehouses = DeserializeInts(entity.AllowedWarehouseIdsJson).ToHashSet();
        if (request.WarehouseId.HasValue && !allowedWarehouses.Contains(request.WarehouseId.Value))
        {
            return Result.Failure<ConnectorRunDto>(WmsErrors.Forbidden(
                "connector.warehouse_scope_denied",
                "The requested warehouse is not assigned to this connector."));
        }

        var authorization = await AuthorizeWarehousesAsync(
            request.WarehouseId.HasValue ? [request.WarehouseId.Value] : allowedWarehouses,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ConnectorRunDto>();
        }

        if (!string.Equals(entity.Status, WmsConnectorStatuses.Active, StringComparison.Ordinal))
        {
            return Result.Failure<ConnectorRunDto>(WmsErrors.Conflict(
                "connector.not_active",
                "Only an active connector can run synchronization."));
        }

        var requestedMode = request.Mode.Trim().ToLowerInvariant();
        var requestedOperation = request.Operation.Trim().ToLowerInvariant();
        var modes = DeserializeStrings(entity.ModesJson).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!modes.Contains(requestedMode))
        {
            return Result.Failure<ConnectorRunDto>(WmsErrors.Validation(
                "connector.mode_not_enabled",
                "The requested connector mode is not enabled for this instance."));
        }

        var adapter = FindAdapter(entity.ConnectorType);
        if (adapter is null || !adapter.SupportedModes.Contains(requestedMode) ||
            !adapter.SupportedOperations.Contains(requestedOperation))
        {
            return Result.Failure<ConnectorRunDto>(WmsErrors.Dependency(
                "connector.capability_not_registered",
                "The registered connector adapter does not support this operation and mode.",
                isRetryable: false));
        }

        var cursor = NormalizeCursor(request.Cursor) ?? NormalizeCursor(entity.Cursor);
        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? ConnectorIdentity.BuildIdempotencyKey(entity.Id, requestedOperation, requestedMode, cursor)
            : request.IdempotencyKey.Trim();
        var existing = await context.ConnectorRuns.SingleOrDefaultAsync(
            run => run.ConnectorInstanceId == entity.Id && run.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            return Result.Success(ToDto(existing));
        }

        var now = clock.UtcNow;
        var runEntity = new WmsConnectorRunEntity
        {
            ConnectorInstanceId = entity.Id,
            Operation = requestedOperation,
            Mode = requestedMode,
            Status = WmsConnectorRunStatuses.Running,
            IdempotencyKey = idempotencyKey,
            CursorBefore = cursor,
            CorrelationId = WmsExecutionIdentifiers.Normalize(requestContext.CorrelationId),
            AttemptCount = 1,
            CreatedAtUtc = now,
            StartedAtUtc = now
        };
        context.ConnectorRuns.Add(runEntity);
        entity.LastRunAtUtc = now;
        entity.UpdatedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken);

        try
        {
            var connectorContext = ToContext(entity) with
            {
                Cursor = cursor,
                CorrelationId = runEntity.CorrelationId
            };
            if (string.Equals(requestedMode, WmsConnectorModes.Push, StringComparison.OrdinalIgnoreCase))
            {
                var pushed = await adapter.PushAsync(
                    new ConnectorPushRequest(
                        connectorContext,
                        requestedOperation,
                        runEntity.CorrelationId,
                        request.BatchSize),
                    cancellationToken);
                runEntity.RecordsSeen = pushed.RecordsSent;
                runEntity.RecordsCreated = pushed.RecordsSent;
                runEntity.Status = WmsConnectorRunStatuses.Succeeded;
                runEntity.CursorAfter = cursor;
            }
            else
            {
                var pulled = await adapter.PullAsync(
                    new ConnectorPullRequest(
                        connectorContext,
                        requestedOperation,
                        cursor,
                        request.BatchSize,
                        runEntity.CorrelationId),
                    cancellationToken);
                await ApplyPulledRecordsAsync(entity, runEntity, pulled, cancellationToken);
                runEntity.CursorAfter = pulled.HasMore ? pulled.NextCursor : pulled.NextCursor ?? cursor;
                if (runEntity.Conflicts > 0)
                {
                    runEntity.Status = WmsConnectorRunStatuses.Failed;
                    runEntity.ErrorCode = "connector.mapping_conflict";
                    runEntity.ErrorMessage = "One or more external records conflicted with the connector mapping policy.";
                }
                else
                {
                    runEntity.Status = WmsConnectorRunStatuses.Succeeded;
                    entity.Cursor = runEntity.CursorAfter ?? string.Empty;
                }
            }

            runEntity.CompletedAtUtc = clock.UtcNow;
            if (runEntity.Status == WmsConnectorRunStatuses.Succeeded)
            {
                entity.HealthStatus = WmsConnectorHealthStatuses.Healthy;
                entity.HealthSummary = "The last connector synchronization completed successfully.";
                entity.LastSuccessfulRunAtUtc = runEntity.CompletedAtUtc;
                entity.ConsecutiveFailureCount = 0;
            }
            else
            {
                entity.HealthStatus = WmsConnectorHealthStatuses.Degraded;
                entity.HealthSummary = runEntity.ErrorMessage;
                entity.ConsecutiveFailureCount++;
            }

            entity.UpdatedAtUtc = clock.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(ToDto(runEntity));
        }
        catch (OperationCanceledException)
        {
            runEntity.Status = WmsConnectorRunStatuses.Canceled;
            runEntity.CompletedAtUtc = clock.UtcNow;
            await context.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Connector synchronization failed for {ConnectorId}", entity.Id);
            return await FailRunAsync(entity, runEntity, "connector.sync_failed", cancellationToken);
        }
    }

    private async Task ApplyPulledRecordsAsync(
        WmsConnectorInstanceEntity instance,
        WmsConnectorRunEntity run,
        ConnectorPullResult pulled,
        CancellationToken cancellationToken)
    {
        var mapping = await context.ConnectorMappingProfiles.AsNoTracking().SingleAsync(
            profile => profile.ConnectorType == instance.ConnectorType &&
                       profile.Name == instance.MappingProfileName &&
                       profile.Version == instance.MappingProfileVersion,
            cancellationToken);
        var existing = await context.ConnectorExternalRecords
            .Where(record => record.ConnectorInstanceId == instance.Id)
            .ToDictionaryAsync(record => record.ExternalKey, StringComparer.Ordinal, cancellationToken);
        var allowedWarehouseIds = DeserializeInts(instance.AllowedWarehouseIdsJson).ToHashSet();
        var policy = mapping.ConflictPolicy;
        foreach (var record in pulled.Records.Take(MaximumBatchSize))
        {
            run.RecordsSeen++;
            if (string.IsNullOrWhiteSpace(record.RecordType) || string.IsNullOrWhiteSpace(record.ExternalId))
            {
                run.Conflicts++;
                continue;
            }

            if (record.WarehouseId.HasValue && !allowedWarehouseIds.Contains(record.WarehouseId.Value))
            {
                run.Conflicts++;
                continue;
            }

            var externalKey = ConnectorIdentity.BuildExternalKey(instance.Id, record.RecordType, record.ExternalId);
            var payloadHash = ComputePayloadHash(record.Fields);
            if (!existing.TryGetValue(externalKey, out var current))
            {
                current = new WmsConnectorExternalRecordEntity
                {
                    ConnectorInstanceId = instance.Id,
                    ExternalKey = externalKey,
                    RecordType = record.RecordType.Trim(),
                    ExternalId = record.ExternalId.Trim(),
                    WarehouseId = record.WarehouseId,
                    PayloadHash = payloadHash,
                    ExternalVersion = record.ExternalVersion,
                    Status = "Seen",
                    LastRunId = run.Id,
                    FirstSeenAtUtc = clock.UtcNow,
                    LastSeenAtUtc = clock.UtcNow
                };
                context.ConnectorExternalRecords.Add(current);
                existing[externalKey] = current;
                run.RecordsCreated++;
                continue;
            }

            current.LastSeenAtUtc = clock.UtcNow;
            current.LastRunId = run.Id;
            if (string.Equals(current.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                run.RecordsSkipped++;
                continue;
            }

            if (string.Equals(policy, WmsConnectorConflictPolicies.PreferExternal, StringComparison.Ordinal))
            {
                current.PayloadHash = payloadHash;
                current.ExternalVersion = record.ExternalVersion;
                current.Status = "Seen";
                run.RecordsUpdated++;
            }
            else if (string.Equals(policy, WmsConnectorConflictPolicies.PreferWms, StringComparison.Ordinal))
            {
                current.Status = "Conflict";
                run.RecordsSkipped++;
            }
            else
            {
                current.Status = "Conflict";
                run.Conflicts++;
            }
        }
    }

    private async Task<Result> AuthorizeWarehousesAsync(
        IEnumerable<int> warehouseIds,
        CancellationToken cancellationToken)
    {
        foreach (var warehouseId in warehouseIds.Distinct().Order())
        {
            var authorization = await warehouseAccessService.AuthorizeAsync(
                ConnectorPermission,
                warehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization;
            }
        }

        return Result.Success();
    }

    private async Task<Result<ConnectorRunDto>> FailRunAsync(
        WmsConnectorInstanceEntity instance,
        WmsConnectorRunEntity run,
        string errorCode,
        CancellationToken cancellationToken)
    {
        run.Status = WmsConnectorRunStatuses.Failed;
        run.ErrorCode = errorCode;
        run.ErrorMessage = "The connector operation failed; inspect connector health and retry after the dependency is available.";
        run.CompletedAtUtc = clock.UtcNow;
        instance.HealthStatus = WmsConnectorHealthStatuses.Unhealthy;
        instance.HealthSummary = run.ErrorMessage;
        instance.ConsecutiveFailureCount++;
        instance.UpdatedAtUtc = clock.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(run));
    }

    private IConnectorAdapter? FindAdapter(string connectorType) =>
        adapters.FirstOrDefault(adapter =>
            string.Equals(adapter.ConnectorType, connectorType, StringComparison.OrdinalIgnoreCase));

    private static ResultError? ValidateMappingProfile(ConnectorMappingProfileRequest request)
    {
        if (!WmsConnectorTypes.IsKnown(request.ConnectorType?.Trim() ?? string.Empty))
        {
            return WmsErrors.Validation("connector.type_invalid", "The connector type is not in the published contract catalog.");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200 || request.Version < 1)
        {
            return WmsErrors.Validation("connector.mapping_identity_invalid", "A mapping name and positive version are required.");
        }

        if (string.IsNullOrWhiteSpace(request.ExternalIdField) || request.ExternalIdField.Trim().Length > 200)
        {
            return WmsErrors.Validation("connector.external_id_field_invalid", "A bounded external identifier field is required.");
        }

        if (!WmsConnectorConflictPolicies.IsKnown(request.ConflictPolicy.Trim()))
        {
            return WmsErrors.Validation("connector.conflict_policy_invalid", "The mapping conflict policy is not supported.");
        }

        try
        {
            _ = CultureInfo.GetCultureInfo(request.CultureName.Trim());
        }
        catch (CultureNotFoundException)
        {
            return WmsErrors.Validation("connector.culture_invalid", "The mapping culture is not recognized.");
        }

        if (request.Rules is null || request.Rules.Count == 0 ||
            request.Rules.Any(rule => string.IsNullOrWhiteSpace(rule.ExternalField) || string.IsNullOrWhiteSpace(rule.WmsField)))
        {
            return WmsErrors.Validation("connector.mapping_rules_invalid", "At least one complete external-to-WMS mapping rule is required.");
        }

        return null;
    }

    private static ResultError? ValidateInstance(ConnectorInstanceCreateRequest request)
    {
        if (!WmsConnectorTypes.IsKnown(request.ConnectorType?.Trim() ?? string.Empty) ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
        {
            return WmsErrors.Validation("connector.instance_identity_invalid", "A supported connector type and bounded name are required.");
        }

        if (request.AllowedWarehouseIds is null || request.AllowedWarehouseIds.Count == 0 ||
            request.AllowedWarehouseIds.Any(id => id <= 0))
        {
            return WmsErrors.Validation("connector.warehouse_scope_required", "At least one positive allowed warehouse is required.");
        }

        if (request.Modes is null || request.Modes.Count == 0 ||
            request.Modes.Any(mode => !WmsConnectorModes.IsKnown(mode.Trim().ToLowerInvariant())))
        {
            return WmsErrors.Validation("connector.mode_invalid", "Every connector mode must be pull, push, webhook, or file.");
        }

        if (request.MappingProfileVersion < 1 || string.IsNullOrWhiteSpace(request.MappingProfileName))
        {
            return WmsErrors.Validation("connector.mapping_reference_invalid", "An active mapping profile reference is required.");
        }

        if (!string.IsNullOrWhiteSpace(request.Schedule) && request.Schedule.Trim().Length > 120)
        {
            return WmsErrors.Validation("connector.schedule_invalid", "A connector schedule is limited to 120 characters.");
        }

        try
        {
            _ = ConnectorSecurityPolicy.NormalizeCredentialReference(request.CredentialReference);
        }
        catch (ArgumentException exception)
        {
            return WmsErrors.Validation("connector.credential_reference_invalid", exception.Message);
        }

        return null;
    }

    private static ResultError? ValidateRun(ConnectorRunRequest request)
    {
        if (!WmsConnectorOperations.IsKnown(request.Operation?.Trim() ?? string.Empty))
        {
            return WmsErrors.Validation("connector.operation_invalid", "The connector operation is not supported.");
        }

        if (!WmsConnectorModes.IsKnown(request.Mode?.Trim() ?? string.Empty))
        {
            return WmsErrors.Validation("connector.mode_invalid", "The connector mode is not supported.");
        }

        if (request.BatchSize is < 1 or > MaximumBatchSize)
        {
            return WmsErrors.Validation("connector.batch_size_invalid", $"The connector batch size must be between 1 and {MaximumBatchSize}.");
        }

        return null;
    }

    private static string? NormalizeSchedule(string? schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule))
        {
            return null;
        }

        var value = schedule.Trim();
        return value.Length > 120
            ? throw new ArgumentException("A connector schedule is limited to 120 characters.", nameof(schedule))
            : value;
    }

    private static ConnectorInstanceContext ToContext(WmsConnectorInstanceEntity entity) =>
        new(
            entity.Id,
            entity.ConnectorType,
            entity.Name,
            DeserializeInts(entity.AllowedWarehouseIdsJson).ToHashSet(),
            entity.CredentialReference,
            entity.CredentialVersion,
            DeserializeStrings(entity.ModesJson).ToHashSet(StringComparer.OrdinalIgnoreCase),
            entity.MappingProfileName,
            entity.MappingProfileVersion,
            NormalizeCursor(entity.Cursor),
            string.Empty);

    private static ConnectorMappingProfileDto ToDto(WmsConnectorMappingProfileEntity entity) =>
        new(
            entity.Id,
            entity.ConnectorType,
            entity.Name,
            entity.Version,
            entity.ExternalIdField,
            ConnectorMappingEngine.DeserializeRules(entity.RulesJson),
            entity.CultureName,
            entity.ConflictPolicy,
            entity.IsActive,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

    private static ConnectorInstanceDto ToDto(WmsConnectorInstanceEntity entity) =>
        new(
            entity.Id,
            entity.ConnectorType,
            entity.Name,
            DeserializeInts(entity.AllowedWarehouseIdsJson),
            entity.CredentialReference,
            entity.CredentialVersion,
            DeserializeStrings(entity.ModesJson),
            entity.Schedule,
            entity.MappingProfileName,
            entity.MappingProfileVersion,
            entity.Status,
            entity.Cursor,
            entity.HealthStatus,
            entity.HealthSummary,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc,
            entity.LastRunAtUtc,
            entity.LastSuccessfulRunAtUtc,
            entity.LastHealthCheckAtUtc,
            entity.ConsecutiveFailureCount);

    private static ConnectorRunDto ToDto(WmsConnectorRunEntity entity) =>
        new(
            entity.Id,
            entity.ConnectorInstanceId,
            entity.Operation,
            entity.Mode,
            entity.Status,
            entity.IdempotencyKey,
            entity.CursorBefore,
            entity.CursorAfter,
            entity.AttemptCount,
            entity.RecordsSeen,
            entity.RecordsCreated,
            entity.RecordsUpdated,
            entity.RecordsSkipped,
            entity.Conflicts,
            entity.ErrorCode,
            entity.ErrorMessage,
            entity.CreatedAtUtc,
            entity.StartedAtUtc,
            entity.CompletedAtUtc);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static int[] DeserializeInts(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<int[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string[] DeserializeStrings(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? NormalizeCursor(string? cursor) =>
        string.IsNullOrWhiteSpace(cursor) ? null : cursor.Trim()[..Math.Min(cursor.Trim().Length, 1_000)];

    private static string ComputePayloadHash(IReadOnlyDictionary<string, string?> fields)
    {
        var canonical = fields
            .OrderBy(field => field.Key, StringComparer.Ordinal)
            .Select(field => $"{field.Key}={field.Value}")
            .ToArray();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", canonical)))).ToLowerInvariant();
    }

    private static string Limit(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}

public abstract class ReferenceConnectorAdapterBase : IConnectorAdapter
{
    public abstract string ConnectorType { get; }

    public IReadOnlySet<string> SupportedModes { get; } = new HashSet<string>(
        [WmsConnectorModes.Pull, WmsConnectorModes.Push, WmsConnectorModes.Webhook, WmsConnectorModes.File],
        StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> SupportedOperations { get; } = new HashSet<string>(
        [
            WmsConnectorOperations.MasterDataSync,
            WmsConnectorOperations.InboundOrders,
            WmsConnectorOperations.AdvanceShippingNotices,
            WmsConnectorOperations.OutboundOrders,
            WmsConnectorOperations.InventoryAvailability,
            WmsConnectorOperations.ShipmentConfirmation,
            WmsConnectorOperations.Returns,
            WmsConnectorOperations.CarrierLabels,
            WmsConnectorOperations.Tracking,
            WmsConnectorOperations.Acknowledgements
        ],
        StringComparer.OrdinalIgnoreCase);

    public Task<ConnectorHealthResult> TestConnectionAsync(
        ConnectorInstanceContext connector,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConnectorHealthResult(
            true,
            false,
            WmsConnectorHealthStatuses.Healthy,
            "Reference adapter contract is registered; no remote network call was made."));

    public Task<ConnectorPullResult> PullAsync(
        ConnectorPullRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConnectorPullResult(
            [],
            request.Cursor,
            false,
            "reference-adapter"));

    public Task<ConnectorPushResult> PushAsync(
        ConnectorPushRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConnectorPushResult(0, "reference-adapter"));
}

public sealed class GenericErpReferenceConnectorAdapter : ReferenceConnectorAdapterBase
{
    public override string ConnectorType => WmsConnectorTypes.GenericErpV1;
}

public sealed class EcommerceOrderReferenceConnectorAdapter : ReferenceConnectorAdapterBase
{
    public override string ConnectorType => WmsConnectorTypes.EcommerceOrdersV1;
}
