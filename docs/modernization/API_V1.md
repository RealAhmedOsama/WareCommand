# Versioned REST API foundation

Issue #88 now has a bounded `/api/v1` contract foundation. The published query surface is intentionally small and explicit while the remaining integration work is still tracked as open.

## Published contract

The authoritative OpenAPI document is served at [`/api/v1/openapi.json`](/api/v1/openapi.json) and is also checked in at `Wms.ASP/wwwroot/api/v1/openapi.json`. It is OpenAPI 3.1 JSON with operation IDs, paging limits, permission requirements, response schemas, and Problem Details responses.

The current query operations are:

- `GET /api/v1/warehouses`
- `GET /api/v1/items`
- `GET /api/v1/locations`
- `GET /api/v1/inventory/stock`
- `GET /api/v1/reports/movements`

Each operation has a bounded `page`/`pageSize` contract. The infrastructure services apply warehouse scope and the filters before paging; the API maps results into Application-owned API records rather than returning MVC view models, EF entities, or persistence-only fields.

## Boundary rules

- All versioned resources require a bearer API-client credential and the existing permission policy for that resource. Human WareCommand session cookies are deliberately not accepted on this surface.
- The credential format is `Authorization: Bearer <clientId>.<one-time-secret>`. The secret is shown only by the administrator create/rotate response and is stored as a PBKDF2 hash.
- The API rate-limit policy applies to every versioned endpoint.
- `X-Correlation-ID`, `X-Operation-ID`, `X-Reference-ID`, and `Idempotency-Key` remain host-level propagation headers. Query operations are side-effect free; mutating commands will only be published after the idempotency and credential boundaries are complete.
- Failures use `application/problem+json` with a stable `errorCode`, retryability, and optional field errors. Correlation references remain in the response headers and unexpected failures stay provider-safe.
- The version metadata endpoint is anonymous and contains no tenant, warehouse, user, secret, or connection information.
- v1 is additive. A breaking contract change requires a new version, and deprecated operations must remain documented for at least one release cycle.

## Remaining acceptance gates

This is committed progress, not full issue closure. Remaining work includes machine-to-machine credentials and scopes (#89), idempotent command endpoints, transactional outbox/inbox delivery (#90), the remaining master-data and document/work/shipment/return/count resources, ETag/conditional mutation handling, client SDK/contract-generation decisions, provider and load evidence, browser/handheld qualification, and production deployment/rollback evidence.

The PostgreSQL read boundary is now provider-qualified for the published
warehouse, item, and location query services: bounded pages are returned, the
provider-specific search predicate translates correctly, and a warehouse-scoped
caller cannot read an excluded warehouse through the warehouse/location reads.
The disposable PostgreSQL 17 harness passed 57/57 on 2026-09-22 (port 55511)
and cleaned its test container. This does not close API commands, remaining
resources, SDK generation, load/contract matrices, browser clients, or
production rollout.
