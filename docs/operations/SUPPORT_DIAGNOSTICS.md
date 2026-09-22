# Support diagnostics and safe troubleshooting

Support diagnostics are evidence collection, not a second inventory-editing
interface. Every future diagnostics screen or bundle generator must require
an authorized operator, a reason, an explicit time/reference scope, a bounded
size, expiry, redaction, and an audit record.

## Safe bundle contents

A bundle may contain application/schema version, environment name, health
component states, migration identifiers, job/outbox/inbox counts and safe
references, backup freshness, selected correlation/reference IDs, and bounded
log/telemetry summaries. It must not contain passwords, API keys, cookies,
bearer headers, Data Protection keys, connection-string secrets, raw business
payloads, unrestricted personal data, or a database dump.

The Application `SupportBundlePolicy` enforces a 31-day maximum evidence
window, five-million-byte maximum bundle size, one-day maximum expiry, a
required reason, and future-window protection. Sensitive keys are replaced with
`[redacted]` and non-sensitive values are length-bounded.

## Operator sequence

1. Capture the release revision, environment, time, and correlation/reference
   ID before restarting anything.
2. Check `/health/live` and `/health/ready`, schema/migration state, storage,
   Data Protection keys, backup freshness, worker/job backlog, outbox/inbox
   state, and recent critical alerts.
3. Generate only the smallest authorized bundle needed for the incident. Record
   the reason, scope, expiry, download identity, and audit reference.
4. Prefer idempotent job retry, webhook replay, health recheck, reconciliation
   dry-run, or printer diagnostics. Never edit inventory directly in SQL.
5. Preserve the bundle and relevant logs under the approved retention policy;
   delete or expire it when the incident record no longer requires it.

The current slice defines the redaction/expiry/size policy and tests. The
administrator diagnostics screen, durable bundle storage/download, audit trail,
safe remediation commands, runbook rehearsals, and production support workflow
remain follow-up gates.
