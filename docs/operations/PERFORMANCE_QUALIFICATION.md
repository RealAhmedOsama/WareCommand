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

## Remaining qualification

Add repeatable benchmark/load runners for allocation, mutation, UOM/GS1,
reconciliation, query projections, login/dashboard, scans, pick/pack/ship,
transfers, reports, imports, webhooks, and jobs. Capture database CPU/IO/locks,
memory/GC, pool usage, backlog, query plans, and worker scaling on documented
PostgreSQL environments and representative generated data. Publish comparable
results, safe worker defaults, and scaling triggers only after repeated runs
and reconciliation evidence.
