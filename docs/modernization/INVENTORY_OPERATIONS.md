# Inventory operation boundaries

Issue #38 is being delivered as a staged repair because the existing movement
service still maintains the legacy `Stock` projection while the ledger and
reservation engines are being adopted. The current slice makes the shared
movement boundary explicit without claiming that the entire workflow cutover
is complete.

## Current operation contract

`StockMovementService` remains the single application-facing boundary for
receipt, putaway, pick, and adjustment. The application use cases perform
authorization, unit conversion, lot resolution, command idempotency, and one
transaction around the mutation plus `SaveChangesAsync`/commit. The service
rechecks item, location, status, lot, serial, capacity, warehouse, and
handling-unit invariants before changing data.

The LPN selector is now carried through DTOs, MVC bind allowlists, use cases,
stock lookup, legacy stock rows, movement rows, ledger balance keys, and serial
lifecycle state. A missing selector means the unwrapped stock dimension; it no
longer permits a query to choose an arbitrary LPN row. Putaway preserves one
LPN across both legs and only moves the physical LPN location when the complete
unreserved LPN content is moved. Partial LPN putaway is rejected until the
content ledger can represent that split safely.

Adjustments retain the existing absolute `Movement.Quantity` compatibility
field and now persist `AdjustmentBeforeQuantity`, signed `AdjustmentDelta`, and
`AdjustmentAfterQuantity`. Audit output includes the signed delta as well.
Migration: `20260920173950_AddMovementAdjustmentHistory`.

## Remaining #38 scope

- Make the ledger/balance service authoritative for all four operations rather
  than retaining the legacy `Stock` mutation as a compatibility projection.
- Add safe reversal commands and complete reason/reference/correlation
  propagation for every adapter.
- Reconcile partial LPN content moves, target-LPN putaway, and scanner/desktop
  entry points with the LPN content history service.
- Finish the full PostgreSQL contention, retry/response-loss, Web, and WinForms
  matrix for these operations.
- Wire the remaining sales, transfer, replenishment, work, backorder,
  pick/ship/return, and document-line adapters from their owning issues.
