# Backup and disaster-recovery runbook

This runbook applies to a PostgreSQL deployment with `Wms:Backups:Enabled=true`.
The operator owns the change window, secret access, target isolation, and
evidence record. Preserve the original artifact and logs until the incident is
closed.

## Stop conditions

Stop application traffic and stop migrations when any of these is true:

- `/health/ready` reports the backup check unhealthy during a release window;
- the latest artifact cannot be authenticated, listed by `pg_restore`, or
  reconciled after restore;
- the offsite copy has a different SHA-256 or is unavailable;
- the target database contains unexpected user objects;
- the operator cannot identify the exact source revision, artifact, or restore
  target.

Never repair a backup incident by deleting the status file, pruning manually,
or running a destructive restore against the only surviving database.

## First response

1. Capture UTC time, incident owner, source revision, `/health/ready` output,
   backup job execution/correlation ID, `backup-status.json`, disk capacity,
   and the artifact plus sidecar paths. Redact connection strings, passwords,
   keys, tokens, and user data from the incident record.
2. Preserve the newest local and offsite artifacts. Do not run retention cleanup
   while the incident is being assessed.
3. Verify the artifact in an isolated workspace with
   `scripts/verify-backup.ps1`. A failed verification means that artifact is
   not a recovery point.
4. Select the newest verified artifact whose manifest timestamp meets the
   recovery objective. If no artifact meets it, escalate the RPO breach and use
   the managed PostgreSQL/WAL recovery procedure if configured.

## Corruption or failed backup

1. Leave the application database online if it is still serving correct data,
   but disable release/migration activity and open an incident.
2. Inspect the bounded backup failure metric, job ledger attempt history, disk
   space, key-file readability, offsite reachability, and PostgreSQL client
   tool versions without printing secrets.
3. Verify the newest prior artifact and its offsite copy. If both pass, record
   the usable recovery point and repair the backup destination/tool/credential.
4. Run a new backup with `-VerifyAfter`. Readiness is cleared only after the
   new local and offsite verification succeeds.

## Accidental local deletion or server loss

1. Provision a clean PostgreSQL instance and a protected application volume.
2. Restore the Data Protection key directory and asset directory from their
   independent protected copies. If those copies are unavailable, record that
   browser sessions and any protected assets may require re-issuance or manual
   recovery.
3. Restore the newest verified offsite `.wcbak` into an empty database using
   `scripts/restore-postgresql.ps1`.
4. Confirm the manifest counts, quantity totals, critical references, checked-in
   migration state, authentication bootstrap policy, and `/health/ready`.
5. Run the application smoke path in the isolated environment before changing
   DNS or traffic. Record elapsed restore time against the 60-minute RTO target.

## Failed migration or deployment

1. Stop the web host and prevent retries or new migration attempts.
2. Preserve the pre-deployment artifact and exact migration/release revision.
3. Prefer a forward fix when the schema/data is valid. If the database is not
   trustworthy, restore the pre-deployment artifact into an isolated target,
   reconcile it, and switch only during the approved rollback window.
4. Do not use an unreviewed reverse migration as a rollback strategy. The
   application must start only after the checked-in migration set and target
   schema agree.

## Credential rotation

1. Create the replacement database credential with the minimum required role
   grants while the old credential remains valid.
2. Update the deployment secret manager, restart/reload the host in the change
   window, and confirm database/readiness checks.
3. Run a verified backup and isolated `verify` using the new credential. Do not
   rotate away the old credential until that evidence is captured.
4. Revoke the old credential, then run one more readiness and backup check.
   If the new credential fails, restore the previous secret and investigate;
   never put either credential into repository files or command history.

Encryption-key rotation is a separate controlled operation. Keep the old key
available until every artifact encrypted with it is beyond retention or has
been explicitly re-encrypted and verified; losing the old key makes those
artifacts unreadable.

## Restore rehearsal

Perform at least one rehearsal before production release and on the approved
recurring operations cadence:

1. Select a real encrypted artifact and copy it to the isolated rehearsal
   workspace without modifying the source or offsite copy.
2. Provision an empty PostgreSQL database with the target PostgreSQL major
   version and the required TLS policy.
3. Run `verify`, then `restore`, and capture start/end UTC timestamps.
4. Confirm all manifest counts and quantities, the critical-reference query,
   migration history, admin login/bootstrap boundary, Data Protection keys, and
   any asset inventory.
5. Exercise the read-only inventory/report smoke path, discard the rehearsal
   target through the approved cleanup process, and attach the evidence to the
   release/operations record.

The rehearsal is the evidence for restore usability. A successful dump alone
does not close a recovery incident.
