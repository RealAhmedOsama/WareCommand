# Cycle counting

Issue #62 now has a committed planning and snapshot progress slice. Cycle-count
plans are durable, effective schedules; due plans create immutable count tasks,
dimension lines, and linked `WarehouseWork` of type `Count`.

## Planning boundary

`CycleCountPlan` supports warehouse, location, item, item-class, frequency,
threshold, blind-mode, and freeze-policy configuration. The generator:

- evaluates only active due plans;
- captures the physical on-hand quantity for every countable inventory
  dimension at the snapshot timestamp;
- creates an explicit zero-quantity line for a scoped empty location;
- groups multi-item scopes into deterministic location tasks;
- uses a unique plan/due/location task key so a job retry cannot create a
  second task for the same scheduled scope;
- links every task to available `WarehouseWork` and uses a neutral quantity
  token for blind work, so the generic work line does not carry the expected
  count; and
- advances the plan only after the generated task/work transaction succeeds.

The scheduled job is `wms.cycle-count-generation`. Administrative endpoints are:

- `GET /api/inventory/cycle-counts/plans`
- `POST /api/inventory/cycle-counts/plans`
- `PUT /api/inventory/cycle-counts/plans/{planId}`
- `POST /api/inventory/cycle-counts/generate`

Blind task summaries intentionally return `ExpectedQuantity = null`; the
expected snapshot remains server-side on `CycleCountLine`.

## Current evidence

- `Wms.Infrastructure.Tests/Inventory/CycleCountServiceTests.cs` covers blind
  snapshot generation, neutral blind work quantity, work linkage, and duplicate
  plan-key rejection.
- The focused cycle-count set passed 2/2.
- Debug ASP/Infrastructure builds passed with 0 warnings and 0 errors.
- Migration `20260921070421_AddCycleCounting` adds plan, task, and line tables;
  migration lifecycle verification remains required before commit handoff.
- A targeted disposable PostgreSQL proof passed 1/1. It confirms that plan
  keys are unique within a warehouse while the same key can be used in another
  warehouse, and that generated task keys remain globally unique across plans.
  The container and verification port were clean after the run.

## Remaining #62 gates

This is progress, not closure. Count scanner completion and save/resume,
location/item/lot/serial/LPN completeness, movement freeze/reconciliation,
recount, approval thresholds, exactly-once `CountVariance` posting, cancellation
and correction, variance history/KPIs, independent approval RBAC, PostgreSQL
concurrency/volume/provider evidence, and browser/handheld English/Arabic
RTL/LTR evidence remain open. The generic work engine still has no count
completion handler; no balance is directly overwritten by the planning job.
