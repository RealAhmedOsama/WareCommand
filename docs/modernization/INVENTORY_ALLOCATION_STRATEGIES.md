# Inventory allocation strategies

Issue #64 adds a policy-driven allocation boundary to reservation and outbound
work creation.

## Policy contract

`InventoryAllocationStrategyPolicy` is effective-dated per warehouse and can
be scoped by item, item category, and demand type. Matching policies are
resolved by selector specificity, then effective start, revision, and ID. The
policy snapshot is captured on a reservation and copied to allocation-backed
warehouse work, so later policy edits do not silently rewrite existing demand
or executable work.

Supported strategies are:

- FIFO: earliest receipt date, then location priority and balance ID.
- FEFO: earliest expiry date, then receipt date, location priority, and ID.
- LIFO: latest receipt date, then location priority and ID.
- Fixed location: only the configured location is eligible.
- Location priority, nearest, and route priority: the configured location
  priority is the deterministic operational route key.

Receipt dates come from the first positive opening, receipt, putaway, or return
ledger row for the complete inventory dimension, with the balance creation time
as a deterministic fallback. Expired or otherwise ineligible lots are rejected;
minimum shelf-life policies reject lots before the cutoff. FEFO policies make
missing-expiry behavior explicit by ranking it last or falling back to receipt
date. Whole-license-plate preference promotes a complete LPN when it can satisfy
the requested quantity; otherwise normal ranked allocation remains available.

## Interfaces

- Policy search/create/update: `/api/inventory/allocation-strategies/policies`
- Dry-run ranking and explanations:
  `/api/inventory/allocation-strategies/simulate`
- Existing reservation simulation now returns the effective strategy reason for
  every candidate.

The simulation surface is read-only. It reports the selected rank, rejected
reason, receipt/expiry evidence, and remaining quantity without changing the
ledger or reservation state.

## Qualification

- `InventoryAllocationStrategyServiceTests`: 4/4 focused tests passed.
- `InventoryReservationServiceTests`: 6/6 regression tests passed.
- Debug builds of Infrastructure and ASP: 0 warnings, 0 errors after generated
  migration warning suppression.
- Migration: `20260921080706_AddInventoryAllocationStrategies`.

Live PostgreSQL migration execution, provider-backed performance evidence, and
browser/UI acceptance remain deployment/provider gates. No remote issue state
was changed; the local execution tracker records the committed progress and
those remaining gates.
