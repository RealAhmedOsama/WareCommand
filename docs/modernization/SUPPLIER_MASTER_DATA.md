# Supplier master data and receiving defaults

## Current local boundary

Supplier master data is persisted in `Suppliers` with normalized unique
supplier codes and normalized optional external ERP identifiers. The profile
stores localized/legal names, tax and registration references, structured
address/contact data, active status, notes, and receiving defaults:

- preferred active warehouse and active receivable dock;
- default lead time and bounded over/under-delivery tolerances;
- lot/expiry requirements, quality profile, label rule, and three-letter
  default currency code.

`SupplierItemReferences` maps a supplier vendor SKU/barcode to an internal
item and optional item packaging. Vendor identifiers are unique per supplier,
item and packaging foreign keys are restrictive, and inactive mappings remain
available for historical inspection but are excluded from normal resolution.

`ISupplierManagementService` enforces supplier-read/manage permissions,
preferred-warehouse visibility, receiving-location validity, duplicate
detection, bounded paging, normalized search, lifecycle transitions, and
immutable audit records. Deletion is refused when supplier-item mappings or
item-master default-supplier references exist; deactivation is the safe
historical alternative.

The web surface is `SupplierMasterController` with localized English and
Arabic list/details/create/edit/import/export screens. The API surface is
`/api/suppliers`, including the `resolve` route used by future ASN/receipt
imports. CSV import/export supports repeated supplier rows for item mappings,
quoted values, row-level numeric/boolean validation, warehouse/dock code
resolution, and an atomic all-or-nothing import transaction.

## Evidence

- `Wms.Infrastructure.Tests/Suppliers/SupplierManagementServiceTests.cs`
- `Wms.Domain/Entities/Supplier.cs`
- `Wms.Domain/Entities/SupplierItemReference.cs`
- `Wms.Application/Suppliers/SupplierManagementContracts.cs`
- `Wms.Infrastructure/Suppliers/SupplierManagementService.cs`
- `Wms.ASP/Controllers/SupplierMasterController.cs`
- `Wms.ASP/Controllers/SuppliersController.cs`
- `Wms.Infrastructure/Database/Migrations/20260920192411_AddSupplierMasterData.cs`

## Remaining issue scope

This is a committed progress slice, not closure of backlog issue #42. The
current model has no purchase-order, ASN, receipt, return, or quality-document
aggregate to snapshot supplier/default values at document creation. Their
future adapters must reject inactive suppliers, call the resolver, apply
defaults only when the document has no authorized override, and persist
historical snapshots so later supplier edits cannot rewrite history. Browser
E2E/localized visual qualification, PostgreSQL plan/volume qualification, and
desktop/scanner consumers also remain with the owning document/client issues.

No production migration, deployment, push, or remote issue closure was
performed.
