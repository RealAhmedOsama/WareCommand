# Web UI and backend contract coverage

This is a source map for the next approved UI design phase. It records the web
surface and operational contracts present in the WareCommand host; it is not a
visual specification or a statement that every WMS journey is complete. The
source review covers implementation commit `393ac25` and its formatting-only
correction `5f5d0a5` (2026-09-25).

## Handoff

- The retained Bootstrap/Razor host has functional server-rendered screens for
  the dashboard, inventory inquiry/adjustment, item and supplier master data,
  warehouses, locations, units of measure, purchase orders, ASNs, receipts,
  receiving, putaway, picking, reports, administration, settings, and audit.
  The screens exercise actual application services; their existence does not
  imply full end-to-end readiness for every warehouse journey.
- Customers, sales orders/allocation, most inventory dimensions and controls,
  quality, exceptions, warehouse work, packing/shipping/returns, approvals,
  notifications, attachments, labels, workforce, and integration administration
  have authorized service-backed HTTP contracts but no corresponding Razor
  operational screen in this host. They are **API-only** until a screen is
  designed and approved.
- Reporting assistant, forecasting, anomaly detection, and governed
  recommendations now have authorized Web API read/action surfaces and service
  tests. They are **API-only** until their operator screens are designed.
  Deterministic data generation is a bounded non-production PostgreSQL test
  fixture, not a customer-facing feature or seeding API. The closed #125–#129
  implementation slices do not imply visual or release qualification.
- The result-to-HTTP switches were inconsistent: 23 API controllers could turn
  `BusinessRule` failures into HTTP 500 while peers returned 409. The affected
  switches now map these rejected business/lifecycle outcomes to 409; the
  authenticated warehouse-work regression below verifies rejection and
  transaction rollback.
- No app shell, new dashboard, redesigned screen, sample production data, or
  changes to the supplied `Front-End/` assets are part of this handoff.

## How to read this inventory

**Functional screen** means a Razor route/view is wired to application-backed
reads/actions. **API-only** means the route is present but no module view
directory exists. **Backend-only/incomplete** means no operational MVC or API
route exposes the full module runtime. **Not applicable** means the host surface
is account/shell infrastructure, not an operational WMS module.

The DTO/query/input types named below are application contracts, not database
entities. Controller links identify the HTTP boundary; application contract
links identify the reusable service boundary. The main screen groups are the
required initial design candidates; API-only modules remain discoverable through
their routes and do not imply that a screen already exists.

## Functional Razor screens

| Module and classification | Current route/view evidence | Read DTOs and commands | Permission and warehouse scope | Query, lifecycle, errors, and tests | Handoff gap |
|---|---|---|---|---|---|
| Dashboard — **functional screen** | `GET /Dashboard`, `GET /Dashboard/RefreshData`; `Views/Dashboard/Index.cshtml`; `DashboardController` | `IDashboardReadService` / `DashboardReadSnapshot` with catalog, inventory, replenishment-signal, and recent-movement sections; refresh is a read, not a mutation. | `dashboard.view`; snapshot is filtered through current/authorized warehouse; report sections have their own `reports.read` gate. | Refresh has an explicit per-section status (`Available`/`Forbidden`/unavailable), not fabricated metrics. Dashboard flow tests cover controlled SQLite data, refresh after receiving, cross-warehouse rejection, report permission, and EN/AR rendering. | Current screen group is usable for its covered KPIs; complete workflow/readiness gaps remain in their owning issues. |
| Inventory inquiry and adjustment — **functional screen** | `GET /Inventory`, `GET|POST /Inventory/Adjust`; `Views/Inventory/{Index,Adjust}.cshtml`; `InventoryController` | `InventoryInquiryPageDto` (`StockDto` rows), summaries and adjustment result via inventory query/use cases. | `inventory.read` for inquiry; `inventory.adjust` for mutations; services authorize the selected warehouse. | Query filters include warehouse/location/item/lot/serial/LPN/status/owner, scan/search, expiry, availability and zero balance; sort is an enum; service page size is bounded. Expected errors are validation/permission/not-found/concurrency results, rendered as MVC validation or problem responses at the API boundary. Tests: `AuthorizationFlowTests`, `ReportsFlowTests`, inventory inquiry and stock adjustment service tests. | No missing query contract found; redesigned inventory/adjustment visual flow remains future design work. |
| Items and UOM — **functional screen** | `/Items` CRUD/details/duplicate/import and `/UnitsOfMeasure` CRUD/item conversions; `Views/Items/*`, `Views/UnitsOfMeasure/*` | `ItemPageDto`, `ItemDto`, `UnitOfMeasurePageDto`, `ItemUnitAssignmentDto`, `QuantityConversionResult`; create/update/active/import and conversion/assignment actions. | `items.read` / `items.manage`; global catalog/reference data, with warehouse-dependent operations enforcing warehouse access separately. | Item filters include search/type/lifecycle/category and allowlisted sort; page is bounded in `ItemManagementService`. MVC uses data annotations/model state plus domain results. Tests: `ItemManagementFlowTests`, `UnitOfMeasureFlowTests`, `ItemManagementServiceTests`, `UnitOfMeasureServiceTests`. | No customer-facing item/UOM API is promised by these MVC screens. API-client item query is separately available at `/api/v1/items` with API-client credentials. |
| Warehouses and locations — **functional screen** | `/Warehouses` list/create/details/edit/configure/activate/deactivate/select; `/Locations` list/create/edit/details/children/generate/import/labels; matching view directories. | `WarehousePageDto`, `WarehouseDto`, `LocationPageDto`, `LocationLabelDto`; warehouse lifecycle/workflow configuration and location CRUD/generation/import. | `warehouse.manage`, `locations.read/manage`; selection exposes only warehouses returned by `IWarehouseAccessService`; services re-check warehouse authorization. | Warehouse/location queries have bounded paging; location list filters by warehouse/search/type/active and sorting is service-defined. Validation is MVC model state plus domain rules. Tests: `WarehouseManagementFlowTests`, `LocationManagementFlowTests`, corresponding service tests, plus cross-warehouse dashboard/inventory tests. | No missing contract for the current forms; visual workflow changes remain for the approved designs. |
| Suppliers and purchase orders — **functional screen** | `/SupplierMaster` CRUD/details/import/export and `/PurchaseOrderManagement` list/create/details/edit/import; corresponding views. API counterparts: `/api/suppliers`, `/api/purchase-orders`. | `SupplierPageDto` / `SupplierDto`; `PurchaseOrderPageDto` / `PurchaseOrderDto`; supplier resolution, PO create/update/submit/approve/release/cancel/receipt planning/import/export. | `suppliers.read/manage`, `purchase-orders.read/manage`; suppliers/items are master data; PO execution checks its warehouse through the service. | Search/status/supplier/date filters, fixed sort enums, and bounded pages are contract-specific. Domain lifecycle rejects invalid PO transitions; API results carry typed errors and MVC forms surface validation. Tests: supplier and purchase-order service tests; supplier/item/warehouse MVC flows cover the common shell and authorization patterns. | No API-contract gap identified; this host has no dedicated HTTP flow test for each PO transition. |
| ASN, receipts, receiving, quality, putaway — **functional screen** | `/AdvanceShippingNoticeManagement`, `/ReceiptManagement`, `/Receiving/Receive`, `/Receiving/Putaway`; views under those directories. APIs: `/api/advance-shipping-notices`, `/api/receipts`, `/api/receiving/sessions`, `/api/quality`, `/api/putaway-rules`, `/api/inbound-exceptions`, `/api/inbound/cross-dock`. | `AdvanceShippingNoticePageDto`, `ReceiptPageDto`, `ReceivingSessionDto`, `QualityInspectionPageDto`, `PutawayRulePageDto`; create/submit/arrive/complete ASN, receive/correct/complete receipt, start/scan/pause/resume/complete receiving, inspect/disposition, suggest/execute putaway, and exception/cross-dock commands. | `advance-shipping-notices.read/manage`, `receipts.read/manage`, `receiving.execute`, `quality.read/inspect/manage`, `locations.read`, `work.read/execute/manage`; warehouse-scoped operations authorize their warehouse. | ASN/receipt/inspection/session/work statuses are explicit domain lifecycles. Lists expose filters and bounded pages where the query contract has page fields; scan/command inputs have data annotations and operation/idempotency keys where required. MVC validation and module APIs' `ProblemDetails` are the error surfaces. Tests: ASN, receipt, receiving-execution, quality, putaway-rule, inbound-exception, cross-dock, and putaway-work service tests; `ScanningFlowTests` covers authenticated resolution/invalid scan. | Existing views cover the current receive/putaway/basic receipt workflow. QC, discrepancy, exception, and cross-dock detail screens are API-only. No missing transition contract found in this review. |
| Picking — **functional screen** | `/Picking`; `Views/Picking/Index.cshtml`. APIs: `/api/outbound/waves`, `/api/outbound/picking-strategies`, `/api/allocations`, `/api/work`, `/api/workforce`, `/api/outbound-exceptions`. | `WavePageDto`, `WarehouseWorkPageDto`, `PickingStrategyPageDto`, allocation/order DTOs; simulate/create/process/cancel wave, allocate/release order, claim/assign/start/exception/complete work, strategy and queue management. | `picking.execute`, `work.read/execute/manage`, `allocation.manage`, `sales-orders.read/manage`; work and inventory commands authorize the selected warehouse. | Query inputs are finite filters/status/typed sort plus bounded page where exposed. Work commands require idempotency keys and use the explicit Open → Available/Assigned → InProgress → Paused/Completed/Exception/Cancelled lifecycle; business-rule failures map to 409. Tests: `PickingStrategyServiceTests`, `WaveServiceTests`, `SalesOrderAllocationServiceTests`, `WarehouseWorkServiceTests`, pick-work handler and workforce service tests. #130 records its PostgreSQL journey scope; #131 records the authenticated scanner/dashboard browser scope. | Picking has a screen; wave, allocation, workforce, and exception management do not. Those focused slices do not qualify every scanner route or a physical handheld. |
| Reports — **functional screen** | `/Reports`; `Views/Reports/Index.cshtml`; `ReportsController`. | `ReportPage<T>`, `ReportGroupPageDto`, movement-ledger and report query DTOs; read/export actions only. | `reports.read`; report queries require/validate the authorized warehouse scope. | Search/date/group filters and bounded pages are present for supported reports; deterministic ordering comes from the service query. MVC validation/error handling applies. Tests: `ReportsFlowTests`, inventory/report service tests, and `ApiV1FlowTests` for paged movement reads. | The separate read-only reporting assistant is API-only at `/api/reporting-assistant/query`; no assistant view or stored conversation history exists. |
| Administration, settings, audit — **functional screens** | `/Administration`, `/Settings`, `/Audit`; one index view each. Additional service-backed JSON routes: `/api/administration/*`. | `AdministrationModuleDto`, `AdministrationReadinessDto`, `AuditPage`; readiness/history/settings/export/import and audit search/export. | `access.manage`, `settings.manage`, `audit.read`; administration readiness is warehouse-filtered where applicable; settings may be global or warehouse override. | Administration history is paged/bounded; audit filters are allowlisted. Problem responses and MVC validation/errors remain module-specific. Tests: `AdministrationFlowTests`, `SettingsFlowTests`, `AuthorizationFlowTests`, `AdministrationServiceTests`, `AuditEntryTests`. | Existing configuration/readiness screens are functional; integrations/connector-specific operational views remain API-only. |
| Account and shell — **not applicable to WMS operations** | `/Account/*`, `/Home/*`, retained `Views/Shared/_Layout.cshtml`; sign-in, locale, profile, users/access, privacy and error views. | Identity/profile/user DTOs and forms. The layout links only to the permissions it checks; warehouse selector submits to `POST /Warehouses/Select`. | Identity plus action-level `access.manage` and administrator policies; warehouse selector returns only accessible warehouses. | MVC model state, antiforgery, identity validation and localized errors; account/auth, localization, accessibility and shell tests exercise these flows. | Infrastructure only; menu visibility is not an authorization boundary. Every action retains its own authorization attribute/policy. |

## API-only operational modules

These routes are present on the authenticated Web host. Most are API-only;
where a related Razor workflow exists, the row calls that out. API controllers
call the application services named by their module contract files; there is no
second business layer. Each listed route uses its controller's class/action
authorization attributes. Warehouse scope is enforced by the underlying
service for warehouse-owned records, not by the route name or menu. The
**Read/action contract** cell identifies the DTOs, commands, lifecycle/query
families, and principal permission boundary; the linked contract file is
authoritative for exact fields and validation.

| Module — classification | HTTP and application contract | Permission/scope; query/error/test evidence | Visual/backend gap |
|---|---|---|---|
| Customers — **API-only** | `/api/customers`; `CustomerPageDto`, `CustomerDto`, document snapshot and item-reference resolution; CRUD/active/import/export. | `customers.read/manage`; global customer master, document reads resolve warehouse scope. Search/status pages are bounded. `CustomerManagementServiceTests`. | No customer screen. |
| Sales orders and allocation — **API-only** | `/api/sales-orders`, `/api/allocations`; `SalesOrderDto/PageDto`, allocation DTOs; create/submit/release/cancel/import and simulate/allocate/release allocation. | `sales-orders.read/manage`, `allocation.manage`, inventory scope; idempotency keys required at allocation command boundary; filters/paging in query DTOs. `SalesOrderServiceTests`, `SalesOrderAllocationServiceTests`. | No order or allocation screens; open visual work is not represented as a missing business layer. |
| Lots, serials, license plates — **API-only** | `/api/lots`, `/api/serial-numbers`, `/api/license-plates`; lot/serial/LPN page/detail/traceability contracts; receive/status/containment/move/pack/release/ship commands as supported by each module. | `inventory.read/adjust` plus LPN/serial-specific scopes; warehouse-scoped lookup and mutation. Lot/serial/LPN lifecycle rules are domain-enforced; list filters/pages are module-defined. `LotServiceTests`, `SerialNumberServiceTests`, `LicensePlateServiceTests`. | No lot, serial, or LPN screens. |
| Inventory ownership — **API-only** | `/api/inventory-ownership`, `/api/inventory/ownership/reports`; `InventoryOwnerDto`, transfer DTOs, balance/usage/statement report rows; owner CRUD/deactivate and idempotent ownership transfer. | `inventory-ownership.read/manage`; owners are global master data, transfer/report rows are warehouse-scoped. Report filters include warehouse/item/owner/date and paging. `InventoryOwnershipServiceTests`, report/service integration coverage. | No ownership or statement screens. |
| Inventory status — **API-only** | `/api/inventory-statuses`; `InventoryStatusDto`, `InventoryStatusTransitionDto`, `InventoryStatusChangeResult`; configure status/allowed transition, activate, change stock status. | `inventory.read`; definitions/transitions require `settings.manage`, stock change `inventory.adjust`; status/stock warehouse scope is checked. Fixed status catalog (not paged); invalid transitions are business-rule conflicts. `InventoryStatusServiceTests`, `InventoryLedgerServiceTests`. | No status/transition setup screen. |
| Classification and disposition — **API-only** | `/api/inventory/classifications`; `InventoryClassificationPolicyDto`, `InventoryClassificationDto`, history/comparison/recalculation DTOs. `/api/inventory/dispositions`; policy/candidate/disposition/recall/trace DTOs. | `inventory.read`, mutation `inventory.adjust` and action-specific policy; warehouse/item/status/date filters are typed, result limits bounded. Changes have explicit active/approved/recalled/closed transitions and map business-rule errors to 409. `InventoryClassificationServiceTests`, `InventoryDispositionServiceTests`. | No classification, expiry/disposition, recall, or trace screens. |
| Allocation strategy — **API-only** | `/api/inventory/allocation-strategies`; policy, resolution snapshot, candidate and simulation-result DTOs; policy CRUD/activation and simulation. | `inventory.read`; changes `inventory.adjust`; simulation is non-mutating, warehouse scope is verified; list query has active/search/paging criteria. `InventoryAllocationStrategyServiceTests`. | No allocation strategy screen. |
| Replenishment policy/execution — **API-only** | `/api/inventory/replenishment-policies`; policy/signal DTOs; CRUD/activate/signal query. Generation is also available through the normal work/replenishment services (`ReplenishmentGenerationResultDto`, `ReplenishmentWorkPlanDto`). | `inventory.read`, change `inventory.adjust`, generated work `work.manage`; warehouse scoped. Signals filter by warehouse/item/policy and page/limit is bounded. `InventoryReplenishmentPolicyServiceTests`, `ReplenishmentExecutionServiceTests`, replenishment work-handler tests. | No policy/signal/generation workbench. |
| Slotting — **API-only** | `/api/inventory/slotting`; `SlottingPolicyDto`, `SlottingAnalysisResultDto`, `SlottingRecommendationDto`; policy CRUD, analyze/simulate, approve/reject recommendation. | `inventory.read`, mutations `inventory.adjust` or `work.manage`; warehouse/item query and service-bounded recommendations. Recommendation state is proposed/approved/rejected and approval routes through work authorization. `SlottingServiceTests`. | No slotting screen. |
| Cycle counts — **API-only** | `/api/inventory/cycle-counts`; `CycleCountPlanDto`, `CycleCountTaskSummaryDto`, `CycleCountGenerationResultDto`; plan CRUD and bounded task generation. | `inventory.read`, plan changes `inventory.adjust`, generation `work.manage`; warehouse/location/item/class filters, generation limit 200. Plan activity and task lifecycle are service-controlled. `CycleCountServiceTests`. | No count-plan/task screen. |
| Reconciliation — **API-only** | `/api/inventory/reconciliation`; `InventoryReconciliationReportDto`, issue rows; bounded reconciliation query with warehouse/item/date/deep/batch/max-issue inputs. | `inventory.read`; service reauthorizes warehouse scope; batch and issue limits bounded. Read-only diagnostic, typed safe error response. `InventoryReconciliationServiceTests`, `InventoryReconciliationJobTests`. | No reconciliation screen. |
| Transfers and internal movement — **API-only** | `/api/transfers`, `POST /api/internal-movements`; `TransferPageDto`, `TransferOrderDto`, movement result; create/confirm/release/ship/receive/close/cancel and internal move. | `inventory.read/adjust`; source and destination warehouse access checked, with idempotent command/movement keys. Transfer list has filters/status and bounded page. `TransferServiceTests`, `StockMovementServiceTests`. | No transfer screen. |
| Receiving exceptions and ASN support — **API-only** | `/api/inbound-exceptions`, `/api/inbound/cross-dock`; exception page/plan DTOs, policy, simulate/plan/cancel and resolution commands. | ASN/work/allocation permission split; warehouse scope enforced. Lists have status/source filters and bounded page; resolution/replay conflicts are typed. `InboundExceptionServiceTests`, `CrossDockServiceTests`. | No exception/cross-dock workbench. |
| Quality — **API-only** | `/api/quality`; inspection/profile page and detail DTOs; create/sample/result/disposition/override commands. | `quality.read/inspect/manage/override` per route; warehouse scoped. Query filters and pages are explicit; invalid dispositions are domain business-rule errors. `QualityInspectionServiceTests`. | No quality inspection screen. |
| Receiving session and putaway rules — **API-only contract alongside functional forms** | `/api/receiving/sessions`, `/api/putaway-rules`; `ReceivingSessionDto`, `PutawayRulePageDto`, suggestions/rejections; start/scan/pause/resume/correct/complete and rule CRUD/suggest. | `receiving.execute`, `locations.read/manage`, `work.execute`; warehouse scoped. Session scans use client operation keys; suggestion candidate set is bounded, query fields are typed. `ReceivingExecutionServiceTests`, `PutawayRuleServiceTests`, putaway work handler tests. | The forms exist, but these API lifecycle/scan contracts do not have a designed browser operator workflow. |
| Warehouse work and workforce — **API-only** | `/api/work`, `/api/workforce`; `WarehouseWorkPageDto/Dto`, `WorkforceSuggestionsDto`, profiles/queues/routes/metrics; create/assign/claim/release/start/pause/resume/exception/cancel/complete plus queue/profile/activity actions. | `work.read`, then per-action `work.manage/execute/override`; `reports.read` for workforce metrics. Warehouse-scoped; typed filters and bounded work pages; every work command requires an idempotency key and accepts cancellation. Business-rule/transition failures are 409 after this issue's correction. `WarehouseWorkServiceTests`, pick/putaway/replenishment handler tests, `WorkforceServiceTests`, and `WarehouseWorkContractFlowTests`. | No general work queue, supervisor, or workforce screen. |
| Waves and picking strategies — **API-only** | `/api/outbound/waves`, `/api/outbound/picking-strategies`; wave/strategy/template page and simulation DTOs; simulate/create/process/cancel/remove line and strategy/rule mutations. | `inventory.read`, `allocation.manage`, `inventory.adjust`; warehouse scoped; finite filters and bounded pages. Idempotent wave/strategy outcomes are service-owned. `WaveServiceTests`, `PickingStrategyServiceTests`. | No wave planning or strategy screen. |
| Packing and shipping — **API-only** | `/api/packing`, `/api/shipping`; packing session/package DTOs and `ShipmentDto`; open/scan/measure/close package, create shipment/load, load/unload, confirm/cancel/tracking. | `packing.execute`, `shipping.execute`; setup of carrier/service separately requires `settings.manage`. Warehouse access is checked for sessions/shipments; commands have idempotency keys and bounded payloads. `PackingServiceTests`, `ShipmentServiceTests`. Carrier provider acceptance is separate from local shipment contract. | No pack/ship screens. |
| Returns — **API-only** | `/api/returns`, `/api/supplier-returns`; return authorization/receipt/disposition and supplier return DTO/page; authorize/receive/inspect/dispose and approve/release/pack/ship/acknowledge/close/exception/cancel. | `receiving.execute`, supplier-return read/manage, and `shipping.execute` per action; warehouse scoped. Filter/status pages bounded; command keys protect replay; lifecycle errors are typed. `ReturnServiceTests`, `SupplierReturnServiceTests`. | No customer or supplier returns screens. |
| Value-added services — **API-only** | `/api/value-added-services`; `KitDefinitionDto`, `ValueAddedServicePageDto`, order/genealogy DTOs; kit/order create/release/complete/cancel/reverse. | `value-added-service.read/manage`; warehouse operations scoped; list filters/page bounded, completion/reversal idempotent. `ValueAddedServiceTests`. | No kitting/assembly screen. |
| Labels — **API-only** | `/api/labels`; `WmsLabelTemplateDto`, `WmsRenderedLabel`, `WmsLabelPrintResult`; template save/activate/rollback, preview, print and retry. | `inventory.read` at controller level; template actions/preview require `settings.manage`; print/retry use `inventory.read`, with service-side warehouse authorization. Template lookup is filtered; print jobs have bounded service behavior. Validation errors are problem details. `LabelServicesTests`, `LabelTemplateEngineTests`. | No label/template management screen; device-specific print UX remains later UI work. |
| Approvals — **API-only** | `/api/approvals`; reason/policy/request/inbox DTOs; request/approve/reject/cancel/escalate/expire/start/complete execution and mark inbox read. | `approval.read/manage` per action; request warehouse context and actor are server-derived/checked. Inbox/request queries are paged and bounded; lifecycle and concurrency outcomes use typed errors. `ApprovalServiceTests`. | No approval inbox or policy screen. |
| Notifications — **API-only** | `/api/notifications`; inbox/unread/channel capability DTOs; read/dismiss/preferences and capability/readiness endpoints. | `notifications.read/manage`; user-scoped inbox, not caller-selected warehouse data. Page/query limits and channel status are explicit; disabled/unhealthy provider is reported as such. `NotificationServiceTests`, module-wiring and SMTP transport tests. | No notification center screen. |
| Attachments — **API-only** | `/api/attachments`; attachment list/detail/evidence DTOs; upload/download/evidence/deletion request. | `attachments.read/manage`; service authorizes entity access and current warehouse; size/type/scan status validated; failures are problem details. `AttachmentServiceTests`, `LocalAttachmentStorageTests`. | No attachment browser/evidence screen. |
| Identification/scanning — **API-only shared-shell support** | `/api/scanning/resolve`; `IdentificationResolutionDto` and GS1 resolution DTOs; item/location/LPN/lot/serial/package lookup. The scanner command bar is embedded in supported shell screens, not a separate module view. | `items.read` plus service warehouse/record authorization; one bounded lookup input, no paged result set; malformed/ambiguous values return validation or typed resolution errors. `ScanningFlowTests`, `IdentificationServiceTests`, `MvcShellFlowTests`. | The #131 browser slice now exercises the real receiving scanner input, focus, offline queue/replay, idempotency, and actor isolation through authenticated PostgreSQL-backed pages. Remaining module routes, keyboard-only/axe/visual checks, and device certification stay open; see `docs/modernization/BROWSER_QUALIFICATION.md`. |
| Integrations/connectors/B2B — **API-only; live readiness blocked where transport is absent** | `/api/integrations`, `/api/connectors`, `/api/b2b`; capability, subscription, delivery, connector and document DTOs; configure/rotate/revoke/test/replay/submit/acknowledge. | `settings.manage`; record visibility follows settings/warehouse scope. Typed statuses distinguish contract-only, configured, and verified. List/page rules are route-specific; provider/partner errors are safe and explicit. Tests: `ConnectorServiceTests`, `IntegrationDeliveryTests`, `B2bDocumentServiceTests`, `ModuleWiringFlowTests`. | Current generic ERP/ecommerce/reference connectors remain `ContractOnly`; no provider traffic or partner certification is implied. No admin integration screen. |
| Bulk exchange, retention, admin API — **API-only** | `/api/bulk`, `/api/retention`, `/api/administration`; import/export execution and retention/readiness/history DTOs; preview/execute/cancel/export, dry-run/purge/restore-policy, client/webhook admin commands. | `settings.manage` or `access.manage` per route; warehouse scope where record-bound. Imports have execution identity/status and cancellation; execution results are idempotent where keys are defined. Tests: `BulkImportServiceTests`, `RetentionServiceTests`, `AdministrationServiceTests` and administration flow tests. | No import execution monitor, retention control, or full admin center beyond the Razor readiness surface. |
| Versioned API client — **API-only integration boundary** | `/api/v1`: warehouses, items, locations, inventory stock, movement reports, metadata/OpenAPI. | Explicit API client bearer scheme/scopes, not the human cookie; warehouse restrictions checked server-side. Five read resources only; page envelopes expose page/pageSize/count/pages; page size >200 returns `400 api.invalid_paging`; cancellation and allowlisted sort/filter are part of each action query. `ApiV1FlowTests`, `ApiClientSecurityFlowTests`. | Not a browser UI API; no v1 command resources, and it must not be treated as evidence that each operator screen exists. |

## API-only intelligence and qualification tooling

| Module — classification | Current contract/runtime evidence | Visual/runtime boundary |
|---|---|---|
| Reporting assistant — **API-only** | `POST /api/reporting-assistant/query` plans and executes bounded, read-only answers through six existing report services; permissions and warehouse scope are rechecked for each tool. The response carries typed fields/citations and does not persist raw questions or conversation state. See `Wms.ASP/Controllers/ReportingAssistantController.cs` and `docs/modernization/REPORTING_ASSISTANT.md`. | #125 delivered the executor boundary. No assistant screen, conversation history, or external model/provider is present. |
| Forecasting — **API-only** | `/api/forecasting` exposes immutable forecast runs/points, actual comparisons, bounded export, recalculation jobs, and separate audited overrides. Forecasts use shipment/return history through a persisted cutoff. See `Wms.ASP/Controllers/ForecastingController.cs` and `docs/modernization/FORECASTING.md`. | #126 delivered the durable runtime. There is no forecast screen. Purchase orders and in-transit inbound supply are excluded; forecast metrics are measured backtest results, not a production accuracy guarantee. |
| Anomaly detection — **API-only** | `/api/anomalies` exposes bounded source rules, recalculation jobs, findings, assignment, lifecycle, and history. Nine operational source adapters, append-only observations, source-permission redaction, and deduplicated in-app alerts are wired. See `Wms.ASP/Controllers/AnomalyDetectionController.cs` and `docs/modernization/ANOMALY_DETECTION.md`. | #127 delivered the durable runtime. There is no investigation screen; findings do not prove fraud or authorize inventory, account, or worker action. |
| Governed recommendations — **API-only** | `/api/recommendations` provides type-scoped generation, search/detail/history, review/approve/reject/expire/execute, and bounded quality outcomes. Five deterministic sources revalidate and execute through normal replenishment, slotting, workforce-claim, exception-hold, or forecast commands. See `Wms.ASP/Controllers/RecommendationsController.cs` and `docs/modernization/RECOMMENDATIONS_GOVERNANCE.md`. | #128 delivered the command adapters. No recommendation UI or paid/live provider exists; the registered deterministic provider is disabled by default and shadow-only. Quality outcomes do not claim measured business lift. |
| Deterministic qualification data — **test fixture only** | `Wms.Application/DataGeneration` defines bounded profiles; `DeterministicPostgreSqlDataGenerationFixture` writes normal-command test journeys into fresh isolated PostgreSQL schemas and reports counts/fingerprints/reconciliation. See `docs/operations/DATA_GENERATION.md`. | #129 delivered the reusable test writer. It is not a customer-facing route, production seed path, or a substitute for the explicit large-capacity workload. |

## Qualification handoff

The stabilization issues close bounded evidence slices, not the entire WMS
release definition. PostgreSQL #130 recorded 13 scenario reports and 199
reconciliation checkpoints with no unexpected issues; unsupported scope,
including cross-dock reservation/work materialization, is recorded in its
evidence comment. Browser #131 verified the authenticated scanner/picking and
dashboard journey; the local scanner focus/input case passed 1/1. Physical
handhelds, all routes, accessibility audits, and partner devices remain
unverified.

Performance #132 must be read with its measured outcome: 33/48 baseline budget
evaluations failed and the extended run failed 47/62; 50-way contention was
admitted and 100-way was classified unsupported. Reconciliation was clean in
both runs, but the recorded latency budgets were not met. Recovery #133 rehearsed
response-loss replay, worker restart, dead-letter recovery, PostgreSQL
serialization retry, and a populated encrypted backup/restore in disposable
resources; this is not production RPO/RTO evidence.

The source map is ready for the later approved visual-design phase. Master #108
remains the owner for production migration/deployment, restore ownership,
independent security/capacity review, partner/handheld acceptance, and any
provider enablement. A new vendor transport needs a separately selected
provider/protocol and scoped code issue; missing adapter code is not described
as a credentials-only blocker.

## Cross-cutting contract checks

- **Lookups/dropdowns:** MVC forms bind lists from application services on the
  GET action. Existing reusable HTTP lookups include `/api/scanning/resolve`,
  `/api/suppliers/resolve`, customer item-reference resolution, and module list
  endpoints. There is no generic lookup controller. Future screens should use
  the specific module query or shared application service; do not create a
  parallel lookup/business layer.
- **Authorization:** MVC controller/action attributes and API controller/action
  attributes enforce `WmsPermissions`; menu checks in `_Layout.cshtml` only hide
  unavailable links. `AuthorizationFlowTests` and
  `WarehouseWorkContractFlowTests` exercise server-side denial. API client v1
  additionally tests scoped bearer credentials and cross-warehouse `403`.
- **Warehouse selection and data scope:** `Warehouses.Select` accepts only an
  accessible warehouse, and the request warehouse context feeds services.
  Inventory/work/report/receiving/shipping mutations authorize the actual
  warehouse again in application services. Item, supplier, and customer master
  records are global reference data; linked warehouse operations still apply
  their warehouse checks.
- **Validation and cancellation:** typed request DTOs use data annotations and
  invariant decimal validators where present; application/domain validation
  remains authoritative. I/O-bound async actions accept and pass the request
  `CancellationToken`; synchronous metadata/catalog actions need none. MVC POST/PUT/DELETE actions
  are globally protected by `AutoValidateAntiforgeryTokenAttribute`; mutating
  cookie-authenticated JSON actions explicitly validate the antiforgery header.
  Simulation/preview POSTs are non-mutating.
- **Paging and sorting:** paging is not a universal promise on every list. The
  query contract for each route declares whether page, page size, filter and
  sort exist. Services bound/clamp pages (commonly up to 200; some reports use
  500); API v1 explicitly rejects page sizes above 200. Sort inputs are enums
  or service allowlists where exposed. Capability catalogs and fixed lookup
  lists are intentionally not paged.
- **Errors:** API controllers generally return typed `ProblemDetails` with
  `errorCode` and `retryable`; conflict/concurrency/business-rule errors should
  be `409`, validation `400`, forbidden `403`, not found `404`, and dependency
  unavailable `503`. MVC routes render model-state/domain errors in the form.
  The `BusinessRule`→`409` classification is now consistent across all 46
  controller result mappers; payload serialization still varies by module.
- **Idempotency:** keys are mandatory only on mutation contracts that declare
  them (not every create/update/export). Examples include inventory commands,
  receiving scans, allocations, transfers, warehouse work, shipping, returns,
  and value-added service completion. Replays with a different request payload
  must conflict. See `docs/modernization/INVENTORY_IDEMPOTENCY.md` and the
  owning module docs before adding client retries.
- **Data truth:** the dashboard refresh test proves its metrics change with an
  actual controlled receiving mutation; reviewed operational reads use their
  WMS query services. Connector/provider capability fields explicitly report
  unavailable or contract-only implementations instead of pretending a live
  connection. The PostgreSQL journey and load evidence below is bounded to its
  named scenarios and workload; neither is a claim that every listed module
  meets the production release gates.

## Qualification evidence

The Web host tests use `WareCommandWebApplicationFactory` with an isolated
SQLite database, real Identity login/cookies, controlled records, and scoped
warehouse assignments. Service tests use controlled SQLite stores/fakes; the
separate PostgreSQL harness is a different qualification level.

- Screen/API HTTP coverage: `OperationalDashboardFlowTests`,
  `AuthorizationFlowTests`, `ApiV1FlowTests`, `ApiClientSecurityFlowTests`,
  `ItemManagementFlowTests`, `LocationManagementFlowTests`,
  `UnitOfMeasureFlowTests`, `WarehouseManagementFlowTests`,
  `ReportsFlowTests`, `AdministrationFlowTests`, `SettingsFlowTests`,
  `ScanningFlowTests`, and `WarehouseWorkContractFlowTests`.
- API/service coverage by module: infrastructure test classes named in the
  module rows and `Wms.Infrastructure.Tests`; their presence does not imply an
  authenticated HTTP/UI flow test for every endpoint.
- This issue's new work-contract tests prove: an authenticated user with
  `work.read` but not `work.execute` receives `403`; a real authenticated
  putaway completion that violates the Open-state lifecycle receives
  `409 work.completion_invalid`; the transaction leaves the work Open, source
  quantity unchanged, destination empty, and movement ledger untouched.
- Original #124 local evidence: the selected authenticated ASP flow filter
  passed 30/30, `WarehouseWorkContractFlowTests` passed 2/2, and formatter
  verification passed for the changed controllers/tests. Its exact pushed SHA
  and CI run remain in the
  [#124 evidence comment](https://github.com/RealAhmedOsama/WareCommand/issues/124#issuecomment-5796418251).
- #128 evidence: implementation commit `393ac25`, formatting-only follow-up
  `5f5d0a5`, and passing exact-SHA
  [Actions run 36092918964](https://github.com/RealAhmedOsama/WareCommand/actions/runs/36092918964).
  `RecommendationGovernanceServiceTests` passed 7/7; the Release ASP
  test-project build had 0 warnings/errors; and the authenticated PostgreSQL
  recommendation HTTP flow passed 1/1 with zero reconciliation issues. Full
  suite and artifact counts are in the
  [execution checkpoint](../implementation/EXECUTION_STATUS.md).
- PostgreSQL #130 recorded 13 scenario reports and 199 checkpoints on its
  exact revision; unsupported scenarios are excluded from pass counts. Browser
  #131's local scanner focus/input case passed 1/1. Performance #132 reports
  33/48 baseline and 47/62 extended budget failures; see the
  [performance qualification](../operations/PERFORMANCE_QUALIFICATION.md) for
  the resource decision.
- The current full Actions run's separate Windows/Linux coverage artifacts,
  secret-scan artifact, skipped dependency review, exact SHA, and conclusion
  are recorded in the [execution checkpoint](../implementation/EXECUTION_STATUS.md);
  these suite results are not added to the focused local counts above.
