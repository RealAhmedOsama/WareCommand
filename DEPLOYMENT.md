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

## Container baseline

`docker-compose.yml` is the local Web plus PostgreSQL baseline. It keeps both
services on an internal network, binds host ports to loopback by default,
persists PostgreSQL data in `postgres-data`, persists ASP.NET Core Data
Protection keys in `warecommand-data`, and injects the database password as a
Compose secret sourced from `WARECOMMAND_POSTGRES_PASSWORD`. The web container
uses the non-root runtime user from the .NET image, exposes port 8080, has a
real liveness/readiness health probe, and receives a 30-second graceful-stop
window.

The application refuses to start with pending PostgreSQL migrations. Apply the
checked-in migration explicitly before starting the web service:

```powershell
$env:WARECOMMAND_POSTGRES_PASSWORD = '<local-only-secret>'
$env:WARECOMMAND_POSTGRES_PORT = '55434'
$env:WARECOMMAND_WEB_PORT = '58080'
docker compose -f .\docker-compose.yml up --detach postgres
$env:WARECOMMAND_POSTGRES_CONNECTION = 'Host=127.0.0.1;Port=55434;Database=warecommand;Username=warecommand;Password=<local-only-secret>'
pwsh -NoProfile -File .\scripts\migrate-postgresql.ps1 -Apply
docker compose -f .\docker-compose.yml up --detach --build web
Invoke-WebRequest http://127.0.0.1:58080/health/live
Invoke-WebRequest http://127.0.0.1:58080/health/ready
```

`docker compose down` stops the stack while retaining the named volumes.
`docker compose down --volumes` is destructive and removes the local database
and Data Protection keys. Do not use it for a production rollback.

The image is HTTP-only on its internal 8080 port. In production, terminate
HTTPS at a managed reverse proxy, forward `X-Forwarded-For` and
`X-Forwarded-Proto`, set `ForwardedHeaders:Enabled=true`, and configure only
the proxy addresses in `ForwardedHeaders:KnownProxies`. The host refuses to
start when forwarded-header processing is enabled without valid trusted proxy
addresses, and it accepts only one symmetric forwarded-header hop. The proxy
must enforce the external HTTPS policy; do not bake certificates or private
keys into the image. If the application terminates TLS directly, provide the
certificate through the platform secret store and configure a separate HTTPS
endpoint. HSTS is emitted by the host outside Development.

The production example keeps forwarded-header processing disabled until the
operator supplies the actual proxy addresses. Set the production `Security`
limits and named rate-limit values deliberately for the deployment; do not
widen them to compensate for an unbounded client or report workload.
For the Compose baseline, set `WARECOMMAND_FORWARDED_HEADERS_ENABLED=true`
and `WARECOMMAND_FORWARDED_HEADERS_PROXY` to the reverse proxy address
together; enabling the flag without the address intentionally fails startup.

Keep `/var/lib/warecommand/keys` on persistent protected storage. Losing this
directory invalidates encrypted cookies and other Data Protection payloads.
The application logs to standard output/error so the container runtime can
collect and retain logs. Compose uses `unless-stopped` restart behavior, but
restart policy does not replace backups, migration review, readiness checks,
or an operational owner.

Use immutable image tags such as the commit SHA and retain the OCI version,
revision, and build-date labels. The checked-in `.env.example` contains only
placeholders; copy it to a local ignored `.env` or inject values through the
deployment secret manager.

## Controlled PostgreSQL rollout

1. Set `ConnectionStrings__DefaultConnection` for the web host and
   `WARECOMMAND_POSTGRES_CONNECTION` for the migration tooling.
2. Back up the target database using [`scripts/backup-postgresql.ps1`](scripts/backup-postgresql.ps1)
   and the [backup procedure](docs/modernization/BACKUPS.md); record the
   artifact checksum and restore/verification result before continuing.
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

## Authentication bootstrap and operations

The production baseline creates no users and contains no default password.
Provision the first administrator only during a controlled change by enabling
`Authentication:Bootstrap` and supplying the username, email, profile fields,
and a secret password through the platform secret manager. The password may be
mounted through `Authentication:Bootstrap:PasswordFile`. Disable bootstrap
after the first successful run; the bootstrapper is otherwise a no-op once any
user exists.

Set `Authentication:CookieSecure=true` for an HTTPS browser-facing endpoint,
keep Data Protection keys on the persistent protected volume, and configure
the SMTP secret values before relying on self-service password reset email.
Account creation and disabling are administrator-only. The Web and desktop
hosts both persist the real Identity user ID in movement audit data; the
desktop client requires a fresh sign-in on every restart.

## Data migration

SQLite-to-PostgreSQL migration is a separate controlled operation. Use the
read-only dry run first, then apply with a mandatory backup directory and a
fresh or explicitly approved target. See
[`docs/modernization/SQLITE_TO_POSTGRESQL_MIGRATION.md`](docs/modernization/SQLITE_TO_POSTGRESQL_MIGRATION.md).

## Recovery boundary

The migration tool rolls back its target transaction when validation fails and
does not modify the SQLite source. Production database backup, restore,
maintenance-window, and application rollback procedures are defined in the
[disaster-recovery runbook](docs/operations/BACKUP_DISASTER_RECOVERY.md) and
still need an environment-specific owner and rehearsal before release.
