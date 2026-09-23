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
  timestamp and the exact UTF-8 payload bytes; verification rejects timestamps
  outside the five-minute default replay window. Requests carry stable event and
  delivery IDs so receivers can deduplicate at-least-once HTTP delivery.
- A typed HTTP transport is available only when explicitly enabled with exact
  destination hosts, schemes, and ports. It pins connections to checked public
  DNS answers, rejects private/link-local/metadata destinations, disables
  redirects and proxies, bounds request/response bytes, and honors bounded
  exponential backoff plus `Retry-After`.
- `GET /api/integrations/capabilities` distinguishes `Disabled`, `Configured`,
  and `Verified`. Verification means an active subscription has received a
  successful response; it does not claim partner-side business acceptance.
- `wms.integration-retries` dispatches both existing notification deliveries
  and the integration outbox. The checked-in default transport remains
  fail-closed and performs no network call; enabled configuration is validated
  at startup. See [Webhook delivery transport](WEBHOOK_DELIVERY.md) for the
  configuration and local qualification boundary.

## Event/version policy

Published names carry their major schema version, for example
`inventory.movement-recorded.v1`. Payloads are JSON snapshots with an explicit
event ID, aggregate type/key, timestamp, correlation ID, and SHA-256 payload
hash. Additive fields may be added within a version; incompatible changes
require a new event type/version and a compatibility adapter. Persisted payloads
are bounded to 1,000,000 characters, HTTP requests to 1,000,000 UTF-8 bytes, and
response reads to 8,000 bytes. Response bodies are discarded after bounded
reading and are not persisted.

## Remaining qualification gates

The transport foundation is locally qualified with a disposable HTTP receiver.
Remaining work includes full master-data/work lifecycle producer coverage,
atomic claim concurrency on PostgreSQL, process-crash/restart qualification,
manual retry and administration history screens, inbound connector handlers,
retention/archival, high-volume ordering, and provider/load/production migration
evidence. No external endpoint is called by the default local configuration;
live partner acceptance remains a separate deployment gate.

The PostgreSQL persistence boundary is qualified for the exactly-once
identities: outbox event IDs, inbox source/message claims, and webhook delivery
rows for an outbox/subscription pair each reject duplicates. The disposable
PostgreSQL 17 harness passed 53/53 on 2026-09-22 (port 55507) and cleaned its
test container. This proves database identity behavior only; transport,
concurrent claim/restart, handler coverage, load, and production-provider gates
remain open.
