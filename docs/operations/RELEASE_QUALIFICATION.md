# Release qualification and controlled deployment

This runbook makes a release reviewable and repeatable. It does not authorize a
production deployment; the environment owner must approve the change window,
backup, migration, and rollback decision separately.

## Current repository qualification — 2026-09-25

The source revision under review is code SHA
`5b89bfdaad82d3d3597e2d2860af8335f17a62c8` on canonical `master`. It adds a
fresh aggregate permission/warehouse snapshot for the shared MVC layout to
reduce repeated database reads. Exact-SHA
[Actions run 36102019301](https://github.com/RealAhmedOsama/WareCommand/actions/runs/36102019301)
completed successfully: Linux and Windows solution quality/coverage,
formatting, migrations/startup, disposable PostgreSQL integration/migration,
production Docker image, and secret scan passed. Pull-request dependency review
was skipped because the event was a direct push. The run's artifacts are
`windows-test-results-36102019301-1`,
`linux-test-results-36102019301-1`, and `secret-scan-results-36102019301`.

The detailed per-assembly and PostgreSQL test counts below are from the
preceding full qualification, [run 36092918964](https://github.com/RealAhmedOsama/WareCommand/actions/runs/36092918964),
on code SHA `5f5d0a59300f7b48a7ad31a0b50f0382fcaf609c`. The current run above
repeated the platform and provider checks after three additional navigation
authorization tests; its result artifacts are available from the linked run.

The Windows and Linux quality jobs each recorded 782 passed, 77 skipped, zero
failed, and zero other skips. The same per-assembly counts appeared on both
platforms:

| Test assembly | Passed | Skipped | Total | Qualification note |
| --- | ---: | ---: | ---: | --- |
| `Wms.Domain.Tests` | 175 | 0 | 175 | Full solution run |
| `Wms.Application.Tests` | 137 | 0 | 137 | Full solution run |
| `Wms.Infrastructure.Tests` | 393 | 68 | 461 | All 68 skips are PostgreSQL-provider gated |
| `Wms.ASP.Tests` | 62 | 8 | 70 | All 8 skips are PostgreSQL-provider gated |
| `Wms.Architecture.Tests` | 15 | 0 | 15 | Full solution run |
| `Wms.DataMigration.Tests` | 0 | 1 | 1 | Provider-backed case runs in the dedicated migration job |
| **Per-platform total** | **782** | **77** | **859** | No failures; all skips are provider-gated |

The dedicated PostgreSQL job had no skipped or failed test in its selected
groups:

| Provider group | Test assembly | Passed | Skipped/failed |
| --- | --- | ---: | ---: |
| `core` | `Wms.Infrastructure.Tests` | 1 | 0 |
| `harness` | `Wms.Infrastructure.Tests` | 56 | 0 |
| `dashboard` | `Wms.ASP.Tests` | 3 | 0 |
| `data-generation` | `Wms.Infrastructure.Tests` | 4 | 0 |
| `journeys` | `Wms.Infrastructure.Tests` | 7 | 0 |
| `resilience` | `Wms.ASP.Tests` | 3 | 0 |
| `browser` | `Wms.ASP.Tests` | 1 | 0 |
| **PostgreSQL group total** |  | **75** | **0** |
| SQLite-to-PostgreSQL migration | `Wms.DataMigration.Tests` | 1 | 0 |

The Linux deterministic-repeat step ran the 312 Domain/Application tests twice;
both repetitions passed. These repeats are not added to either platform total.
Measured line coverage was 8.22% on both hosts (Windows 68,698/835,651 lines;
Linux 68,699/835,651); the CI contract does not set a coverage threshold.

The production Docker image build and secret scan passed. Dependency review was
skipped because this was a direct push rather than a pull request. The uploaded
Windows, Linux, and secret-scan artifacts are
`windows-test-results-36092918964-1` (20,122,104 bytes),
`linux-test-results-36092918964-1` (20,270,198 bytes), and
`secret-scan-results-36092918964` (7,417 bytes). Download them from the exact
Actions run above. The PostgreSQL job publishes its run log, not a success
artifact; the provider-group counts and migration result are recorded above.

The prior run `36091549462` on `393ac2593ea60ff323dcf7926a5f9ada91f7e1d4`
failed only its Windows formatter step: nested authorization-switch blocks
needed one additional indentation level, and the dependent PostgreSQL job was
skipped. Commit `5f5d0a5` applies that correction; the exact-SHA Windows
formatting and migration/startup checks passed in run `36092918964` and passed
again in run `36102019301`. The formatter failure is retained as qualification
history, not counted as a test assertion failure.

The code's focused recommendation evidence is also separate from the solution
totals: `RecommendationGovernanceServiceTests` passed 7/7, the Release ASP
test-project build had zero warnings/errors, and the authenticated PostgreSQL
recommendation lifecycle passed 1/1 with zero reconciliation issues. These
results are in the [#128 evidence comment](https://github.com/RealAhmedOsama/WareCommand/issues/128#issuecomment-5826763905)
and the [execution checkpoint](../implementation/EXECUTION_STATUS.md).

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

The local packet validator is `scripts/verify-release-evidence-packet.ps1`.
It runs the checked-in release preflight, migration lifecycle, security-boundary,
and support/security tests, then emits a bounded secret-free packet. Its local
decision is always `GO_WITH_RESTRICTIONS`; it cannot imply production approval.

```powershell
pwsh -NoProfile -File scripts/verify-release-evidence-packet.ps1 -PlanOnly
pwsh -NoProfile -File scripts/verify-release-evidence-packet.ps1 `
  -Environment LocalQualification `
  -EvidencePath artifacts/release-local.json
```

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
