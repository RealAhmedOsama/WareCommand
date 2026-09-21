# Scanner-first picking execution

## Current progress

Issue #55 has a committed backend execution slice. Released `Pick` work can now be completed from an exact scanner contract through `POST /api/work/{workId}/complete` with one scan per work line.

Each pick line carries the reservation and reservation-allocation identifiers that released sales-order work created. Completion validates the item, source location, destination warehouse/location type, inventory status, lot, serial, source license plate, quantity, and planned destination. A destination override requires the existing supervisor authorization and reason contract.

The normal path performs these changes inside the warehouse-work transaction:

1. Remove the exact source stock dimension.
2. Create or increment the staging, packing, or shipping stock dimension.
3. Record the movement's destination and move a traced serial to that destination when present.
4. Consume the exact reservation allocation and append the source ledger leg.
5. Append the destination ledger leg with a deterministic idempotency key.
6. Complete the work line and persist the work command/audit record through the existing work service.

Duplicate line scans, mismatched dimensions, unavailable source stock, partial LPN picks, and target-LPN changes are rejected before a movement is created. Partial quantity is supported by the handler but still requires the work service's supervisor override or exception route before the work can be completed.

## Local evidence

- `Wms.Infrastructure.Tests/WarehouseWork/PickWarehouseWorkCompletionHandlerTests.cs`: exact dimension/ledger/reservation transaction, duplicate scan rejection, and partial-LPN rejection.
- `Wms.Infrastructure/WarehouseWork/PickWarehouseWorkCompletionHandler.cs`: scanner contract and atomic execution boundary.
- `Wms.Infrastructure/Database/Migrations/20260921035314_AddReservationLinksToWarehouseWorkLines.cs`: reservation links on work lines.
- `Wms.Infrastructure/Inventory/InventoryReservationService.cs`: exact allocation consumption and reuse of an outer work transaction.
- `Wms.Infrastructure/Services/StockMovementService.cs`: exact inventory-status source lookup and optional ledger delegation for composed movements.

Focused qualification passed 3/3 tests. Debug Infrastructure and ASP builds pass with 0 warnings and 0 errors, and the migration lifecycle gate remains required after the final commit.

## Remaining #55 scope

This is progress, not closure. The remaining acceptance work is:

- short, damage, not-found, alternate/substitute, and supervisor override exception documents;
- target-LPN/container flow and complete serial/LPN concurrency matrices;
- reconnect/offline replay and concurrent duplicate-scan qualification;
- picker productivity and order picked-quantity projections;
- handheld/PWA browser journey in English and Arabic with both LTR and RTL layouts;
- PostgreSQL contention/provider/volume evidence and end-to-end order-to-pack qualification.

Remote issue state remains open until the committed local work is authorized for push and the remaining acceptance gates are evidenced.
