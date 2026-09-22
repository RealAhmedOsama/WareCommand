# Inventory Classification

Issue #63 now has a durable warehouse-scoped ABC classification foundation.

## Deterministic calculation

`InventoryClassificationPolicy` defines one effective-dated policy per warehouse
window:

- `ShippedQuantity`: total shipped quantity in the lookback window.
- `ShippedLines`: count of shipped movement rows.
- `MovementVelocity`: pick, ship, and transfer quantity divided by lookback days.
- `InventoryValue`: positive on-hand quantity multiplied by the item's standard cost.
- `Criticality`: hazardous (4), temperature-controlled (3), quality-required (2),
  special-handling (1), plus a bounded margin contribution of up to 2 points.

Items are ordered by metric descending and ID ascending. The running share is
compared with the configurable A and B thresholds; a first item that crosses a
threshold still receives that tier, so a warehouse with one active item does not
produce a misleading C-only result. Items at or below the minimum activity value,
or with no usable metric, are explicitly `Unclassified`.

The calculation stores the lookback window, all input metrics, policy revision,
input version (`abc-v1`), and a deterministic run key. Repeating the same policy
and `AsOfUtc` does not append duplicate history.

## Safety and operations

- Results are isolated by `(WarehouseId, ItemId)`.
- Dry-run recalculation returns current/proposed comparisons without persistence.
- Manual overrides require `inventory.adjust`, a reason, and optional future expiry.
  An active override is never silently overwritten by scheduled recalculation; an
  expired override is replaced by the next automatic result and the history explains
  why.
- Every effective change and override is captured in
  `InventoryClassificationHistories` and the immutable audit stream.
- `wms.inventory-classification-recalculation` runs daily in the maintenance queue
  and processes bounded item batches.
- Cycle-count plans using `ItemClass` now select active ABC classifications rather
  than the legacy item-category shortcut. Replenishment signals expose the active
  class and prioritize A, then B, then C within each signal kind.

The disposable PostgreSQL harness passes the targeted warehouse-scope proof
1/1. It confirms unique policy keys within a warehouse, one current result per
warehouse/item, and independent policy/result rows for the same item in another
warehouse. The container and verification port were clean after the run.

## API surface

- `GET/POST/PUT /api/inventory/classifications/policies`
- `GET /api/inventory/classifications`
- `GET /api/inventory/classifications/history`
- `POST /api/inventory/classifications/recalculate?dryRun=true|false`
- `PUT /api/inventory/classifications/overrides`

## Remaining gates

This is a committed progress slice, not a closure claim for #63. Dashboard and
bulk override/import UX, downstream slotting/service-alert consumers, PostgreSQL
volume/deadlock qualification, and browser/handheld EN/AR RTL/LTR evidence remain
open. No production migration, deployment, push, or remote issue closure was done.
