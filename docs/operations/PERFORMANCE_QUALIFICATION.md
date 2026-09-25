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
and lock waiters peaked at 19. This evidence does not identify an index or
cache change as the remedy; profile the slow application paths before changing
query architecture.

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

Every result is local TestServer/PostgreSQL qualification only. It does not
establish production capacity, worker scaling limits, production RTO/RPO, or a
release approval. Keep any machine-specific JSON evidence outside the
repository; publish only a reviewed, redacted summary with its exact SHA.
