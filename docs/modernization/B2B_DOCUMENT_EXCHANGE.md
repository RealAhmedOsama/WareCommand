# B2B document exchange

WareCommand treats EDI as an optional adapter boundary. Normal WMS workflows
use canonical Application contracts and do not depend on X12, EDIFACT, SFTP, or a
customer-specific partner mapping.

## Current foundation

- Canonical versioned document types cover item/location master data, purchase
  orders, ASNs, receipts, sales/warehouse orders, inventory status, shipment
  confirmation, and returns.
- Trading-partner profiles persist standard, warehouse scope, enabled document
  types, mapping-profile version, acknowledgement requirement, and a
  deployment-managed credential reference. Raw passwords/tokens are rejected.
- Mapping profiles are versioned per canonical document type and standard. They
  contain external-to-canonical field rules without adding partner fields to the
  domain model.
- Canonical envelopes require message IDs, direction, transport mode, warehouse,
  interchange/group/document control numbers, version, line count, and a bounded
  canonical field map. The provider envelope is parsed before this boundary.
- The document store uses unique partner/control-number, partner/message-ID, and
  optional partner/idempotency keys. Payload hashes make retries safe without
  returning the canonical payload in UI DTOs.
- Invalid envelopes are persisted in `Quarantined` state with actionable field
  errors. Acknowledgements correlate to a document and use deterministic control
  numbers. Failed/quarantined documents have an explicit replay transition.
- `IB2bDocumentAdapter`, `IB2bDocumentHandler`, and `IB2bDocumentTransport` keep
  parsing, normal WMS command routing, and transport concerns separate. No direct
  database writes are exposed to a partner adapter.

## Remaining qualification

This is a canonical exchange/persistence slice, not partner certification.
X12 850/856/940/945/947 and EDIFACT serializers/parsers, SFTP/file-drop/API/
webhook transports, functional acknowledgements such as 997/CONTRL, complete
purchase-order/ASN/shipment/return command handlers, outbox/inbox wiring,
transport retry/resume, retention, golden partner samples, PostgreSQL/provider
qualification, browser/admin surfaces, and production credential rollout remain
explicit gates. No EDI network call or production migration has been performed.
