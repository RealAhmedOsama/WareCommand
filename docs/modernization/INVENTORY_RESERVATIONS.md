# Inventory reservations and allocation

WareCommand now has a durable reservation boundary for outbound and transfer
demand. A reservation identifies the demand contract; one or more allocation
rows identify the exact warehouse, location, item, lot, serial, LPN, inventory
status, and base unit dimensions selected for that demand.

## Quantity and lifecycle rules

- `InventoryBalance.ReservedQuantity` remains materialized by the inventory
  ledger. Reservation code never edits that column directly.
- A hard or soft reservation both consumes eligible available-to-reserve
  quantity. The mode is retained on the demand record so later allocation and
  workflow policies can distinguish the operational commitment without allowing
  concurrent commands to oversubscribe stock.
- Allocation quantity is immutable in meaning: consumption and release are
  accumulated separately. The active remainder is derived as
  `AllocatedQuantity - ConsumedQuantity - ReleasedQuantity`.
- Partial allocation leaves a clear backorder quantity. Invalid, inactive,
  held, expired, non-pickable, or otherwise non-allocatable dimensions are
  excluded before allocation.
- Reservation and allocation revision tokens participate in EF optimistic
  concurrency. The ledger balance revision is the final oversubscription guard;
  a competing stale writer receives the existing typed concurrency conflict.

Candidate selection uses the persisted item policy and warehouse/location
rules. FEFO items sort eligible lot balances by expiry first; other items sort
by location priority and balance age. A request selector is stored as a policy
snapshot so reallocation cannot silently broaden the original demand contract.

## History and operations

`InventoryReservationEvents` is append-only. It records creation, allocation,
release, consumption, reallocation, expiry, cancellation, actor, correlation,
quantity, allocation identity, and reason. The service supports reserve,
release, consume, cancel, reallocate, expiry sweep, demand lookup, and inquiry.

All quantity-changing operations run in the caller's unit-of-work transaction:
reservation rows, allocation rows, immutable history, ledger transactions, and
materialized balances commit or roll back together. Demand identity is unique
per warehouse, so a repeated demand request returns the existing reservation
instead of creating a second one.

## Current boundary and remaining work

The engine and SQLite lifecycle qualification are committed under backlog #37.
The current slice intentionally does not claim completion of #37: sales-order,
transfer-order, replenishment, work, backorder, pick/ship/return document
adapters, scanner/job/integration command idempotency, administrative MVC/API
permission/audit adapters, and PostgreSQL parallel contention qualification
remain with #38–#41 and later owning issues. The migration is checked in but is
not applied to production, and no remote issue closure or push is implied.
