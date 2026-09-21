# Sales-Order Allocation and Release

## Implemented slice

Issue #53 now coordinates confirmed sales-order lines with the existing inventory reservation ledger and warehouse-work engine. Each line uses a deterministic `SalesOrderLine` demand key, so retries return the same reservation instead of creating a second demand reservation. Replanning uses the reservation service's release-and-reselect path and is blocked after pick work has been released until that work is explicitly unreleased.

Eligible inventory is selected inside the reservation transaction using warehouse, pickable-location, active/available/allocatable-status, lot expiry, serial placement, license-plate placement, and item unit rules. FEFO items sort by lot expiry, then location priority, receipt age, and balance ID; other items use location priority, receipt age, and balance ID. Inventory-balance revision tokens and the ledger's canonical ordering protect parallel reservations from silently reserving the same quantity twice; a concurrency conflict is returned for retry.

## Commands and API boundary

- `GET /api/allocations/{salesOrderId}` returns line-level reservation state, selected dimensions, active quantity, consumed quantity, backorder quantity, and linked pick work.
- `POST /api/allocations/{salesOrderId}/simulate` evaluates inventory without writing reservations or work and returns candidate-level selection/rejection explanations.
- `POST /api/allocations/{salesOrderId}/allocate` allocates all or selected lines and can release the resulting reservations to pick work.
- `POST /api/allocations/{salesOrderId}/release` creates one available Pick work item per active reservation allocation using a deterministic creation key.
- `POST /api/allocations/{salesOrderId}/reallocate` safely reselects inventory only before released work has executed.
- `POST /api/allocations/{salesOrderId}/unrelease` cancels unreleased pick work and releases reservations back to available inventory.
- `POST /api/allocations/{salesOrderId}/cancel` cancels unreleased pick work and reservation demand; picking, exception, completed-work, and consumed-reservation states are rejected.
- `POST /api/allocations/batch` processes requested orders in priority, requested-ship-date, and ID order and reports per-order failures without silently discarding successful orders.

Partial allocation is represented on the sales-order line as the difference between original demand, active/consumed allocation, and cancellation. Release is blocked when a shortage exists and the order snapshot disallows partial shipment. Every command requires an idempotency key at the API boundary; reservation demand keys and work creation/command keys provide durable replay protection at the mutation boundaries.

## Evidence

- `Wms.Infrastructure.Tests/SalesOrders/SalesOrderAllocationServiceTests.cs`: 3 focused integration tests passed.
- `Wms.Infrastructure.Tests/Inventory/InventoryReservationServiceTests.cs`: 5 reservation lifecycle tests passed.
- `Wms.Infrastructure.Tests/WarehouseWork/WarehouseWorkServiceTests.cs`: 4 work lifecycle tests passed.
- `Wms.Infrastructure.Tests/SalesOrders/SalesOrderServiceTests.cs`: 3 sales-order regression tests passed.
- Debug Infrastructure and ASP builds passed with 0 warnings and 0 errors.
- `scripts/verify-migrations.ps1` remains green; #53 adds no schema migration.

This is a committed local progress slice, not issue closure. Allocation-workbench screens, handheld/browser EN/AR RTL/LTR qualification, PostgreSQL parallel contention and volume evidence, full lot/serial/LPN matrices, pick/pack/ship consumption wiring, and broader end-to-end reconciliation remain open dependencies. No production migration, deployment, push, or remote issue closure was performed.
