# Notification center boundary

The notification center stores one durable notification with recipient-specific
rows in `WmsNotifications`, `WmsNotificationRecipients`, and
`WmsNotificationPreferences`. Notification content is stored in English and
Arabic snapshots so delivery and inbox rendering do not depend on a later
resource change. The API returns only the current user's in-app rows and
rechecks notification permission and warehouse scope before read or
acknowledge operations. A deep link is navigation data, not an authorization
grant; the destination endpoint must authorize again.

## Implemented core

- Stable deduplication keys and optional cooldown keys prevent duplicate rows
  during repeated event evaluation.
- User, role, and warehouse audiences are resolved against active identity,
  role, permission, and warehouse-assignment data.
- In-app unread count, localized list, read, and acknowledge operations are
  available under `notifications.read`.
- User, role, and warehouse channel preferences support enablement, quiet
  periods, time zones, and digest intervals. Mandatory security, backup, and
  integrity kinds cannot be disabled.
- Email uses the same configured SMTP transport as password resets. SMTP
  configuration is disabled by default in the production example, requires
  STARTTLS outside the explicit Development numeric-loopback sink, and reads the
  password only from `Authentication:Smtp:Password`. Mail is plain text with
  UTF-8 encoding and locale-selected English or Arabic content.
- Notification webhooks enqueue `notification.published.v1` into the existing
  integration outbox. Delivery runs through #121 with stable event/delivery
  IDs, and only subscriptions that explicitly list the notification's
  warehouse can receive it. Global subscriptions are not eligible for these
  events. The payload contains notification content and warehouse scope, never
  recipient IDs or email addresses.
- `Delivered` means the in-app row is available. Email and webhook rows move
  through `Pending`, `Failed`, `Queued`, `TransportAccepted`, `Disabled`, or
  `DeadLettered`. `TransportAccepted` means the SMTP server or webhook receiver
  accepted the request; it does not prove mailbox delivery or partner-side
  processing. Retryable notification delivery stops after ten attempts.
- Before an external send, dispatch rechecks the user's active account,
  warehouse assignment, required permission, current email address, and latest
  channel preference. A disabled/ineligible user or opt-out is suppressed with
  a safe error code. Exception text, addresses, credentials, and message bodies
  are not placed in notification error fields or delivery logs.
- `GET /api/notifications/capabilities` is limited to `settings.manage` and
  returns aggregate channel readiness and delivery counts without recipient or
  warehouse details. `Disabled`, `Configured`, and `Verified` describe channel
  configuration and transport evidence. Email verification is an SMTP
  acceptance during the current process; webhook verification requires a
  previously successful response from an active, explicitly scoped endpoint.
- External delivery rows carry attempts, lease, retry time, safe error codes,
  and correlation ID. Notification dispatch is invoked by the existing
  integration-retry background job after commit.
- The disposable PostgreSQL harness passed 49/49 on 2026-09-22; it proves
  durable notification deduplication and recipient/channel uniqueness at the
  provider boundary.

## Remaining qualification

Business event producers still need to publish each low-stock, expiry, QC,
approval, count-variance, shipment, backup, and reconciliation event through
this boundary. Digest aggregation, escalation policies, notification-center
UI, browser/handheld QA, high-volume PostgreSQL load evidence, and live-provider
configuration remain separate gates. The generated migration must be applied
through the normal deployment migration process; it has not been applied to
production.

## Local channel qualification

The infrastructure tests run an in-process SMTP sink with plaintext allowed
only under a Development numeric-loopback configuration. They verify the
recipient, stable message headers, UTF-8 Arabic subject/body, transient 4xx and
permanent 5xx classification, timeout/cancellation behavior, and rejection of
plaintext SMTP outside Development. The integration tests enqueue notification
events through the durable outbox and deliver through a controlled HTTP receiver
using the #121 transport. They verify the transition to `TransportAccepted`,
restart-safe retry identity, and that global and cross-warehouse subscriptions
receive no notification event. Password-reset regression coverage verifies
that Identity uses the same email transport. These local tests do not make
real outbound email or webhook calls.

On 2026-09-23, the focused infrastructure command passed 24/24:
`dotnet test Wms.Infrastructure.Tests/Wms.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~NotificationServiceTests|FullyQualifiedName~SmtpEmailTransportTests|FullyQualifiedName~IntegrationDeliveryTests"`.
The ASP password-reset and authorized channel-capability checks passed 2/2:
`dotnet test Wms.ASP.Tests/Wms.ASP.Tests.csproj --no-restore --filter "FullyQualifiedName~ModuleWiringFlowTests|FullyQualifiedName~AccountNotificationTests"`.
