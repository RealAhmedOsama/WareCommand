# Customer and Ship-To Master Data

## Implemented slice

Issue #51 now has a durable customer master with normalized unique customer, ERP, and channel identifiers; localized/legal/contact/billing data; carrier/service, priority, packaging, label, and partial-shipment defaults; active/inactive lifecycle; and notes.

Each customer can own multiple normalized ship-to addresses with recipient/contact data, delivery instructions, delivery windows, active/default state, and customer item references. A customer has at most one active default destination. Omitted existing addresses/references are deactivated during an update instead of being physically deleted, preserving master history for later order/shipment/return foreign keys.

## API boundary

- `GET /api/customers` and `GET /api/customers/{id}` provide authorized search, paging, sorting, and historical inactive visibility.
- `POST /api/customers`, `PUT /api/customers/{id}`, `PATCH /api/customers/{id}/active`, and `DELETE /api/customers/{id}` provide guarded lifecycle management.
- `GET /api/customers/document-snapshot` returns scalar customer and ship-to values for a new outbound document. Callers must persist that result on confirmation; later master edits do not rewrite it.
- `GET /api/customers/resolve-item` resolves a customer SKU/barcode to an internal item while excluding inactive customers/references by default.
- `POST /api/customers/import` accepts the documented CSV shape and reports invalid row/field details before writes.
- `GET /api/customers/export` emits the same customer/ship-to CSV shape.

`customers.read` and `customers.manage` are separate permissions. The manager and receiver role defaults include read access; only the warehouse-manager/admin defaults include management access.

## Persistence and safety

`Customers`, `CustomerShipToAddresses`, and `CustomerItemReferences` use restrict foreign keys and unique indexes. Customer deletion is refused once ship-to or item-reference history exists; future sales-order, shipment, and return documents must add their own restrict references and immutable snapshots rather than weakening this boundary.

Migration: `20260921023248_AddCustomersAndShipToMasterData`.

## Evidence

- `Wms.Domain.Tests/Entities/CustomerTests.cs`
- `Wms.Infrastructure.Tests/Customers/CustomerManagementServiceTests.cs`
- `Wms.Infrastructure.Tests/Integration/PostgreSqlIntegrationTests_Harness.cs` proves normalized customer code, ERP identifier, and channel identifier duplicates are rejected while omitted external identifiers remain reusable; disposable PostgreSQL verification passed `25/25` with container cleanup.
- `Wms.ASP/Controllers/CustomersController.cs`

The remaining #51 qualification is the browser/handheld master-data UI, Arabic/English RTL/LTR visual run, PostgreSQL provider/volume run, and wiring into the #52 order confirmation and #55 return documents. Those are intentionally tracked as open dependencies rather than claimed complete by the backend slice.
