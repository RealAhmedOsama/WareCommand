# Advance shipping notices and expected inbound loads

## Current local boundary

Issue #44 is implemented as a committed expected-inbound slice. An advance
shipping notice (ASN) stores a warehouse and supplier, expected arrival window,
carrier and vehicle/container references, tracking and external/source
references, a sanitized source payload, optional dock assignment, lifecycle
audit fields, a revision token, and expected lines. Each line snapshots the
item, unit of measure, conversion, optional packaging, quantity tolerances,
and pre-advised lot, expiry, serial, and license-plate/SSCC identity.

The ASN lifecycle is:

`Draft -> Submitted -> Expected -> Arrived -> Receiving -> Completed`

with controlled cancellation before receipt history and an explicit
`Exception` path for discrepancies after check-in. A discrepancy is append
only and records its kind, expected/received/variance quantities, details, and
actor/time. Completion closes only lines that satisfy their captured
under-delivery and over-delivery tolerances.

`IAdvanceShippingNoticeService` enforces warehouse and supplier scope,
validates active item/UOM/packaging/dock references, allocates warehouse-scoped
`ASN-{warehouse}-{sequence}` numbers, prevents duplicate supplier/source/
external references, and reserves linked open purchase-order quantities across
multiple ASNs. It supports API and CSV create/update/submit/expected/dock/
arrive/exception/complete/cancel/discrepancy operations, export, audit actions,
and row-level import errors. Imported source payloads redact credential-like
fields before persistence.

Arrival only records the expected load. It does not create inventory. When a
receiving command references an ASN, the receiving use case validates the ASN
line, lot/expiry/serial/LPN/SSCC pre-advice, and optional purchase-order line,
creates the canonical movement, and appends ASN and purchase-order receipt
allocations in the same transaction. This keeps physical stock mutation and
inbound-document allocation atomic and prevents an arrival/check-in from
duplicating stock.

The API surface is `/api/advance-shipping-notices`. The MVC surface is
`AdvanceShippingNoticeManagementController` with localized English/Arabic
list/details/create/edit/import screens and lifecycle actions. Dedicated read
and manage permissions, audit actions, migration configuration, and receiving
form pass-through are wired locally.

## Evidence

- `Wms.Domain.Tests/Entities/AdvanceShippingNoticeTests.cs`
- `Wms.Infrastructure.Tests/Inbound/AdvanceShippingNoticeServiceTests.cs`
- `Wms.Application.Tests/UseCases/Receiving/ReceiveItemUseCaseTests.cs`
- `Wms.Domain/Entities/AdvanceShippingNotice.cs`
- `Wms.Infrastructure/Inbound/AdvanceShippingNoticeService.cs`
- `Wms.Application/Inbound/AdvanceShippingNoticeContracts.cs`
- `Wms.ASP/Controllers/AdvanceShippingNoticesController.cs`
- `Wms.ASP/Controllers/AdvanceShippingNoticeManagementController.cs`
- `Wms.Infrastructure/Database/Migrations/20260920211634_AddAdvanceShippingNotices.cs`
- `scripts/verify-migrations.ps1`

## Remaining issue scope

This is a committed progress slice, not closure of backlog issue #44. Receipt
sessions, quality inspection, returns, and downstream inbound/ordered
projections still need to consume the ASN contract. Import is currently
row-oriented; grouped multi-line and atomic batch-import behavior, complete
external API/import idempotency, and authorized document-level overrides remain
with their owning workflows.

Qualification still required includes browser screenshots/E2E for English and
Arabic in both RTL and LTR, Desktop and scanner adapters, external-provider
integration, and PostgreSQL migration/query/volume/concurrency/provider tests.
The migration is checked in but has not been applied to production. No push,
deployment, production migration, or remote issue closure was performed.
