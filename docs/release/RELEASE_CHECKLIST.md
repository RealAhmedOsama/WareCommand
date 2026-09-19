# WareCommand release checklist

This checklist is completed for a proposed release tag. Local qualification is
evidence, not permission to deploy. A release owner must record the exact
revision, environment, decision, and rollback boundary.

## 1. Identity and scope

- [ ] Version follows the SemVer rule in [`CONTRIBUTING.md`](../../CONTRIBUTING.md).
- [ ] The source commit is identified and the `CHANGELOG.md` section is ready.
- [ ] Included issues are closed with implementation, verification, and
      dependency evidence.
- [ ] No unrelated working-tree changes are included.
- [ ] The candidate image tag is immutable and the OCI version/revision labels
      match the release identity.

## 2. Build and qualification

- [ ] Restore, Release build, analyzers, formatting, and tests pass.
- [ ] Provider-gated PostgreSQL and migration checks pass or have an explicit
      approved exception.
- [ ] Production container build passes; runtime is non-root and has no
      development assets or secrets.
- [ ] Health liveness and readiness checks return the expected status.
- [ ] Browser/host smoke checks cover the affected user journeys.
- [ ] Dependency, vulnerability, secret, and security checks are reviewed.

## 3. Backup and migration

- [ ] Target database backup completed; owner, timestamp, location, and restore
      result are recorded outside the repository.
- [ ] The exact checked-in migration script was reviewed against the target.
- [ ] Migration compatibility with the previous application version is proven.
- [ ] Data reconciliation, row/quantity totals, and relationship checks pass.
- [ ] Migration is applied by an explicit controlled command, never by normal
      application startup.

## 4. Runtime and data protection

- [ ] HTTPS termination and forwarded-header trust are configured at the
      approved reverse proxy or application endpoint.
- [ ] Database credentials and certificates come from the deployment secret
      manager; no secret is in the image, repository, or command history.
- [ ] PostgreSQL data and `/var/lib/warecommand/keys` are persistent protected
      volumes with an owner and restore plan.
- [ ] Logs go to the approved collector; health, restart, and migration alerts
      have an owner.
- [ ] Shutdown timeout and restart behavior were exercised in the target-like
      environment.

## 5. Rollout and rollback

- [ ] Change window, approver, operator, and communication path are recorded.
- [ ] Previous application image/tag is available.
- [ ] Application rollback procedure is tested or explicitly bounded.
- [ ] Database rollback is a rehearsed restore or a documented forward-fix;
      destructive reverse migrations are not assumed safe.
- [ ] Stop/rollback criteria and health/readiness thresholds are explicit.
- [ ] Post-release smoke and monitoring review are scheduled.

## 6. Final evidence and tag boundary

- [ ] Release evidence is attached to the release issue/PR and summarized in
      the execution tracker.
- [ ] The annotated tag is created only after the checklist is approved.
- [ ] Tag/image push and deployment are separate human-authorized actions; no
      automation performs them implicitly.
