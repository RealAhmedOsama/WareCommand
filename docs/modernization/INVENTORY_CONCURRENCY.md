# Inventory concurrency strategy

WareCommand uses optimistic concurrency for inventory state. The database is
the authority: an application lock is never used as the correctness boundary.

## Tokens and write rules

- `InventoryBalance.Revision` is a monotonic concurrency token for the
  materialized balance. The balance service increments it with every physical
  or reservation delta.
- `Stock`, `SerialNumber`, `LicensePlate`, and `LicensePlateContent` carry the
  same monotonic `Revision` token. Their existing `UpdatedAt` tokens remain as
  an additional persistence guard.
- The `20260920161218_AddInventoryConcurrencyTokens` migration backfills the
  new revision columns with zero for existing rows.
- `InventoryTransaction` is append-only. A balance update and its ledger rows
  are saved in the same unit-of-work transaction; a failed concurrency check
  therefore rolls back both the projection and the ledger append.

The balance writer rejects a negative physical balance unless the warehouse
policy explicitly permits it, and rejects reservations above positive on-hand.
The revision predicate makes two writers that read the same balance compete on
the same database row: one commits and the other receives a recoverable
`data.concurrency_conflict` result.

## Lock ordering and retries

When a command has multiple balance dimensions, `IInventoryLedgerService`
orders them by warehouse, location, item, lot, serial identity, LPN, status,
and base UOM before loading or applying balances. Source/destination order in
the request cannot create an inverse lock order.

Concurrency conflicts are not blindly retried after a partial workflow. The
caller must retry the complete idempotent command after reloading its source
state. A future workflow may use a bounded retry (two retries maximum) only
around a complete transaction that can be safely replayed; a retry of an
already-mutated `DbContext` is explicitly unsafe. PostgreSQL serialization or
deadlock retries must use the same whole-command boundary and preserve the
original idempotency key.

The persistence boundary translates EF concurrency failures into
`ConcurrencyConflictException`, preserving the entity/key for structured
diagnostics while returning only a safe retry message to users. Telemetry and
the existing inventory operation scopes count conflict outcomes.

## Current qualification boundary

The local suite proves stale balance writers are rejected and the affected
inventory/status/LPN paths continue to pass. PostgreSQL 20–100-way parallel
reservation/pick/move tests remain open with the reservation/document workflow
issues because those commands do not yet exist in the current model.
