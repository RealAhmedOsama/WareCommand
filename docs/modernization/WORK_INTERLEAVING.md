# Task interleaving and route sequencing

## Local implementation boundary

Work interleaving is a transparent recommendation layer on top of the
existing warehouse-work and workforce eligibility contracts. It does not
assign work, mutate reservations, or replace the normal FIFO/priority queue.
Supervisor-maintained directed route edges carry route code, sequence,
travel-minutes, optional distance, and notes between active warehouse
locations. Location ancestry lets a zone-level edge serve child locations.

An interleaving policy stores explicit priority, deadline, travel, and zone
affinity weights plus an optional maximum travel cost. When no active policy or
route is available, the service uses a deterministic in-memory policy and
reports the standard priority/deadline fallback instead of guessing a route.

## Recommendation and safety invariants

- Suggestions include only available, unassigned work that passes the existing
  queue, worker, shift, skill, certification, team, zone, and capacity checks.
- Overdue work is ranked before future-due work, future deadlines before
  undated work, and priority/deadline ordering remains ahead of travel-cost
  tie-breaking. Every result includes the numeric score and a stable textual
  breakdown.
- A worker's current location and successive selected stops are used for
  directed route lookup. Missing routes are explicit `IsFallback` results;
  configured maximum travel is a hard filter except for an explicitly
  requested manual override.
- Suggestions are read-only. Claim/assign remains the authoritative
  concurrency boundary, so two workers cannot safely receive the same work;
  idempotent work commands and revision concurrency remain unchanged.
- Manual override is explicit in the suggestion request and still passes
  eligibility. The existing supervisor assignment path remains available and
  auditable for a deliberate override.

The authenticated `/api/workforce` surface now includes route and policy
list/create/update endpoints and suggestion parameters for current location,
route/policy selection, and manual override. Route and policy mutations use
`work.manage`, antiforgery validation, warehouse authorization, and immutable
audit records.

## Qualification status

Focused workforce tests cover deterministic route-cost ordering and score
explanations, no-route priority fallback, explicit manual override, claim
exclusion after assignment, existing queue/skill/shift eligibility, command
replay, capacity/revision protection, and activity/metrics reporting. The
`AddWorkInterleaving` migration is generated but not applied here.
The PostgreSQL harness also passed 46/46 on 2026-09-22; its new regression
proves duplicate route-edge identities and duplicate warehouse-scoped policy
codes are rejected while the same values remain valid in another warehouse.

This is committed progress rather than final closure. Provider-backed queue
contention and representative live-queue performance, telemetry-calibrated
travel costs, full supervisor visualization, browser/handheld EN/AR RTL/LTR
qualification, production migration/deployment, push, and remote issue
synchronization remain open gates.
