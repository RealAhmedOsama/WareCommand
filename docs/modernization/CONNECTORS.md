# Connector architecture

WareCommand keeps connector capability contracts in `Wms.Application.Connectors`
and places adapters in `Wms.Infrastructure.Connectors`. Core business modules do
not reference ERP, commerce, marketplace, carrier, or vendor SDKs.

## Current foundation

- Connector types are versioned (`generic-erp.v1`, `ecommerce-orders.v1`,
  `marketplace.v1`, and `carrier.v1`). Operations cover master data, inbound and
  outbound documents, inventory availability, shipment confirmation, returns,
  carrier labels/tracking, and acknowledgements.
- Instances persist explicit warehouse scope, deployment-managed credential
  references, credential version, enabled pull/push/webhook/file modes, an
  optional schedule, mapping profile reference, cursor, health, and failure
  counters. A raw secret or token is rejected at the application boundary.
- Versioned mapping profiles provide an external-ID field, field transformations,
  code/value maps, culture, and reject/prefer-external/prefer-WMS conflict policy.
- Runs persist correlation, idempotency, cursor before/after, bounded batch size,
  record counters, conflict state, safe error codes, and timestamps. A repeated
  idempotency key returns the existing run instead of invoking an adapter again.
- External record identities are deterministic hashes of connector, record type,
  and external ID. Payload hashes detect retries and changes without storing raw
  provider payloads in connector state.
- Warehouse authorization is checked before connector creation, listing, secret
  rotation, health checks, and synchronization. Adapter failure is isolated in
  the connector run and health state.
- The generic ERP and e-commerce reference adapters are deterministic, no-network
  contract fixtures. They prove registration and boundary behavior; they do not
  claim live provider connectivity.

## Adapter rules

An adapter must implement `IConnectorAdapter`, declare its connector type,
supported modes, and supported operations, and use the supplied connector
context. It must not log credential references as secrets, raw payloads, or
authorization headers. Pull adapters return a bounded page and an opaque next
cursor; push adapters must be retry-safe and use the connector run idempotency
key when calling a remote system.

Inbound webhook/file adapters should first use the shared integration inbox and
bulk-exchange validation boundaries. Outbound events should use the shared
transactional outbox. Mapping a document into a WMS module remains an explicit
module handler and must not be hidden inside a vendor adapter.

## Remaining qualification

This issue is a committed architecture slice, not live integration closure.
XLSX/file attachment ingestion, background schedule registration, connector
administration screens/API, connection-test provider implementations, real HTTP/
SFTP/webhook transports, retry throttling, replay history, complete document
handlers, Shopify/WooCommerce/marketplace/carrier adapters, PostgreSQL concurrency
and provider/load tests, browser localization, and production credential rollout
remain separate gates. No network is called by the checked-in reference
adapters, and no production migration or deployment has been performed.

The PostgreSQL persistence boundary is qualified for connector mapping profile
identity, connector instance names, per-instance run idempotency, and
per-instance external-record identity. Later mapping versions and reuse across
separate connector instances are accepted; duplicates within each protected
scope are rejected. The disposable PostgreSQL 17 harness passed 55/55 on
2026-09-22 (port 55509) and cleaned its test container. Live transports,
schedule/handler wiring, concurrency/load, localization, and production
credential gates remain open.
