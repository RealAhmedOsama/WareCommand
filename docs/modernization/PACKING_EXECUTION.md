# Packing execution

## Scope

The packing boundary verifies picked outbound stock, moves it into a package
license plate at a packing station, records package contents and measurements,
and closes the package without automatically shipping it.

The implementation covers:

- warehouse-scoped packing stations and station availability;
- order, shipment/wave/reference, and staging-LPN session metadata;
- carton, tote, pallet, bag, and custom package types;
- exact item, order-line, lot, serial, inventory-status, source-location, and
  source-LPN scan identity;
- split and consolidated package ownership through package contents;
- expected weight, actual weight, tolerances, dimensions, volume, and label
  reference;
- atomic stock movement, LPN content, serial-location, order-line packed
  quantity, ledger, movement, audit, and command-idempotency updates;
- controlled close, reopen, remove, void, and session completion.

## API surface

The controller is `Wms.ASP/Controllers/PackingController.cs` under
`/api/packing`.

- `POST /stations` and `POST /stations/{stationId}/status`
- `POST /sessions`, `GET /sessions/{sessionId}`, and
  `POST /sessions/{sessionId}/complete`
- `POST /packages`, `GET /packages/{packageId}`
- `POST /packages/{packageId}/pack`
- `POST /packages/{packageId}/close`
- `POST /packages/{packageId}/remove`
- `POST /packages/{packageId}/reopen`
- `POST /packages/{packageId}/void`

All mutating package/session operations require an idempotency key and the
packing permission. Management of station definitions/status additionally
requires settings management permission.

## Invariants

- A package target LPN must be active, open/returned, empty when opened, and
  physically located at the station.
- A pack scan must match the picked order line, remaining picked quantity,
  source stock dimensions, and serial identity. Serial scans are exactly one
  unit.
- A package cannot close empty or outside its configured weight tolerance.
- A closed package is immutable until an explicit reopen command. Removal
  requires the package to be open; voiding requires zero packed quantity.
- A session cannot complete while any package remains open.
- Stock, package content, order-line packed quantity, movement, inventory
  ledger, audit, and command record are committed together.

## Qualification

Focused relational tests are in
`Wms.Infrastructure.Tests/Packing/PackingServiceTests.cs` and cover:

- normal packing, target-LPN transfer, weight/dimension capture, replay, and
  session completion;
- wrong order ownership and weight-variance rejection;
- close/reopen/remove/void lifecycle and restoration of source stock.

The checked-in schema migration is
`Wms.Infrastructure/Database/Migrations/20260921043412_AddPackingExecution.cs`.

## Remaining release gates

This slice does not claim full issue closure. Provider qualification against the
PostgreSQL harness, scanner/device reconnect behavior, production scale and
concurrent station testing, label rendering/printer integration, and the
downstream shipping workflow remain open dependencies. Shipment creation and
automatic shipment confirmation are intentionally outside this issue.
