# Inbound Exceptions

## Boundary

Inbound exceptions are durable, warehouse-scoped queue records for blocked receiving. Each record may link a receipt and line, ASN and line, receiving session, license plate, item, staging location, and warehouse work. The idempotency key is unique within a warehouse so a repeated handheld submission returns the original record instead of duplicating quantity or work.

The existing ASN lifecycle still owns dock assignment and check-in. The exception service adds the cross-document exception ledger and preserves the ASN/receipt state change in the same database transaction.

## Codes and controls

The standardized codes are unknown item, missing PO, missing ASN, over/short receipt, damaged package, invalid lot/expiry/serial, duplicate LPN, capacity/no-location, rejected quality, and document mismatch. Each record has severity, queue, due time, owner/team, notes, bounded attachment references, quantities, and an immutable audit trail of creation, assignment, review, and resolution.

Supported resolutions are hold, supervisor review, correct source data, blind receipt approval, return to vendor, quarantine, repack/relabel, and cancel. Supervisor-sensitive resolutions require `receiving.override` and a reason. Resolved source-data/approval/repack flows resume an exception receipt/ASN and requeue linked warehouse work. Return/quarantine/cancel routes do not make stock generally available.

When a receipt line already has stock, creation refuses to capture an exception while the matching stock remains in the system `AVAILABLE` status. The operator must first use the existing inventory-status workflow to move it to HOLD, QUARANTINE, DAMAGED, or another non-available status. This prevents a blocked case from silently becoming allocatable stock; status disposition and physical return/repack documents remain downstream work.

## API

- `GET /api/inbound-exceptions` supports warehouse, code, severity, status, queue, owner, receipt/session, search, closed, and overdue filters and returns queue counters.
- `POST /api/inbound-exceptions` creates an idempotent exception and atomically marks linked receipt/ASN/work state as blocked where applicable.
- `POST /api/inbound-exceptions/{id}/assign` assigns an owner or team.
- `POST /api/inbound-exceptions/{id}/review` starts supervisor review.
- `POST /api/inbound-exceptions/{id}/resolve` applies a controlled resolution.

## Evidence

- Domain lifecycle: `Wms.Domain.Tests/Entities/InboundExceptionTests.cs`
- SQLite service/concurrency-safety boundary: `Wms.Infrastructure.Tests/Inbound/InboundExceptionServiceTests.cs`
- Persistence: `Wms.Infrastructure/Database/Migrations/20260921015804_AddInboundExceptions.cs`
- API: `Wms.ASP/Controllers/InboundExceptionsController.cs`

Browser supervisor screens, handheld exception capture, attachment storage, provider/volume qualification, and physical return/repack/quarantine documents remain open #50 work and are not claimed by this slice.
