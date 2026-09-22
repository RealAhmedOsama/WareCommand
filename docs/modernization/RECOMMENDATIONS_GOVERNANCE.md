# Governed advisory recommendations

## Current slice

Issue #107 currently has an application-owned governance boundary in
`Wms.Application/Recommendations`. It defines approved recommendation types,
versioned source snapshots, provider/model and deterministic-baseline versions,
confidence and bounded impact ranges, expiry, numeric feature summaries,
explanations, lifecycle states, and warehouse/permission scope.

New recommendations are shadow-mode advisory records. Provider availability,
the operational kill switch, bounded feature names, prompt-injection markers,
expiry, and deterministic source metadata are checked before creation. Approval
requires a fresh deterministic eligibility result and an exact current-state
fingerprint match. Execution is only a reference to a normal idempotent WMS
command; the recommendation record itself cannot mutate inventory or block a
worker.

## Remaining qualification

Remaining work includes provider abstraction and cost/latency budgets,
durable recommendation persistence, backtest/shadow comparison, approval UI
and audit events, normal-command adapters for each use case, stale/retry
handling, model quality/drift/acceptance monitoring, redaction and
prompt-injection/data-poisoning tests, localized dashboards, browser/provider/
load qualification, and production kill-switch rehearsal. Deterministic
replenishment, slotting, work, and exception rules remain authoritative when a
provider is unavailable or a recommendation is rejected or expired.
