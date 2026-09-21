# Internal movements and warehouse transfers

## Scope

Issue #60 now has a durable transfer boundary for cross-warehouse movement and
same-warehouse internal movement. Transfer inventory uses the non-allocatable
`IN_TRANSIT` system status while it is held at a source-warehouse `Transit`
location.

Implemented in this slice:

- `TransferOrder` and line aggregates with source/destination warehouses,
  transit location, item/UOM, requested/shipped/received quantities, lot,
  serial, LPN, inventory-status, priority, reference, note, and lifecycle
  state;
- Draft, Confirmed, Released, Picking, In Transit, Partially Received,
  Received, Closed, and Cancelled state guards;
- source stock validation, balanced source-to-transit and transit-to-destination
  movement legs, lot/serial/LPN dimension preservation, partial ship/receive,
  destination status selection for discrepancy/damage intake, and atomic
  immutable inventory-ledger entries;
- explicit `InternalMovement` records for same-warehouse location moves;
- idempotent create, confirm, release, ship, receive, close, cancel, and
  internal-movement commands with request-hash conflict detection;
- cross-warehouse authorization checks, audit records, API endpoints, and the
  `AddTransfersAndInternalMovements` migration.

## API surface

`Wms.ASP/Controllers/TransfersController.cs` exposes:

- `/api/transfers` for list/detail/create and transfer lifecycle commands;
- `/api/internal-movements` for same-warehouse location movement.

Mutation endpoints require `inventory.adjust`, an idempotency key, and the
global antiforgery policy. Both source and destination warehouse permissions
are checked for cross-warehouse operations.

## Invariants

- A transfer must use distinct source and destination warehouses, and its
  transit location must belong to the source warehouse and be typed `Transit`.
- `IN_TRANSIT` is non-available, non-allocatable, non-pickable, and
  non-shippable. It cannot be supplied as a caller-selected line status.
- Ship and receive commands cannot exceed the remaining line quantity; source
  stock must be unreserved and available; serialized lines move exactly one
  unit and validate the serial identity.
- Every material movement writes a movement record, compatibility stock change,
  and balanced source/destination ledger legs in one transaction.
- Reusing an idempotency key with the same request replays the original result;
  reusing it with a different request is rejected.
- LPN operations require the complete LPN quantity at the source location so a
  partial LPN cannot silently move while its parent location remains stale.

## Qualification

`Wms.Infrastructure.Tests/Transfers/TransferServiceTests.cs` passes 3/3
focused SQLite tests covering cross-warehouse transit, partial receipt,
idempotent replay, balanced internal movement, and pre-mutation location
validation.

The checked-in schema migration is
`Wms.Infrastructure/Database/Migrations/20260921061826_AddTransfersAndInternalMovements.cs`.
Migration lifecycle verification reports no pending model changes.

## Remaining release gates

This is a committed #60 progress slice, not full issue closure. Reservation
allocation and automatic pick/load work generation, inbound receiving-session
and putaway integration at the destination, multi-line/whole-pallet LPN
orchestration, over/short/damage exception documents and reversal/cancellation
after shipment, concurrent PostgreSQL/provider/volume qualification, transfer
KPIs/list/detail screens, and browser/handheld English/Arabic RTL/LTR proof
remain downstream work.
