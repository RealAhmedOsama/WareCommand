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
for every line, and only then transitions the aggregate to `Completed`. Work
completion runs inside the scoped unit-of-work transaction, so movement,
inventory, work-line actuals, command idempotency, audit, and the terminal
state commit or roll back together.

Issue #48 now registers `PutawayWarehouseWorkCompletionHandler`. A finalized
receipt generates one available putaway work item per receipt movement using a
receipt/line/movement creation key. A required inbound quality inspection holds
generation until a later quality-disposition integration releases it. The
completion API accepts typed scans containing line, item, source, destination,
quantity, license plate, and explicit destination-override identity. The
handler validates those identities server-side and delegates stock, location
capacity, status, lot, serial, and whole-license-plate movement rules to the
existing `IStockMovementService.PutawayAsync` boundary.

The current slice supports whole-LPN and non-LPN putaway. Partial-LPN content
movement, exception-to-return-staging flows,
the legacy manual putaway route, handheld/browser UI and RTL/LTR qualification,
productivity reporting, and PostgreSQL contention/provider qualification remain
open gates for #48 and its downstream issues.

Issue #49 adds the deterministic `IPutawayRuleService` boundary. New receipt
work asks it for the highest-ranked valid destination and snapshots that
suggestion onto the work line; no-match responses remain explicit until the
#50 staging/exception workflow owns the fallback.

## Persistence and tests

The `WarehouseWorks`, `WarehouseWorkLines`, and `WarehouseWorkCommands` tables
are added by the `AddWarehouseWorkEngine` migration. Focused tests cover domain
transitions, assignment ownership, override reasons, quantity bounds,
creation-key idempotency, receipt putaway generation, command replay,
completion replay, scan identity validation, and missing handler dependency
behavior. SQLite tests are local qualification only; the PostgreSQL harness and
contention tests remain part of the later qualification work.
