# WareCommand observability runbook

This runbook is for operators of a deployed WareCommand host. Health responses
are deliberately terse; use the correlation ID from the request/log record and
the collector's trace ID for diagnosis.

## Readiness is unhealthy

1. Request `/health/live` and `/health/ready` from the same network location as
   the orchestrator.
2. Check the structured startup, database, and migration events in the log
   sink. Do not enable SQL text or payload logging to investigate.
3. For PostgreSQL connectivity, verify the service/network, secret-file
   availability, credentials, and database role without printing the secret.
4. For a migration mismatch, stop traffic, apply the checked-in migration using
   the documented release procedure, and restart only after the schema is
   current. Never mutate production data from a health probe.
5. For storage, verify the mounted `/var/lib/warecommand` volume and free space;
   do not delete application data as an emergency response.

## High request errors or slow operations

1. Group traces by operation and status class, not by raw SKU, serial, barcode,
   user input, or request body.
2. Compare `warecommand.request.duration` with
   `warecommand.database.command.duration` to separate application latency from
   database latency.
3. Use the correlation and operation IDs to locate the single structured failure
   record. The MVC exception handler is the authoritative error boundary, so do
   not create duplicate catch-all logging.
4. If a concurrency/conflict alert is active, preserve the failed command and
   inspect the owning workflow's retry/idempotency behavior before retrying.

## Job failures or backlog

Jobs remain disabled by default. When the runner is enabled, inspect
`/health/ready`, Hangfire storage health, execution-ledger lease age, retry
counts, and backlog growth. The administrator-only dashboard is at the
configured `Wms:Jobs:DashboardPath` (normally `/jobs`). Retry a failed job only
after checking its idempotency key and correlation record; do not delete job
rows or force-complete work to clear an alert. A dead-lettered execution needs
an operator decision and an explicit re-enqueue, not an automatic data edit.

## Low disk or backup failure

Confirm which volume is low and whether the backup destination is reachable.
Follow the deployment backup/restore procedure and preserve evidence before
cleanup. A backup failure is not resolved until a restore rehearsal or provider
acknowledgement proves the backup is usable. The current host emits no backup
success claim; the future backup adapter must call the shared failure metric.

## Local verification

Run from the repository root with non-production configuration:

```powershell
dotnet run --project .\Wms.ASP\Wms.ASP.csproj --environment Development
Invoke-WebRequest http://127.0.0.1:8080/health/live
Invoke-WebRequest http://127.0.0.1:8080/health/ready
```

For collector qualification, set `Wms__Telemetry__Otlp__Enabled=true` and an
explicit disposable collector endpoint. Never point a local run at a production
collector or production database.
