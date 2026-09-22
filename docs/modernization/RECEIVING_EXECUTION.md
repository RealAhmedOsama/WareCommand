# Scanner-first receiving execution

Issue #46 adds a durable operator session boundary for inbound receiving. A
session is separate from a receipt document: one session can contain many
client scans and each successful scan still delegates the stock mutation to the
transactional receiving use case and its persisted receipt.

## Implemented boundary

- `ReceivingSession`, `ReceivingSessionLine`, and `ReceivingSessionScan` keep
  the warehouse/location, source reference, PO/ASN demand snapshots, prior
  received quantity, tolerances, expected lot/expiry/serial/LPN identities,
  scan status, movement/receipt references, revisions, errors, and audit-safe
  operation identifiers.
- Sessions support PO, ASN, dock-arrival, external-reference, and supervisor-
  authorized blind receiving. Pause/resume, partial completion, cancellation,
  supervisor correction, and reason capture are domain-guarded.
- The scanner API accepts raw item/package/GTIN/GS1 values plus explicit lot,
  expiry, serial, UOM, quantity, destination, license-plate, and demand-line
  data. PO/ASN lines enforce remaining quantity, over-delivery tolerance, and
  pre-advised lot/expiry/serial/LPN identity where supplied.
- Each scan has a unique `(session, clientOperationId)` key. Completed scans
  replay without executing stock mutation again; failed/pending scans can be
  retried with the same operation ID. The receiving UI persists retryable
  network failures in local storage and exposes a retry queue.
- Authorized LPN creation, receiving session/scan audit actions, and
  compensating receipt correction are wired through the existing permission,
  receipt, stock-movement, and audit boundaries. Receipt reversal also rolls
  back PO/ASN received totals and reopens closed source lines when required.
- `Receiving/Receive.cshtml` now includes a keyboard/scanner-first session
  surface with large controls, Enter/F1/F2 handling, persisted retry queue,
  session ledger, and layout-inherited English LTR / Arabic RTL support.
- Migration: `20260920230458_AddScannerReceivingSessions`.

## Local evidence

- `Wms.Domain.Tests/Entities/ReceivingSessionTests.cs`: blind override,
  pause/resume, prior-received quantity, tolerance, partial completion, and
  correction-history invariants.
- `Wms.Infrastructure.Tests/Receiving/ReceivingExecutionServiceTests.cs`:
  start idempotency, lifecycle persistence, paused-scan rejection, successful
  scan ledger persistence, and replay without a second receiving mutation.
- `Wms.Application.Tests` receiving qualification: 12 focused
  receive/idempotency tests passed, including the persisted-receipt fail-closed
  boundary.
- Disposable PostgreSQL verification passed 20/20. The provider run includes a
  regression proving `(ReceivingSessionId, ClientOperationId)` rejects a
  duplicate scan in one session while allowing the same client operation in a
  different session; the test container was removed after the run.
- Debug builds of Infrastructure and ASP: 0 warnings, 0 errors.
- `scripts/verify-migrations.ps1`: migration lifecycle verification is required
  before the scoped commit.

## Boundary and remaining qualification

This is a locally committed progress slice. No migration was applied to
production, nothing was pushed, and the remote issue remains open pending an
authorized push. A browser handheld run, PostgreSQL volume/concurrency test,
offline device qualification, label-printer qualification, and live provider
qualification remain release gates. Quality inspection, putaway orchestration,
returns, wave/allocation, and downstream inbound projections consume the
receipt/session boundary in their owning issues.
