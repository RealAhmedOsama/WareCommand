# Replenishment execution

Issue #61 now has a committed policy-driven execution slice. Effective
replenishment signals can be planned into available `WarehouseWork` rows and
completed through the normal scanner/work transaction boundary.

## Planning contract

`IReplenishmentExecutionService` evaluates the existing effective-dated policy
signals and:

- subtracts already-open replenishment work for the same item and destination;
- resolves an active, pickable destination (the policy location when present,
  otherwise a deterministic warehouse fallback);
- rejects an inactive or full destination before creating work;
- selects eligible source balances in location-priority/FIFO order, or
  item-configured FEFO order;
- preserves lot, serial, license-plate, inventory-status, and base-UOM
  identity on every generated line;
- avoids duplicate plans through the warehouse work creation key; and
- creates work only. It never mutates stock from the planning job.

The scheduled `wms.replenishment-generation` job calls this service. Operators
can request the same idempotent planning path through:

`POST /api/inventory/replenishment-policies/generate?warehouseId=...&policyId=...`

The existing work queue remains the operational queue:

`GET /api/work?type=Replenishment&includeTerminal=false`

## Execution contract

`ReplenishmentWarehouseWorkCompletionHandler` requires exactly one scanner
scan per line and verifies item, source, destination, lot, serial, license
plate, quantity, warehouse, and destination-override identity. It delegates the
move to the stock movement/ledger boundary, so source and destination stock,
movement history, materialized balances, and the work completion are committed
or rolled back together. Complete-LPN and destination-capacity checks remain
enforced by the movement boundary.

## Current evidence

- `Wms.Infrastructure.Tests/Inventory/ReplenishmentExecutionServiceTests.cs`
  covers work generation, open-work de-duplication, and destination capacity.
- `Wms.Infrastructure.Tests/WarehouseWork/ReplenishmentWarehouseWorkCompletionHandlerTests.cs`
  covers atomic source-to-pick-face execution and duplicate-scan rejection.
- The focused replenishment set passed 5/5.
- Debug ASP and Infrastructure builds passed with 0 warnings and 0 errors.
- No new migration was required; the existing warehouse-work schema is the
  durable plan ledger.
- The targeted PostgreSQL policy-boundary proof passes 1/1 and confirms that
  the persisted quantity-order check rejects an invalid target update while
  preserving the valid policy. A serialized full PostgreSQL harness pass also
  passed 34/34; the default parallel wrapper terminated its disposable
  container under load, so parallel provider stability remains open.

## Remaining #61 gates

This is progress, not closure. Reservation-backed source allocation, demand
and wave-demand triggers, manual simulation explanations, changed-demand
cancel/replan, explicit blocked/shortage/damage workflow, whole-LPN and
multi-line orchestration, scanner reconnect/offline replay, KPI/reporting
screens, PostgreSQL contention/volume/provider qualification, and browser or
handheld English/Arabic RTL/LTR evidence remain open. Those dependencies are
recorded in `docs/implementation/EXECUTION_STATUS.md`.
