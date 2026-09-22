# Supplier Returns and Return-to-Vendor

## Local implementation boundary

Supplier returns are a distinct supplier-facing document. They do not reuse a
customer return and do not model a return as a negative receipt. A return has
warehouse and supplier ownership, optional purchase-order/ASN/receipt/quality
lineage, an RMA or supplier authorization reference, traceable source
dimensions, and a dedicated status history:

`Draft -> Approved -> Picking -> Packed -> Shipped -> Acknowledged -> Closed`

`Draft` and `Approved` may be cancelled. Operational failures are represented
by the `Exception` status for the future exception-resolution surface.

## Inventory and work invariants

- Approval validates the complete item/UOM/location/status/lot/serial/LPN
  dimension and reserves only currently unreserved source quantity.
- Receipt, purchase-order, ASN, and quality-disposition lineage is checked for
  the same warehouse, supplier, item, and referenced document. Receipt and
  quality-disposition quantities require supervisor override when exceeded.
- Released work is `WarehouseWorkType.Return`, queued as `RTV`, and carries the
  return-line source dimension and staging destination.
- Return work consumes the source reservation and records a pick in the same
  transaction as the staging movement. Staged stock is always written under
  `RETURN_PENDING`, including when the source status was available, damaged, or
  quarantined; normal allocation therefore cannot consume it while it awaits
  shipment.
- A supervisor-approved short pick releases the unused reservation, reduces the
  approved quantity to the physically staged quantity, and permits partial
  packing/shipping. A normal short pick is rejected.
- Shipment removes staged `RETURN_PENDING` stock once, records the ship ledger
  entry and serial shipment, and is idempotent by operation plus request key.
- Approved cancellation releases the source reservation and records the
  compensating ledger entry. Cancellation after release is rejected because a
  staged movement requires a compensating operational action rather than a
  silent document state change.

## API and audit boundary

The authenticated `/api/supplier-returns` endpoints cover list, details,
create, approve, release, pack, ship, acknowledge, close, and cancel. Mutating
commands require antiforgery protection, warehouse scope, typed permissions,
and idempotency keys. Supplier authorization references are unique per supplier
and warehouse. Creation, approval, release, pack, ship, acknowledgement,
closure, and cancellation use dedicated audit actions.

## Qualification status

The local SQLite service tests cover the full reserve/move/pack/ship/
acknowledge/close lifecycle, exactly-once repeated shipment, over-quantity
rejection, duplicate supplier authorization rejection, approved cancellation
release, and supervisor-approved partial shipment. Infrastructure and ASP
builds must remain warning-free, and the EF migration is generated but not
applied here.

The focused `SupplierReturnServiceTests` rerun passed 5/5 on 2026-09-22,
refreshing the reserve/move/pack/ship lifecycle and partial/cancellation
boundaries.

This slice is intentionally recorded as progress rather than closure. Real
carrier/EDI acknowledgement, provider-backed concurrency and warehouse-volume
evidence, packing/loading and carrier/document adapters, labels and report
surfaces, complete browser/handheld EN/AR RTL/LTR screens, production
migration/deployment, and remote issue synchronization remain external or
dependent qualification gates.
