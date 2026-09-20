# Inventory statuses

Issue #32 adds a persisted inventory-status dimension to physical stock. A stock
identity is now `(item, location, lot, serial, inventory status)`; restricted
quantity remains visible and is not silently treated as pickable quantity.

## System catalog

The migration seeds these global, non-deletable statuses:

| Code | Default policy |
| --- | --- |
| `AVAILABLE` | available, allocatable, pickable, shippable, countable |
| `RESERVED` | restricted from allocation; reservation workflows remain dependent on their owning issue |
| `QC_PENDING` | restricted until a controlled release transition |
| `QUARANTINE` | restricted and forced for quarantine locations |
| `HOLD` | restricted until a controlled release transition |
| `DAMAGED` | restricted and forced for damaged locations |
| `EXPIRED` | restricted from allocation, picking, and shipping |
| `RETURN_PENDING` | restricted and forced for returns locations |
| `SCRAP_PENDING` | restricted with no release transition in the system catalog |

Status rules are server-side flags for allocation, picking, shipping, and
counting. Warehouse-specific statuses may be added with a normalized code,
localized names, and an optional forced location type. System statuses cannot be
disabled; custom statuses in use by stock or movement history cannot be disabled.

## Controlled changes

Every configured transition is explicit and may require a reason. A partial
quantity change keeps the item, location, lot, serial, and other stock identity
dimensions intact. Serial-controlled stock must move as a complete serial unit.

A status change writes two positive `StatusChange` movement legs with the same
quantity and reference: an outbound leg for the source status and an inbound leg
for the target status. The stock split, movement legs, audit record, and status
identity are saved in one unit-of-work transaction and stock uses the updated-at
concurrency token.

Receipt resolves `QC_PENDING` for quality-inspection items and honors forced
location status. Putaway preserves the source status unless the destination
location forces another status. Picking and FEFO candidate selection reject
inactive, non-allocatable, or non-pickable stock. Inventory inquiry reports both
physical quantity and status/net availability.

## Remaining scope

Reserve, transfer, count approval, pack, ship, replenishment, and return
workflows are not present in the current model. Their status eligibility and
transitions remain dependencies of the owning backlog issues; #32 is therefore a
committed progress slice until those workflows are implemented and qualified.
