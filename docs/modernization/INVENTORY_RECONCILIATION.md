# Inventory reconciliation and invariant checks

## Current local boundary

`IInventoryReconciliationService` provides an authorized, read-only report
through `GET /api/inventory/reconciliation`. Warehouse and item scope are
enforced through the same warehouse-access boundary used by inventory reads.
The query supports an optional warehouse, item, UTC date window, deep/shallow
mode, batch size, and bounded issue output. Date windows constrain row-level
transaction checks; current ledger-to-balance comparison always uses the full
ledger so a partial event window cannot be mistaken for a current-state total.

The report compares ledger dimension totals with materialized balances in both
directions and checks ledger before-plus-delta equations, reservation limits,
dimension references, movement/reversal references, serial placement and
quantity uniqueness, license-plate lifecycle references, and LPN content
quantities/references. Deep mode additionally compares active reservation
allocation remainders with balance reservations. Queries use server-side
aggregation plus bounded primary-key/key batches; the issue collector retains a
bounded sample while preserving total and severity counts.

No repair command exists in this slice. The service never updates inventory,
rewrites immutable ledger rows, changes reservation state, or silently repairs
master data. Every issue includes a stable code, severity, affected reference,
quantities where applicable, and a suggested investigation step.

Two durable maintenance jobs use the same service:

- `wms.inventory-health-check` runs shallow checks every ten minutes.
- `wms.inventory-reconciliation` runs the deep report daily.

They persist deduplicated durable notifications only when issues are found.
Migration and restore verification can call the same report contract before
declaring a target healthy.

## Evidence

- `Wms.Infrastructure.Tests/Inventory/InventoryReconciliationServiceTests.cs`
- `Wms.Infrastructure.Tests/Jobs/InventoryReconciliationJobTests.cs`
- `Wms.Infrastructure/Inventory/InventoryReconciliationService.cs`
- `Wms.Application/Inventory/InventoryReconciliationContracts.cs`
- `Wms.ASP/Controllers/InventoryReconciliationController.cs`
- `Wms.Application/Jobs/WmsJobContracts.cs`
- `Wms.Infrastructure/Jobs/WmsOperationalJobHandlers.cs`

## Remaining issue scope

This is a progress slice, not closure of backlog issue #41. Intentional
corruption coverage for every serial/LPN/reservation invariant, PostgreSQL
large-volume query plans, post-migration/restore command wiring, operational
metrics and alert dashboards, deterministic repair commands with a dedicated
permission/reason/dry-run/audit gate, and completed document/work reference
checks remain open. Repair must be added only after each source-of-truth case
has a reversible, audited procedure.
