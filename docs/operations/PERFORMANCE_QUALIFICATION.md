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

Every result is local TestServer/PostgreSQL qualification only. It does not
establish production capacity, worker scaling limits, production RTO/RPO, or a
release approval. Keep any machine-specific JSON evidence outside the
repository; publish only a reviewed, redacted summary with its exact SHA.
