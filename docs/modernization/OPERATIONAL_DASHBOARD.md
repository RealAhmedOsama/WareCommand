# Operational dashboard and KPI boundary

This document records the bounded implementation slice for issue #74. The
MVC and WinForms dashboards now use server-side, warehouse-scoped inventory
facts and no longer present a quantity as a currency-valued stock metric. The
dashboards are not yet the complete inbound, outbound, accuracy, or
productivity command center described by the issue.

## Inventory KPI definitions

All quantities are canonical base-unit quantities from `InventoryBalances`.
When the application is operating against legacy stock during migration, the
same aggregate is calculated directly by the database query over `Stock`; no
stock-row collection is materialized in the controller.

| KPI | Definition |
| --- | --- |
| Stock-keeping units | Distinct item IDs with a non-zero on-hand or reserved balance in the authorized warehouse scope. |
| On hand | Sum of `OnHandQuantity` across the authorized balance scope. |
| Reserved | Sum of `ReservedQuantity` across the authorized balance scope. |
| Available | Sum of `OnHandQuantity - ReservedQuantity`; this is not a second inventory source. |
| Held | On-hand quantity whose canonical inventory status is `HOLD`. |
| Damaged | On-hand quantity whose canonical inventory status is `DAMAGED`. |
| Expired | On-hand quantity whose canonical inventory status is `EXPIRED`. |
| Expiring soon | On-hand quantity with a lot expiry date from the business date through the configured expiry-warning horizon, excluding canonical `EXPIRED` status. |
| Stock locations | Distinct location IDs containing a non-zero or reserved balance in the authorized scope. |

The dashboard deliberately does not show inventory value. A monetary KPI
requires an explicit, effective costing basis and currency policy; quantity
is not a valid substitute for that value.

## Current implementation

- `IInventoryInquiryService.GetDashboardMetricsAsync` authorizes the request,
  applies the warehouse scope and inquiry filters, and performs one grouped
  server-side aggregate over the canonical balance read model.
- The MVC controller consumes this read model and no longer calls the legacy
  `GetAllStockAsync` path to count locations or calls a list-valued stock
  summary to derive a KPI.
- The WinForms dashboard KPI cards use the same aggregate contract; its
  separate low-stock grid remains a bounded policy/settings-driven list.
- The dashboard displays on-hand, reserved, available, held, damaged,
  expired, and expiring quantities in English and Arabic.
- Replenishment alerts remain policy-driven and limited by the configured
  alert-list limit. The visible list is not presented as a complete count of
  low-stock, out-of-stock, or overstock policies.

## Remaining issue #74 gates

This is progress only. Closure still requires optimized read models and
benchmarks for inbound expected/receiving/dock-to-stock/QC/putaway, outbound
allocation/shortage/pick/pack/ship, count accuracy/variance/reconciliation,
and workforce productivity/queue age; warehouse/date/shift filters; drilldown
contracts; trend and snapshot storage; permission-aware exports; policy-signal
counts; valid effective-dated costing; provider-backed query-plan and
warehouse-size evidence; and browser/handheld EN/AR RTL/LTR qualification.
Those pieces depend on the owning workflow and reporting issues rather than
being inferred from the current dashboard page.
