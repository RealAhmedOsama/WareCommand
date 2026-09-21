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

- All versioned resources require authentication and the existing permission policy for that resource.
- The API rate-limit policy applies to every versioned endpoint.
- `X-Correlation-ID`, `X-Operation-ID`, `X-Reference-ID`, and `Idempotency-Key` remain host-level propagation headers. Query operations are side-effect free; mutating commands will only be published after the idempotency and credential boundaries are complete.
- Failures use `application/problem+json` with a stable `errorCode`, retryability, and optional field errors. Correlation references remain in the response headers and unexpected failures stay provider-safe.
- The version metadata endpoint is anonymous and contains no tenant, warehouse, user, secret, or connection information.
- v1 is additive. A breaking contract change requires a new version, and deprecated operations must remain documented for at least one release cycle.

## Remaining acceptance gates

This is committed progress, not full issue closure. Remaining work includes machine-to-machine credentials and scopes (#89), idempotent command endpoints, transactional outbox/inbox delivery (#90), the remaining master-data and document/work/shipment/return/count resources, ETag/conditional mutation handling, client SDK/contract-generation decisions, provider and load evidence, browser/handheld qualification, and production deployment/rollback evidence.
