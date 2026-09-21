# Shipping execution

## Scope

The shipping boundary groups closed packing packages into a shipment, opens a
load, verifies package/LPN scans, confirms shipment atomically, and records
manual or provider tracking updates.

Implemented in this slice:

- carrier and carrier-service master records;
- shipment header with ship-to snapshot, planned/actual dates, route/load and
  trailer references, external reference, status, and tracking fields;
- shipment lines derived from package contents;
- package-to-shipment eligibility and loaded/shipped trace;
- load opening, package load/unload scan verification, and cancellation after
  unload;
- atomic shipment confirmation that removes package-LPN stock, writes ship
  movements and ledger entries, ships serials, marks packages/LPNs/orders, and
  records an idempotent command and audit entry;
- manual/provider tracking events, including delivered transition;
- `ICarrierAdapter` application boundary for provider booking/label work.

## API surface

`Wms.ASP/Controllers/ShippingController.cs` exposes `/api/shipping`:

- carrier and service creation;
- shipment creation and detail retrieval;
- load opening and package load/unload scans;
- shipment confirmation and cancellation;
- manual/provider tracking updates.

All shipment commands require a caller with `shipping.execute` and an
idempotency key. Carrier master changes additionally require settings
management permission.

## Invariants

- Shipment creation accepts only closed, warehouse-scoped packages that are not
  already assigned to another shipment.
- A package can load only into an open load belonging to the shipment, and its
  package LPN must still be closed.
- Confirmation requires every eligible package to be loaded. Reserved stock,
  wrong package ownership, duplicate scans, and non-closed package LPNs are
  rejected.
- Stock deletion, serial shipment, ship movement, inventory ledger, package,
  LPN, order-line, order, shipment, audit, and idempotency updates share one
  database transaction.
- A loaded package must be unloaded before cancellation; a confirmed shipment
  cannot be cancelled.
- Tracking events are durable and source/provider references are preserved.

## Qualification

`Wms.Infrastructure.Tests/Shipping/ShipmentServiceTests.cs` passes 2/2 focused
relational tests covering carrier setup, wrong-package rejection, load/unload
and cancellation, atomic stock/LPN/order reconciliation, duplicate confirmation
replay, and delivered tracking.

The checked-in schema migration is
`Wms.Infrastructure/Database/Migrations/20260921045607_AddShippingExecution.cs`.

## Remaining release gates

This is a committed shipping execution progress slice, not full issue closure.
Configurable grouping policies, multi-order/partial-shipment matrices,
reservation release/exception resolution, carrier booking/label/webhook
adapters, retry/outbox behavior, loading work entities, override reason
permissions, manifests/packing slips/tracking views, return/correction flow,
serial/lot/concurrency/provider-volume qualification, and browser/handheld
EN/AR RTL/LTR proof remain downstream work.

Carrier adapters must be invoked outside the shipment database transaction;
the current core only defines that boundary and supports manual tracking.
