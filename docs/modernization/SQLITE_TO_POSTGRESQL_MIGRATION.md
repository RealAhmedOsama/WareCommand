# SQLite to PostgreSQL migration lifecycle

The Wms.DataMigration executable is an offline import tool for existing
WareCommand SQLite data. It reads the source database in read-only mode and
does not alter the source. The target must already have the checked-in EF
migrations applied; the application and importer refuse a target with pending
migrations.

## Controlled sequence

1. Stop application writes and take the operational maintenance window required
   by the deployment environment.
2. Restore or provision an empty PostgreSQL target.
3. Apply the reviewed schema migrations explicitly:

       $env:WARECOMMAND_POSTGRES_CONNECTION = '<target-connection-string>'
       dotnet tool restore
       pwsh -NoProfile -File .\scripts\migrate-postgresql.ps1 -Apply

4. Run a dry-run against the SQLite source. Dry-run is the default and writes
   no target rows:

       dotnet run --project .\Wms.DataMigration\Wms.DataMigration.csproj -- --source 'D:\safe\warehouse.db' --report '.\artifacts\data-migration\dry-run.json'

5. Review the JSON report. It includes source and target row counts for Items,
   ItemBarcodes, Warehouses, Locations, Lots, Stock, and Movements, stock
   available/reserved totals, movement totals, relationship validation, and
   warnings.
6. Run apply only after the report is accepted. The --backup-directory option
   is mandatory; the tool creates a uniquely named SQLite backup before writing:

       dotnet run --project .\Wms.DataMigration\Wms.DataMigration.csproj -- --source 'D:\safe\warehouse.db' --apply --backup-directory 'D:\safe\warecommand-backups' --report '.\artifacts\data-migration\apply.json'

7. Verify the report has status Applied and start the host. Startup checks the
   migration history and refuses to run if a later schema migration is pending.

The importer preserves primary keys for all core entities and ItemBarcodes,
inserts locations in parent-before-child order, preserves nullable lot and
movement references, and resets PostgreSQL identity sequences after import.
Re-running an accepted import requires --allow-existing-target; rows are
upserted by identifier and the same reconciliation checks run again. The tool
does not delete target rows, so an unrelated non-empty target is rejected by
default and any extra rows fail reconciliation.

## Failure and rollback

- Source schema incompatibility, missing relationships, negative stock, or
  invalid stock reservations are rejected before target writes.
- The source connection is read-only. The mandatory apply backup is made before
  the PostgreSQL transaction starts.
- Every target write and validation runs in one serializable transaction. A
  write error or reconciliation failure rolls the transaction back; the
  source and target remain unchanged.
- After a successful commit, restore the target using the environment's
  approved PostgreSQL backup/restore procedure, or discard the fresh target
  database and repeat the reviewed migration/import sequence. Do not use the
  importer as a delete or rollback tool, and do not point it at production
  until the approved backup and restore rehearsal has passed.

The disposable qualification command exercises a representative source with
warehouses, parent/child locations, items, barcodes, a lot, stock quantities,
and movement history. It also reruns idempotently and forces a target unique-key
failure to rehearse transaction rollback:

    pwsh -NoProfile -File .\scripts\verify-data-migration.ps1

CI runs scripts/verify-migrations.ps1, which checks that the EF model has no
pending migration and that PostgreSQL startup does not apply migrations
implicitly. The full data import check uses an isolated PostgreSQL container
and never uses a production connection.
