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
