# Outbound wave planning

Issue #65 adds a durable outbound-wave boundary around the existing sales-order
allocation and warehouse-work services. A wave groups eligible demand by
warehouse, applies deterministic selection criteria, records step outcomes, and
can be resumed without creating duplicate allocation or pick work.

## Planning contract

`WaveTemplate` stores reusable manual, scheduled, and rule-based criteria:

- warehouse, priority, capacity, requested ship-date range, carrier, customer,
  minimum priority, source type, and release behavior;
- an active flag, schedule expression, revision, and unique warehouse-local
  template key; and
- audit coverage for create/update changes.

Wave creation selects confirmed, allocating, or partially allocated sales-order
demand in priority-descending, ship-date-ascending, order-ID, line-number
order. It excludes demand already present in a non-terminal wave, takes a
bounded capacity, stores the criteria JSON, and records a successful
`SelectDemand` history row. Creation keys are unique per warehouse and replay
the existing wave.

## Processing and cancellation contract

`WaveService` processes a bounded batch of lines and saves after each line. The
line idempotency key is derived from the process key and wave-line ID, so a
retry can safely replay the existing allocation boundary. The service records
allocation, replenishment, work-creation, validation, release, and completion
step history with attempts, counts, and error details.

Allocation and pick-work creation remain owned by
`ISalesOrderAllocationService`; optional replenishment generation is invoked
for shortages. A wave becomes `Released` only when every active line is
released, `Completed` when non-release processing finishes, `PartiallyProcessed`
when work remains or shortages/errors exist, and `Failed` when all active lines
fail. Released or execution-started waves cannot be cancelled. Before release,
an unprocessed or failed line can be removed, and cancellation releases
reservation-backed lines through the existing allocation cancellation path.

## API and scheduling

`Wms.ASP/Controllers/WavesController.cs` exposes:

- `/api/outbound/waves` for search, detail, simulation, creation, processing,
  line removal, and cancellation;
- `/api/outbound/waves/templates` for template search/create/update; and
- the scheduled `wms.wave-planning` job, which runs active scheduled templates
  in a five-minute idempotency slot.

Mutation endpoints require `allocation.manage` (template writes require
`inventory.adjust`), an authenticated actor, and the global antiforgery policy.
Read paths enforce warehouse scope through `IWarehouseAccessService`.

## Schema and qualification

The durable schema is
`Wms.Infrastructure/Database/Migrations/20260921084632_AddOutboundWaves.cs`
with `WaveTemplates`, `Waves`, `WaveLines`, and `WaveProcessingHistory`.
Unique creation keys, wave numbers, per-wave line identity, and per-step
attempts are database constraints; active demand conflict detection is also
performed against non-terminal waves before selection.

Focused SQLite coverage in
`Wms.Infrastructure.Tests/Outbound/WaveServiceTests.cs` passed 5/5 for
template scheduling, deterministic selection, creation replay, duplicate
processing suppression, and remove/cancel state guards. Debug ASP and
Infrastructure builds passed with 0 warnings and 0 errors. Migration listing
and idempotent PostgreSQL script generation passed; no live database update was
attempted because the configured PostgreSQL endpoint requires a password.

This is committed #65 progress, not full closure. Provider-backed concurrent
wave/high-volume qualification, full shortage/replenishment retry evidence,
multi-line/serial/LPN strategy matrices, workbench and throughput screens,
browser/handheld English-Arabic RTL/LTR evidence, and production migration or
deployment remain open gates. The local execution tracker is authoritative for
those dependencies.
