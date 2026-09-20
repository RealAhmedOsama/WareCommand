# WareCommand durable jobs

Issue #21 adds the durable background-work boundary. It is intentionally
separate from synchronous stock commits: receipt, allocation, pick, ship, and
adjustment flows remain request-scoped and must not be moved into a fire-and-
forget worker.

## Runtime boundary

When `Wms:Jobs:Enabled=true`, the ASP.NET host requires PostgreSQL and starts a
Hangfire server with these queues:

| Queue | Current work |
| --- | --- |
| `default` | reserved for general work |
| `reports` | scheduled movement-report summaries |
| `integrations` | retry adapter boundary; pending integration storage is added by the integration issue |
| `alerts` | expiry and low-stock notifications |
| `maintenance` | execution-ledger cleanup plus future cycle-count/replenishment adapters |

Recurring jobs use the stable names in `WmsJobCatalog`. Registration is
idempotent, so application restarts do not create duplicate schedules. Every
execution carries a bounded correlation ID, actor context, optional warehouse,
a bucketed idempotency key, and a per-job timeout (five or ten minutes today).

## Durability and failure handling

The `WmsJobExecutions` ledger is the application idempotency boundary. A recent
running execution is skipped, a successful execution is skipped permanently for
its key, and failed/canceled/dead-lettered executions can be retried with a new
attempt. The unique `(JobName, IdempotencyKey)` index is the database guard
against duplicate outcomes. `WmsJobNotifications` deduplicates operational
failure and alert records.

Hangfire applies bounded exponential-style delays from
`Wms:Jobs:RetryDelaysInSeconds` and retains a failed job for operator review or
manual retry. `WmsPermanentJobException` records a dead-lettered ledger state
without asking Hangfire to retry a known permanent failure. Cancellation is
recorded and rethrown so host shutdown can requeue the work according to
Hangfire's shutdown behavior.

Handlers receive `WmsJobContext`, `CancellationToken`, and the background actor
`system/background-jobs`. Logging, audit context, and telemetry use the same
correlation boundary as HTTP work. No handler uses `Task.Run` or mutates stock
inside a detached task.

## Operations and configuration

The dashboard is available at the configured `Wms:Jobs:DashboardPath` (default
`/jobs`) only to authenticated administrators. Readiness checks PostgreSQL job
storage and runner registration; the dashboard is not public and should not be
used as a substitute for metrics or alerts.

The base and production-example configuration keep jobs disabled:

```text
Wms__Jobs__Enabled=false
Wms__Jobs__StorageSchema=hangfire
Wms__Jobs__WorkerCount=4
Wms__Jobs__MaximumRetryAttempts=5
Wms__Jobs__RetryDelaysInSeconds=30,120,600,1800,3600
```

Enable the runner only with a current PostgreSQL schema and an operational
backup/restore plan. Local SQLite/testing hosts intentionally keep it disabled;
the PostgreSQL integration gate remains the provider-specific qualification.
