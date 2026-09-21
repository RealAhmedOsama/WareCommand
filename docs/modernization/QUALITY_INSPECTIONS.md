# Quality inspections

WareCommand now has a durable inbound quality boundary for receipt lines.

## Contract

- `QualityProfile` scopes a required inspection by warehouse, supplier, item,
  item category, and receipt source.
- Sampling is explicit: fixed quantity, percentage, every Nth license plate,
  or full inspection.
- `QualityProfileTest` defines a typed numeric, text, boolean, or choice test.
  Results store the captured value, evaluated pass/fail outcome, quantity,
  notes, and attachment references.
- `QualityInspection` is generated once per receipt line and license plate
  identity. It snapshots the receipt, item, supplier, lot, serial, LPN, and
  inbound inventory status.
- `QualityInspectionDisposition` is a structured stock decision. Pass,
  partial pass, fail, retest, hold, return-to-vendor, rework, damage, and
  scrap map to the persisted inventory-status transition system and retain the
  status movement reference.

## API

- `GET /api/quality/profiles`
- `POST /api/quality/profiles`
- `POST /api/quality/profiles/{id}/active`
- `GET /api/quality/inspections`
- `GET /api/quality/inspections/{id}`
- `POST /api/quality/inspections/{id}/results`
- `POST /api/quality/inspections/{id}/dispositions`
- `POST /api/quality/inspections/{id}/close`

Quality-pending, quarantine, hold, damaged, return-pending, and scrap-pending
statuses remain non-allocatable/non-pickable through the existing inventory
status rules. Supervisor overrides require `quality.override`, a reason, and an
immutable audit record. Closed inspections reject ordinary edits.

## Current boundary

Receipt finalization invokes exact-once inspection generation, and generic
required profiles participate in inbound QC-pending status resolution. The
handheld inspection screen, supplier trend reporting, physical return/rework/
scrap work documents, and full PostgreSQL/concurrency qualification remain
tracked dependencies in the backlog; this issue does not mark those later
workflow contracts complete.
