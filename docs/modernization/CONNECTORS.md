# Connector architecture and provider inventory

Connector contracts live in `Wms.Application.Connectors`; provider transports
belong in `Wms.Infrastructure.Connectors`. The checked-in host does not register
the generic ERP or e-commerce reference fixtures. `GET /api/connectors/capabilities`
reports contract-only and implemented adapter status, configuration and
verification separately. `GET /api/connectors` adds per-instance capability,
configuration, and verification states while retaining its existing fields.

## What is implemented locally

- Versioned type and operation catalogs, warehouse-scoped instance and mapping
  profile persistence, credential-reference validation, and run history.
- Field mapping, deterministic external-record identity, idempotency, cursor and
  conflict handling, and typed fail-closed results when a transport is missing,
  disabled, or reference-only.
- `IConnectorAdapter` contracts for pull, push, and connection verification.
  Adapters default to `ContractOnly` and must explicitly identify an implemented
  provider transport. A registered operational adapter's modes and operations
  are the only executable operations exposed by capability discovery.
- Generic ERP and e-commerce reference classes remain test/contract fixtures.
  They report `Reference`, advertise no runnable modes or operations, cannot be
  activated, cannot pass connection checks, and throw if pull or push is called.

The instance credential field stores a deployment-managed reference only. The
host has no connector credential resolver. `Configured` therefore means that the
instance's configuration reference and mapping are stored; it does not mean the
provider secret resolved. `Verified` requires a successful provider connection
check. A healthy connector check also does not prove partner acceptance.

## Adapter inventory

| Contract type | Local behavior | Missing code | Credential requirement | Live acceptance gate |
| --- | --- | --- | --- | --- |
| `generic-erp.v1` | Generic mapping/run persistence boundaries; reference class is test-only | Selected ERP protocol, authentication, network transport, provider paging and write semantics | Deployment-managed reference plus a provider-specific secret resolver | Selected ERP sandbox proves scoped pull/push, retry and reconciliation behavior |
| `ecommerce-orders.v1` | Generic mapping/run persistence boundaries; reference class is test-only | Selected commerce API client, authentication, rate-limit and webhook behavior | Deployment-managed reference plus a provider-specific secret resolver | Selected store sandbox proves order, inventory and retry behavior |
| `marketplace.v1` | Type and operation contract catalog only | A named marketplace API, transport, credential resolution and provider-specific mappings | Provider-issued secret through a deployment-managed resolver | Marketplace test account and provider acceptance |
| `carrier.v1` | Type and operation contract catalog only | A named carrier API, label/tracking transport, credential resolution and lifecycle mapping | Carrier-issued secret through a deployment-managed resolver | Carrier sandbox proves label, tracking, cancellation and acceptance behavior |

The credential-reference string is not a transport or proof of credentials. No
carrier label/tracking or ERP/commerce/marketplace operation is currently
runnable in the shipped host.

## Related capability boundaries

- `/api/b2b/capabilities` retains its standards and transport-mode contract
  catalogs for compatibility and adds `implementationStatus`,
  `implementedTransportModes`, `configurationStatus`, and
  `verificationStatus`. Only canonical envelope validation and local document
  persistence are implemented; no EDI parser or B2B transport is registered.
- `/api/integrations/capabilities` reports webhook configuration and delivery
  evidence. `Verified` means an active endpoint accepted a signed delivery; it
  does not claim external partner certification.
- Administration readiness has a non-blocking connector-transports check.
  Missing provider transports remain visible without blocking core warehouse
  workflows.

## Next provider work

Open a separately scoped implementation issue only after a concrete provider and
protocol are selected. That issue must name authentication and credential
resolution, supported documents/operations, warehouse scope, paging and retry
semantics, sandbox/test fixtures, partner acceptance evidence, and rollout
gates. Do not build arbitrary ERP, marketplace, or carrier adapters from these
generic contracts alone.

The PostgreSQL persistence boundary was previously qualified for connector
mapping profile identity, connector instance names, per-instance run idempotency,
and per-instance external-record identity. The disposable PostgreSQL 17 harness
passed 55/55 on 2026-09-22. That evidence covers local persistence only; it does
not qualify a live provider transport.
