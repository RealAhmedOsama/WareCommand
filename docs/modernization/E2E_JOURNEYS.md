# End-to-end warehouse journeys

## Current slice

Issue #96 currently has executable, versioned journey specifications in
`Wms.Application/Journeys`. The catalog records inbound PO/ASN/receipt/QC/
putaway, outbound order/allocation/release/pick/pack/ship, and Arabic transfer
source/in-transit/destination flows. Each journey declares expected states,
reference types, retry/concurrency intent, locale, and a zero-error
reconciliation requirement across documents, work, reservations, balances,
ledger, serials, and license plates.

The result contract rejects a passing run with reconciliation errors, and retry
journeys must identify an explicit retryable step. These contracts are the
executable specification boundary; they do not claim that a real PostgreSQL
journey runner is complete.

## Remaining qualification

Add independent real-service runners for every critical happy/partial/failure
journey, including returns, count variance, replenishment, expiry/recall,
ownership, LPN, lot/serial, kitting, cancellation, reversal, retries, and
concurrency. Run them against isolated PostgreSQL schemas after migrations and
assert all affected aggregates plus ledger/balance/reservation reconciliation.
