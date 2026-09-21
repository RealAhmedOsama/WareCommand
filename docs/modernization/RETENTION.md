# Data retention and legal holds

The retention center is a policy and evidence boundary. It is not a shortcut
around the inventory ledger, audit history, backups, or foreign-key safety.

## Classification

| Class | Default | Minimum | Current behavior |
| --- | ---: | ---: | --- |
| Immutable operational history | permanent | permanent | Inventory transactions, movements, reservation events, and traceability history are never candidates. |
| Security audit | permanent | permanent | Authentication and audit rows remain append-only and are never candidates. |
| Documents | 3,650 days | 365 days | Only non-immutable attachments already marked `PendingDeletion` are considered. The attachment row remains as a tombstone. |
| Notifications | 365 days | 30 days | Only expired, resolved, or fully acknowledged non-active notifications are considered; recipient rows are removed by cascade. |
| Idempotency | 90 days | 7 days | Only terminal inventory command replay records past expiry are considered; in-progress commands are protected. |
| Temporary | 30 days | 1 day | Terminal job executions and resolved job notifications are considered. |
| Integration payloads | 365 days | 30 days | Policy is registered, but no payload store is wired yet. |
| Generated reports | 365 days | 30 days | Policy is registered, but no report artifact store is wired yet. |
| Telemetry | 180 days | 30 days | Policy is registered, but the configured telemetry sink owns retention. |

Policies can be global or scoped by company code and warehouse. A policy below
the documented minimum is rejected. Immutable classes cannot be enabled for
purge.

## Holds and archive references

A hold records the class, target type, target identifier, warehouse scope,
reason, and optional case reference. A wildcard target can hold every matching
target in the class and scope. Active holds are evaluated immediately before a
run's candidate list is executed; held records are counted and skipped.

Destructive targets first receive a durable archive reference containing the
source identity, locator, hash when available, searchable metadata, and run
identifier. Attachment bytes are deleted only after that reference is saved;
the attachment metadata remains so operational references and foreign keys are
not orphaned. Failed storage deletion leaves the archive reference retryable.

An archive reference is a traceability record, not a backup. A backup is a
recoverability copy of the database and is governed by
[`BACKUPS.md`](BACKUPS.md). Retention execution requires a caller to provide a
verified-backup assertion and an authorization reference; this does not replace
the backup and restore verification gates.

## Runs and production safety

Every run is durable and records status, time boundary, company/warehouse
scope, batch size, candidate counts by class, hold/skip counts, archive and
purge counts, cursor information, and the last error. The existing cleanup job
records a dry-run preview only. It never performs retention deletion
automatically.

The API requires a completed preview with the same time boundary and scope
before a destructive run. It also requires all three explicit gates:

1. `AllowDestructive=true`;
2. `BackupVerified=true`; and
3. a non-empty authorization reference.

Runs are batch-oriented, cancellation-aware, and retryable by reusing a run
identifier. Archive-reference uniqueness and terminal-state checks make a
retry idempotent. No production purge or archive was executed as part of this
implementation; live execution still requires explicit operational approval,
verified backup evidence, restore compatibility evidence, and provider/live
qualification.
