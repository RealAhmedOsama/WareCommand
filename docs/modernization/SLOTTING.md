# Deterministic Slotting

## Implemented boundary

Issue #70 now has a persisted deterministic slotting boundary:

- `SlottingPolicy` stores warehouse-scoped effective dates, lookback period,
  scoring weights, candidate location types, recommendation expiry, and a
  concurrency revision.
- `SlottingService` reads ABC classifications when present, item/package
  physical attributes, shipment and replenishment ledger activity, sales-order
  demand counts, current canonical balances, location capacity/profile fields,
  reservations, and open warehouse work.
- Candidate locations are rejected before scoring when storage profile,
  temperature, hazard, pickability, mixed-item, mixed-lot, capacity, or
  open-work constraints fail. Scores are deterministic and include velocity,
  travel priority, free space, replenishment activity, and order-affinity
  factors.
- Every persisted recommendation stores its analysis run, source data period,
  factor snapshot, constraint snapshot, score comparison, and advisory benefit
  estimates. Estimates are labelled as estimates and are not presented as
  guaranteed savings.
- Analysis is idempotent by policy revision, item, source/target, and source
  period. `DryRun` returns the same explainable recommendation shape without
  changing operational data.
- Approval is historical and stateful. It uses a stable
  `slotting:{recommendationId}` creation key and delegates the physical move to
  existing replenishment warehouse work. Replays return the linked work and do
  not create another work item. Rejected and expired recommendations remain in
  the database.
- `wms.slotting-analysis` is registered as a bounded daily maintenance job
  after ABC recalculation. The API exposes policy CRUD, analysis, simulation,
  recommendation search, approval, and rejection with warehouse authorization,
  anti-forgery protection on mutations, audit events, and rate limiting.

## Qualification

- `SlottingServiceTests` covers hard capacity selection, deterministic source
  periods and factor evidence, idempotent analysis replay, non-mutating dry
  run, and exactly-once approval/work replay.
- Migration: `20260921122015_AddSlottingAnalysis`.

The targeted PostgreSQL identity proof
`SlottingPolicyKeysAreUniquePerWarehouse` passed 1/1 against a disposable
PostgreSQL 17 instance on 2026-09-22. It proves that a slotting policy key
cannot be reused inside one warehouse while the same normalized key remains
valid in another warehouse; container and port cleanup were verified in the
same run. Commit: `f905408`.

## Remaining closure gates

This is a committed progress slice, not full issue closure. Remaining work
includes provider-backed contention and representative warehouse-size/query
plan evidence, richer order-affinity and route-distance inputs, multi-item
before/after simulation and dashboard history, durable batch checkpointing and
reconciliation, full item/location temperature and hazard profile governance,
scanner/browser client wiring with EN/AR RTL/LTR evidence, and production
migration/deployment qualification. Recommendation scoring remains
deterministic; ML/AI enhancement is intentionally outside this issue.
