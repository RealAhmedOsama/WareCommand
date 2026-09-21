# Inventory ownership and consignment

## Local implementation boundary

Inventory ownership is a first-class dimension independent from physical
warehouse location. The supported owner kinds are `CompanyOwned`,
`SupplierConsignment`, `CustomerOwned`, and `ExternalOwner`. Company-owned
stock is the built-in `COMPANY` dimension; every other owner uses an active
owner master record and an immutable owner-code snapshot.

Owner identity is carried through stock, inventory balances and immutable
transactions, reservations and allocations, movements, receipts, warehouse
work, transfers, cycle-count snapshots, license-plate content, shipment
package content, and shipment confirmation. Owner dimensions are included in
the unique balance/stock identities, so balances cannot merge across owners.

## Operational invariants

- A receipt or movement must carry one normalized owner dimension. Supplier,
  customer, and external owner links are validated against the owner master;
  ownership is not inferred silently from a document party.
- Reservation candidates default to company-owned stock. A demand must supply
  an exact owner kind, owner id, and owner-code snapshot to consume another
  owner, and inactive owner records are not eligible.
- Ownership changes use an approved, idempotent ownership-transfer command.
  The command moves quantity between source and destination owner balances,
  records two immutable `OwnershipTransfer` ledger legs, stores source and
  destination snapshots, and emits an audit record. Reusing the idempotency
  key with a different request is rejected.
- Status changes, adjustments, putaway, picking, packing, shipment, and LPN
  moves preserve the owner dimension. LPN content and shipment content also
  retain it; a mutable LPN rejects content from a different owner.
- Cycle-count lines capture the owner snapshot from the balance being counted,
  so count work cannot silently move a count across owner dimensions.
- Inquiry, summaries, movement reports, reconciliation keys, and reservation
  diagnostics expose or group by owner dimensions. Existing rows are migrated
  deterministically as company-owned. The ownership report API also provides
  owner-scoped balances with age, usage aggregates, and an immutable statement.

## API and persistence boundary

`/api/inventory/ownership` provides owner list/detail/create/deactivate and
ownership-transfer/list operations. `/api/inventory/ownership/reports`
provides owner-scoped balances, ageing, usage, and statement rows. Mutations
use warehouse authorization, typed ownership permissions, antiforgery
protection, rate limiting, audit records, and idempotency where a command
changes stock. The generated
`AddInventoryOwnership` migration creates owner masters/transfers, adds owner
columns and indexes to existing dimensions, and uses `OwnerKind = 1` plus
`OwnerCodeSnapshot = COMPANY` for all pre-existing rows. The migration has not
been applied to a production or shared database.

## Local evidence

- `Wms.Infrastructure.Tests/Inventory/InventoryOwnershipServiceTests.cs`
  covers a two-leg owner transfer, source/destination quantities, report
  balance/usage/statement reconciliation, replay, and idempotency conflict.
- `InventoryLedgerServiceTests` covers separate company/external balances and
  reconciliation.
- `InventoryReservationServiceTests` covers default company-only eligibility
  and explicit external-owner allocation.
- `ReceiptServiceTests` covers explicit owner preservation from receipt plan to
  receipt line, and `LicensePlateServiceTests` rejects mixed-owner content.
- Existing cycle-count, LPN, packing, and inventory-inquiry tests pass with
  the expanded dimensions.
- Infrastructure and ASP builds are warning-free, and the EF migration is
  generated but intentionally not applied here.

## Qualification status

This is committed local progress, not closure of backlog issue #78. Provider
backed PostgreSQL concurrency and warehouse-volume evidence, reviewed
production migration/restore execution, supplier/customer master wiring in
every external import/integration, complete browser and handheld owner
screens, and localized EN/AR RTL/LTR UX remain dependent qualification gates.
The remote issue remains open until this commit is pushed and those gates are
reviewed.
