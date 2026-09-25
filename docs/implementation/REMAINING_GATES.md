# Remaining Master-Plan Gates

This is the current follow-up inventory for master-plan issue #108. Original
issues #2–#107 and stabilization issues #109–#133 are closed as bounded
implementation or evidence slices; closure does not erase remaining acceptance
work. Source and qualification state is reviewed through code SHA
`5383231da04332679b774ec422465e7e140cdba8` on 2026-09-25. Earlier exact-SHA
CI run
[#36119978483](https://github.com/RealAhmedOsama/WareCommand/actions/runs/36119978483)
passed on `64cc727`: Linux and Windows solution checks, all seven disposable
PostgreSQL groups, migration, production Docker, and secret scan. The #134 handoff
documents the screen, provider, and release boundaries. Local or hosted evidence
is not production, live-provider, or external-review approval.

Earlier code SHA `6bd664dcc87edc134e04b3ff42019050e4e1b0af` passed exact-SHA
Actions run
[#36133733475](https://github.com/RealAhmedOsama/WareCommand/actions/runs/36133733475),
which passed Linux and Windows quality/coverage, all seven PostgreSQL groups,
SQLite-to-PostgreSQL migration, Docker, and secret scanning. The direct-push
dependency review was skipped. The preceding
code SHA `efda6b042372b8f6a3561a6a6adbb20b78f06777` passed exact-SHA run
[#36130823467](https://github.com/RealAhmedOsama/WareCommand/actions/runs/36130823467).
SHA `93f1b63ef49a4b5cad456b6e0200d68c9695710e` passed exact-SHA Actions run
[#36125207777](https://github.com/RealAhmedOsama/WareCommand/actions/runs/36125207777):
Linux/Windows quality and coverage, all seven PostgreSQL groups, migration,
production Docker, and secret scan. Direct-push dependency review was skipped.
The local two-repeat profile on `6bd664d` exited nonzero with 22 budget misses;
all 194 HTTP requests succeeded per repeat and both deep reconciliations had no
issues. This does not clear the capacity gate.

Latest pushed code SHA `5383231da04332679b774ec422465e7e140cdba8` has exact-SHA
Actions run
[#36174604818](https://github.com/RealAhmedOsama/WareCommand/actions/runs/36174604818)
passed Linux/Windows quality and coverage, all seven PostgreSQL groups,
SQLite-to-PostgreSQL migration, Docker, and secret scan. Dependency review was
skipped for the direct push. The exact-source local performance profile records
17 failing workload/repeat entries and 31 metric breaches with clean
reconciliation; see `PERFORMANCE_QUALIFICATION.md`. The focused receipt tests
passed 7/7. CI does not qualify the local performance budgets or production
capacity.

## Current residual register

The latest local performance profile is on `5383231`; it recorded 17 failing
workload/repeat entries and 31 metric breaches. The remaining master-plan gates
below still require fresh evidence and assigned owners; a passing code CI run
does not close capacity or production approval.

| Owner | Current evidence | Remaining requirement and next action |
| --- | --- | --- |
| [#108 master plan](https://github.com/RealAhmedOsama/WareCommand/issues/108) | Open release owner; implementation and local qualification are mapped below and in `EXECUTION_STATUS.md`. | Keep open until every applicable release gate has a named accountable operator and fresh evidence: authorized production migration/deployment and change window; real-data migration and restore; secret/proxy/storage configuration; measured capacity and the failed latency budgets; independent security review; and selected partner/device acceptance. Do not infer release readiness from CI. |
| [#115](https://github.com/RealAhmedOsama/WareCommand/issues/115), [#116](https://github.com/RealAhmedOsama/WareCommand/issues/116), [#117](https://github.com/RealAhmedOsama/WareCommand/issues/117), [#118](https://github.com/RealAhmedOsama/WareCommand/issues/118), [#119](https://github.com/RealAhmedOsama/WareCommand/issues/119) CI/database gates | Closed after architecture-test portability, formatting/import/migration-encoding, complete secret-scan, coverage-artifact, and PostgreSQL/migration corrections. The 2026-09-25 exact-SHA run and artifact names are in `EXECUTION_STATUS.md`. | No open repair from these bounded issues. Keep their current CI jobs enabled; investigate any future failed job on its exact SHA before release. |
| [#120 dashboard](https://github.com/RealAhmedOsama/WareCommand/issues/120) | Closed with authorized dashboard refresh/runtime data wiring. | Screen redesign and device coverage belong to the separate UI handoff; do not describe the API/data refresh as complete redesign. |
| [#121](https://github.com/RealAhmedOsama/WareCommand/issues/121), [#122](https://github.com/RealAhmedOsama/WareCommand/issues/122) notification transports | Closed with guarded HTTP webhook and SMTP transport implementations and controlled local receiver/sink evidence. | Keep transports disabled until explicit configuration. A named destination/provider, production secrets, mailbox/partner acceptance, operations history, and restart/load qualification remain under #108 or a newly scoped provider issue. |
| [#123 connector capability truth](https://github.com/RealAhmedOsama/WareCommand/issues/123) | Closed with contract-only/reference connector status no longer reported as healthy live connectivity. | Generic ERP/e-commerce/marketplace/carrier operations, B2B EDI, file/SFTP, and partner-specific adapters are still missing code. Select a vendor/protocol, then open scoped implementation work under #108; credentials alone do not supply an adapter. |
| [#124 UI/backend map](https://github.com/RealAhmedOsama/WareCommand/issues/124) | Closed with a source-linked screen, route, permission, DTO, and test matrix. | Many implemented workflows remain API-only. Ahmed's later design phase owns screen selection and operator-flow design; implementation follows that handoff. |
| [#125 reporting assistant](https://github.com/RealAhmedOsama/WareCommand/issues/125), follow-up to original #104 | Closed with a bounded, permission-checked, read-only executor over existing report services. | No assistant UI, conversation history, or external model/provider is implemented. Any provider and history need separate privacy, retention, security, and acceptance scope. |
| [#126 forecasting](https://github.com/RealAhmedOsama/WareCommand/issues/126), follow-up to original #105 | Closed with persisted forecast runs, comparisons, bounded exports, recalculation jobs, and audited overrides. | Forecast inputs omit purchase orders and in-transit supply. No finished forecast UI or production accuracy claim exists; calibration requires a named dataset and agreed backtest thresholds. |
| [#127 anomaly detection](https://github.com/RealAhmedOsama/WareCommand/issues/127), follow-up to original #106 | Closed with nine bounded source adapters, durable findings/history, scheduled jobs, lifecycle APIs, permission-aware redaction, and deduplicated alerts. | No investigation screen or action authority is supplied; findings do not establish fraud. Browser, handheld, load, and production qualification remain open. |
| [#128 recommendations](https://github.com/RealAhmedOsama/WareCommand/issues/128), follow-up to original #107 | Closed after [exact-SHA CI run 36092918964](https://github.com/RealAhmedOsama/WareCommand/actions/runs/36092918964) passed for five deterministic recommendation sources that revalidate and call normal WMS commands; quality outcomes persist. See the [issue evidence comment](https://github.com/RealAhmedOsama/WareCommand/issues/128#issuecomment-5826763905). | No operator UI, paid/live provider, or measured business lift is present. Provider use stays disabled by default and shadow-only; lift needs an agreed evaluation dataset and thresholds. |
| [#129 test data](https://github.com/RealAhmedOsama/WareCommand/issues/129) | Closed with a deterministic PostgreSQL test fixture using normal application commands and reconciliation. | The writer is test-only. It is not a production seeding route or a substitute for the large-capacity load dataset; only add those paths after separate authorization and scoped requirements. |
| [#130 PostgreSQL journeys](https://github.com/RealAhmedOsama/WareCommand/issues/130), follow-up to original #96 | Original qualification recorded 13 scenario reports and 199 clean reconciliation checkpoints. A later journey now verifies cross-dock reservation and pick-work materialization, idempotent replay, the one-unit fallback putaway, pick completion from receiving, and partial-plan status; exact pushed-SHA CI evidence is linked in the issue comment. | Evidence remains bounded to named scenarios. Cluster picking and other unsupported journey branches remain unverified; expand the matrix before claiming complete workflow qualification. |
| [#131 browser qualification](https://github.com/RealAhmedOsama/WareCommand/issues/131), follow-up to original #97 | Closed after authenticated scanner/picking/dashboard coverage; the local scanner focus/input case passed 1/1. | Physical handhelds, all operational routes, full accessibility/visual review, and partner/device acceptance remain unverified. |
| [#132 performance](https://github.com/RealAhmedOsama/WareCommand/issues/132), follow-up to original #98 | Closed with measured bounded workload and reconciliation results. The 2026-09-25 navigation-read follow-up on `5b89bfd` reduced local standard-profile budget failures from 37/48 on `2e94bfa` to 29/48; both candidate repeats reconciled cleanly. HTTP failures fell from 23/30 to 16/30. Later #108 follow-ups `9c2ff15` shortened the PostgreSQL receipt-counter lock and `0a8c041` added bounded inventory-balance retries; the two-repeat profile reconciled with zero issues but recorded 24 budget failures. Same-stock allocation at concurrency 20 improved to 5/20 successes, with 15 conflicts still remaining; limited-stock allocation completed 20/20 but retained high latency. Follow-up `64cc727` serializes same-warehouse/item PostgreSQL reservations; the local same-stock c20 burst then succeeded 20/20 with zero errors/conflicts, and both deep reconciliations were clean. The profile still recorded 21 budget failures, including allocation and receiving latency. Commit `93f1b63` reduces authenticated warehouse authorization from eight SQL commands to one fresh query; focused authorization/webhook tests pass 29/29. Its two-repeat profile recorded 19 budget failures with zero reconciliation errors and business assertions passing. Same-stock allocation c20 p95 remained over budget at 3,082/2,944 ms. Exact-SHA CI run `36125207777` passed Linux/Windows, all seven PostgreSQL groups, migration, Docker, and secret scan. The SQLite transaction guard is `2943fdb`; its focused and full ASP tests passed locally. The #108 receipt-counter follow-up `ababe69` uses PostgreSQL `UPDATE ... RETURNING`; its two-repeat profile recorded 22 budget failures, with clean reconciliation but variable receiving percentiles. The inventory-summary follow-up `a1c27b7` recorded 24 misses. The ledger-write follow-up `431b2aa` recorded 20 failing workload/repeat entries and cleaner allocation c20 p95, but p50/p95, receiving, and other latency budgets remained unmet. Latest follow-up `418ace1` reuses the authorized warehouse scope for demand idempotency; focused tests passed 7/7, while its exact-source profile recorded 17 failing entries (35 metric breaches) with clean reconciliation and high repeat variance. Detailed figures are in `PERFORMANCE_QUALIFICATION.md`. | The latest #108 profile still has 17 failing workload/repeat entries (35 metric breaches); allocation c20 p50/p95 exceed budget. The earlier extended run still records 47/62 failures and 100-way was unsupported. Keep capacity/reliability open; no budgets or approved capacity changed. |
| [#133 recovery](https://github.com/RealAhmedOsama/WareCommand/issues/133), follow-up to original #99 | Closed after disposable response-loss, restart, retry/dead-letter, serialization, and populated encrypted restore rehearsals. | These are not production RPO/RTO results. Assign a production backup/restore owner, target, and approved rehearsal before release. |
| [#134 documentation handoff](https://github.com/RealAhmedOsama/WareCommand/issues/134) | This checkpoint reconciles the source map, connector inventory, current CI evidence, and remaining gates. | The exact pushed documentation commit and its CI run are recorded in the issue evidence comment after that run completes. Visual design itself is Ahmed's later phase. |

## Local workstreams

| Workstream | New issue | Affected original issues | Local completion evidence |
| --- | --- | --- | --- |
| Bounded PostgreSQL/provider matrix | #109 | #6, #25–#70, #78–#95 | Disposable PostgreSQL groups run with isolated schemas, bounded memory, provider-specific assertions, cleanup proof, and redacted result counts. |
| Browser/client qualification | #110 | #20, #46–#87, #88–#93, #96–#98, #104–#107 | Local Web/PWA smoke matrix covers authentication, scanner shell, EN/AR LTR/RTL, responsive/focus/accessibility assertions, and redacted artifacts. |
| Performance/load evidence | #111 | #18, #21, #34, #54, #75, #90–#99, #102 | Repeatable local workloads carry revision/dataset/environment metadata, bounded concurrency, PostgreSQL/query/worker metrics, and reconciliation results. |
| Non-production resilience | #112 | #21, #22, #34, #90, #94–#99 | Test-only timeout/conflict/restart/response-loss/dead-letter/restore scenarios prove no partial or duplicate business result and cannot activate in production. |
| Release/security/support packet | #113 | #6, #7, #9, #10, #14, #15, #18, #22, #82, #84, #85, #87, #94, #95, #100–#103 | Local preflight, migration, backup, security-boundary, redaction, compatibility, and GO/GO_WITH_RESTRICTIONS/NO_GO packet checks pass without secrets. |
| Remaining local module wiring | #114 | #82–#93, #96–#107 and dependent workflow owners | Focused ASP flow coverage proves the registered connector, B2B, bulk-exchange, integration, notification, and warehouse-work handler boundaries are available through authorized local surfaces; real vendor transport/certification stays explicit. |

## Task 7 local wiring checkpoint

The selected local boundary for #114 is now executable through the ASP host:

- `/api/connectors` exposes the existing connector service for list, mapping,
  instance, credential-rotation, connection-test, and run commands.
- `/api/b2b` exposes capability discovery plus mapping-profile,
  trading-partner, document-submit, acknowledgement, and replay commands.
- `/api/bulk` exposes capability discovery, user-scoped preview/execute/cancel
  flows, execution lookup, and CSV export through the existing bulk services.
- `/api/integrations/capabilities` reports the registered outbox/inbox/webhook
  composition without exposing payloads or credentials.
- `ModuleWiringFlowTests` verifies service resolution, adapter/handler counts,
  administrator authorization, and the four local HTTP boundaries.

The local flow is intentionally an adapter over the existing application
contracts: authorization, audit/idempotency, persistence, and provider-neutral
service behavior remain owned by those services. Real carrier/email/SFTP/EDI
transports, partner certification, production credentials, and device/printer
qualification remain external gates.

## Tracker evidence links

Evidence comments were posted to the canonical `WareCommand` repository:

- [#109 evidence](https://github.com/RealAhmedOsama/WareCommand/issues/109#issuecomment-5782476538)
- [#110 evidence](https://github.com/RealAhmedOsama/WareCommand/issues/110#issuecomment-5782476742)
- [#111 evidence](https://github.com/RealAhmedOsama/WareCommand/issues/111#issuecomment-5782476931)
- [#112 evidence](https://github.com/RealAhmedOsama/WareCommand/issues/112#issuecomment-5782477118)
- [#113 evidence](https://github.com/RealAhmedOsama/WareCommand/issues/113#issuecomment-5782477299)
- [#114 evidence](https://github.com/RealAhmedOsama/WareCommand/issues/114#issuecomment-5782477460)
- [#108 master-plan reconciliation](https://github.com/RealAhmedOsama/WareCommand/issues/108#issuecomment-5782477673)

The links above are historical evidence for the #109–#114 workstreams. The
current source revision is stated at the top and current issue owners are in the
residual register; do not use the older revision comparison in any archived
comment as the current repository state.

## Explicitly non-local gates

The following cannot be honestly completed by local code changes alone:

- production migration/deployment, production secrets, proxy/TLS/storage, and
  an approved change window;
- live provider throughput/acceptance, real carrier/email/SFTP/EDI credentials,
  partner certification, or vendor-specific device/printer qualification;
- independent security review, external penetration testing, or production
  capacity approval.

Those gates remain owned by #108 and the release documents. The #109–#114
comments preserve the earlier local workstream evidence; they do not convert
external requirements into a local completion claim.

## Closure rule

A local workstream issue may be closed only after its focused tests, relevant
full project tests, and evidence packet pass. Master-plan #108 may be closed
only when the explicit non-local gates have an authorized owner and fresh
evidence, not merely because all implementation slices are committed.
