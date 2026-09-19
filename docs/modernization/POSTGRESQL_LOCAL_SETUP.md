# PostgreSQL local setup

WareCommand uses PostgreSQL as its production database provider. The web host
defaults to PostgreSQL and requires an explicit connection string; it does not
fall back to SQLite when the production connection is missing.

SQLite remains an explicit local/demo option. Set both configuration values
when selecting it:

```powershell
$env:Wms__DatabaseProvider = 'Sqlite'
$env:ConnectionStrings__DefaultConnection = 'Data Source=warehouse-development.db'
dotnet run --project .\Wms.ASP\Wms.ASP.csproj
```

For PostgreSQL, provide the connection string through an environment variable
or a local, untracked configuration source:

```powershell
$env:Wms__DatabaseProvider = 'PostgreSql'
$env:ConnectionStrings__DefaultConnection = 'Host=localhost;Port=5432;Database=warecommand;Username=warecommand;Password=<local-password>'
dotnet run --project .\Wms.ASP\Wms.ASP.csproj
```

The WinForms host defaults to its explicit SQLite demo database. Operators can
select PostgreSQL with the same `Wms__DatabaseProvider` and
`ConnectionStrings__DefaultConnection` environment variables.

## Disposable PostgreSQL qualification

Docker is required for the isolated PostgreSQL check. The script creates a
temporary PostgreSQL 17 container and removes it in `finally`; it does not use
or modify an existing application database.

```powershell
pwsh -NoProfile -File .\scripts\verify-postgresql.ps1
```

The script sets `WARECOMMAND_TEST_POSTGRES_CONNECTION` only for the focused
integration test. To run that test against an explicitly provisioned test
database instead, set the variable yourself and run:

```powershell
dotnet test .\Wms.Infrastructure.Tests\Wms.Infrastructure.Tests.csproj `
  -c Release --filter 'FullyQualifiedName~PostgreSqlIntegrationTests'
```

Do not point the integration test or migration commands at a production
database. The design-time factory uses `WARECOMMAND_POSTGRES_CONNECTION`, or a
localhost development default, solely for PostgreSQL migration generation:

```powershell
$env:WARECOMMAND_POSTGRES_CONNECTION = 'Host=localhost;Port=5432;Database=warecommand;Username=warecommand;Password=<local-password>'
dotnet ef migrations add <MigrationName> `
  --project .\Wms.Infrastructure\Wms.Infrastructure.csproj `
  --startup-project .\Wms.ASP\Wms.ASP.csproj `
  --context WmsDbContext
```

Apply checked-in PostgreSQL migrations explicitly before starting a host:

$env:WARECOMMAND_POSTGRES_CONNECTION = 'Host=localhost;Port=5432;Database=warecommand;Username=warecommand;Password=<local-password>'
dotnet tool restore
pwsh -NoProfile -File .\scripts\migrate-postgresql.ps1 -Apply

PostgreSQL startup refuses to run against a schema with pending migrations;
it does not silently alter production schema. SQLite demo mode uses
EnsureCreated against its explicit local file because it is not the
production persistence path.

For the controlled SQLite-to-PostgreSQL data import lifecycle, see
docs/modernization/SQLITE_TO_POSTGRESQL_MIGRATION.md.
