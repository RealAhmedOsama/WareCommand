# WareCommand execution status

This tracker is the compact local checkpoint for Master Plan #108. Remote issue
states and checkboxes are not changed by local work. Completed rows mean the
implementation and the applicable local evidence are committed; they do not
claim deployment or external-provider qualification.

| Issue | State | Acceptance evidence | Commit | Dependencies / blocker | Exact next action |
| ---: | --- | --- | --- | --- | --- |
| #2 | verified locally; committed | Restore passed; Debug and Release builds passed with 25 baseline warnings; 147/147 Release tests passed; MVC `/`, `/Dashboard`, `/Items`, `/Inventory` returned 200 against disposable SQLite; WinForms exposed a live main window; reproduction script passed | `f80c431` | Pre-existing untracked `Front-End/` prevents a clean working tree without destroying the supplied package | Upgrade the solution to .NET 10/C# 14 for #3 |
| #3 | verified locally; committed | .NET 10.0.401 SDK selected; all projects target .NET 10; stable package upgrades restored; Release build and 147/147 tests passed; MVC/WinForms baseline script passed | `342f1da` | Depends on #2; no production deployment or provider qualification claimed | Centralize package versions and establish strict quality gates for #4 |
| #4 | verified locally; committed | Central package management enabled; repository analyzer/editorconfig gates added; Release build passed with 0 warnings/0 errors; 147/147 Release tests passed; `dotnet format --verify-no-changes --severity error` passed; package graph reported no warnings/downgrades/conflicts; baseline script passed | `00d6700` | Depends on #3; performance analyzers CA1848/CA1873/CA1860 remain enabled as suggestions for targeted follow-up | Load and implement the acceptance requirements for #5 |
| #5 | verified locally; committed | Capability ownership documented in `ARCHITECTURE.md`; application/infrastructure registration extensions and Web/Desktop seed profiles centralize composition; Web/WinForms no longer construct persistence or demo domain data; 5 architecture tests plus 147 existing tests passed (152/152); format gate, package graph, and MVC/WinForms smoke script passed | `188429e` | Depends on #4; reserved capabilities remain explicit extension points and no production/provider readiness is claimed | Load and implement the acceptance requirements for #6 |
| #6–#107 | pending | Not yet evaluated as complete | — | Ordered by the master plan and prerequisite constraints | Implement #6 next; preserve the phase order and authoritative issue scope |

## Phase checkpoint

- Phase 0: #2, #3, #4, and #5 committed; #6–#11 pending.
- Phase 1–9: pending implementation and qualification.
- Nothing has been pushed, deployed, or migrated against production data.

## Evidence paths

- Baseline narrative: `docs/modernization/BASELINE_STATUS.md`
- Reproduction command: `scripts/verify-baseline.ps1`
- Approved frontend package: pre-existing untracked `Front-End/WareCommand-Landing-Page/`
