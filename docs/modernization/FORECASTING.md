# Demand forecasting and inventory-risk projections

## Runtime boundary

The deterministic engine and source adapter live in Wms.Application/Forecasting.
The adapter uses completed ShipmentLine quantities at each shipment's
ActualShipAtUtc, converted into the item's current base unit through active,
item-specific conversion paths. A shipment without an actual ship time, including
an unshipped cancellation, contributes no demand.

Physically received customer returns are negative net-demand events on the
warehouse-local receipt period. A linked return uses its original shipment-line
unit; an unlinked return uses the current item base unit. Negative actual net
demand is retained for backtesting, while projected demand is bounded at zero.
Returns received after a run's source cutoff cannot change that run.

Daily, Monday-based weekly, and monthly series use the warehouse time zone.
Only completed periods through the persisted UTC source cutoff are included.
Missing periods between the first completed shipment and the last completed
period are explicit zeroes; no periods before the first completed shipment are
invented. History is bounded to 365 daily, 520 weekly, or 120 monthly periods.
At least three non-censored observations are required by the baseline engine.

Stockout censoring is based on intra-period ledger transitions and period-end
available inventory reconstructed backward from current balance snapshots and
the immutable inventory ledger. A 50,000-row ledger cap bounds the
reconstruction. Missing snapshots, unsupported unit paths, invalid time zones,
or cap overflow set a data-quality flag and make projected stock risk Unknown;
they do not claim a verified no-stockout history.
Unreceived purchase orders and inbound quantities are not included in the supply
projection, which is called out on every run.

## Versioned runs and API

Migration 20260923152346_AddDurableForecasting creates:

- ForecastRuns: scope, source cutoff and fingerprint, model/version and
  parameters, input window, backtest scores, data-quality flags, risk and
  staleness evidence.
- ForecastRunPoints: immutable normalized historical actuals, censoring
  markers, baseline forecasts, and measured-error bounds.
- ForecastOverrides: separate append-only, reasoned override versions.

Runs are deduplicated by warehouse, item, granularity, horizon, and the
deterministic input fingerprint. A source or policy change creates a new run;
it does not rewrite a prior run. WmsDbContext rejects updates and deletes to
runs, points, and overrides.

The authenticated /api/forecasting routes provide warehouse-scoped
pagination, run details, actual-versus-forecast metrics, and bounded CSV export.
Read access requires reports.read and inventory.read. POST
/api/forecasting/recalculate requires forecasting.recalculate, antiforgery,
and a warehouse scope; it queues the existing durable job and returns 202.
The daily scheduled job rotates through at most 500 item/warehouse pairs per
execution. Hosts with durable jobs disabled return 503 for explicit requests.

POST /api/forecasting/{runId}/overrides requires forecasting.override.
Overrides name a forecast period, non-negative base-UOM quantity, and reason.
Each version is separately audited and the baseline run remains unchanged. CSV
uses quoted fields and formula-safe text cells and rejects runs exceeding 1,000
points.

Forecasting is advisory. It cannot create a purchase order, reservation,
allocation, or inventory transaction. Backtest MAE, MAPE, and bias are measured
on the persisted series; they are not an accuracy guarantee.

## Migration and recovery

Deploy the additive migration through the normal database-migration process
after taking the standard database backup. The migration does not rewrite
operational inventory or order records. Downgrading past
AddDurableForecasting drops all forecast runs, points, and overrides; recover
those records from the pre-migration backup or roll forward with a corrective
migration. Do not use a destructive downgrade as routine deployment rollback.

## Qualification boundary

Application tests cover local-time bucketing, sparse zero periods, return timing,
UOM conversion, source-cutoff exclusion, signed backtesting, idempotent run
reuse, changed-source versioning, persisted overrides, append-only enforcement,
and warehouse scope. PostgreSQL HTTP coverage exercises migration-backed reads
and safe export. Forecast charts, provider/production load qualification, and
purchase-order/in-transit supply integration remain separate work.
