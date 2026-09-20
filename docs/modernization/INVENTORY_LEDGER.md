# Inventory ledger and balance model

WareCommand now has a pragmatic transactional inventory ledger. It is not
event sourcing: `InventoryTransactions` are immutable quantity legs and
`InventoryBalances` is the current materialized balance for fast inquiry.

## Dimension and invariants

Every balance and transaction is keyed by:

- warehouse and location;
- item, lot, serial identity, and license plate;
- inventory status; and
- the item's normalized base unit of measure.

Each ledger row records signed on-hand and reservation deltas, before/after
quantities, transaction type, reference type/id/line, reason, actor, UTC time,
correlation, transaction group, and idempotency key. A move is two rows with
the same group: one negative source leg and one positive destination leg.

The balance writer is `IInventoryLedgerService`. It applies the balance delta
and appends the immutable rows through the same unit of work. Reservations are
tracked separately from physical on-hand, so a reservation does not subtract
physical stock. Negative on-hand is rejected unless the owning warehouse has
`AllowNegativeStock` enabled. The database also uses a balance revision
concurrency token and a unique idempotency key/entry sequence constraint.

`WmsDbContext` rejects updates and deletes of persisted inventory transactions.
No inventory-balance update/delete operation is exposed by the balance
repository; balance mutation is kept inside the ledger service.

## Current mutation coverage

The existing receive, putaway, pick, adjustment, inventory-status change, and
license-plate content/move/ship/return paths append ledger rows alongside the
legacy `Stock` and `Movement` projections. The legacy projections remain for
compatibility while later backlog work moves reservations, work execution,
packing, shipping, transfers, counts, and returns onto the same boundary.

## Cutover and reconciliation

Migration `20260920153824_AddInventoryLedger` creates the two tables and seeds
one `OpeningBalance` row plus one materialized balance for every existing
`Stock` row. Existing `Movements` are retained as immutable pre-ledger history;
the migration does not replay ambiguous historical adjustment semantics or
rewrite that table. All post-cutover changes append `InventoryTransactions`.

`IInventoryLedgerService.ReconcileAsync` verifies that each materialized
balance equals the sum of its ledger deltas and reports missing or mismatched
dimensions. This cutover is local qualification only: the migration has not
been applied to production data.

Focused evidence is in
`Wms.Domain.Tests/Entities/InventoryBalanceTests.cs`,
`Wms.Domain.Tests/Entities/InventoryTransactionTests.cs`, and
`Wms.Infrastructure.Tests/Inventory/InventoryLedgerServiceTests.cs`.
