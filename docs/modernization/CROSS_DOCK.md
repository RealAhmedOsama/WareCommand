# Cross-dock planning

Issue #67 now has a durable, explainable planning boundary for planned and
opportunistic cross-dock decisions.

## Current behavior

- `CrossDockPolicy` is warehouse-scoped, effective-dated, priority ordered, and
  can match item/category, supplier, inbound source, customer, and sales order.
- Policies can require expiry, enforce a minimum shelf-life window, select a
  staging/packing/shipping destination, and separately allow planned or
  opportunistic matching.
- Simulation selects confirmed outbound demand deterministically by order
  priority, requested ship date, order ID, and line number.
- Existing active cross-dock plan lines are subtracted before a new simulation
  match, so repeated planning does not silently claim the same demand twice.
- Receipt snapshots preserve source receipt/line, PO/ASN references through the
  receipt line, UOM, accepted/received quantity, inventory status, lot, serial,
  LPN, receiving location, destination, policy, order, customer, and item.
- Excess, rejected, damaged, quarantined, blocked-status, and unmatched
  quantities remain explicit fallback quantities. Simulation and plan creation
  do not write stock, reservations, or ledger movements.
- Plan creation is warehouse-local and replay-safe through a unique creation
  key; cancellation is allowed only before a future execution boundary.

## Deliberate boundary

This slice does not claim execution. Reservation creation, concurrency-safe
source claiming, receiving-to-staging movement, cross-dock warehouse work,
short/excess replan, packing/shipping handoff, cancellation release, KPI views,
provider contention, high-volume qualification, and handheld/browser EN/AR
RTL/LTR evidence remain open. The persisted status and explanation identify a
plan as `Matched` or `Fallback`; they do not mean that inventory moved.

The next implementation slice must connect the plan to the existing reservation
and warehouse-work transaction boundaries, reject or replan when a normal
putaway work item already owns the receipt movement, and add exactly-once
PostgreSQL/concurrency and receive-to-ship tests before #67 can be closed.
