# Customer returns and disposition

## Scope

The return boundary creates warehouse-scoped RMAs, validates planned versus
explicitly unplanned returns, receives scanner-identified goods into Return
Pending stock, supports inspection, and applies auditable dispositions.

Implemented in this slice:

- RMA headers and lines with optional customer, order, shipment, package, lot,
  and serial trace references;
- Requested, Authorized, Received, Inspecting, Disposed, Closed, and Cancelled
  state transitions;
- planned-return shipment policy and authorized-quantity bounds;
- exact serial identity validation against shipped serial history;
- atomic receipt movements, Return Pending inventory ledger entries, stock,
  serial quarantine, and idempotent command/audit records;
- partial dispositions to available, damaged, repair, scrap, vendor, or
  customer-return paths with destination location validation;
- API endpoints for authorization, inspection, receiving, disposition, close,
  cancel, and detail retrieval.

## API surface

`Wms.ASP/Controllers/ReturnsController.cs` exposes `/api/returns`.
Receiving commands require `receiving.execute`; disposition requires
`inventory.adjust`. Every mutating request requires an idempotency key and an
anti-forgery token.

## Invariants

- A planned RMA must reference a shipped, delivered, or closed shipment; an
  unplanned RMA must opt into the unplanned policy explicitly.
- Authorized quantities cannot exceed the matching shipment line quantity, and
  received quantities cannot exceed the RMA line quantity.
- Every receipt enters the Return Pending inventory status and remains in the
  return location until a disposition moves or consumes it.
- Serialized receipts require the exact shipped serial identity and are placed
  in serial quarantine until disposition changes their lifecycle state.
- A disposition cannot exceed the received, undisposed receipt quantity and
  requires a valid destination for restock, damaged, and repair paths.
- Stock, movements, ledger entries, serial state, RMA state, command replay,
  and audit records are written in one transaction.

## Qualification

`Wms.Infrastructure.Tests/Returns/ReturnServiceTests.cs` passes 3/3 focused
SQLite tests covering pending stock, inspection, partial restock/scrap,
idempotent replay, unplanned policy, atomic over-receipt rejection, and exact
serialized identity.

The checked-in schema migration is
`Wms.Infrastructure/Database/Migrations/20260921051743_AddReturnsAndDisposition.cs`.

The disposable PostgreSQL harness also passes 30/30. It proves that return
command idempotency keys are unique per `(ReturnAuthorizationId, Operation)`;
the same client key remains reusable for another operation or another RMA.
The test container is removed and the verification port is free after the run.

## Remaining release gates

This is a committed returns execution progress slice, not full issue closure.
Inbound carrier/RMA tracking and In Transit state, supplier/RTV integration,
inspection reason/disposition catalogs, return labels, RMA list/search views,
partial-shipment and multi-receipt provider matrices, PostgreSQL concurrency
qualification, and browser/handheld EN/AR RTL/LTR proof remain downstream work.
