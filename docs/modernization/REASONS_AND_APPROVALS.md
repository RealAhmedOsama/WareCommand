# Reason Codes and Approval Policies

The approval boundary standardizes the explanation and authorization required
for sensitive warehouse commands. It is intentionally operation-specific: a
policy must name a module and operation, so enabling approvals for one command
does not silently create a global approval gate for every warehouse action.

## Local contract

- `ReasonCode` stores a stable code, category, English and Arabic names and
  descriptions, severity, active dates, notes/attachment requirements, module
  and operation scope, and optional warehouse scope.
- `ApprovalPolicy` selects by module, operation, warehouse, reason code,
  quantity, value, variance percentage, item risk, and status risk. Matching is
  deterministic: priority descending, specificity descending, then code and
  identifier ascending. Minimum thresholds are inclusive.
- A selected policy snapshots its approval levels, expiry, separation-of-duty
  setting, protected permission, reason code, and current-state hash into an
  `ApprovalRequest`. Requests without a matching policy return an explicit
  `ApprovalRequired = false` result after the reason code is still validated.
- Each approval level accepts one of its configured roles. When separation of
  duties is enabled, the requester cannot approve or escalate their own
  request. Approvals advance one level at a time and only the final level moves
  the request to `Approved`.
- `ApprovalDecision` is append-only. The EF boundary rejects updates and
  deletes, and decision idempotency keys make duplicate approve/reject/cancel/
  escalate requests safe to replay.
- `ApprovalInboxItem` is recipient-specific and durable. It gives approvers an
  in-app queue with read and resolved state; channel delivery such as email or
  webhook remains behind the notification work in #83.
- `ApprovalExecution` is a unique lease per approval request. Starting the
  lease rechecks the stored protected permission, expiry, approval status, and
  exact current-state hash. A second execution key cannot start the same
  request, and completion is replay-safe.

## Protected-command boundary

An affected module should call `RequestAsync` before it mutates inventory or a
document. If a policy is selected, the module must persist its pending command
context and wait for the approval request. After approval, the module calls
`BeginExecutionAsync` with the current state hash, performs its own business
mutation in the same transaction boundary, and calls `CompleteExecutionAsync`.
The approval service never mutates inventory or documents itself; rejection,
expiry, stale-state failure, or a failed authorization therefore leave those
records untouched.

The request records the operation permission and execution re-authorizes it,
so approval is not a substitute for the caller's current RBAC or warehouse
scope. Idempotency and the unique execution row are the shared exactly-once
gate; the module remains responsible for its own command transaction and
provider concurrency checks.

## API and qualification

`/api/approvals` exposes reason-code and policy administration, request
evaluation, request history, approve/reject/cancel/escalate/expire actions,
execution start/complete, and the recipient inbox. All mutations use typed
permission policies and antiforgery protection. Audit actions cover
configuration, request, decision, expiry, and execution transitions.

The local qualification is complete for the shared boundary: threshold
selection, localized reason validation, required evidence, multi-level roles,
self-approval rejection, decision replay, rejection/expiry, stale-state
revalidation, exactly-once execution replay, authorization, audit, and inbox
notification are covered by `ApprovalServiceTests`. The disposable PostgreSQL
harness passed 47/47 on 2026-09-22; it additionally proves unique request
idempotency, per-request decision idempotency, and one execution lease per
approval request at the provider boundary.

## Remaining gates

This issue is a committed shared-core progress slice, not a production or
remote closure. The following remain explicitly dependent work:

- wire every sensitive command (adjustment, override, cancellation, hold,
  status change, discrepancy, short pick, damage, reprint, return, scrap,
  recount, VAS, and purge/retention holds) to the boundary;
- connect the attachment reference to the secure storage/retention contract
  in #82 and add module-specific evidence links;
- add localized notification channels and deep links in #83;
- complete administration/history/inbox screens and browser/handheld EN/AR
  RTL/LTR qualification;
- qualify PostgreSQL concurrency, provider-backed notification delivery,
  migration/restore, production deployment, push, and remote issue closure.
