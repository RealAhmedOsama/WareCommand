# PostgreSQL backup and restore

Issue #22 provides the application-owned PostgreSQL backup boundary. It is
enabled only on a PostgreSQL host with the durable job runner enabled. SQLite
and local demo hosts keep the feature disabled.

## Recovery objectives

The default operating policy is:

- RPO target: 24 hours. The scheduled backup runs once per day at 01:00 UTC.
- Freshness guard: 36 hours. When backups are enabled, readiness is unhealthy
  until a verified artifact is available within this window.
- RTO target: 60 minutes for restoring the database into an isolated target,
  reconciling the inventory totals/references, and completing the application
  smoke path. This is a policy target, not a measured production guarantee;
  record the elapsed time during every rehearsal.

An operator must treat a missed backup, failed verification, missing offsite
copy, or expired freshness window as a data-protection incident. Do not clear
the alert by deleting status files or by force-completing the job.

## Artifact and secret boundary

Each `.wcbak` artifact contains:

1. A PostgreSQL `pg_dump` custom-format dump with compression.
2. A JSON manifest containing the UTC creation time, source revision, database
   name, row/quantity summary, and hashes of the packaged files.
3. Data Protection keys when `DataProtection:KeyDirectory` exists.
4. Files under the optional `Wms:Backups:AssetsPath` directory. The current
   repository has no upload subsystem, so this path is opt-in and must point to
   the real protected asset store if a deployment adds one.

The package is encrypted with AES-256-CBC and authenticated with HMAC-SHA256.
The key is exactly 32 raw bytes, 64 hexadecimal characters, or a 32-byte
Base64 value. Keep it in a secret manager or a root-readable secret file; it
must not be committed, placed in `appsettings*.json`, or passed as a command
argument. A `.sha256` sidecar is written and checked after offsite replication.

PostgreSQL connections used by `pg_dump` and `pg_restore` inherit the
connection's `SslMode` and certificate paths. Production connections must use
TLS with certificate verification (`VerifyFull` or the platform-equivalent)
and an approved root certificate. The offsite path must be a separately
protected volume or managed object-store mount with encryption at rest and
authenticated encryption in transit; a second directory on the same failed
disk is not an offsite copy.

## Configuration

The production example and Compose baseline expose:

```text
Wms__Jobs__Enabled=true
Wms__Backups__Enabled=true
Wms__Backups__RootPath=/var/lib/warecommand/backups
Wms__Backups__EncryptionKeyFile=/run/secrets/warecommand-backup-key
Wms__Backups__OffsitePath=<separately-mounted-protected-path>
Wms__Backups__RetentionDays=30
Wms__Backups__MinimumRetainedBackups=2
Wms__Backups__MaximumAgeHours=36
```

The web image includes the PostgreSQL client tools. A non-container host must
provide compatible `pg_dump` and `pg_restore` binaries and set
`Wms:Backups:PgDumpExecutable`/`Wms:Backups:PgRestoreExecutable` when they are
not on `PATH`.

## Commands

The standalone `Wms.Backup` utility and PowerShell wrappers use environment
variables for connection strings and the key file. They never add a password
or key to PostgreSQL tool arguments or normal output.

```powershell
$env:WARECOMMAND_POSTGRES_CONNECTION = '<secret-managed-TLS-connection-string>'
$env:WARECOMMAND_BACKUP_ENCRYPTION_KEY_FILE = '<protected-key-file>'
pwsh -NoProfile -File .\scripts\backup-postgresql.ps1 -VerifyAfter
pwsh -NoProfile -File .\scripts\verify-backup.ps1 -ArtifactPath '<artifact-path>'
pwsh -NoProfile -File .\scripts\prune-backups.ps1
```

For a restore, provision an isolated empty PostgreSQL database, set the target
connection only in the process environment, and run:

```powershell
$env:WARECOMMAND_RESTORE_TARGET_CONNECTION = '<isolated-target-TLS-connection-string>'
pwsh -NoProfile -File .\scripts\restore-postgresql.ps1 -ArtifactPath '<artifact-path>' -TargetConnectionString $env:WARECOMMAND_RESTORE_TARGET_CONNECTION
```

The restore refuses a target with user objects. The only override requires the
exact `RESTORE-WARECOMMAND-DATA` confirmation and an approved restore window.
After restore it compares item/warehouse/location/lot/stock/movement counts,
available/reserved/movement quantities, and critical inventory foreign-key
references.

## Retention and monitoring

Pruning first authenticates and validates each candidate, including the
manifest hashes and `pg_restore --list`. It removes only verified artifacts
older than the configured retention period and never reduces any configured
backup directory below `MinimumRetainedBackups`. Invalid or unreadable
artifacts are retained for investigation and cause an operational review.

The backup job records `backup-status.json`, emits the bounded
`warecommand.backups.failures` metric on failure, and contributes a `backup`
check to `/health/ready`. The status file is reloaded on host startup only when
the recorded successful artifact still exists; otherwise readiness fails closed
until a new backup succeeds.

## Migration and release gate

Run a fresh backup and record the artifact path, SHA-256, operator, UTC time,
source revision, and restore result before applying a production migration or
deploying a release. Use [`DEPLOYMENT.md`](../../DEPLOYMENT.md) for the
controlled rollout and
[`BACKUP_DISASTER_RECOVERY.md`](../operations/BACKUP_DISASTER_RECOVERY.md)
for incident decisions.

## Point-in-time recovery boundary

The application implementation provides verified logical snapshots, not WAL
archiving or point-in-time recovery. If the deployment requires an RPO below
24 hours, configure PostgreSQL continuous archiving/WAL retention through the
managed database platform, test recovery to a named timestamp, and document
that provider procedure beside the application artifact. Do not describe the
logical `.wcbak` schedule as PITR.
