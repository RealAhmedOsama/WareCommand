# Explainable operational anomaly detection

## Delivered backend

Issue #106 established the deterministic, explainable signal and lifecycle
contract. Issue #127 connects that boundary to bounded operational sources,
versioned PostgreSQL settings, append-only runs/findings/observations/history,
durable scheduled and manual recalculation, warehouse-scoped investigation
routes, and the in-app notification service.

The detector still emits advisory findings only. It cannot change inventory,
accounts, or worker state, and a finding is not proof of fraud, error, or
misconduct. Finding source evidence is immutable; corrections and source
disappearance are recorded as later observations. Status, assignment, and
time-bounded suppression are the only mutable finding fields. Rule updates
insert a new version.

## Supported signal adapters

| Rule | Source query and window | Default threshold and notes |
|---|---|---|
| Inventory adjustment | Immutable `InventoryTransactions` rows with `Type=Adjustment` and `OccurredAtUtc` inside the requested UTC window. One signal per transaction. | 5 base units; compare absolute ledger delta with zero. |
| Reversal burst | Immutable `Type=Reversal` ledger rows in the requested window, grouped by UTC hour and warehouse. | 3 reversals in an hour. |
| Count variance | `CycleCountLines` with a recorded count whose task is AwaitingApproval, Approved, or Completed and whose submit time is in the requested window. Planned, active, recount-only, and cancelled work is excluded. | 1 base unit; compare counted quantity with expected quantity. |
| Duplicate scan | Completed `ReceivingSessionScans` requested in the window, grouped by receiving session and exact scan value in memory. Raw scan values are never persisted or logged; findings keep only a truncated SHA-256 digest and session ID. | At least one repeat beyond the first scan. |
| Receiving discrepancy | Nonterminal `InboundExceptions` with expected and actual base quantities and creation time in the window. Resolved and cancelled exceptions are excluded. | 1 base unit. |
| Shipping discrepancy | Nonterminal `OutboundExceptions` with expected and actual base quantities and creation time in the window. Resolved and cancelled exceptions are excluded. | 1 base unit. |
| Ageing work | Current nonterminal `WarehouseWorks`, measured from `DueAtUtc` or otherwise `CreatedAt`; age is whole hours at evaluation time. | 72 hours. This is a current-state snapshot, not a historical work-status reconstruction. |
| Integration failure | Warehouse-tagged integration outbox rows in terminal `DeadLettered` state whose dead-letter time is in the window. Transient failures are excluded. | 1 terminal dead-letter. Inbox rows have no warehouse key and are excluded with a data-quality flag. |
| Negative balance | Current `InventoryBalances` snapshot for the warehouse, where `OnHandQuantity < 0`. | Any negative quantity. This is a current-state snapshot, not a historical balance reconstruction. |

Each source query reads at most 1,001 rows. If that reveals more than the
1,000-row per-source cap, the rule is skipped for that run and a quality flag
is saved; partial source subsets are not evaluated. A run accepts at most
10,000 signals and creates at most 1,000 findings. Windows are limited to 31
days. Scheduled runs are capped at 250 active warehouses and use a fixed
seven-day UTC event window ending at the current UTC day boundary. Current
snapshot rules record evaluation time and values in the run fingerprint so an
unchanged retry reuses its run.

## Persistence and investigation

`AnomalyRuleConfigurations` stores immutable global or warehouse-specific
versions. A warehouse version takes precedence over the latest global version.
Initial global defaults are seeded by the migration; external notifications
remain disabled until explicitly enabled by a rule setting.

`AnomalyDetectionRuns` is unique by warehouse and normalized input fingerprint.
Findings use a stable warehouse/rule/source/version fingerprint. Repeated runs
append observations without changing the original source values, explanation,
or first-detection window. If a previously anomalous source is corrected or no
longer eligible, a later run appends its changed or absent observation. Rule
settings, runs, observations, and investigation history are append-only;
database uniqueness and a finding revision concurrency token protect retries
and concurrent investigation actions.

The lifecycle supports New, Investigating, Explained, ConfirmedIssue,
FalsePositive, Resolved, and Suppressed. Status changes require comments and
valid transitions. Suppressions expire within 31 days and scheduled processing
restores the prior status with a system history entry. Assignment requires a
user or team and is recorded in history. Queries and mutations authorize the
finding warehouse. Source type and record ID are redacted unless the caller
also has the underlying module permission; no anomaly endpoint bypasses the
source module's authorization.

New findings publish a deduplicated in-app alert to authorized warehouse
managers and inventory controllers. Email and webhook channels are added only
when the corresponding rule version explicitly opts in and the normal channel
configuration/preferences permit delivery. Alert links lead to the scoped
finding API; source access remains governed by the normal module routes.

## API and qualification boundary

`/api/anomalies` exposes bounded search/detail, durable recalculation enqueue,
assignment, lifecycle transitions, and versioned rule settings. A daily UTC
job runs through `WmsJobCatalog` and `WmsJobHandlerCatalog`. Relevant code is
under `Wms.Application/AnomalyDetection`,
`Wms.Infrastructure/AnomalyDetection`, and
`Wms.ASP/Controllers/AnomalyDetectionController.cs`.

Focused unit, SQLite service, and PostgreSQL integration qualification cover
the source adapters, idempotence, correction history, authorization,
concurrency, notification deduplication, source caps, and migration. Browser
UI wiring, localized/handheld investigation journeys, external-channel
provider delivery, representative load/contention, restore rehearsal, and
production evidence remain separate qualification work. No document or
unscoped integration-inbox signal is inferred where warehouse attribution is
not available.
