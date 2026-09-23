# Dashboard read contract

`GET /Dashboard/RefreshData` returns the same `DashboardReadSnapshot` used by the initial `/Dashboard` render. The route requires an authenticated principal with `dashboard.view`, uses the existing report rate limit, sends `Cache-Control: no-store`, and honors request cancellation.

An optional `warehouseId` selects one warehouse. The server checks that warehouse against the caller's dashboard scope, then each section's existing read service checks its own permission and warehouse scope. A denied refresh returns HTTP 403 without dashboard data. Omitting the parameter reads all warehouses in the caller's authorized scope, except a user assigned to exactly one warehouse is resolved to that warehouse. The response's `warehouseScope` records `SelectedWarehouse` or `AllAuthorizedWarehouses`.

The time zone is the selected warehouse's configured time zone. For an all-warehouse scope, the global WMS localization time zone is used. `businessDate` and expiry totals use that effective zone. Recent movements include both configured business dates, converted to a half-open UTC range by the report query. The catalog counts are company-wide because items are not warehouse-owned.

```json
{
  "warehouseId": 12,
  "warehouseScope": "SelectedWarehouse",
  "timeZoneId": "America/Chicago",
  "businessDate": "2026-09-23",
  "generatedAtUtc": "2026-09-23T16:30:00+00:00",
  "recentMovementPeriodDays": 7,
  "refreshIntervalSeconds": 300,
  "availableWarehouses": [],
  "catalog": {
    "status": "Available",
    "data": { "totalItems": 125, "activeItems": 117 },
    "errorCode": null
  },
  "inventory": {
    "status": "Available",
    "data": {
      "stockKeepingUnits": 42,
      "onHandQuantity": 170.5,
      "reservedQuantity": 18,
      "availableQuantity": 152.5,
      "heldQuantity": 2,
      "damagedQuantity": 1,
      "expiredQuantity": 0,
      "expiringQuantity": 8,
      "stockLocations": 16
    },
    "errorCode": null
  },
  "lowStockSignals": { "status": "Available", "data": [], "errorCode": null },
  "recentMovements": { "status": "Available", "data": [], "errorCode": null }
}
```

`status` is `Available`, `Forbidden`, or `Unavailable`. A `null` `data` value is never interpreted as zero; a successful empty result uses `Available` with an empty list or zero-valued metrics. Error codes are machine-readable and do not include exception details. Catalog counts are grouped in the database. Inventory values come from `IInventoryInquiryService`; recent movements come from the authorized report query with one database page and a maximum of 200 rows; replenishment signals come from the policy service. No item list or movement history is loaded to calculate the dashboard.

The page refreshes its displayed values and recent lists in place. During a request it shows a loading state; on a failed request it keeps the last successful values and timestamp and reports the failure. Partial snapshots mark only failed or forbidden sections unavailable.
