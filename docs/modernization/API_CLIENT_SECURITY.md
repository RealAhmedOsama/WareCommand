# API client security foundation

Issue #89 now has a local credential and authorization foundation. It is deliberately separate from human WareCommand cookies: `/api/v1` accepts only the API-client authentication scheme, while the version metadata and OpenAPI document remain anonymous.

## Credential contract

- Administrators manage clients through `/api/administration/clients`.
- A create or rotate operation returns `clientId` and a generated secret once. The secret is never included in the safe client DTO, audit metadata, logs, or exports.
- Persistence stores only a salted PBKDF2-SHA256 hash with a per-secret salt. The client ID is unique and the secret version is monotonic.
- Rotation keeps the previous hash valid only for the requested bounded overlap (default one hour, maximum thirty days), then the old secret naturally stops working.
- Revocation clears both current and previous hashes and changes the status immediately. Expiry and exact IP restrictions are checked on every authentication attempt.

## Scope and warehouse boundary

The published client scope catalog is explicit and reuses the application permission names for resource authorization. A client must either have an explicit set of active warehouse IDs or an administrator-approved global warehouse scope. `IWarehouseAccessService` applies the API context to permission checks, warehouse filters, and resource authorization, so a valid secret cannot bypass warehouse scope.

The API rate-limit partition uses the presented client ID (never the secret) when a bearer credential is present, with the existing IP fallback for human and malformed requests. Correlation, operation, reference, and idempotency headers continue through the host request context.

The persistence boundary is provider-qualified: PostgreSQL rejects a duplicate
client ID even when the attempted row belongs to a different owner, while the
stored credential remains in the versioned PBKDF2 format and never equals the
presented secret. The disposable PostgreSQL 17 harness passed 52/52 on
2026-09-22 (port 55506) and cleaned its test container. This does not close
network-policy, concurrent-rotation/load, mutating-command replay, external
provider, or production secret-rollout gates.

## Remaining acceptance gates

This is committed progress, not full issue closure. OAuth2 client-credentials compatibility, richer CIDR/network policy, client-aware concurrency/replay/idempotency integration for mutating commands, complete administration UI/history, abuse dashboards, external-provider qualification, PostgreSQL/load/concurrent-rotation evidence, browser/handheld EN/AR RTL/LTR evidence, production secret/configuration rollout, and incident runbook qualification remain open.
