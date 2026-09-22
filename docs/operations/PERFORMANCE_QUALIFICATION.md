# Performance budgets and capacity qualification

## Current slice

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

## Remaining qualification

Add repeatable benchmark/load runners for allocation, mutation, UOM/GS1,
reconciliation, query projections, login/dashboard, scans, pick/pack/ship,
transfers, reports, imports, webhooks, and jobs. Capture database CPU/IO/locks,
memory/GC, pool usage, backlog, query plans, and worker scaling on documented
PostgreSQL environments and representative generated data. Publish comparable
results, safe worker defaults, and scaling triggers only after repeated runs
and reconciliation evidence.
