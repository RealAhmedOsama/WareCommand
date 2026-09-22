# Remaining Master-Plan Gates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert the remaining local qualification and integration gaps behind WMS issues #2–#107 into focused GitHub issues, implement every locally reproducible gap with fresh evidence, and reconcile the master-plan issue without claiming production or external-provider approval that was not performed.

**Architecture:** Keep all qualification logic in repository-owned scripts, contracts, and tests. Use disposable PostgreSQL and local/fake-provider seams for repeatable evidence; keep production-only deployment, live-provider, external certification, and independent-review gates explicit and separate. Each workstream gets one focused issue, one or more tests, one local commit, and a GitHub evidence comment.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core 10, PostgreSQL 17 disposable containers, xUnit, PowerShell, existing WMS qualification contracts, and Playwright where a runnable local Web host is available.

**Spec:** `docs/implementation/EXECUTION_STATUS.md`, `docs/operations/PERFORMANCE_QUALIFICATION.md`, `docs/operations/RESILIENCE_QUALIFICATION.md`, `docs/operations/RELEASE_QUALIFICATION.md`, `docs/security/THREAT_MODEL.md`, and GitHub master plan issue #108.

## Global Constraints

- Preserve the pre-existing untracked `Front-End/` package.
- Do not mutate production data, deploy, create real provider credentials, or claim live-provider/external-review approval from local evidence.
- Use disposable PostgreSQL schemas/containers and cleanup-safe ownership markers.
- Keep security boundaries fail-closed, credentials redacted, and fault-injection hooks impossible to enable in production.
- Write a failing test before production behavior changes and run the full relevant solution test command before closure claims.
- Use `refs #N` in commit/tracker text; do not add auto-closing keywords to comments.

## Review Focus

- Provider contention and duplicate/idempotency behavior must remain isolated by warehouse/schema and prove cleanup; owned by the provider-matrix workstream.
- EN/AR LTR/RTL, responsive breakpoints, keyboard/focus, and redacted browser artifacts must be exercised together; owned by browser qualification.
- Performance results must include revision, dataset, hardware, PostgreSQL, query-plan, pool, worker, and reconciliation metadata; owned by performance qualification.
- Recovery scenarios must reject partial mutation, duplicate business outcomes, unreconciled state, and production-enabled fault injection; owned by resilience qualification.
- Release evidence must not contain secrets and must record backup, migration, compatibility, health, worker-drain, and reconciliation decisions; owned by release/security evidence.

---

### Task 1: Residual-gate issue inventory and tracker truth

**Files:**
- Create: `docs/implementation/REMAINING_GATES.md`
- Modify: `docs/implementation/EXECUTION_STATUS.md`
- Test/verify: `scripts/verify-release-preflight.ps1` and a deterministic tracker-audit command

**Interfaces:**
- Consumes: remaining-gate text from issues #2–#107 and qualification documents.
- Produces: one canonical mapping from new issue to affected child issues, local evidence, and non-local blockers.

- [ ] Build the grouped residual-gate inventory and verify it covers every explicit remaining-gate category.
- [ ] Record the exact local command and expected artifact for each group.
- [ ] Update stale push/remote-state language without deleting historical evidence.
- [ ] Run the tracker/preflight checks and commit the documentation.

### Task 2: Bounded PostgreSQL/provider qualification

**Files:**
- Modify: `scripts/verify-postgresql.ps1`, provider test fixtures, and provider-focused tests as required by the inventory.
- Create/modify: provider matrix runner and evidence documentation.

**Interfaces:**
- Consumes: `PostgreSqlTestDatabase`, checked-in migrations, provider-specific tests, and issue-specific identity tests.
- Produces: bounded group execution with per-group counts, container/port cleanup, schema isolation, and a redacted evidence packet.

- [ ] Add a failing test for bounded group selection and cleanup reporting.
- [ ] Implement the smallest runner change that avoids the all-in-one native RX-memory failure.
- [ ] Run the red/green focused test cycle and disposable PostgreSQL groups.
- [ ] Run the full relevant provider test command, record skips/failures, and commit.

### Task 3: Local browser/client qualification

**Files:**
- Modify: `Wms.ASP.Tests`, browser qualification contracts, and local-run scripts/docs.
- Create: runnable Playwright project/configuration only if the existing host can be launched safely.

**Interfaces:**
- Consumes: MVC/PWA/accessibility/localization contracts and the existing Web test factory.
- Produces: deterministic EN/AR LTR/RTL responsive smoke evidence with redacted trace/screenshot metadata.

- [ ] Add failing coverage for authentication, scanner shell, PWA state, focus, direction, and responsive assertions.
- [ ] Implement local host/browser wiring without provider or production writes.
- [ ] Run focused browser tests, then the full ASP test project.
- [ ] Commit evidence and document any unavailable real-device/external gate.

### Task 4: Performance and capacity qualification contracts

**Files:**
- Modify: `Wms.Application/Performance`, `Wms.Application.Tests/Performance`, and `docs/operations/PERFORMANCE_QUALIFICATION.md`.
- Create: a repeatable local benchmark runner and redacted result format.

**Interfaces:**
- Consumes: performance budgets, deterministic data-generation profiles, provider fixture, and reconciliation contracts.
- Produces: repeatable local measurements for representative reads/mutations, with dataset/revision/environment metadata and no production-capacity claim.

- [ ] Add failing tests for metadata completeness, reconciliation failure, and bounded result serialization.
- [ ] Implement the runner with cancellation, bounded concurrency, and no secret/payload capture.
- [ ] Run repeated disposable PostgreSQL/local workload samples and record variance.
- [ ] Commit results and identify remaining production-capacity gates.

### Task 5: Non-production resilience and recovery qualification

**Files:**
- Modify: `Wms.Application/Resilience`, `Wms.Application.Tests/Resilience`, infrastructure test hooks, and `docs/operations/RESILIENCE_QUALIFICATION.md`.
- Create: test-only fault-injection seams and recovery evidence helpers.

**Interfaces:**
- Consumes: journey specifications, idempotency, outbox/inbox, job, backup/restore, and provider boundaries.
- Produces: deterministic non-production scenarios for timeout/conflict/restart/response-loss/dead-letter/reconciliation.

- [ ] Add failing tests proving hooks cannot be enabled in production/staging configuration.
- [ ] Add one real local hook at a time and verify duplicate/partial/reconciliation invariants.
- [ ] Run recovery scenarios against disposable PostgreSQL and cleanup all artifacts.
- [ ] Commit evidence and leave unimplemented real-environment gates explicit.

### Task 6: Release, security, and support evidence packet

**Files:**
- Modify: `scripts/verify-release-preflight.ps1`, `scripts/verify-security-boundaries.ps1`, `docs/operations/RELEASE_QUALIFICATION.md`, `docs/operations/SUPPORT_DIAGNOSTICS.md`, and `docs/security/THREAT_MODEL.md`.
- Create: local evidence-packet validation and redaction tests.

**Interfaces:**
- Consumes: exact revision, migration list, provider/browser/performance/resilience results, backup/restore metadata, and support-bundle policy.
- Produces: a secret-free, reproducible local GO/GO_WITH_RESTRICTIONS/NO_GO packet; never a live GO without authorized environment evidence.

- [ ] Add failing tests for secret/key/cookie/payload redaction and incomplete-gate rejection.
- [ ] Implement packet validation and cross-reference checks.
- [ ] Run all local preflight/security/migration/backup checks.
- [ ] Commit the packet and document external approval blockers.

### Task 7: Remaining integration/module wiring

**Files:**
- Modify the owning application/infrastructure/API/UI tests and contracts identified by the inventory for attachments, notifications, retention, administration, API commands, integrations, bulk exchange, connectors, and B2B documents.
- Modify the corresponding modernization docs and migration/handler tests.

**Interfaces:**
- Consumes: existing provider-neutral contracts, RBAC/audit/idempotency, job/outbox/inbox boundaries, and browser/localization surfaces.
- Produces: locally executable command/resource/module wiring with focused tests; real vendor transports/certification remain separately named gates.

- [ ] For each selected missing local boundary, add a failing contract/flow test.
- [ ] Implement only the smallest module wiring needed for that contract.
- [ ] Run focused and full relevant project tests after each independently reviewable slice.
- [ ] Commit each completed slice with a separate evidence comment.

### Task 8: Issue and master-plan reconciliation

**Files:**
- Modify: GitHub issues for the new workstreams and issue #108; update `docs/implementation/EXECUTION_STATUS.md`.

**Interfaces:**
- Consumes: fresh commit/test/evidence results and explicit external blockers.
- Produces: one comment per new issue, correct labels/state, and a master-plan status that distinguishes local completion from production readiness.

- [ ] Create only the focused new issues that correspond to Task 1’s inventory.
- [ ] Add evidence comments and close a new issue only when its local acceptance criteria pass.
- [ ] Keep externally dependent issues open or mark them blocked with exact owner/gate evidence.
- [ ] Close #108 only if every master-plan acceptance gate is actually satisfied; otherwise report the remaining blocker instead of falsely closing it.
