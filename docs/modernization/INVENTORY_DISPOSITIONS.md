# Inventory dispositions, expiry, damage, and recall

Issue #69 now has a committed local progress slice. This document describes
the boundary that is real in the current source tree and the work that is
still intentionally open.

## Implemented locally

- `InventoryDispositionPolicy` stores warehouse-scoped, effective-dated expiry
  warning, minimum shelf-life, scrap-approval, and destruction-witness rules.
- `IInventoryDispositionService.GetExpiryCandidatesAsync` evaluates canonical
  `InventoryBalances` using the warehouse timezone and an explicit business
  date. The expiry-date boundary is date-only: stock is expired only after its
  expiry business date, while warning and minimum-shelf-life thresholds remain
  configurable.
- Disposition requests capture the complete stock dimension snapshot, reason,
  reference, witness, actor, idempotency key, approval state, and execution
  result. Replayed keys return the original request instead of creating a
  second disposition.
- Approval is mandatory by default for scrap and destruction. Destruction also
  requires a witness unless an explicitly matching policy disables that gate.
- Execution delegates to the existing inventory-status boundary. Partial
  quantity moves preserve item/location/lot/serial/LPN dimensions and write
  the existing movement legs plus canonical immutable ledger legs in the same
  status-change operation.
- Scrap, donation, and destruction currently move stock to `SCRAP_PENDING`;
  no stock or history is deleted.
- Recall cases persist item/lot/serial/LPN selectors, mark a selected lot as
  recalled with the injected clock, and expose a trace composed from current
  canonical balances, the legacy stock projection, and immutable ledger rows.
- Read-only metrics, audit actions, API routes, EF configuration, and the
  migration `20260921113248_AddInventoryDispositions` are included.

The targeted PostgreSQL identity proof
`InventoryDispositionPolicyKeysAreUniquePerWarehouse` passed 1/1 against a
disposable PostgreSQL 17 instance on 2026-09-22. It proves that a disposition
policy key cannot be reused inside one warehouse while the same normalized key
remains valid in another warehouse; the disposable container and port were
cleared after the run. Commit: `f35ad9f`.

## Existing protection reused

Expired/recalled lots and non-allocatable inventory statuses are already
rejected by the reservation eligibility boundary. Shipping and picking also
honor the status flags. The existing hourly expiry job continues to transition
lots and upsert deduplicated notifications; it does not silently create a
disposition or remove stock.

## Remaining work before issue closure

- Wire policy minimum-shelf-life and recall/disposition blocks into every
  allocation, pick, pack, and ship command, including direct legacy callers.
- Add receipt, move, pick, pack, count, return, quality-inspection, and scanner
  reason adapters, plus durable expiry/damage work-queue generation and alert
  reconciliation.
- Connect recall trace to receipt, purchase-order, sales-order, shipment, and
  customer document projections with complete inbound/current/shipped
  quantities; the current trace is ledger/reference based and deliberately
  conservative.
- Add atomic provider-qualified concurrent disposition execution across the
  request state and stock/ledger mutation, including recovery of interrupted
  `Executing` rows. The current row revision protects updates, while the
  status service supplies the stock/ledger concurrency boundary.
- Implement approved scrap/destruction removal, witness evidence, retention,
  and any return-to-vendor/donation/rework document adapters without deleting
  historical inventory evidence.
- Complete warehouse work-queue integration, reports/KPIs beyond the bounded
  metrics endpoint, browser/handheld scanner flows, English/Arabic RTL/LTR
  acceptance, PostgreSQL volume/contention/provider qualification, and
  production migration/deployment.

The tracker therefore records #69 as committed progress, not as a closed
issue.
