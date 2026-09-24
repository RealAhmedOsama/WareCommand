# Governed advisory recommendations

## Governance boundary

`Wms.Application/Recommendations` defines the approved recommendation types,
bounded source snapshots, provider/model and deterministic-baseline versions,
confidence and impact ranges, expiry, numeric feature summaries, explanations,
permission scope, and lifecycle rules. Proposals are advisory and remain in
shadow mode. They cannot write stock, change work priority, resolve exceptions,
or bypass an existing WMS command.

The runtime stores proposals in `GovernedRecommendations` and immutable
review/decision/execution events in `GovernedRecommendationEvents`. A proposal
has a stable source-derived identifier, a revision concurrency token, bounded
safe metadata, and a unique event idempotency key. Reviewer comments redact
email addresses and common bearer/API-key/password/token values before they are
stored. The runtime does not accept prompts or raw provider payloads.

## Runtime routes

- `POST /api/recommendations/replenishment/generate` creates deterministic
  shadow proposals from the current replenishment signal and an eligible
  replenishment work plan.
- `GET /api/recommendations`, `GET /api/recommendations/{id}`, and
  `GET /api/recommendations/{id}/history` enforce report, recommendation-type,
  and warehouse scope.
- `POST /api/recommendations/{id}/review`, `/approve`, `/reject`, `/expire`,
  and `/execute` require the expected revision and an idempotency key. Review,
  approval, rejection, expiry, and execution history survives restarts.

Approval re-runs the deterministic replenishment planner in dry-run mode and
compares a fingerprint of the policy signal, destination capacity, open work,
eligible source lines, inventory status, lot, serial, license plate, and
ownership dimensions. A stale or blocked plan returns a conflict without
creating work. Execution repeats that check inside a serializable transaction,
then calls the existing replenishment execution service. Its normal
`WarehouseWorkService` command and the recommendation's executed state commit
atomically. Work creation keys and event keys make response-loss replay safe;
failed commands are recorded as retryable and require a new idempotency key.

`PostgreSqlJourneyTests.GovernedReplenishmentRecommendationPersistsApprovalAndCreatesOneNormalWorkCommand`
qualifies the persisted service path with a deterministic integration fixture.
It verifies that concurrent approval writes one decision and returns a revision
conflict to the loser, execution creates one authorized replenishment work
item, response-loss replay reuses that work, and inventory and reservations
remain unchanged until normal work completion. A fresh service provider reloads
the executed record, complete history, and warehouse-scoped search result from
PostgreSQL. Review history redacts sensitive comment values. Deep inventory
reconciliation runs after each lifecycle transition.

## Provider and operating controls

`Recommendations` settings include `Enabled`, `KillSwitchEnabled`,
`ProviderAvailable`, `ProviderName`, `ShadowMode`,
`MaximumProposalsPerRequest`, and `ProviderTimeoutSeconds`. The application
defaults are disabled, kill switch active, and shadow mode enabled. The only
registered provider is `deterministic-wms`; it makes no network or paid-model
calls. Unknown, disabled, unavailable, timed-out, or over-budget generation
fails closed while deterministic WMS operations continue. Generation also
uses the shared API rate limit.

Generated deterministic proposals record
`deterministic-baseline-matched`; execution events record the normal work
reference and result. This is a local shadow comparison and outcome trail, not
a calibrated backtest or model-quality/drift dashboard.

## Current implementation boundary

The durable API and command adapter currently generate and execute replenishment
recommendations. The application contract still names slotting, workload
priority, exception resolution, and risk summary, but no adapter is registered
for those types; they cannot be approved for execution through this runtime.
No recommendation UI or automatic decision path is included. Other types need
their own authoritative revalidation and normal-command adapters before they
can enter the runtime.

Remaining qualification includes broader multi-type adapters, enabled HTTP
generation/approval/execution integration, calibrated backtest and outcome
quality monitoring, provider-failure rehearsal, representative load, restore,
and production kill-switch evidence.
