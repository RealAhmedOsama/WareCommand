# WareCommand

WareCommand is an early-stage warehouse management system implemented as a
.NET 10 modular monolith with an ASP.NET Core MVC host and a WinForms desktop
host. The repository is actively being modernized; local verification is not a
production-readiness or deployment claim.

## Current state

- PostgreSQL is the production persistence adapter.
- SQLite is an explicit local/demo adapter only.
- EF Core migrations are checked in and must be applied explicitly before a
  PostgreSQL host starts.
- Inventory, catalog, locations, receiving, putaway, picking, adjustments, and
  movement reporting are implemented at the domain/application level.
- Identity authentication, account management, lockout, password reset, audit
  events, permission-based RBAC, warehouse-scoped access, and explicit
  Web/WinForms user sessions are implemented locally;
  tenant isolation, external integrations, cycle counting, replenishment,
  shipping, load qualification, and production operations remain partial or
  planned.
- The authoritative implementation checkpoint is
  [`docs/implementation/EXECUTION_STATUS.md`](docs/implementation/EXECUTION_STATUS.md).

## Supported clients

The ASP.NET Core Web host is the primary supported WareCommand client. The
WinForms host is an optional retained workstation client during the transition
period for scanner-heavy stations; it uses the same Application/Infrastructure
services, Identity session, warehouse scope, and database migrations. It is
not a second business-rule or persistence product. See
[`docs/modernization/WINFORMS_TRANSITION.md`](docs/modernization/WINFORMS_TRANSITION.md)
for the complete capability inventory, parity gates, distribution boundary,
and no-deletion rollback policy.

## Quick start

From the repository root:

```powershell
dotnet tool restore
dotnet restore .\Warehouse Management System.sln
dotnet build .\Warehouse Management System.sln -c Release --no-restore
dotnet test .\Warehouse Management System.sln -c Release --no-build --no-restore
```

For a local web demonstration, use the Development profile. It uses an ignored
SQLite file and explicitly opts into the deterministic Demo seed profile:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project .\Wms.ASP\Wms.ASP.csproj
```

The first Web run has no account unless you explicitly enable bootstrap with a
secret password. Follow [`docs/modernization/AUTHENTICATION.md`](docs/modernization/AUTHENTICATION.md)
to provision the first administrator; no default password is supplied.

For the desktop demonstration:

```powershell
dotnet run --project '.\Warehouse Management System\Wms.WinForms.csproj'
```

For PostgreSQL, set the connection string, apply migrations, and then start the
web host. The production configuration uses the `None` seed profile, so it does
not create demo items, stock, or users automatically:

```powershell
$env:WARECOMMAND_POSTGRES_CONNECTION = 'Host=localhost;Port=5432;Database=warecommand;Username=warecommand;Password=<local-password>'
$env:ConnectionStrings__DefaultConnection = $env:WARECOMMAND_POSTGRES_CONNECTION
pwsh -NoProfile -File .\scripts\migrate-postgresql.ps1 -Apply
dotnet run --project .\Wms.ASP\Wms.ASP.csproj
```

For the reproducible local Web plus PostgreSQL container baseline, see
[`DEPLOYMENT.md`](DEPLOYMENT.md) and run the explicit migration sequence there.
The Compose stack does not apply migrations implicitly; it persists database
and Data Protection volumes, exposes the web health endpoints, and injects the
database password through a Compose secret.

Use [`scripts/verify-baseline.ps1`](scripts/verify-baseline.ps1) for the local
MVC and WinForms smoke path and [`scripts/verify-postgresql.ps1`](scripts/verify-postgresql.ps1)
for disposable PostgreSQL qualification.

## Seed profiles

Both hosts use the same `WmsSeedService` and the same data definitions:

| Profile | Contents | Default |
| --- | --- | --- |
| `None` | Schema only; no demo rows | Non-Development web host |
| `Reference` | One warehouse and the minimal receiving/storage locations | Explicit opt-in |
| `Demo` | Reference data, sample catalog/barcodes, hierarchy, and sample stock | Development/local demo |

Seeding is identifier-based and idempotent. A profile is selected through
`Wms:SeedProfile`; when it is omitted, Development resolves to `Demo` and other
environments resolve to `None`. Explicit `Demo` is therefore an opt-in outside
Development, never an automatic production behavior.

## Documentation

- [`CONTRIBUTING.md`](CONTRIBUTING.md) — workflow, versioning, commits, issue
  evidence, and release/tag governance
- [`CHANGELOG.md`](CHANGELOG.md) — unreleased and approved release changes
- [`ARCHITECTURE.md`](ARCHITECTURE.md) — layers, capability ownership, and composition rules
- [`TESTING.md`](TESTING.md) — repeatable local, integration, and smoke verification
- [`DEPLOYMENT.md`](DEPLOYMENT.md) — current deployment boundary and migration gates
- [`SECURITY.md`](SECURITY.md) — repository and runtime security expectations
- [`docs/modernization/AUTHENTICATION.md`](docs/modernization/AUTHENTICATION.md) — account provisioning, reset, cookies, and WinForms session policy
- [`docs/modernization/AUTHORIZATION.md`](docs/modernization/AUTHORIZATION.md) — permission matrix, warehouse scope, and access-management rules
- [`ROADMAP.md`](ROADMAP.md) — implemented, partial, planned, and deferred work
- [`docs/release/RELEASE_CHECKLIST.md`](docs/release/RELEASE_CHECKLIST.md) — backup, migration, rollout, monitoring, and rollback gates
- [`docs/modernization/BACKUPS.md`](docs/modernization/BACKUPS.md) — encrypted PostgreSQL backup, restore, retention, and RPO/RTO policy
- [`docs/modernization/TIME_AND_DATES.md`](docs/modernization/TIME_AND_DATES.md) — UTC storage, IANA time zones, business dates, DST, and report ranges
- [`docs/operations/BACKUP_DISASTER_RECOVERY.md`](docs/operations/BACKUP_DISASTER_RECOVERY.md) — incident and restore-rehearsal runbook
- [`docs/modernization/POSTGRESQL_LOCAL_SETUP.md`](docs/modernization/POSTGRESQL_LOCAL_SETUP.md)
- [`docs/modernization/SQLITE_TO_POSTGRESQL_MIGRATION.md`](docs/modernization/SQLITE_TO_POSTGRESQL_MIGRATION.md)
- [`docs/modernization/BASELINE_STATUS.md`](docs/modernization/BASELINE_STATUS.md) — original baseline evidence

## Repository hygiene

Runtime databases, logs, local settings, secrets, coverage, test results, and
generated artifacts are ignored. No runtime database is required to clone or
build the repository; hosts create their explicitly configured local database
or connect to the configured PostgreSQL instance.

The supplied untracked `Front-End/` package is preserved as user work and is
not part of the backend qualification described here.
