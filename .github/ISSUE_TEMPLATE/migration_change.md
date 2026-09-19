---
name: Migration or schema change
about: Plan a PostgreSQL/schema change with compatibility and rollback evidence.
title: "[Migration] "
labels: "database"
assignees: ""
---

## Change summary

- Tables/columns/indexes/constraints affected:
- Application capability and release version:
- Reason and expected data shape:

## Compatibility plan

- [ ] Additive expand step is safe with the previous application version.
- [ ] Backfill/reconciliation is bounded and observable.
- [ ] Read/write cutover is explicit.
- [ ] Contract cleanup is deferred until old readers/writers are retired.

## Backup and rollback

- Backup command/owner/location:
- Restore rehearsal or evidence:
- Application rollback revision:
- Database rollback strategy (or forward-fix rationale):
- Data Protection key and secret continuity:

## Verification

- [ ] Fresh database migration.
- [ ] Upgrade from representative prior schema/data.
- [ ] Pending-migration startup refusal.
- [ ] Idempotent rerun and reconciliation.
- [ ] Smoke/health checks after migration.

Exact commands and expected evidence:

```text

```
