# Testing WareCommand

This file is the repeatable testing entry point. It reports local evidence; it
does not imply production, security, load, or external-provider qualification.

## Standard local gate

From a restored checkout:

```powershell
dotnet tool restore
dotnet restore '.\Warehouse Management System.sln'
dotnet build '.\Warehouse Management System.sln' -c Release --no-restore
dotnet test '.\Warehouse Management System.sln' -c Release --no-build --no-restore --logger 'console;verbosity=minimal'
dotnet format '.\Warehouse Management System.sln' --verify-no-changes --no-restore --severity error
pwsh -NoProfile -File '.\scripts\verify-migrations.ps1'
```

The PostgreSQL integration and SQLite-to-PostgreSQL migration tests are
provider-gated. Without their environment variables they are reported as
skipped rather than falsely reported as passing.

## Disposable PostgreSQL gates

These scripts start a uniquely named PostgreSQL 17 container, run the focused
qualification, and remove only the container they started:

```powershell
pwsh -NoProfile -File '.\scripts\verify-postgresql.ps1' -Port 55432
pwsh -NoProfile -File '.\scripts\verify-data-migration.ps1' -Port 55433
```

The first gate verifies the checked-in schema, startup refusal when migrations
are pending, explicit migration application, seed/query behavior, and UTC
readback. The second gate verifies representative SQLite import, identifier
preservation, relationship reconciliation, idempotent rerun, source-hash
preservation, and target rollback on invalid source data.

## Container qualification

The container baseline is intentionally migration-gated. Start only PostgreSQL,
apply the checked-in migration with `scripts/migrate-postgresql.ps1 -Apply`,
and then start the Web service from `DEPLOYMENT.md`. Verify both
`/health/live` and `/health/ready`, confirm the Web container is non-root, stop
and restart it, and confirm the named PostgreSQL and Data Protection volumes
remain present. Stop PostgreSQL while Web is running and verify readiness
returns `503`; restore PostgreSQL and verify readiness returns `200` again.

The production Docker job also validates both Compose files and builds an
immutable commit-tagged image. It does not start a production stack, apply a
live migration, push an image, or deploy a service.

## Host smoke checks

```powershell
pwsh -NoProfile -File '.\scripts\verify-baseline.ps1' -WebPort 5244
```

The smoke script uses temporary local databases and ports. It verifies MVC
routes and that the WinForms process reaches a live main window; it is not a
browser acceptance suite.

## Fresh-clone expectation

No runtime database is checked in. A clone should restore, build, and test
without hidden files. A local SQLite database is created only when an explicit
SQLite configuration is selected. PostgreSQL startup requires a configured
connection and a schema that has already received the checked-in migrations.

The current command results and commit checkpoints are recorded in
[`docs/implementation/EXECUTION_STATUS.md`](docs/implementation/EXECUTION_STATUS.md).

## Pull-request CI

`.github/workflows/quality.yml` is the repository CI entry point. It restores
packages on every run; the NuGet cache only accelerates that restore and cannot
hide restore failures. The Linux job builds the production projects that do
not target Windows, runs their tests with coverage, and verifies migrations.
The Windows job builds the complete solution (including WinForms), runs the
complete test suite with coverage, checks formatting, and verifies migrations.

Additional jobs run disposable PostgreSQL integration and data-migration
checks, validate the Compose file, build the production Docker image without
pushing it, scan for secrets with Gitleaks, and review dependency changes on
pull requests. Test and coverage artifacts are uploaded even when a quality
job fails.

The dependency gate fails on vulnerabilities and on deprecated packages that
are not explicitly documented. The current restore graph reports the xUnit v2
packages as legacy, so those five package names are a temporary allowlist and
produce a warning. Any new deprecated package must fail CI; the allowlist
should be removed when the test suite is migrated to xUnit v3.

Recommended protected-branch status checks are `Linux quality and coverage`,
`Windows solution quality and coverage`, `Disposable PostgreSQL integration
and migration`, `Production Docker image`, and `Secret scan`. Pull requests
should be required for `master`, with the branch up to date before merge and
force-push deletion disabled. This workflow performs no deployment.
