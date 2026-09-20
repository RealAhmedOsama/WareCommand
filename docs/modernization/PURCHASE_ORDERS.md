# Purchase orders and inbound demand

## Current local boundary

Issue #43 is implemented as a committed inbound-demand slice. A purchase
order stores the warehouse and supplier identity plus immutable code/name
snapshots, dates, external/source metadata, currency, notes, lifecycle audit
fields, a revision token, and ordered lines. Each line snapshots the item,
supplier item reference, entered and base quantities, unit-of-measure
conversion factor/path/rules/rounding, and over/under-delivery tolerances.

The domain lifecycle is:

`Draft -> Confirmed -> PartiallyReceived -> Received -> Closed`

with controlled cancellation from `Draft` or `Confirmed`, and reopening from
`Cancelled` or `Closed`. Confirming revalidates active warehouse, supplier,
item, and unit-of-measure masters. A line rejects receipts above its captured
over-delivery tolerance and can close only after its captured under-delivery
minimum is met. Receipt allocations are append-only and retain the canonical
movement conversion fields and actor/timestamp/reference metadata.

`IPurchaseOrderService` enforces purchase-order read/manage permissions and
warehouse scope, allocates warehouse-scoped `PO-{warehouse}-{sequence}`
numbers with the sequence revision token, rejects duplicate supplier/source/
external references, and writes lifecycle/receipt/import audit records.
`ReceiveItemUseCase` validates the optional purchase-order line before stock
movement creation and records the allocation before the existing receiving
transaction commits, so inventory and inbound demand cannot commit separately.

The API surface is `/api/purchase-orders`; the MVC surface is
`PurchaseOrderManagementController` with paginated list/details/create/edit,
confirm/cancel/close/reopen, CSV import/export, and English/Arabic resource
coverage. Import currently creates one purchase order per data row and
returns row-level errors; multi-line grouping and an atomic batch-import
contract remain future integration work.

## Evidence

- `Wms.Domain.Tests/Entities/PurchaseOrderTests.cs`
- `Wms.Infrastructure.Tests/Purchasing/PurchaseOrderServiceTests.cs`
- `Wms.Domain/Entities/PurchaseOrder.cs`
- `Wms.Domain/Entities/PurchaseOrderLine.cs`
- `Wms.Infrastructure/Purchasing/PurchaseOrderService.cs`
- `Wms.Application/Purchasing/PurchaseOrderContracts.cs`
- `Wms.ASP/Controllers/PurchaseOrdersController.cs`
- `Wms.ASP/Controllers/PurchaseOrderManagementController.cs`
- `Wms.Infrastructure/Database/Migrations/20260920201043_AddPurchaseOrders.cs`

## Remaining issue scope

This is a committed progress slice, not closure of backlog issue #43. ASN,
receipt-document, quality, return, and downstream ordered/inbound projections
still need to consume the purchase-order contract. Remaining qualification
includes browser screenshots/E2E for both cultures and RTL/LTR, Desktop and
scanner adapters, PostgreSQL migration/query/concurrency/load tests, and
provider-specific import/export and response-loss behavior. Multi-line/grouped
imports, authorized document-level overrides, and complete external-integration
adapters remain with the owning follow-up issues.

The migration is checked in but has not been applied to production. No push,
deployment, production migration, or remote issue closure was performed.
