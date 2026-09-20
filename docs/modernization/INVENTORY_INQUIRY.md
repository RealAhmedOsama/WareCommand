# Inventory inquiry and availability

Issue #39 is being delivered in bounded read-model slices. This slice moves
the ASP inventory page from the legacy `Stock` collection to an authorized,
server-side query over the canonical `InventoryBalances` materialization. It
does not claim that transaction traceability, reservation-detail screens, or
every desktop/scanner caller has been migrated.

## Query contract

`IInventoryInquiryService` accepts composable filters for warehouse, location,
item, lot, serial, LPN, status, expiry range, exact scan value, normalized
text search, available-only rows, zero-balance inclusion, sort direction, and
page/page-size. The service applies the warehouse authorization scope before
counting, sorting, projecting, or paging. A requested warehouse is also passed
through `IWarehouseAccessService.AuthorizeAsync`; a limited user cannot read a
different warehouse by changing the query string.

Exact scan lookup covers SKU, item barcode, location code/barcode, lot,
serial, LPN, and status code. Text search covers the same operational
identifiers plus item/location/status names. PostgreSQL uses `ILIKE`; the
fallback provider path uses translated `LIKE`/uppercase expressions.

The detailed result is a `StockDto` read model, never an EF entity. The query
executes `COUNT`, `ORDER BY`, `SKIP`, `TAKE`, and projection in the database.
The summary path uses database `GROUP BY` and aggregates, then materializes
only the small summary result. No `GetAll().Where/GroupBy` operation remains
on the ASP inventory route.

## Quantity terminology

- `QuantityAvailable` is retained as the compatibility name for physical
  on-hand quantity.
- `QuantityReserved` is the reserved quantity stored on the balance.
- `AvailableQuantity` is physical available: on-hand minus reserved.
- `AvailableToPromiseQuantity` is currently the non-negative physical
  available quantity only when the item, location, and inventory status are
  active and allocatable. The reservation ledger is already included in the
  reserved balance; supply/order demand is not modeled yet.
- `HeldQuantity` reports on-hand in the `HOLD` status.
- `InTransitQuantity` and `OrderedQuantity` are explicit zero values until
  inbound transfer and order/supply read models exist.

## Remaining #39 scope

- Add transaction-history, reservation-detail, and forward/backward
  traceability projections over the immutable ledger, reservation events,
  serial lifecycle, lot history, and LPN content history.
- Add export-friendly query specifications and bounded export execution rather
  than turning the page query into an unbounded download.
- Migrate remaining desktop/dashboard/scanner inquiry callers from legacy
  `Stock` reads.
- Measure PostgreSQL plans at representative volume and add only measured
  indexes; the current slice intentionally adds no speculative index
  migration.
- Complete PostgreSQL provider, high-volume, and cross-client qualification
  before closing the issue.
