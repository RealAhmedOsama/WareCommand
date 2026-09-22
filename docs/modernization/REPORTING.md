# Reporting platform

## Current boundary

The report read path is `IReportQueryService` in `Wms.Application.Reporting`, implemented by `OperationalReportQueryService`. It returns report DTOs and metadata rather than EF entities. The first supported report is the movement ledger exposed at `/Reports` and `/Reports/Export`.

Every interactive query and export requires `reports.read`. The query service applies the requested warehouse authorization before building the query, then limits rows to the caller's assigned warehouse scope. A movement with two location legs is visible only when both legs are inside the caller's scope. Export reuses the same query contract and page size, so it cannot bypass the on-screen filters or scope.

## Query contract

The movement ledger supports combined filters for:

- inclusive business dates in the configured localization time zone;
- warehouse, free-text search, item SKU, location, movement type, user, reference, lot, serial, and license plate;
- stable sorting by timestamp, SKU, quantity, movement type, or user;
- server-side page size and page number; and
- server-side grouping by item, location, movement type, or user.

The service counts and pages in the database. Only the selected page is projected into `MovementReportDto`. Group totals use the mapped canonical movement quantity and remain bounded to the requested group page.

## Quantity and time definitions

- `Quantity` and `BaseQuantity` mean the immutable canonical base quantity recorded on the movement. They are not currency or stock value.
- Display-unit conversion is optional and is applied only to the bounded result page through the shared unit-conversion service.
- `FromDate` and `ToDate` are inclusive warehouse-business dates. The query converts the range to UTC using the typed localization time-zone setting.
- `GeneratedAtUtc`, `DataCutoffUtc`, and the authenticated user ID are included in the result metadata together with the effective time zone and normalized filters.

## Export boundary

CSV export streams UTF-8 with a BOM so Excel and Arabic text open correctly. It writes the same projected ledger rows as the screen, follows the configured maximum report row limit, checks cancellation between pages, and does not materialize the full report before the response begins.

PDF/Excel renderers, persisted report artifacts, scheduled recipient/channel configuration, retention/legal-hold policy, and external delivery are intentionally owned by the reporting, notification, retention, and integration backlog issues (#83, #84, #90–#93). The existing idempotent `wms.report-generation` job remains a bounded scheduled summary notification until those shared delivery boundaries are implemented.

## Qualification

The focused `ReportsFlowTests` rerun passed 3/3 on 2026-09-22 for combined
filters/scope paging, grouped aggregation, UTF-8 CSV export, and
English/Arabic direction. PDF/Excel, scheduled delivery, retention, and
provider-scale gates remain open.

`Wms.ASP.Tests/ReportsFlowTests.cs` covers combined filters, date boundaries, paging, warehouse isolation, grouped aggregation/page clamping, UTF-8 CSV export, and English/Arabic document direction. `Wms.Application.Tests/Localization/WmsLocalizationResourceTests.cs` covers resource parity. The ASP.NET and Infrastructure projects must build with zero warnings and errors before this slice is committed.
