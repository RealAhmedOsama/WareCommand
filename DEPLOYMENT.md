# WareCommand deployment boundary

WareCommand has local build and disposable-container evidence, not a completed
production release. Deployment remains subject to the remaining Master Plan
#108 release, security, backup, operations, and UI gates.

## Runtime model

- ASP.NET Core MVC is the web host.
- PostgreSQL is the production database provider.
- The web host does not apply migrations on startup. It refuses to start when
  checked-in migrations are pending.
- SQLite is for explicit local/demo use and is not the production persistence
  path.
- The WinForms host is a local desktop/demo host and is not a production
  service process.

## Controlled PostgreSQL rollout

1. Set `ConnectionStrings__DefaultConnection` for the web host and
   `WARECOMMAND_POSTGRES_CONNECTION` for the migration tooling.
2. Back up the target database using the approved operational procedure.
3. Review or apply the checked-in migration script with
   `scripts/migrate-postgresql.ps1`; use `-Apply` only in the approved
   environment and change window.
4. Confirm the migration history and schema are current.
5. Start the host with the production `None` seed profile and perform the
   application-specific smoke checks.
6. Record the exact source revision, migration result, backup location, and
   rollback decision.

The application configuration defaults to `Wms:SeedProfile=None` outside
Development. Enabling `Reference` or `Demo` in a non-development environment
is an explicit operational decision and must not be treated as a deployment
default.

## Data migration

SQLite-to-PostgreSQL migration is a separate controlled operation. Use the
read-only dry run first, then apply with a mandatory backup directory and a
fresh or explicitly approved target. See
[`docs/modernization/SQLITE_TO_POSTGRESQL_MIGRATION.md`](docs/modernization/SQLITE_TO_POSTGRESQL_MIGRATION.md).

## Recovery boundary

The migration tool rolls back its target transaction when validation fails and
does not modify the SQLite source. Production database backup, restore,
maintenance-window, and application rollback procedures still need an
environment-specific owner and rehearsal before release.
