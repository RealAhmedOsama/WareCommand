# Serial-number tracking

## Identity policy

Serial identity is item-scoped. The authoritative key is the unique pair
`(ItemId, Number)`, where `Number` is trimmed and upper-cased with invariant
rules before persistence. The same printed serial may therefore exist for two
different item records, but it cannot be active twice for one item.

`SerialNumberId` is the authoritative relationship on stock and movement rows.
The existing serial-number text on those rows is retained as a compatibility
and display snapshot; new serial-controlled operations populate both values.

Serial-controlled quantity is exactly one for receipt, putaway, pick, and
positive adjustment. A zero adjustment is allowed to correct the serial out
of inventory, and the database check constraint prevents a serial-linked stock
row from carrying a quantity outside the `0..1` range.

## Lifecycle and controls

The persisted state tracks the current warehouse/location, optional lot,
optional license-plate value, receipt/shipment references, status, and last
movement time. Supported statuses are `Available`, `Hold`, `Quarantine`,
`Damaged`, `Shipped`, `Returned`, `Scrapped`, and `Corrected`.

Receipt, putaway, pick, and adjustment resolve the serial through the same
identity service. The service rejects a missing serial for serial-controlled
items, a serial on non-serial items, wrong-lot or wrong-location movement,
duplicate active receipt, migration-conflict allocation, and fractional or
multi-unit quantity. Status changes require a reason and are audited. Search,
traceability, status changes, and bounded bulk receipt are exposed through
`api/serial-numbers`.

Shipment, return, pack, count, transfer, reservation, and a first-class LPN
relationship remain dependent on the corresponding inventory workflow issues.
Until the LPN model is delivered, the serial record keeps only a nullable
license-plate string so serial identity is not blocked on that later model.

## Legacy migration

The PostgreSQL migration derives serial records from legacy stock and movement
text. It links unambiguous rows to `SerialNumberId`. Rows with conflicting
lots, locations, quantities, or overlong normalized values are created as
`Corrected` records with `HasMigrationConflict = true`; their source details
are written to `SerialNumberMigrationConflicts` and they remain unavailable
until an operator reconciles them. The data-migration tool emits the same
conflict report and refuses to treat those rows as allocation-ready.
