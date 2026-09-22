# Inbound receipt documents

Issue #45 adds the durable receipt document boundary between inbound demand and
inventory movements.

## Implemented boundary

- `Receipt` and `ReceiptLine` persist warehouse, supplier, PO/ASN, dock,
  receiving location, source references, user/timestamp, item/UOM/package
  snapshots, lot/expiry/serial/LPN/status snapshots, expected and received
  quantities, accepted/rejected/damaged/quarantined quantities, and remaining
  quantity.
- Receipt lifecycle transitions are domain-guarded across Draft, Open,
  Receiving, PartiallyCompleted, Completed, Exception, Cancelled, Reversed,
  and Corrected.
- `ReceiveItemUseCase` opens and persists the receipt and line in the same
  transaction before creating the stock movement. The movement must carry both
  receipt IDs, and finalization records the receipt-line movement history before
  completing the document.
- The receiving use case now fails closed when `IReceiptService` is unavailable;
  it cannot fall back to a stock-only movement. The receipt plan is opened and
  finalized on every successful receiving path, including PO/ASN receiving.
- PO/ASN receipt plans are consumed by finalization in the caller-owned
  transaction, so source progress, allocation history, the receipt document,
  the stock movement, and audit entries commit together.
- Reversal creates a compensating adjustment movement, preserves the original
  receipt-line movement, records the reason and related movement, and prevents a
  second reversal. Correction reverses the original and creates a linked draft
  correction document.
- Receipt-line links provide durable references for quality inspections,
  putaway work, and labels. API and localized English/Arabic MVC list, create,
  details, print, search, export, lifecycle, link, reversal, and correction
  routes are permission-gated by warehouse scope.
- Migration: `20260920222012_AddInboundReceipts`.

## Local evidence

- `Wms.Domain.Tests/Entities/ReceiptTests.cs`: lifecycle, quantity balance, and
  movement-history invariants.
- `Wms.Infrastructure.Tests/Receiving/ReceiptServiceTests.cs`: persisted-open
  ordering, movement mismatch rejection, completion, reversal history, and
  duplicate-reversal protection.
- `ReceiveItemUseCaseTests` plus `ReceiveItemIdempotencyTests`: 12/12 passed,
  including the missing-receipt-service fail-closed regression.
- Receipt/receiving infrastructure tests: 8/8 passed.
- Full Debug solution tests: 679 passed, 20 provider-gated/data-migration
  skips.
- Full Debug solution build and Release Application build: 0 warnings, 0
  errors.
- `scripts/verify-migrations.ps1`: no pending model changes; migration lifecycle
  verification passed.

## Boundary and remaining qualification

This is a locally committed progress slice. No migration was applied to
production, no external provider qualification was claimed, nothing was pushed,
and the remote issue remains open pending an authorized push. Quality,
putaway-label, returns, and downstream inbound projections consume the durable
receipt-line link boundary in their owning issues. Browser visual/RTL-LTR,
Desktop/scanner, PostgreSQL volume/concurrency, and live-provider qualification
remain separate release gates.
