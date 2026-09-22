# Picking strategy execution

Issue #66 adds a configurable planning boundary for scalable outbound picking.
The planner keeps one source of truth for inventory execution: every plan line
references an existing reservation-backed `WarehouseWorkLine`. Batch, cluster,
zone, and pick-and-pass plans group and sequence that work without moving stock,
consuming reservations, or creating a second mutation path.

## Strategy contract

`PickingStrategyPolicy` is warehouse-scoped and effective-dated. Policies can
match a wave template, order source/profile, item, source zone, or package
profile and carry deterministic priority, order/container limits, weight and
volume limits, and a sequence mode. If no policy matches, single-order
planning remains the default.

`PickingPlan` stores the strategy snapshot and capacity boundary. Each
`PickingPlanLine` preserves the warehouse-work line, sales-order line,
reservation/allocation IDs, source location, lot, serial, source LPN, inventory
status, planned/picked quantity, zone, group key, and target container. A
complete order is selected as a unit so a capacity boundary cannot silently
split order attribution.

The routing behavior is:

- `SingleOrder`: one execution container per order; the existing single-order
  pick workflow remains valid.
- `Batch`: common item/source/lot/serial/LPN/status demand shares a batch
  container while each order line remains a separate attributed plan line.
- `Cluster`: one target container per order; the scanner must accept the
  expected target before the container can progress.
- `Zone`: common source-zone work is sequenced for downstream consolidation.
- `PickAndPass`: work is grouped per order and source zone, with target scans
  and ordered handoff records. A later handoff is rejected until its prior
  sequence is complete.

## API and execution boundary

`Wms.ASP/Controllers/PickingStrategiesController.cs` exposes policy search and
configuration, plan search/detail/simulation/creation, target-container scans,
handoff completion, refresh, and cancellation. Mutation routes require the
existing warehouse-scoped allocation or picking permissions and antiforgery
protection.

`PickingStrategyService.RefreshAsync` reads actual quantities and status from
the linked warehouse work. The existing `WarehouseWorkService` and
`PickWarehouseWorkCompletionHandler` remain responsible for claiming,
scanning, moving stock, consuming reservations, recording ledger entries, and
updating order picked quantity. A plan cancellation never silently cancels
inventory work or reservations; it is blocked after execution starts and the
existing allocation cancellation boundary remains explicit.

## Schema and qualification

The durable schema is
`Wms.Infrastructure/Database/Migrations/20260921093608_AddPickingStrategies.cs`.
It adds strategy policies, plan lines, logical target containers, and ordered
zone handoffs with warehouse-local creation keys, per-plan sequence constraints,
and foreign keys back to work, demand, inventory dimensions, locations, and
handling units.

`Wms.Infrastructure.Tests/Outbound/PickingStrategyServiceTests.cs` passed 5/5
for batch attribution, cluster target-scan rejection, ordered pick-and-pass
handoffs, policy resolution/capacity, and single-order isolation. The combined
strategy/wave/allocation/reservation regression filter passed 23/23. Debug
Infrastructure and ASP builds passed with 0 warnings and 0 errors. EF reported
no pending model changes and generated a 409651-byte idempotent PostgreSQL
script. No live database migration, push, or deployment was performed.

The targeted PostgreSQL identity proof
`PickingStrategyPolicyKeysAreUniquePerWarehouse` passed 1/1 against a
disposable PostgreSQL 17 instance on 2026-09-22. It proves that a policy key
cannot be reused inside one warehouse while the same normalized key remains
valid in another warehouse; the disposable container and port were cleared
after the run. Commit: `3ffba50`.

This is committed #66 progress, not closure. Provider-backed serializable
contention, high-volume/performance qualification, mixed lot/serial/LPN
end-to-end execution, shortage/reallocation and cancellation/replan matrices,
packing/consolidation metrics, browser/handheld scanner UI, and English/Arabic
RTL/LTR evidence remain open gates. The local execution tracker is
authoritative for the dependency and issue state.
