# Outbound exceptions

## Scope

The outbound exception boundary provides a durable, reason-coded queue for
shortage, pick, pack, load, demand, carrier, and cutoff failures. It links the
exception to order demand, reservations, warehouse work, shipment/load/package
identity, and inventory dimensions without permitting direct administrator
stock edits.

Implemented in this slice:

- reason, severity, queue, due-time, assignment, review, notes, attachment
  reference, and warehouse-scoped exception state;
- links to sales orders/lines, inventory reservations, work/work lines,
  shipments, packages/loads, items, locations, lots, serials, and LPNs;
- queue list/detail with warehouse scope, filters, search, overdue and open
  counts;
- supervisor-gated reallocate, substitute, partial-backorder, release,
  cancel-line/order, repick/repack, hold, carrier correction, unload, and
  override resolution contracts;
- reservation release/reallocation, order-line substitution/cancellation,
  work recovery, order hold/cancel, shipment carrier correction, and direct
  load-unload state changes inside the exception transaction where referenced;
- immutable command replay records and audit records for create, assignment,
  review, and resolution.

## API surface

`Wms.ASP/Controllers/OutboundExceptionsController.cs` exposes
`/api/outbound-exceptions`. Reading requires `sales_orders.read`; mutation
requires `sales_orders.manage`; sensitive resolution requires `work.override`,
an explicit override reason, and an idempotency key.

## Invariants

- All linked references must belong to the exception warehouse and linked line,
  work, load, and service identities are cross-checked.
- Terminal exceptions cannot be changed. Resolution commands are unique by
  exception, operation, and idempotency key; a reused key with a different
  request is rejected.
- Reservation release/reallocation uses the inventory reservation service and
  the ambient exception transaction, preserving reservation events and ledger
  balances.
- Sensitive substitution, cancellation, carrier, unload, and supervisor
  actions require a supervisor override reason and permission.
- Order, work, reservation, shipment, exception, command, and audit writes are
  committed together when a linked resolution is executed.

## Qualification

`Wms.Infrastructure.Tests/Outbound/OutboundExceptionServiceTests.cs` passes 3/3
focused SQLite tests covering overdue queueing, assignment/review, supervisor
gating, idempotent resolution replay/conflict, and non-mutating rejection.

The checked-in schema migration is
`Wms.Infrastructure/Database/Migrations/20260921054513_AddOutboundExceptions.cs`.

The disposable PostgreSQL harness also passes 31/31. It proves that a
resolution command key is unique per `(OutboundExceptionId, Operation)` while
the same client key can be reused for another operation or another exception.
The test container is removed and the verification port is free after the run.

## Remaining release gates

This is a committed outbound-exception progress slice, not full issue closure.
End-to-end shortage/damage/reallocate matrices against live pick/pack/ship
transactions, substitution product/UOM policy, complete reservation-to-order
quantity reconciliation, carrier adapter/outbox failure handling, load and
package correction workflows, attachments/KPI reporting, concurrent
PostgreSQL/provider qualification, and browser/handheld EN/AR RTL/LTR proof
remain downstream work.
