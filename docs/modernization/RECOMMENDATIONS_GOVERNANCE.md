# Governed advisory recommendations

## Governance boundary

`Wms.Application/Recommendations` defines the approved recommendation types,
bounded source snapshots, provider/model and deterministic-baseline versions,
confidence and impact ranges, expiry, numeric feature summaries, explanations,
permission scope, and lifecycle rules. Proposals are advisory and remain in
shadow mode. They cannot write stock, change work priority, resolve exceptions,
or bypass an existing WMS command. A reviewer must review and explicitly approve
each proposal before it can call a normal WMS command.

The runtime stores proposals in `GovernedRecommendations` and immutable
review/decision/execution events in `GovernedRecommendationEvents`. A proposal
has a stable source-derived identifier, a revision concurrency token, bounded
safe metadata, and a unique event idempotency key. Reviewer comments redact
email addresses and common bearer/API-key/password/token values before they are
stored. The runtime does not accept prompts or raw provider payloads.

## Runtime routes

- `POST /api/recommendations/replenishment/generate` keeps the original
  replenishment route. `POST /api/recommendations/generate/{type}` also
  generates bounded shadow proposals for `Replenishment`, `Slotting`,
  `WorkloadPriority`, `ExceptionResolution`, and `RiskSummary`.
- `GET /api/recommendations`, `GET /api/recommendations/{id}`, and
  `GET /api/recommendations/{id}/history` enforce report, recommendation-type,
  and warehouse scope.
- `GET /api/recommendations/quality` summarizes baseline matches, lifecycle
  decisions, normal-command outcomes, and failed executions by type. The
  default window is 30 days; explicit windows are capped at 366 days. A bounded
  result reports `isTruncated` when its row or event cap is reached.
- `POST /api/recommendations/{id}/review`, `/approve`, `/reject`, `/expire`,
  and `/execute` require the expected revision and an idempotency key. Review,
  approval, rejection, expiry, and execution history survives restarts.

Each type uses an authoritative deterministic source and a registered command
adapter:

- Replenishment re-runs the dry-run planner and fingerprints policy signal,
  destination capacity, open work, eligible source lines, inventory status,
  lot, serial, license plate, and ownership dimensions. Execution calls the
  normal replenishment service.
- Slotting stores the deterministic slotting analysis reference, re-reads its
  pending status and source version, then calls `SlottingService.ApproveAsync`
  to create ordinary warehouse work.
- Workload priority uses the current deterministic workforce queue, rechecks
  that the task remains eligible for the actor, then calls the normal work
  claim command with a stable recommendation-derived idempotency key.
- Exception resolution proposes only a hold for an inbound or outbound
  exception already under review. It rechecks status/revision and calls the
  corresponding normal exception resolution command; it does not approve
  receipt, allocate stock, or cancel an order.
- Risk summary is created only from a current medium/high deterministic
  forecast. It rechecks the input fingerprint and refreshes through the normal
  forecast recalculation service.

Execution repeats current-state checks inside a serializable transaction. The
normal command and the recommendation's executed state commit atomically where
they share the request context. Work creation, exception resolution, and
recommendation event keys make response-loss replay safe. Failed commands are
recorded as retryable and require a new lifecycle idempotency key. A source
that changed or became ineligible returns a conflict before the normal command
runs.

`PostgreSqlJourneyTests.GovernedReplenishmentRecommendationPersistsApprovalAndCreatesOneNormalWorkCommand`
qualifies the persisted service path with a deterministic integration fixture.
It verifies that concurrent approval writes one decision and returns a revision
conflict to the loser, execution creates one authorized replenishment work
item, response-loss replay reuses that work, and inventory and reservations
remain unchanged until normal work completion. A fresh service provider reloads
the executed record, complete history, and warehouse-scoped search result from
PostgreSQL. Removing the actor's warehouse assignment hides the proposal from
unscoped search and denies direct record and explicit warehouse reads. Review
history redacts sensitive comment values. Deep inventory reconciliation runs
after each lifecycle transition.

`PostgreSqlDashboardFlowTests.AuthorizedRecommendationHttpLifecycleUsesPostgreSqlAndCreatesNormalWork`
qualifies replenishment and workload-priority generation/review/approval/
execution through authenticated, antiforgery-protected HTTP over PostgreSQL.
It verifies that workload approval uses the existing idempotent claim command,
and reads the quality summary and persisted event history. Local governance
tests also exercise the quality outcome aggregation.

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
`shadow:deterministic-source-matched`; execution events record the normal
command reference and outcome code. `GET /quality` aggregates that evidence.
The match means the proposal was derived from the current deterministic WMS
source; it does not claim a calibrated model, measured business lift, or
permission for automatic operation. Provider enablement remains disabled by
default and requires separate operational evidence.

## Current implementation boundary

No recommendation UI, paid/live model provider, direct stock mutation, or
automatic decision path is included. The quality endpoint counts persisted
normal-command dispositions; it does not yet calculate measured forecast
calibration or business lift. Browser/handheld UI, restore/load rehearsal, and
production kill-switch enablement remain separate release gates.
