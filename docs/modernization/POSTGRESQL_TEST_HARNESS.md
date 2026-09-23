# PostgreSQL integration-test harness

The production provider is PostgreSQL 17. The local and CI gate
`scripts/verify-postgresql.ps1` provisions a uniquely named disposable
`postgres:17` container, waits for `pg_isready`, sets
`WARECOMMAND_TEST_POSTGRES_CONNECTION`, runs the provider tests, and removes
only the container it created.

The reusable `PostgreSqlTestDatabase` fixture creates a random schema inside
that database for the harness collection, applies every checked-in migration
from an empty schema, and drops only that generated schema during teardown.
The connection pool is disabled for the fixture so cleanup cannot be held by a
pooled test connection. Tests use unique business keys and do not depend on
execution order.

The harness currently proves:

- Npgsql is the active provider and the isolated schema has no pending
  migrations;
- unique and foreign-key-backed persistence, decimal precision, UTC
  timestamps, and transaction rollback;
- the work-engine revision token rejects concurrent mutation through the
  application's safe `ConcurrencyConflictException` boundary;
- the existing core PostgreSQL query and startup migration gate.

Run it with:

```powershell
pwsh -NoProfile -File scripts/verify-postgresql.ps1 -Port 55432
```

The runner is bounded into two exact test-class groups so a large provider
qualification run does not require one unbounded test host invocation. Inspect
the planned groups without starting Docker:

```powershell
pwsh -NoProfile -File scripts/verify-postgresql.ps1 -PlanOnly
```

Run one group when isolating a failure, or run both groups sequentially in the
same disposable database:

```powershell
pwsh -NoProfile -File scripts/verify-postgresql.ps1 -Group core -Port 55432
pwsh -NoProfile -File scripts/verify-postgresql.ps1 -Group harness -Port 55433
pwsh -NoProfile -File scripts/verify-postgresql.ps1 -Group dashboard -Port 55434
pwsh -NoProfile -File scripts/verify-postgresql.ps1 -Group all -Port 55435 -EvidencePath artifacts/postgresql-provider.json
```

The JSON result records only the repository revision, provider image, selected
groups, filters, durations, and cleanup policy; credentials and connection
strings are never written to evidence.

The harness intentionally does not replace fast unit tests. Remaining
provider work belongs to the functional issues as their schemas and workflows
arrive: broader check/delete behavior, query-plan/volume qualification,
deadlock/retry and idempotency matrices, outbox/inbox boundaries, reconciliation
and supported upgrade baselines. No production database is touched by this
script.
