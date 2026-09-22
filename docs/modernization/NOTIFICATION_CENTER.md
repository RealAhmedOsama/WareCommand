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
- Email and webhook delivery are adapter interfaces. The default host adapters
  fail closed and persist the missing-provider state; no external call is made
  inside an inventory or notification publish transaction.
- External delivery rows carry attempts, lease, retry time, bounded error text,
  and correlation ID. Dispatch is invoked by the existing integration-retry
  background job after commit.
- The disposable PostgreSQL harness passed 49/49 on 2026-09-22; it proves
  durable notification deduplication and recipient/channel uniqueness at the
  provider boundary.

## Remaining qualification

Business event producers still need to publish each low-stock, expiry, QC,
approval, count-variance, shipment, backup, and reconciliation event through
this boundary. SMTP/webhook provider adapters, digest aggregation, escalation
policies, notification-center UI, browser/handheld QA, high-volume PostgreSQL
load evidence, and live-provider configuration remain separate gates. The
generated migration must be applied through the normal deployment migration
process; it has not been applied to production.
