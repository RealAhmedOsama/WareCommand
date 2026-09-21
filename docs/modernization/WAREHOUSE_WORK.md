# Warehouse work engine

The warehouse work engine is the shared execution boundary for operator work
such as putaway, picking, replenishment, moves, counts, packing, loading, and
returns. It keeps state, assignment, command idempotency, authorization,
concurrency, audit, and source-document references in one aggregate while
delegating type-specific stock operations to completion handlers.

## Current contract

`WarehouseWork` owns the lifecycle:

- `Open` -> `Available` -> `Assigned` -> `InProgress` -> `Paused` or `Completed`;
- exception routing can enter `Exception` and return to an executable state;
- terminal `Completed` and `Cancelled` states reject further aggregate changes;
- revision fields are EF concurrency tokens;
- `WarehouseWorkCommand` provides a unique `(work, operation,
  idempotency-key)` ledger and request-hash reuse protection.

The API is exposed under `/api/work`. Mutating endpoints require the relevant
`work.manage`, `work.execute`, or `work.override` permission and antiforgery
validation. Every command records an immutable audit event and saves the state
change with its command ledger entry.

## Handler composition

`IWarehouseWorkCompletionHandler` is selected by `WarehouseWorkType`. The
engine invokes exactly one handler for completion, requires actual quantities
for every line, and only then transitions the aggregate to `Completed`. This
keeps stock movement, allocation, scanner validation, and destination rules in
the type-specific modules instead of embedding them in the generic engine.

The reusable engine is implemented in issue #54. No concrete putaway handler
is registered yet; issue #48 must add that handler and connect receipt,
quality, license-plate, location-capacity, scanner, and inventory movement
rules. PostgreSQL contention qualification, handheld UI, productivity
reporting, and the downstream work consumers remain open gates.

## Persistence and tests

The `WarehouseWorks`, `WarehouseWorkLines`, and `WarehouseWorkCommands` tables
are added by the `AddWarehouseWorkEngine` migration. Focused tests cover domain
transitions, assignment ownership, override reasons, quantity bounds,
creation-key idempotency, command replay, completion replay, and missing
handler dependency behavior. SQLite tests are local qualification only; the
PostgreSQL harness and contention tests remain part of the later qualification
work.
