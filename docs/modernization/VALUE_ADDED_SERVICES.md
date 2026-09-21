# Value-added services

## Local implementation boundary

Value-added service (VAS) orders cover warehouse light assembly, kitting,
bundling, disassembly, unbundling, relabeling, repacking, inspection, and
configured custom work. They are not a manufacturing-planning or full MRP
engine. A kit definition is versioned by code and effective dates; releasing
an order snapshots its BOM version and English/Arabic instructions so later
kit edits cannot change the released work.

## Inventory, work, and traceability invariants

- Assembly input lines are reserved through the inventory reservation service.
  Approved substitutions are tried only when the original component is short,
  and the released line records the substituted item and original component.
- Completion consumes reserved inputs with explicit
  `ValueAddedConsumption` or `ValueAddedScrap` ledger entries and produces
  finished stock with `ValueAddedProduction`. The service requires each
  completion to account for the proportional BOM quantity; quantities remain
  bounded by each released input line until the additional-input/rework
  adapter is qualified.
- Output lot, serial, LPN, status, and ownership dimensions are validated
  before production. Serial receipt/pick/correction state and immutable
  ownership snapshots follow the same transaction as the ledger operation.
- Every material-to-output and material-to-scrap relationship is a durable
  trace link. The genealogy endpoint returns the input-to-output direction;
  the same links can be traversed from the output line back to its inputs.
- Partial completion, scrap, cancellation before production, and reversal
  after production are explicit audited states. Reversal writes compensating
  `ValueAddedReversal` legs and is idempotent, so it cannot create or destroy
  stock on retry.
- Released orders create `WarehouseWorkType.ValueAddedService` work with
  station/team, queue, line dimensions, reservation links, and snapshotted
  instructions. Work completion is atomic with material changes and is
  command-idempotent.

The authenticated `/api/value-added-services` surface provides versioned kit
creation/read, order creation/listing, release, completion, cancellation,
reversal, and genealogy. Mutations use typed permissions, antiforgery
validation, warehouse authorization, audit actions, and bounded request
validation. Label-template and quality-profile references are retained on the
order; an active quality profile must be scoped to the requested warehouse and
output item when configured.

## Qualification status

Focused SQLite tests cover assembly consumption/production and reversal,
partial completion with scrap/yield, shortage compensation, disassembly
genealogy and reversal, approved substitution, duplicate completion/reversal
replay, and balanced ledger/stock outcomes. The migration is generated but
not applied here, and local Infrastructure/ASP builds must remain warning-free.

This is committed progress rather than final closure. PostgreSQL concurrency
and warehouse-volume evidence, reviewed migration/restore rehearsal, complete
additional-input/rework and quality/label execution adapters, scanner/browser
screens, EN/AR RTL/LTR qualification, provider integrations, production
deployment, push, and remote issue synchronization remain open or dependent
gates.
