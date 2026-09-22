# Demand forecasting and inventory-risk projections

## Current slice

Issue #105 currently has an application-owned deterministic baseline engine in
`Wms.Application/Forecasting`. It supports item/warehouse-scoped daily,
weekly, and monthly horizons; moving-average and simple-exponential-smoothing
baselines; walk-forward backtest error metrics; stockout-period censoring;
confidence bounds derived from measured error; repeatable input fingerprints;
days-of-supply and projected-stockout calculations; and an explicit risk
level. Forecast output is always marked `CanCreateOrders = false`.

The engine returns `InsufficientData` or `StockoutCensored` instead of making a
confident prediction from a cold-start or fully censored history. The selected
baseline is chosen by measured backtest error with deterministic tie-breaking.
No forecast directly creates orders, reservations, or stock movements.

## Remaining qualification

Inputs still need normal report/history adapters for shipped demand,
cancellations, returns, seasonality, promotions, lead times, and data-quality
flags. Remaining work includes durable versioned forecast storage, model
comparison UI/API/export, scheduled idempotent recalculation, manual override
history, actual-versus-forecast monitoring, warehouse/provider/load
qualification, and production operations. Optional ML models remain a later
provider-governed advisory layer and cannot replace deterministic WMS rules.
