# WareCommand audit logging

Issue #14 adds a shared, append-only audit boundary for security-sensitive and
inventory-affecting activity. The audit record is deliberately separate from
the legacy authentication event table so operational searches have one
consistent shape.

## Runtime contract

`IAuditWriter.RecordAsync` adds an `AuditEntry` to the current `WmsDbContext`
unit of work; it does not call `SaveChanges`. A business mutation must add its
audit record before the same save/transaction. The `WmsDbContext` save hooks
reject modified or deleted `AuditEntry` instances. The PostgreSQL migration
creates `WmsAuditEntries` and indexes time, actor, warehouse, action, and
entity lookups.

Every entry carries:

- the action and entity type/id;
- UTC time from `IClock` and a sortable Unix-millisecond value;
- actor user ID/name, warehouse, correlation ID, source client, remote address,
  user agent, success state, and bounded details;
- safe before/after JSON. `AuditMetadataRedactor` removes credential,
  token, connection-string, security-stamp, and sensitive personal-data keys,
  bounds depth/collection/string/payload size, and marks unsupported values
  instead of serializing object graphs.

Web requests initialize `IRequestContext` from the correlation header and
request metadata. Desktop composition selects `Desktop` as the source. API
and job adapters must initialize a source-specific correlation ID and pass an
explicit service actor for non-user work; they must never use a shared
`SYSTEM` fallback for a user action. `IWarehouseContext` carries the active
warehouse for records that do not provide one explicitly.

The shared stock movement service records receipt, putaway, pick, and stock
adjustment events at the mutation boundary. Item/location master-data use
cases, access assignment changes, and authentication events use the same
writer. The action catalog already reserves transfer, count approval,
allocation, pack, shipment, return, settings, and integration actions for the
remaining workflows so those workflows can use the same contract.

## Query and export

`audit.read` is granted to administrators, warehouse managers, and auditors by
default. The MVC audit screen is permission protected, warehouse scoped, and
server-side paginated. It filters by UTC date range, actor ID, warehouse,
action, and entity type. A user without global warehouse access can see only
global entries and entries for assigned active warehouses.

The screen's CSV export applies the identical authorization and filters, emits
the already-redacted fields, and is capped at 10,000 newest matching rows. It
is a read-only export; exported files must be handled as sensitive operational
evidence and stored only in an approved restricted location.

## Retention and database protection

The application role must have `SELECT` and `INSERT` on `WmsAuditEntries` but
not `UPDATE`, `DELETE`, or `TRUNCATE`. Migration/maintenance ownership is kept
separate from the runtime role. The application-level append-only guard is
still required for SQLite/local qualification and catches accidental changes
before SQL is sent.

The operational baseline is seven years, or the longer applicable legal,
contractual, or incident-response requirement. Retention is handled outside
normal request paths:

1. Export/archive closed periods to encrypted, access-logged immutable storage.
2. Verify the archive checksum and restore a sample before retiring a period.
3. Purge only with the separately authorized maintenance role, a recorded
   change ticket, and a tested restore path; never expose purge through the
   audit UI or ordinary application role.
4. Review retention and archive access at least annually.

This issue provides the schema, writer, redaction, scoped search, and bounded
export. Production role grants, archive storage, and the scheduled retention
job remain deployment-specific gates and are not performed by local
qualification.

## Evidence

- `Wms.Infrastructure.Tests/Auditing/AuditEntryTests.cs` covers context
  capture, redaction, append-only enforcement, rollback, filtering, paging,
  and warehouse scope.
- `Wms.Infrastructure.Tests/Services/StockMovementServiceTests.cs` verifies a
  receipt mutation and its audit record use the same boundary.
- `Wms.ASP.Tests/AuthorizationFlowTests.cs` verifies permission denial and
  auditor rendering for the audit screen.
