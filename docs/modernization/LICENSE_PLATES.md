# License plates and handling units

WareCommand models a physical handling unit as a `LicensePlate` (LPN). An LPN
may be a pallet, tote, carton, crate, container, bag, or another configured
unit. The number is unique across the installation; an SSCC is accepted only
after its 18-digit GS1 check digit is validated.

## Inventory contract

`LicensePlateContent` is the LPN-scoped identity of item, lot, serial, status,
packaging, and quantity. The service keeps that content reconciled with the
matching LPN-specific `Stock` balance and writes the corresponding `Movement`
and `LicensePlateHistory` records in one unit-of-work mutation. A serialised
content row always has quantity one.

The physical location and parent relationship are stored on the LPN. Moving a
root LPN moves its descendants and their stock balances; a nested LPN cannot be
moved independently. The service rejects cross-warehouse operations, hierarchy
cycles, mixed content disallowed by the location policy, mutations of packed or
shipped units, and shipment while reservations remain.

## Lifecycle and traceability

The supported lifecycle is open, closed/packed, shipped, returned, and voided.
History records include content receipt, split/merge/content move, whole-LPN
move, nesting, lifecycle changes, locations, references, and the responsible
user. Number sequences are warehouse-scoped, configurable, revisioned, and
used for generated LPN numbers.

The HTTP surface is exposed at `/api/license-plates` with warehouse-scoped
reads, history, content, movement, nesting, packing, shipping, return, void,
and numbering operations. Mutations require the matching WMS permission and
anti-forgery protection.

## Current qualification boundary

The domain, relational model, migration
`20260920145329_AddLicensePlateHandlingUnits`, service, API, and focused SQLite
tests are implemented locally. Existing receive, putaway, pick, allocation,
transfer, replenishment, count, pack, ship, and return workflows still need
their owning backlog issues to pass the LPN identity through their mutation
boundary. Those workflow integrations must call the LPN service or an
equivalent shared inventory mutation path; they must not independently change
LPN content or subtract shipment stock.

The migration is checked in but is not applied to production by normal startup.
Apply it only through the controlled PostgreSQL migration procedure after the
release backup and rollback gates are authorized.
