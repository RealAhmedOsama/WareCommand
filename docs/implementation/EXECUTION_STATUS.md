# WareCommand execution status

This tracker is the compact local checkpoint for Master Plan #108. Remote issue
states and checkboxes are not changed by local work. Completed rows mean the
implementation and the applicable local evidence are committed; they do not
claim deployment or external-provider qualification.

| Issue | State | Acceptance evidence | Commit | Dependencies / blocker | Exact next action |
| ---: | --- | --- | --- | --- | --- |
| #2 | verified locally; pending commit | Restore passed; Debug and Release builds passed with 25 baseline warnings; 147/147 Release tests passed; MVC `/`, `/Dashboard`, `/Items`, `/Inventory` returned 200 against disposable SQLite; WinForms exposed a live main window; reproduction script passed | pending | Pre-existing untracked `Front-End/` prevents a clean working tree without destroying the supplied package | Commit the baseline artifacts, then upgrade the solution to .NET 10/C# 14 for #3 |
| #3–#107 | pending | Not yet evaluated as complete | — | Ordered by the master plan and prerequisite constraints | Continue with #3 after the #2 commit |

## Phase checkpoint

- Phase 0: #2 evidence captured; #3–#11 pending.
- Phase 1–9: pending implementation and qualification.
- Nothing has been pushed, deployed, or migrated against production data.

## Evidence paths

- Baseline narrative: `docs/modernization/BASELINE_STATUS.md`
- Reproduction command: `scripts/verify-baseline.ps1`
- Approved frontend package: pre-existing untracked `Front-End/WareCommand-Landing-Page/`

