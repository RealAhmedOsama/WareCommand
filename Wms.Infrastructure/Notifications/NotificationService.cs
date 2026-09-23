using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Jobs;
using Wms.Application.Localization;
using Wms.Application.Notifications;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Notifications;

public sealed class NotificationService(
    WmsDbContext context,
    INotificationRecipientDirectory recipientDirectory,
    IWarehouseAccessService warehouseAccessService,
    ICurrentUser currentUser,
    IAuditWriter auditWriter,
    IClock clock,
    ILogger<NotificationService> logger) : INotificationService
{
    private const int MaximumPageSize = 200;

    public async Task<Result<NotificationPublishResult>> PublishAsync(
        NotificationPublishInput input,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidatePublishInput(input, clock.UtcNow);
        if (validation is not null)
        {
            return Result.Failure<NotificationPublishResult>(validation);
        }

        var nowUtc = clock.UtcNow;
        var deduplicationKey = input.DeduplicationKey!.Trim();
        var existing = await context.Notifications
            .AsNoTracking()
            .Include(notification => notification.Recipients)
            .SingleOrDefaultAsync(
                notification => notification.DeduplicationKey == deduplicationKey,
                cancellationToken);
        if (existing is not null)
        {
            return Result.Success(ToPublishResult(existing, wasDeduplicated: true));
        }

        var cooldownKey = NormalizeOptional(input.CooldownKey);
        if (cooldownKey is not null && input.Cooldown.HasValue)
        {
            var cooldownCutoff = nowUtc - input.Cooldown.Value;
            var cooldownMatch = (await context.Notifications
                .AsNoTracking()
                .Include(notification => notification.Recipients)
                .Where(notification => notification.CooldownKey == cooldownKey)
                .ToListAsync(cancellationToken))
                .Where(notification => notification.CreatedAtUtc >= cooldownCutoff)
                .OrderByDescending(notification => notification.CreatedAtUtc)
                .FirstOrDefault();
            if (cooldownMatch is not null)
            {
                return Result.Success(ToPublishResult(cooldownMatch, wasDeduplicated: true));
            }
        }

        var mandatory = input.Mandatory || IsMandatoryKind(input.Kind);
        var recipients = await recipientDirectory.ResolveAsync(
            input.Audience,
            input.RequiredPermission,
            cancellationToken);
        if (recipients.Count == 0)
        {
            return Result.Failure<NotificationPublishResult>(WmsErrors.Dependency(
                "notifications.no_authorized_recipients",
                "No active authorized recipients matched the notification audience.",
                isRetryable: false));
        }

        var channels = (input.Channels is null || input.Channels.Count == 0
                ? [NotificationChannel.InApp]
                : input.Channels)
            .Distinct()
            .ToArray();
        var userIds = recipients.Select(recipient => recipient.UserId).ToArray();
        var roleNames = recipients
            .SelectMany(recipient => recipient.Roles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var preferences = await context.NotificationPreferences
            .AsNoTracking()
            .Where(preference =>
                (preference.UserId != null && userIds.Contains(preference.UserId)) ||
                (preference.RoleName != null && roleNames.Contains(preference.RoleName)) ||
                (input.Audience.WarehouseId.HasValue &&
                 preference.WarehouseId == input.Audience.WarehouseId.Value))
            .ToListAsync(cancellationToken);

        var entity = new WmsNotificationEntity
        {
            DeduplicationKey = deduplicationKey,
            CooldownKey = cooldownKey,
            Kind = input.Kind.Trim(),
            Severity = input.Severity,
            TitleEn = input.TitleEn.Trim(),
            TitleAr = input.TitleAr.Trim(),
            MessageEn = input.MessageEn.Trim(),
            MessageAr = input.MessageAr.Trim(),
            SourceType = NormalizeOptional(input.SourceType),
            SourceId = NormalizeOptional(input.SourceId),
            SourceReference = NormalizeOptional(input.SourceReference),
            RequiredPermission = NormalizeOptional(input.RequiredPermission),
            DeepLink = NormalizeOptional(input.DeepLink),
            WarehouseId = input.Audience.WarehouseId,
            Mandatory = mandatory,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = input.ExpiresAtUtc?.ToUniversalTime(),
            CorrelationId = NormalizeOptional(input.CorrelationId) ?? WmsExecutionIdentifiers.NewCorrelationId()
        };

        var activeRecipientCount = 0;
        foreach (var recipient in recipients)
        {
            foreach (var channel in channels)
            {
                var decision = NotificationPreferencePolicy.Resolve(
                    preferences,
                    recipient,
                    input.Audience.WarehouseId,
                    entity.Kind,
                    channel,
                    nowUtc,
                    mandatory);
                var isActive = decision.IsEnabled;
                if (isActive)
                {
                    activeRecipientCount++;
                }

                entity.Recipients.Add(new WmsNotificationRecipientEntity
                {
                    RecipientUserId = recipient.UserId,
                    RecipientEmail = recipient.Email,
                    Channel = channel,
                    DeliveryStatus = isActive
                        ? channel == NotificationChannel.InApp
                            ? NotificationDeliveryStatus.Delivered
                            : NotificationDeliveryStatus.Pending
                        : NotificationDeliveryStatus.Suppressed,
                    NextAttemptAtUtc = isActive && channel != NotificationChannel.InApp
                        ? decision.NextAttemptAtUtc
                        : null,
                    CreatedAtUtc = nowUtc
                });
            }
        }

        context.Notifications.Add(entity);
        await auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.NotificationPublished,
                WmsAuditEntityTypes.Notification,
                deduplicationKey,
                entity.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["kind"] = entity.Kind,
                    ["severity"] = entity.Severity.ToString(),
                    ["mandatory"] = entity.Mandatory,
                    ["recipientCount"] = entity.Recipients.Count,
                    ["activeRecipientCount"] = activeRecipientCount,
                    ["channels"] = channels.Select(channel => channel.ToString()).ToArray()
                },
                ActorUserId: currentUser.UserId,
                ActorUserName: currentUser.UserName,
                Details: "Durable notification published with recipient-specific delivery state."),
            cancellationToken);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(
                exception,
                "Notification publish conflicted for deduplication key {DeduplicationKey}",
                deduplicationKey);
            return Result.Failure<NotificationPublishResult>(WmsErrors.Conflict(
                "notifications.deduplication_conflict",
                "The notification was already published by another request."));
        }

        return Result.Success(new NotificationPublishResult(
            entity.Id,
            entity.Recipients
                .Select(recipient => recipient.RecipientUserId)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            WasDeduplicated: false,
            WasSuppressed: activeRecipientCount == 0));
    }

    public async Task<Result<NotificationPageDto>> ListAsync(
        NotificationQuery query,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidatePage(query.Page, query.PageSize);
        if (validation is not null)
        {
            return Result.Failure<NotificationPageDto>(validation);
        }

        var authorization = await AuthorizeReadAsync(query.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<NotificationPageDto>();
        }

        var userId = GetCurrentUserId();
        var nowUtc = clock.UtcNow;
        var recipients = context.NotificationRecipients
            .AsNoTracking()
            .Include(recipient => recipient.Notification)
            .Where(recipient =>
                recipient.RecipientUserId == userId &&
                recipient.Channel == NotificationChannel.InApp &&
                recipient.DeliveryStatus != NotificationDeliveryStatus.Suppressed);
        if (!string.IsNullOrWhiteSpace(query.Kind))
        {
            var kind = query.Kind.Trim();
            recipients = recipients.Where(recipient => recipient.Notification.Kind == kind);
        }

        if (query.Severity.HasValue)
        {
            recipients = recipients.Where(recipient => recipient.Notification.Severity == query.Severity.Value);
        }

        if (query.WarehouseId.HasValue)
        {
            recipients = recipients.Where(recipient =>
                recipient.Notification.WarehouseId == query.WarehouseId.Value);
        }

        if (query.UnreadOnly)
        {
            recipients = recipients.Where(recipient => recipient.ReadAtUtc == null);
        }

        int totalCount;
        int unreadCount;
        IReadOnlyList<WmsNotificationRecipientEntity> rows;
        if (UsesPortableDateComparison())
        {
            var materialized = await recipients.ToListAsync(cancellationToken);
            if (!query.IncludeExpired)
            {
                materialized = materialized
                    .Where(recipient =>
                        recipient.Notification.ExpiresAtUtc == null ||
                        recipient.Notification.ExpiresAtUtc > nowUtc)
                    .ToList();
            }

            totalCount = materialized.Count;
            unreadCount = materialized.Count(recipient => recipient.ReadAtUtc is null);
            rows = materialized
                .OrderByDescending(recipient => recipient.Notification.CreatedAtUtc)
                .ThenByDescending(recipient => recipient.Id)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToList();
        }
        else
        {
            if (!query.IncludeExpired)
            {
                recipients = recipients.Where(recipient =>
                    recipient.Notification.ExpiresAtUtc == null ||
                    recipient.Notification.ExpiresAtUtc > nowUtc);
            }

            totalCount = await recipients.CountAsync(cancellationToken);
            unreadCount = await recipients
                .Where(recipient => recipient.ReadAtUtc == null)
                .CountAsync(cancellationToken);
            rows = await recipients
                .OrderByDescending(recipient => recipient.Notification.CreatedAtUtc)
                .ThenByDescending(recipient => recipient.Id)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(cancellationToken);
        }
        var locale = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.Locale)
            .SingleOrDefaultAsync(cancellationToken) ?? WmsLocaleCatalog.DefaultLocale;
        var isArabic = string.Equals(
            WmsLocaleCatalog.Normalize(locale),
            WmsLocaleCatalog.Arabic,
            StringComparison.OrdinalIgnoreCase);

        return Result.Success(new NotificationPageDto(
            rows.Select(row => ToDto(row, isArabic)).ToList(),
            query.Page,
            query.PageSize,
            totalCount,
            unreadCount));
    }

    public async Task<Result<int>> GetUnreadCountAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeReadAsync(warehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<int>();
        }

        var userId = GetCurrentUserId();
        var nowUtc = clock.UtcNow;
        var recipients = context.NotificationRecipients
            .AsNoTracking()
            .Where(recipient =>
                recipient.RecipientUserId == userId &&
                recipient.Channel == NotificationChannel.InApp &&
                recipient.ReadAtUtc == null &&
                recipient.DeliveryStatus != NotificationDeliveryStatus.Suppressed &&
                (warehouseId == null || recipient.Notification.WarehouseId == warehouseId));
        var count = UsesPortableDateComparison()
            ? (await recipients
                    .Include(recipient => recipient.Notification)
                    .ToListAsync(cancellationToken))
                .Count(recipient =>
                    recipient.Notification.ExpiresAtUtc == null ||
                    recipient.Notification.ExpiresAtUtc > nowUtc)
            : await recipients
                .CountAsync(
                    recipient =>
                        recipient.Notification.ExpiresAtUtc == null ||
                        recipient.Notification.ExpiresAtUtc > nowUtc,
                    cancellationToken);
        return Result.Success(count);
    }

    public async Task<Result> MarkReadAsync(
        long recipientId,
        CancellationToken cancellationToken = default) =>
        await UpdateRecipientAsync(recipientId, acknowledge: false, cancellationToken);

    public async Task<Result> AcknowledgeAsync(
        long recipientId,
        CancellationToken cancellationToken = default) =>
        await UpdateRecipientAsync(recipientId, acknowledge: true, cancellationToken);

    public async Task<Result<IReadOnlyList<NotificationPreferenceDto>>> ListPreferencesAsync(
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeReadAsync(null, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<NotificationPreferenceDto>>();
        }

        var userId = GetCurrentUserId();
        var warehouseIds = await context.UserWarehouseAssignments
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId && assignment.Warehouse.IsActive)
            .Select(assignment => assignment.WarehouseId)
            .ToListAsync(cancellationToken);
        var roleNames = await GetRoleNamesAsync(userId, cancellationToken);
        var rows = await context.NotificationPreferences
            .AsNoTracking()
            .Where(preference =>
                preference.UserId == userId ||
                (preference.RoleName != null && roleNames.Contains(preference.RoleName)) ||
                (preference.WarehouseId.HasValue && warehouseIds.Contains(preference.WarehouseId.Value)))
            .OrderBy(preference => preference.Scope)
            .ThenBy(preference => preference.Kind)
            .ThenBy(preference => preference.Channel)
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<NotificationPreferenceDto>>(
            rows.Select(ToPreferenceDto).ToList());
    }

    public async Task<Result<NotificationPreferenceDto>> SavePreferenceAsync(
        NotificationPreferenceInput input,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidatePreference(input);
        if (validation is not null)
        {
            return Result.Failure<NotificationPreferenceDto>(validation);
        }

        var userId = GetCurrentUserId();
        var isUserScope = input.Scope == NotificationPreferenceScope.User;
        var authorization = isUserScope
            ? await AuthorizeReadAsync(null, cancellationToken)
            : await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.NotificationsManage,
                input.WarehouseId,
                cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<NotificationPreferenceDto>();
        }

        if (!input.IsEnabled && IsMandatoryKind(input.Kind))
        {
            return Result.Failure<NotificationPreferenceDto>(WmsErrors.BusinessRule(
                "notifications.mandatory_cannot_disable",
                "Mandatory security, backup, and integrity alerts cannot be disabled."));
        }

        var normalizedRole = NormalizeOptional(input.RoleName);
        var normalizedKind = NormalizeOptional(input.Kind);
        var existing = await context.NotificationPreferences
            .SingleOrDefaultAsync(preference =>
                preference.Scope == input.Scope &&
                preference.UserId == (isUserScope ? userId : null) &&
                preference.RoleName == (input.Scope == NotificationPreferenceScope.Role ? normalizedRole : null) &&
                preference.WarehouseId == (input.Scope == NotificationPreferenceScope.Warehouse
                    ? input.WarehouseId
                    : null) &&
                preference.Kind == normalizedKind &&
                preference.Channel == input.Channel,
                cancellationToken);
        existing ??= new WmsNotificationPreferenceEntity
        {
            Scope = input.Scope,
            UserId = isUserScope ? userId : null,
            RoleName = input.Scope == NotificationPreferenceScope.Role ? normalizedRole : null,
            WarehouseId = input.Scope == NotificationPreferenceScope.Warehouse ? input.WarehouseId : null,
            Kind = normalizedKind,
            Channel = input.Channel
        };
        existing.IsEnabled = input.IsEnabled;
        existing.QuietStartMinute = input.QuietStartMinute;
        existing.QuietEndMinute = input.QuietEndMinute;
        existing.TimeZone = string.IsNullOrWhiteSpace(input.TimeZone)
            ? "UTC"
            : input.TimeZone!.Trim();
        existing.DigestMinutes = input.DigestMinutes;
        existing.UpdatedAtUtc = clock.UtcNow;
        if (existing.Id == 0)
        {
            context.NotificationPreferences.Add(existing);
        }

        await auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.NotificationPreferenceChanged,
                WmsAuditEntityTypes.NotificationPreference,
                existing.Id == 0 ? null : existing.Id.ToString(CultureInfo.InvariantCulture),
                existing.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["scope"] = existing.Scope.ToString(),
                    ["roleName"] = existing.RoleName,
                    ["warehouseId"] = existing.WarehouseId,
                    ["kind"] = existing.Kind,
                    ["channel"] = existing.Channel.ToString(),
                    ["isEnabled"] = existing.IsEnabled,
                    ["digestMinutes"] = existing.DigestMinutes
                },
                ActorUserId: userId,
                Details: "Notification delivery preference changed."),
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success(ToPreferenceDto(existing));
    }

    private async Task<Result> UpdateRecipientAsync(
        long recipientId,
        bool acknowledge,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeReadAsync(null, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization;
        }

        var userId = GetCurrentUserId();
        var recipient = await context.NotificationRecipients
            .Include(item => item.Notification)
            .SingleOrDefaultAsync(item =>
                item.Id == recipientId &&
                item.RecipientUserId == userId &&
                item.Channel == NotificationChannel.InApp,
                cancellationToken);
        if (recipient is null || recipient.DeliveryStatus == NotificationDeliveryStatus.Suppressed)
        {
            return Result.Failure(WmsErrors.NotFound(
                "notifications.recipient_not_found",
                "The notification recipient record was not found."));
        }

        var scopedAuthorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.NotificationsRead,
            recipient.Notification.WarehouseId,
            cancellationToken);
        if (scopedAuthorization.IsFailure)
        {
            return Result.Failure(scopedAuthorization.Errors);
        }

        var nowUtc = clock.UtcNow;
        recipient.ReadAtUtc ??= nowUtc;
        if (acknowledge)
        {
            recipient.AcknowledgedAtUtc ??= nowUtc;
        }

        await auditWriter.RecordAsync(
            new AuditRecord(
                acknowledge
                    ? WmsAuditActions.NotificationAcknowledged
                    : WmsAuditActions.NotificationRead,
                WmsAuditEntityTypes.Notification,
                recipient.NotificationId.ToString(CultureInfo.InvariantCulture),
                recipient.Notification.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["recipientId"] = recipient.Id,
                    ["acknowledged"] = acknowledge
                },
                ActorUserId: userId,
                Details: acknowledge
                    ? "Notification acknowledged by the authorized recipient."
                    : "Notification marked read by the authorized recipient."),
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Result> AuthorizeReadAsync(
        int? warehouseId,
        CancellationToken cancellationToken)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.NotificationsRead,
            warehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization;
        }

        return string.IsNullOrWhiteSpace(GetCurrentUserIdOrNull())
            ? Result.Failure(WmsErrors.Unauthorized(
                "notifications.authentication_required",
                "An authenticated user is required to access notifications."))
            : Result.Success();
    }

    private string GetCurrentUserId() =>
        GetCurrentUserIdOrNull() ?? throw new InvalidOperationException(
            "An authenticated user is required for notification access.");

    private string? GetCurrentUserIdOrNull() =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
            ? currentUser.UserId
            : null;

    private async Task<IReadOnlySet<string>> GetRoleNamesAsync(
        string userId,
        CancellationToken cancellationToken) =>
        new HashSet<string>(
            await (
                    from userRole in context.UserRoles.AsNoTracking()
                    join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                    where userRole.UserId == userId && role.Name != null
                    select role.Name!)
                .Distinct()
                .ToListAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);

    private static NotificationDto ToDto(
        WmsNotificationRecipientEntity recipient,
        bool isArabic) =>
        new(
            recipient.Id,
            recipient.NotificationId,
            recipient.Notification.Kind,
            recipient.Notification.Severity,
            isArabic ? recipient.Notification.TitleAr : recipient.Notification.TitleEn,
            isArabic ? recipient.Notification.MessageAr : recipient.Notification.MessageEn,
            recipient.Notification.SourceType,
            recipient.Notification.SourceId,
            recipient.Notification.SourceReference,
            recipient.Notification.DeepLink,
            recipient.Notification.WarehouseId,
            recipient.Notification.Mandatory,
            recipient.Notification.CreatedAtUtc,
            recipient.Notification.ExpiresAtUtc,
            recipient.ReadAtUtc,
            recipient.AcknowledgedAtUtc,
            recipient.Channel,
            recipient.DeliveryStatus,
            recipient.Notification.CorrelationId);

    private static NotificationPreferenceDto ToPreferenceDto(
        WmsNotificationPreferenceEntity preference) =>
        new(
            preference.Id,
            preference.Scope,
            preference.RoleName,
            preference.WarehouseId,
            preference.Kind,
            preference.Channel,
            preference.IsEnabled,
            preference.QuietStartMinute,
            preference.QuietEndMinute,
            preference.TimeZone,
            preference.DigestMinutes,
            preference.UpdatedAtUtc);

    private static NotificationPublishResult ToPublishResult(
        WmsNotificationEntity entity,
        bool wasDeduplicated) =>
        new(
            entity.Id,
            entity.Recipients
                .Select(recipient => recipient.RecipientUserId)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            wasDeduplicated,
            entity.Recipients.All(recipient =>
                recipient.DeliveryStatus == NotificationDeliveryStatus.Suppressed));

    private static ResultError? ValidatePublishInput(
        NotificationPublishInput input,
        DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(input.Kind) || input.Kind.Trim().Length > 100)
        {
            return WmsErrors.Validation("notifications.kind_invalid", "Notification kind is required and must be at most 100 characters.");
        }

        if (string.IsNullOrWhiteSpace(input.TitleEn) || input.TitleEn.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(input.TitleAr) || input.TitleAr.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(input.MessageEn) || input.MessageEn.Trim().Length > 2_000 ||
            string.IsNullOrWhiteSpace(input.MessageAr) || input.MessageAr.Trim().Length > 2_000)
        {
            return WmsErrors.Validation("notifications.content_invalid", "English and Arabic notification title and message values are required and bounded.");
        }

        if (input.Audience is null ||
            ((input.Audience.UserIds is null || input.Audience.UserIds.Count == 0) &&
             (input.Audience.Roles is null || input.Audience.Roles.Count == 0) &&
             !input.Audience.WarehouseId.HasValue))
        {
            return WmsErrors.Validation("notifications.audience_required", "At least one user, role, or warehouse audience is required.");
        }

        if (input.Audience.WarehouseId is <= 0)
        {
            return WmsErrors.Validation("notifications.warehouse_invalid", "The notification warehouse must be a positive identifier.");
        }

        if (!Enum.IsDefined(input.Severity))
        {
            return WmsErrors.Validation("notifications.severity_invalid", "The notification severity is not recognized.");
        }

        if (input.Channels?.Any(channel => !Enum.IsDefined(channel)) == true)
        {
            return WmsErrors.Validation("notifications.channel_invalid", "One or more notification channels are not recognized.");
        }

        if (!string.IsNullOrWhiteSpace(input.RequiredPermission) &&
            !WmsPermissions.IsKnown(input.RequiredPermission.Trim()))
        {
            return WmsErrors.Validation("notifications.permission_invalid", "The notification permission is not recognized.");
        }

        if (string.IsNullOrWhiteSpace(input.DeduplicationKey) || input.DeduplicationKey.Trim().Length > 250)
        {
            return WmsErrors.Validation("notifications.deduplication_key_required", "A stable notification deduplication key is required and must be at most 250 characters.");
        }

        if (input.Cooldown.HasValue &&
            (input.Cooldown.Value <= TimeSpan.Zero || input.Cooldown.Value > TimeSpan.FromDays(7)))
        {
            return WmsErrors.Validation("notifications.cooldown_invalid", "Notification cooldown must be between one second and seven days.");
        }

        if (input.ExpiresAtUtc.HasValue && input.ExpiresAtUtc.Value <= nowUtc)
        {
            return WmsErrors.Validation("notifications.expiry_invalid", "Notification expiry must be in the future.");
        }

        return null;
    }

    private static ResultError? ValidatePreference(NotificationPreferenceInput input)
    {
        if (!Enum.IsDefined(input.Scope) ||
            !Enum.IsDefined(input.Channel))
        {
            return WmsErrors.Validation("notifications.preference_scope_invalid", "The notification preference scope or channel is not recognized.");
        }

        if (input.Scope == NotificationPreferenceScope.User &&
            (!string.IsNullOrWhiteSpace(input.RoleName) || input.WarehouseId.HasValue))
        {
            return WmsErrors.Validation("notifications.preference_scope_values_invalid", "User preferences cannot include role or warehouse scope values.");
        }

        if (input.Scope == NotificationPreferenceScope.Role && string.IsNullOrWhiteSpace(input.RoleName))
        {
            return WmsErrors.Validation("notifications.preference_role_required", "A role is required for a role-scoped preference.");
        }

        if (input.Scope == NotificationPreferenceScope.Role &&
            !WmsRoleNames.Catalog.Contains(input.RoleName!.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return WmsErrors.Validation("notifications.preference_role_invalid", "The selected role is not recognized.");
        }

        if (input.Scope == NotificationPreferenceScope.Role && input.WarehouseId.HasValue)
        {
            return WmsErrors.Validation("notifications.preference_scope_values_invalid", "Role preferences cannot include a warehouse scope value.");
        }

        if (input.Scope == NotificationPreferenceScope.Warehouse && input.WarehouseId is <= 0)
        {
            return WmsErrors.Validation("notifications.preference_warehouse_required", "A valid warehouse is required for a warehouse-scoped preference.");
        }

        if (input.Scope == NotificationPreferenceScope.Warehouse && !string.IsNullOrWhiteSpace(input.RoleName))
        {
            return WmsErrors.Validation("notifications.preference_scope_values_invalid", "Warehouse preferences cannot include a role scope value.");
        }

        if (input.QuietStartMinute.HasValue != input.QuietEndMinute.HasValue ||
            input.QuietStartMinute is < 0 or > 1_439 ||
            input.QuietEndMinute is < 0 or > 1_439)
        {
            return WmsErrors.Validation("notifications.quiet_period_invalid", "Quiet period values must be provided together as minutes from midnight between 0 and 1439.");
        }

        if (input.DigestMinutes is <= 0 or > 1_440)
        {
            return WmsErrors.Validation("notifications.digest_invalid", "Digest interval must be between one and 1440 minutes.");
        }

        if (!string.IsNullOrWhiteSpace(input.TimeZone))
        {
            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZone.Trim());
            }
            catch (TimeZoneNotFoundException)
            {
                return WmsErrors.Validation("notifications.timezone_invalid", "The notification time zone is not recognized.");
            }
            catch (InvalidTimeZoneException)
            {
                return WmsErrors.Validation("notifications.timezone_invalid", "The notification time zone is invalid.");
            }
        }

        return null;
    }

    private static ResultError? ValidatePage(int page, int pageSize) =>
        page < 1 || pageSize is < 1 or > MaximumPageSize
            ? WmsErrors.Validation("notifications.page_invalid", $"Page must be positive and page size must be between 1 and {MaximumPageSize}.")
            : null;

    private static bool IsMandatoryKind(string? kind) =>
        !string.IsNullOrWhiteSpace(kind) &&
        (kind.StartsWith("security.", StringComparison.OrdinalIgnoreCase) ||
         kind.StartsWith("backup.", StringComparison.OrdinalIgnoreCase) ||
         kind.StartsWith("integrity.", StringComparison.OrdinalIgnoreCase) ||
         kind.Contains("reconciliation.failure", StringComparison.OrdinalIgnoreCase));

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private bool UsesPortableDateComparison() =>
        context.Database.ProviderName?.Contains(
            "Npgsql",
            StringComparison.OrdinalIgnoreCase) != true;

}
