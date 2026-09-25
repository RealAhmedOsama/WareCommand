# Performance budgets and capacity qualification

## Budget contracts and HTTP smoke

Issue #98 currently has application-owned performance budget and evaluation
contracts in `Wms.Application/Performance`. Budgets cover representative login/
dashboard, inventory lookup, receiving scan, allocation, reports, import,
background jobs, and reconciliation workloads with throughput, p50/p95/p99,
error, and conflict limits. Every evaluated run must carry environment,
dataset fingerprint, hardware profile, application revision, and timing
metadata.

Mutation-heavy workloads fail qualification when business assertions or
reconciliation fail, even when latency is within budget. These are budgets and
comparison contracts, not claimed production capacity numbers.

The local HTTP smoke runner is `scripts/verify-performance-qualification.ps1`.
It uses fixed health/manifest routes, bounded PowerShell parallelism, and
records only status, timing, error-type, percentile, throughput, revision,
dataset, hardware, and concurrency metadata. It never stores response bodies,
headers, cookies, or credentials, refuses Production/Staging environments, and
fails closed when no request succeeds.

Inspect or run it against an explicitly started local host:

```powershell
pwsh -NoProfile -File scripts/verify-performance-qualification.ps1 -PlanOnly
pwsh -NoProfile -File scripts/verify-performance-qualification.ps1 `
  -BaseUrl http://127.0.0.1:5188 -Requests 24 -Concurrency 4 `
  -EvidencePath artifacts/performance-local.json
```

The 2026-09-22 local sample used revision `2a39130`, 24 requests, concurrency
4, 16 logical CPUs, and `http-smoke-v1`; it completed 24/24 successfully at
16.167 requests/second. This is local repeatability evidence only, not a
production capacity or scaling approval.

## PostgreSQL workload qualification

Run the full local workload against an owned disposable PostgreSQL container
and isolated schema:

```powershell
pwsh -NoProfile -File scripts/verify-postgresql.ps1 `
  -Group performance -Port 55446 `
  -EvidencePath "$env:TEMP/warecommand-performance-local.json"
```

The default profile repeats twice with 40 requests per workload and concurrency
1, 4, and 20. It creates a deterministic `MinimalDevelopment` dataset, warms
the authenticated routes, and then exercises dashboard and inventory pages,
receiving scans, movement reports, CSV item imports, same-stock internal
movements, sales-order allocation/reservation and pick completion, a durable
reconciliation job, and outbox dispatch with no active webhook subscriptions.
It verifies four-unit oversell contention, allocation and pick idempotency,
quantity conservation, and deep reconciliation. Evidence includes the SHA,
dataset counts/fingerprint, PostgreSQL version, host/runtime and container
limits, request mix, latency percentiles, throughput, errors/conflicts, process
CPU/memory/GC, Npgsql metrics, sampled PostgreSQL connections/lock waits, an
`EXPLAIN ANALYZE` query plan, job/outbox state, and reconciliation counts.

Use `-PerformanceSamples 10..100` to change the bounded sample count and
`-PerformanceRepeats 2..5` to change repeat count. `-ExtendedContention` asks
for 50- and 100-way cases; the runner records them as unsupported when the host
or PostgreSQL container fails the resource gate. The gate also reads
PostgreSQL `max_connections`, reserved slots, and current sessions. It reserves
16 connections for fixture and observer work and checks the application pool
limit (`concurrency + 8`) before admitting each level. Supported levels run;
unsupported levels carry a reason in the evidence and are omitted from the
measured request mix. The requested levels and measured levels are recorded
separately. These extended levels remain bounded and are never used against
Production or Staging.

The performance group is explicit and is not included in the broad provider
`all` group. The ordinary PostgreSQL dashboard group keeps a small CI
regression: eight authenticated dashboard refreshes at concurrency four, with
observed p50/p95/p99 and the existing dashboard p99 budget. Transfer and pick
completion are measured, but the existing catalog has no budgets for those
workloads, so their results are reported without invented limits.

### Recorded local qualification — 2026-09-25

The default two-repeat run on `c8286957656c8b51346e2b5f47c5f3da67c70755`
used .NET 10.0.12, Windows x64 with 16 logical processors, and PostgreSQL
17.11. Each repeat generated the same `minimal-development` logical dataset:
1 warehouse, 8 locations, 25 items, 25 inventory rows, and 34 inventory
transactions. Forty samples were measured at concurrency 1, 4, and 20 across
the documented authenticated workload. The runner evaluated 48 budget
observations; **33 failed**. Both repeats completed without a fatal harness
error and passed deep reconciliation with zero issues. No budget was changed.

The lowest-concurrency dashboard throughput was 2.13–2.40 requests/second
against the 5/second budget; inventory throughput was 3.35–3.72/second against
20/second. At concurrency 20, receiving p95 was 16.98–23.27 seconds against
the 750 ms budget. In the same-stock allocation case, 20 requests competed for
four units: four were allocated and sixteen remained backordered, with no
negative or over-reserved balance. These measurements expose unmet latency and
throughput budgets; they do not establish a supported service capacity.

The representative inventory-balance plan used
`IX_InventoryBalances_ItemId` and completed in 0.14–0.17 ms. Database-command
p95 ranged from 13.5 to 18.3 ms, while captured request tails were much longer
and lock waiters peaked at 19. The navigation snapshot follow-up below reduces
repeated layout permission reads. Continue profiling receiving and contention
paths before changing their query architecture.

The extended-resource-gate run used the committed gate at
`e7c0351057b484bc9734a5fab5ced82f25d868ff`, with two repeats and ten samples
at each admitted level. PostgreSQL reported 100 maximum connections, 3
reserved, 6 current, and 92 available non-reserved slots. The 50-way level
required a 58-connection application pool plus 16 fixture/observer slots
(74 total), so it ran. The 100-way level required 108 plus 16 (124 total), so
the runner recorded it as unsupported and did not run it. It evaluated 62
budgets; **47 failed**. Both repeats completed with zero reconciliation
issues and no fatal harness error. At 50-way same-stock contention, four of 50
one-unit requests were allocated and 46 were backordered, with no negative or
over-reserved balance. The command exits nonzero because observed budgets fail; it must not
be treated as a green qualification.

Keep the standard qualification at concurrency 1/4/20 and keep 50/100 opt-in
behind the live resource gate. These are test-runner limits only. Since the
current workload misses its existing thresholds, this machine has no measured
approved operating capacity; do not infer a production concurrency limit from
the admitted 50-way case.

### Navigation snapshot follow-up — 2026-09-25

Commit `5b89bfdaad82d3d3597e2d2860af8335f17a62c8` replaces the layout's
repeated sequential authorization checks with one fresh navigation snapshot
per render. Controller and endpoint authorization remain on their existing
paths. The focused SQLite test verifies three database reads for the snapshot,
current permission changes on the next call, API-client exact scopes, and
user-only wildcard behavior.

The local before/after probe used the same .NET 10.0.12 Windows x64 host,
PostgreSQL 17.11, dataset fingerprint
`f1ec8698f34bd4ee605c085e33750580eb40583cdd93ca8f09faced54145cf9b`, two
repeats, ten samples per workload, and concurrency 1/4/20. The baseline was
code SHA `2e94bfa28bb2984f7e8c9a7f1706d64c31b4beca`; the candidate was
`5b89bfdaad82d3d3597e2d2860af8335f17a62c8`.

| Isolated workload | Concurrency | Baseline average throughput / p95 | Candidate average throughput / p95 |
| --- | ---: | ---: | ---: |
| Dashboard | 1 | 1.04 req/s / 2,021 ms | 3.72 req/s / 322 ms |
| Inventory | 1 | 1.84 req/s / 1,092 ms | 9.76 req/s / 133 ms |
| Report | 20 | 9.37 req/s / 1,407 ms | 33.50 req/s / 301 ms |
| Receiving | 20 | 1.20 req/s / 15,061 ms | 2.11 req/s / 4,910 ms |

Across all 48 checks, failures fell from 37 to 29; isolated HTTP failures fell
from 23/30 to 16/30. All six report and all six item-import checks passed on
the candidate. The 194 measured HTTP requests succeeded in each repeat, and
both candidate repeats passed deep reconciliation with no fatal harness
error. Database-command p95 was 45.04/18.26 ms on the two baseline repeats and
11.84/14.39 ms on the candidate repeats. The benchmark still exits nonzero:
dashboard and inventory throughput budgets, all receiving levels, and
allocation/contention budgets remain unmet. No budget changed, and no service
capacity is approved by this local TestServer/PostgreSQL result.

Every result is local TestServer/PostgreSQL qualification only. It does not
establish production capacity, worker scaling limits, production RTO/RPO, or a
release approval. Keep any machine-specific JSON evidence outside the
repository; publish only a reviewed, redacted summary with its exact SHA.

### Receipt-counter and allocation follow-up — 2026-09-25

Commit `9c2ff159671e02cda0dc98f953a77468f1006965` moves the PostgreSQL
per-warehouse receipt counter into a short independent transaction, so the
counter row is not held for the full receipt transaction. Commit
`0a8c041dcb2465fefd59c3b2a1871997c29b25cd` adds up to five bounded retries for
inventory-balance conflicts during order allocation. The follow-up
`2943fdbbad13688c309b70e27b5b54271b657dd4` uses the independent allocator only
for PostgreSQL; SQLite keeps the caller's transaction because its database-
wide write lock conflicts with a second writer.

The standard two-repeat PostgreSQL profile on `0a8c041` used .NET 10.0.12,
Windows x64 with 16 logical processors, PostgreSQL 17.11, the same logical
dataset fingerprint as the navigation profile, and ten samples at
concurrency 1/4/20. Both repeats completed without a fatal harness error and
deep reconciliation found zero issues. The run still exited nonzero with 24
performance-budget failures.

At concurrency 20, receiving returned all 10/10 responses in both repeats;
throughput was 5.44 and 5.02 requests/second and p95 was 1,837 and 1,992 ms.
The preceding receipt-counter candidate `9c2ff15` measured c20 receiving p95
at 1,960/2,002 ms, compared with 4,007/5,813 ms on the navigation-only
candidate `5b89bfd`. Other percentile and low-concurrency budgets still fail.
Same-stock allocation improved from 1/20
to 5/20 successful requests per repeat after retries, while 15/20 still ended
in balance conflicts; p95 was 3,943 and 9,925 ms. Limited-stock allocation
completed 20/20 requests without errors or conflicts, but p95 remained 3,727
to 5,103 ms. These results do not satisfy the performance catalog and do not
establish supported capacity.

The PostgreSQL performance path is unchanged by the SQLite provider guard in
`2943fdb`. Its focused receipt-flow tests passed 2/2, and the complete ASP test
project passed 62 tests with 8 PostgreSQL-only skips. Exact-SHA remote CI run
[`36112799593`](https://github.com/RealAhmedOsama/WareCommand/actions/runs/36112799593)
passed Linux, Windows, PostgreSQL integration/migration, Docker, and secret
scan. Keep the standard performance gate open; no budget or capacity approval
changed.

### Reservation contention follow-up — 2026-09-25

Commit `64cc72784730aa987bf3d144ba378734f0999d3c` acquires a transaction-
scoped PostgreSQL advisory lock for the warehouse/item before checking demand
replay and reading allocatable balances. This serializes concurrent
reservations for the same stock key across application instances; non-PostgreSQL
providers do not acquire the lock. The focused reservation/allocation tests
passed 12/12.

The exact-SHA local profile used .NET 10.0.12, Windows x64 with 16 logical
processors, PostgreSQL 17.11, ten samples, two repeats, and concurrency 1/4/20.
The command exited nonzero because 21 performance budgets still failed, down
from 24 on `0a8c041`. Both repeats completed and deep reconciliation found zero
issues. The same-stock 20-way case now succeeds 20/20 with zero errors or
conflicts in both repeats; throughput was 5.91/5.95 requests/second, while p95
was 3,233/3,217 ms against the 2,000 ms budget. Limited-stock allocation also
succeeded 20/20 with no errors or conflicts, but p95 remained 2,290/2,322 ms.
Receiving at concurrency 20 returned 10/10 responses per repeat, with p95 of
1,222/1,191 ms against the 750 ms budget. Lower-concurrency allocation,
receiving, and isolated HTTP budgets also remain unmet.

Run command:

```powershell
pwsh -NoProfile -File scripts/verify-postgresql.ps1 -Group performance `
  -Port 55437 -PerformanceSamples 10 -PerformanceRepeats 2 `
  -EvidencePath (Join-Path $env:TEMP 'warecommand-postgresql-performance-64cc727.json')
```

This change removes the measured allocation conflicts but does not qualify the
performance catalog or establish supported capacity. No budget changed; keep
the master performance gate open. Exact-SHA CI run
[`36119978483`](https://github.com/RealAhmedOsama/WareCommand/actions/runs/36119978483)
passed Linux/Windows, all seven PostgreSQL integration groups, migration,
Docker, and secret scan. CI does not execute this full performance profile.

### Authorization-query follow-up — 2026-09-25

Commit `93f1b63ef49a4b5cad456b6e0200d68c9695710e` reduces each authenticated
warehouse authorization from eight SQL commands to one fresh EF query. The
query projects active-user/lockout state, direct or role permission, global
permission, warehouse state, and user assignment. Focused authorization and
webhook tests passed 29/29, including one-command and immediate permission
change assertions.

The exact-SHA local profile used .NET 10.0.12, Windows x64 with 16 logical
processors, PostgreSQL 17, ten samples, two repeats, and concurrency 1/4/20.
It exited nonzero with 19 budget failures, compared with 21 on the prior
`64cc727` profile. Every measured evaluation passed its business assertions and
reported zero reconciliation errors. The result is a small count change, not
capacity qualification: same-stock allocation p95 at concurrency 20 was
3,082/2,944 ms against 2,000 ms, while limited-stock allocation p95 was
2,020/1,881 ms. Receiving p50 improved in both repeats at concurrency 1 and 4;
at concurrency 20 the repeats varied (1,138 and 1,555 ms). No budgets changed.

Run command:

```powershell
pwsh -NoProfile -File scripts/verify-postgresql.ps1 -Group performance `
  -Port 55437 -PerformanceSamples 10 -PerformanceRepeats 2 `
  -EvidencePath (Join-Path $env:TEMP 'warecommand-postgresql-performance-93f1b63.json')
```

Exact-SHA Actions run
[`36125207777`](https://github.com/RealAhmedOsama/WareCommand/actions/runs/36125207777)
completed successfully on `93f1b63`: Linux and Windows quality/coverage, all
seven PostgreSQL groups, SQLite-to-PostgreSQL migration, production Docker, and
secret scan passed. Direct-push dependency review was skipped. CI does not
execute this full performance group; keep the master capacity gate open.

### Warehouse-scope query follow-up — 2026-09-25

Commit `efda6b042372b8f6a3561a6a6adbb20b78f06777` consolidates the active
user, permission, and assigned-warehouse checks in `GetScopeAsync` from four
database commands to one fresh query. The authorization/webhook focus passed
30/30, including the one-command boundary and immediate role grant/removal
freshness checks.

The exact-SHA local profile used the same .NET 10.0.12, Windows x64, PostgreSQL
17, two-repeat, ten-sample, concurrency 1/4/20 configuration. It exited with 20
performance-budget failures, compared with 19 on `93f1b63`. Database-command
telemetry recorded 12,746 samples per repeat, compared with 18,342 on
`93f1b63`; both candidate repeats passed their business assertions and deep
reconciliation with zero issues. The profile does not establish a capacity
gain: budgets remain unmet and none were changed. Evidence is
`%TEMP%\warecommand-postgresql-performance-efda6b0.json`.

Run command:

```powershell
pwsh -NoProfile -File scripts/verify-postgresql.ps1 -Group performance `
  -Port 55437 -PerformanceSamples 10 -PerformanceRepeats 2 `
  -EvidencePath (Join-Path $env:TEMP 'warecommand-postgresql-performance-efda6b0.json')
```

Exact-SHA Actions run
[`36130823467`](https://github.com/RealAhmedOsama/WareCommand/actions/runs/36130823467)
passed Linux and Windows quality/coverage, all seven PostgreSQL integration
groups, SQLite-to-PostgreSQL migration, Docker, and secret scan. Pull-request
dependency review was skipped for the direct push.

### Navigation snapshot follow-up — 2026-09-25

Commit `6bd664dcc87edc134e04b3ff42019050e4e1b0af` consolidates the authenticated
MVC layout's navigation snapshot from three SQL commands to one `UNION ALL`
query. It reads active/lockout state, direct and role permissions, and accessible
warehouses in one fresh command, without multiplying claim rows by warehouse
rows. The focused authorization and webhook transport tests passed 30/30; the
navigation regression asserts one command and visibility of a permission added
immediately before the next snapshot. The Release ASP build had zero warnings
and errors, and the focused whitespace-format check passed.

The exact-SHA local profile used the same .NET 10.0.12, Windows x64, PostgreSQL
17, two-repeat, ten-sample, concurrency 1/4/20 configuration. It exited nonzero
with 22 budget failures, compared with 20 on `efda6b0`. All 194 HTTP requests
succeeded in each repeat and both deep reconciliations had zero issues. The
database-command telemetry sample counts were 13,050 and 13,026. This run does
not show an end-to-end capacity improvement, and no budget or capacity approval
changed. Evidence is
`%TEMP%\warecommand-postgresql-performance-6bd664d.json`.

Run command:

```powershell
pwsh -NoProfile -File scripts/verify-postgresql.ps1 -Group performance `
  -Port 55437 -PerformanceSamples 10 -PerformanceRepeats 2 `
  -EvidencePath (Join-Path $env:TEMP 'warecommand-postgresql-performance-6bd664d.json')
```

Exact-SHA Actions run
[`36133733475`](https://github.com/RealAhmedOsama/WareCommand/actions/runs/36133733475)
completed successfully: Linux and Windows quality/coverage, all seven
PostgreSQL integration groups, SQLite-to-PostgreSQL migration, production
Docker, and secret scan passed. Pull-request dependency review was skipped for
the direct push. CI does not execute this local performance budget group; keep
the master capacity gate open.

### Receipt counter update-returning follow-up — 2026-09-25

Commit `ababe694fb468d424eb8cef02b4347077e1b228e` changes the existing
PostgreSQL receipt-sequence path to one atomic `UPDATE ... RETURNING` statement.
The previous update, follow-up read, and explicit commit held the sequence row
lock across additional round trips. The missing-sequence, exhausted-sequence,
and non-PostgreSQL paths retain their existing behavior. The focused allocator
test passed 1/1.

The exact-source local profile used PostgreSQL 17, two repeats, ten samples per
workload, and concurrency 1/4/20. It exited nonzero with 22 budget failures,
the same count as the prior `6bd664d` profile. All 180 measured HTTP samples in
each repeat succeeded without errors or conflicts, and both deep reconciliations
were clean. Receiving p95 at concurrency 1 was 2,605/1,350 ms, at concurrency 4
1,841/731 ms, and at concurrency 20 2,398/1,584 ms. The prior profile recorded
1,107/742 ms, 729/980 ms, and 1,566/1,795 ms respectively. Candidate database
command p95 also varied between 42.23 ms and 23.74 ms across repeats, compared
with 23.58 ms and 24.84 ms previously. These results do not establish a
repeatable end-to-end capacity improvement; no budgets or capacity limits
changed. Keep the master capacity gate open.

Evidence is `%TEMP%\warecommand-postgresql-performance-ababe69.json`.

Run command:

```powershell
pwsh -NoProfile -File scripts/verify-postgresql.ps1 -Group performance `
  -Port 55437 -PerformanceSamples 10 -PerformanceRepeats 2 `
  -EvidencePath (Join-Path $env:TEMP 'warecommand-postgresql-performance-ababe69.json')
```

The local performance group remains outside CI. A green hosted CI run does not
clear these measured budget misses or establish approved production capacity.

### Inventory summary query follow-up — 2026-09-25

Commit `a1c27b764743b6286a213b77b9e1652e5fb59734` removes the separate
`AnyAsync` existence check from the populated inventory-summary path. The
aggregate query now determines whether canonical balances exist; an empty
result still invokes the legacy-stock fallback. The focused
`InventoryInquiryServiceTests` group passed 7/7, including that fallback.

The exact-source profile used two repeats, ten samples per workload, and
concurrency 1/4/20. It exited nonzero with 24 budget failures, compared with 22
on `ababe69`. All 180 measured HTTP samples per repeat succeeded without errors
or conflicts, and both deep reconciliations were clean. Inventory p50
milliseconds by repeat changed from 482/95 to 109/329 at concurrency 1, 398/111
to 118/292 at concurrency 4, and 437/266 to 293/394 at concurrency 20.
Database-command p95 also varied between 38.22/51.46 ms, compared with
42.23/23.74 ms on the previous profile. The results are mixed and do not
establish a repeatable throughput gain; no budgets or capacity limits changed.
Keep the master capacity gate open.

Evidence is `%TEMP%\warecommand-postgresql-performance-a1c27b7.json`.

Run command:

```powershell
pwsh -NoProfile -File scripts/verify-postgresql.ps1 -Group performance `
  -Port 55437 -PerformanceSamples 10 -PerformanceRepeats 2 `
  -EvidencePath (Join-Path $env:TEMP 'warecommand-postgresql-performance-a1c27b7.json')
```

### Ledger write round-trip reduction — 2026-09-25

Commit `431b2aab082bd0fcb3c54ebc91643e0114b28674` reuses the warehouse scope
already resolved for a ledger operation, consolidates location/item/status and
negative-stock validation into one query, and applies the same checked scope to
idempotency, balance, and legacy-stock reads. The focused
`InventoryLedgerServiceTests` group passed 9/9, including one scope resolution
per operation and denial outside the resolved warehouse scope.

The exact-source local PostgreSQL profile used two repeats, ten samples, and
concurrency 1/4/20. It exited nonzero with 20 budget failures, down from 24 on
`a1c27b7`. Both deep reconciliations were clean. Same-stock allocation at
concurrency 20 completed all 20 requests without errors or conflicts in both
repeats; throughput rose from 2.68/2.62 to 5.56/5.04 requests per second, and
p95 fell from 7,122/7,324 ms to 3,404/3,538 ms. The 2,500 ms p95 and 750 ms p50
allocation budgets still fail. Receiving and other isolated HTTP latency budgets
also remain unmet. No budget or capacity limit changed; this is a bounded local
improvement, not capacity qualification.

Evidence is `%TEMP%\warecommand-postgresql-performance-431b2aa.json`.

Run command:

```powershell
pwsh -NoProfile -File scripts/verify-postgresql.ps1 -Group performance `
  -Port 55437 -PerformanceSamples 10 -PerformanceRepeats 2 `
  -EvidencePath (Join-Path $env:TEMP 'warecommand-postgresql-performance-431b2aa.json')
```

The local performance group remains outside CI. A green hosted CI run does not
clear these measured budget misses or establish approved production capacity.

### Reservation idempotency scope reuse — 2026-09-25

Commit `418ace18d42430a8beaa98a47050174857258d89` reuses the exact warehouse
scope already checked at the start of `ReserveAsync` for the demand-key lookup.
The lookup remains constrained to the requested warehouse and loads the same
allocation/event history. `InventoryReservationServiceTests` passed 7/7; the
idempotent retry assertion confirms one scope resolution.

The exact-source PostgreSQL profile used two repeats, ten samples, and
concurrency 1/4/20. Both deep reconciliations were clean. All 300 isolated HTTP
samples succeeded without errors or conflicts; same-stock allocation completed
all 25 requests per repeat across concurrency 1/4/20, and limited-stock
allocation completed 20/20 in both repeats. The profile exited nonzero with 17
repeat/scenario entries containing 35 individual latency/throughput breaches.
At concurrency 20, same-stock allocation p50 was 1,559/2,966 ms and p95 was
2,594/4,149 ms, above the 750/2,500 ms budgets. Receiving p50 at concurrency 1
was 405/572 ms. Large repeat spreads remain, so the lower failure-entry count
than the previous `431b2aa` profile does not establish a repeatable gain or
capacity. No budgets changed.

Evidence is `%TEMP%\warecommand-postgresql-performance-418ace1.json`.

Run command:

```powershell
pwsh -NoProfile -File scripts/verify-postgresql.ps1 -Group performance `
  -Port 55437 -PerformanceSamples 10 -PerformanceRepeats 2 `
  -EvidencePath (Join-Path $env:TEMP 'warecommand-postgresql-performance-418ace1.json')
```

Exact-SHA hosted CI is recorded in `RELEASE_QUALIFICATION.md`. The local
performance group remains outside CI; capacity and production readiness remain
unqualified.
