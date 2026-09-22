# Release qualification and controlled deployment

This runbook makes a release reviewable and repeatable. It does not authorize a
production deployment; the environment owner must approve the change window,
backup, migration, and rollback decision separately.

## Evidence packet

Record these values against the exact immutable application revision:

- source commit, image digest, build metadata, and configuration revision;
- `dotnet build "Warehouse Management System.sln" --configuration Release` output;
- focused unit/infrastructure/API/browser/provider results and known skips;
- `scripts/verify-migrations.ps1` and the generated SQL/migration review;
- PostgreSQL version, schema version, pending-migration result, and connection
  health from the target environment;
- backup artifact path/checksum, restore verification, and reconciliation result;
- storage/Data Protection key readiness, worker/job drain state, and monitoring
  owner;
- post-deploy smoke results, correlation IDs, error rate/backlog observations,
  and the go/no-go decision.

The evidence packet must not contain passwords, connection-string secrets,
cookies, bearer tokens, Data Protection keys, raw integration payloads, or an
unredacted database dump.

## Preflight gates

1. Review the scoped diff and confirm the source revision is immutable. Run the
   read-only repository preflight:

   ```powershell
   pwsh -NoProfile -File .\scripts\verify-release-preflight.ps1
   ```

2. Run the proportionate build/test/provider/security gates. A narrow green test
   cannot erase a known full-suite failure; list every skip/failure and owner.
3. Confirm production configuration is supplied by the deployment secret manager,
   forwarded-proxy addresses are explicit, HTTPS/cookie settings are correct,
   persistent Data Protection keys and storage are available, and no demo seed
   profile/default credentials are enabled.
4. Confirm the database backup and restore verification are fresh enough for the
   planned RPO. Do not begin a destructive or incompatible migration without
   the backup artifact and rollback/restore owner recorded.
5. Confirm workers can be paused/drained, pending outbox/inbox/job work is
   observable, and a maintenance/communication window exists for mutations.

## Rollout order

1. Announce the window and pause/drain mutation workers according to the
   operations owner’s procedure.
2. Capture the exact backup/checksum and verify it before applying changes.
3. Review the forward-only migration SQL. Apply migrations with the controlled
   migration tool; the application must not apply PostgreSQL migrations during
   startup.
4. Deploy the immutable application image/configuration. Keep the previous image
   available for a rollback decision, but do not assume an old binary is safe
   after a schema change.
5. Verify `/health/live`, `/health/ready`, login/session behavior, warehouse
   scope, a non-mutating inventory inquiry, job/backlog health, integration
   delivery state, printing diagnostics, and backup freshness.
6. Perform only an explicitly approved, traceable smoke mutation. Reconcile the
   affected document, inventory balance, ledger, reservation, serial/LPN, audit,
   and integration state.
7. Resume workers gradually, monitor the agreed window, and record the release
   decision with revision, migration, backup, and correlation references.

## Rollback and restore decision

Application rollback is allowed only when the current schema and contracts are
backward-compatible. For an incompatible forward migration or uncertain data
state, stop mutations, preserve evidence, and use the approved PostgreSQL
restore/recovery procedure instead of running an inverse migration. After a
restore, run migration/schema checks and the relevant reconciliation suite before
resuming operations.

An interrupted migration, failed readiness check, unexpected outbox/job backlog,
or non-zero reconciliation is a release blocker. Do not repair inventory by
direct database edits; use the owning application command or a documented,
audited recovery procedure.

## Compatibility matrix

Release review must compare application revision, EF migration head, API/OpenAPI
contract version, integration event/document versions, PWA assets/service-worker
version, and the retained WinForms client boundary. Additive contract changes
must remain readable by the supported previous consumer during the rollout
window. Breaking changes require an explicit migration/consumer plan.

## Release report

Use the evidence packet as the release report. It must end with one of:

- `GO`: all blockers closed, explicit approval recorded, smoke/reconciliation
  evidence green;
- `GO_WITH_RESTRICTIONS`: named non-critical gaps, owner, expiry, and monitoring
  guard are recorded;
- `NO_GO`: any backup, migration, security, compatibility, health, or
  reconciliation blocker remains.

No release status in this document implies that a live deployment occurred.
