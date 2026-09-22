# Fault injection and recovery qualification

## Current slice

Issue #99 currently has non-production-only resilience scenario and outcome
contracts in `Wms.Application/Resilience`. The catalog covers ambiguous
post-commit response loss, worker restart, permanent provider failure/dead
letter, and backup restore/reconciliation. Each scenario declares a failure
point, classification, retry limit, expected recovery, mutation/durable-work
scope, and reconciliation requirement.

The policy rejects production/staging fault injection and infinite retry
configuration. A recovery outcome fails when it leaves partial mutations,
duplicate business outcomes, unexplained reconciliation errors, missing
durable resume/dead-letter behavior, or incomplete recovery diagnostics.

## Remaining qualification

Wire test-only hooks into database timeout/conflict, transaction boundaries,
workers, storage, printers/carriers/APIs, webhooks, client disconnects,
container restart, data-protection key loading, and backup restore. Execute
representative journeys from #96 against PostgreSQL, verify outbox/job/inbox
recovery and normal-command idempotency, publish retry/dead-letter metrics,
and rehearse operator restore/reconciliation procedures. The hooks must remain
impossible to enable in production.
