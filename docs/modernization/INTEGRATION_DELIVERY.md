# Transactional integration delivery

## Current boundary

WareCommand now has a provider-neutral integration delivery foundation:

- `WmsIntegrationOutbox` stores versioned event envelopes, payload hashes,
  correlation/causation identifiers, warehouse scope, leases, retry state, and
  dead-letter state.
- `WmsIntegrationInbox` deduplicates external messages by source plus external
  message ID and rejects reuse with a different payload hash.
- The inventory movement boundary enqueues movement and adjustment events in the
  same EF change set as the stock, movement, ledger, and audit mutations. The
  event writer intentionally does not call `SaveChangesAsync`; the owning
  command transaction decides whether both business data and the outbox commit.
- Webhook subscriptions store endpoint/filter/scope/status metadata and
  protected secrets. Creation returns the secret once; rotation keeps the
  previous protected secret and an explicit overlap deadline.
- Delivery claims and delivery rows are committed before
  `IWebhookDeliveryTransport` is called. HMAC-SHA256 signatures include a
  timestamp and payload; verification rejects timestamps outside the five-minute
  default replay window.
- `wms.integration-retries` dispatches both existing notification deliveries
  and the integration outbox. The checked-in default transport is fail-closed:
  it performs no network call until a reviewed provider adapter is configured.

## Event/version policy

Published names carry their major schema version, for example
`inventory.movement-recorded.v1`. Payloads are JSON snapshots with an explicit
event ID, aggregate type/key, timestamp, correlation ID, and SHA-256 payload
hash. Additive fields may be added within a version; incompatible changes
require a new event type/version and a compatibility adapter. Payloads are
bounded to 1,000,000 characters and response bodies to 8,000 characters.

## Remaining qualification gates

This issue is a committed foundation, not closure. Remaining work includes
full master-data/work lifecycle producer coverage, a real HTTP transport with
timeout and response-policy tests, atomic claim concurrency on PostgreSQL,
process-crash/restart qualification, manual retry and administration history
screens, inbound connector handlers, retention/archival, high-volume ordering,
and provider/load/production migration evidence. No external endpoint is called
by the default local configuration.
