# Sales Orders and Outbound Demand

## Implemented slice

Issue #52 now has a warehouse-scoped `SalesOrder` aggregate with immutable customer and ship-to snapshots, line-level item/UOM/packaging snapshots, ordered/allocated/picked/packed/shipped/cancelled quantities, revision protection, and explicit Draft, Confirmed, Held, Cancelled, Closed, Exception, and downstream allocation states.

The service validates active customer/ship-to/item references, warehouse ownership, customer item references, quantity conversions, partial-shipment policy, duplicate external references, and controlled lifecycle transitions. Order numbers are allocated per warehouse as `SO-{warehouse}-{sequence}`. Draft updates can replace lines and refresh master-data snapshots; confirmed orders retain their persisted snapshots.

## API boundary

- `GET /api/sales-orders` and `GET /api/sales-orders/{id}` provide authorized warehouse-scoped search, paging, sorting, and details.
- `POST /api/sales-orders` and `PUT /api/sales-orders/{id}` create and update drafts.
- `POST /api/sales-orders/{id}/confirm`, `/hold`, `/release-hold`, `/cancel`, and `/close` provide guarded lifecycle commands with audit entries.
- `POST /api/sales-orders/import` accepts grouped CSV demand rows with row-level validation and idempotent external-reference checks.
- `GET /api/sales-orders/export` emits the documented outbound-demand CSV shape.

`sales-orders.read` and `sales-orders.manage` are separate permissions. Warehouse managers receive manage access through the default role profile; receivers receive read access only. Every mutation validates the caller's warehouse scope and records the corresponding audit action.

## Persistence and safety

`SalesOrders` and `SalesOrderLines` use restrictive customer, ship-to, warehouse, item, UOM, packaging, and source-reference relationships. Line quantities are kept in the ordered UOM and converted to the item's base quantity through the existing conversion service. Backorder is calculated from ordered, allocated, and cancelled quantities, while downstream reservation/allocation remains owned by #53.

Migration: `20260921025547_AddSalesOrders`.

## Evidence and remaining work

- `Wms.Domain.Tests/Entities/SalesOrderTests.cs`: 3 focused domain tests passed.
- `Wms.Infrastructure.Tests/SalesOrders/SalesOrderServiceTests.cs`: 3 focused service tests passed.
- `Wms.ASP/Controllers/SalesOrdersController.cs`.
- Debug ASP build passed with 0 warnings and 0 errors.
- `scripts/verify-migrations.ps1` passed.

This is a committed local progress slice, not issue closure. #53 still owns reservation-backed allocation, release-to-warehouse, FIFO/FEFO selection, partial allocation, and backorder planning. Browser/handheld screens, Arabic/English RTL/LTR visual qualification, PostgreSQL concurrency/volume/provider qualification, split shipment/packing/shipping, after-release amendments, and full end-to-end qualification remain open dependencies. No production migration, deployment, push, or remote issue closure was performed.
