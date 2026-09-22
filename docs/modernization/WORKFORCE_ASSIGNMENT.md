# Workforce assignment, queues, and productivity facts

Issue #68 adds the warehouse-operational boundary for distributing existing
`WarehouseWork` records. It does not introduce HR, payroll, punitive worker
rankings, or persistent device identifiers.

## Implemented locally

- `WarehouseWorkerProfile` stores only a user reference, warehouse, active
  team, resolved UTC shift window, display time zone, skill codes,
  certification codes, preferred location-zone IDs, active state, and a
  concurrency revision.
- `WarehouseWorkQueue` stores warehouse/work-type routing, optional zone,
  priority, capacity, required team/skills/certifications, assignment strategy,
  active state, and a concurrency revision.
- `WarehouseWorkActivity` records explicit travel, wait, exception, or other
  operational intervals with an optional bounded reason. The service records no
  device or session fingerprint.
- Self-claim uses `work.execute` and the existing idempotent/concurrency-aware
  work command boundary. A worker must have an active Identity account, a
  warehouse assignment, an active worker profile, an active shift when one is
  configured, and the queue's team/skill/certification/zone requirements.
- Existing supervisor assignment remains under `work.manage`; supervisor
  overrides remain under `work.override` and are audit-recorded.
- Suggestions are server-side filtered from available work. Queue capacity is
  checked against active assigned/in-progress/paused/exception work before a
  claim. The work row's revision prevents two successful claims of the same
  work item.
- Metrics use server-side counts, grouped queue facts, completed-line and
  completed-unit aggregates, then calculate bounded duration summaries from
  timestamp facts. The response explicitly describes operational context and
  does not rank workers.

## API boundary

- `GET/PUT /api/workforce/profiles`
- `GET/POST/PUT /api/workforce/queues`
- `GET /api/workforce/suggestions`
- `POST /api/work/{workId}/claim`
- `POST /api/workforce/work/{workId}/activity`
- `GET /api/workforce/metrics`

All routes retain warehouse authorization, existing antiforgery and rate-limit
middleware, and the established typed `Result`/Problem Details contract.

## Qualification boundary

The local SQLite tests cover profile/queue eligibility, skills, shift and zone
routing, queue capacity, idempotent replay, activity facts, metrics, and the
non-punitive interpretation contract. Migration/model checks and Debug builds
are separate gates.

The targeted PostgreSQL identity proof
`WorkforceProfilesAndQueueCodesAreUniqueWithinTheirWarehouse` passed 1/1
against a disposable PostgreSQL 17 instance on 2026-09-22. It proves that a
worker has at most one profile per user/warehouse and a queue code is unique
per warehouse while the same user and code remain valid in another warehouse;
the disposable container and port were cleared after the run. Commit:
`d9d3137`.

The issue remains open after this progress slice. Closure still requires:

- provider-backed PostgreSQL contention tests for simultaneous claims on the
  same work and concurrent capacity pressure across a queue;
- large-history query plans/latency and reporting retention decisions;
- browser/handheld workbench and scanner UX, including English/Arabic and
  RTL/LTR evidence;
- an explicit release/reassign/pause/resume/exception matrix through the real
  client and supervisor flows;
- production migration, monitoring, privacy-retention review, and deployment
  authorization.

The next dependent wiring is to align #66/#67 execution paths with these
queues, then extend the #95 PostgreSQL harness before considering closure.
