# WareCommand roadmap

The authoritative work order is Master Plan issue #108. This roadmap is a
human-readable summary; the compact local checkpoint is
[`docs/implementation/EXECUTION_STATUS.md`](docs/implementation/EXECUTION_STATUS.md).
Read it with the source-backed
[`UI/backend coverage map`](docs/ui/UI_BACKEND_COVERAGE.md) and the
[`connector and transport inventory`](docs/modernization/CONNECTORS.md).

## Implemented locally

- .NET 10 and C# 14 solution baseline with centralized package and quality
  gates.
- Modular-monolith project boundaries and shared composition extensions.
- PostgreSQL production adapter, checked-in EF migration, and explicit schema
  lifecycle.
- Safe SQLite-to-PostgreSQL dry-run/apply importer with source backup,
  transaction rollback, reconciliation, and disposable integration evidence.
- Shared deterministic `None`, `Reference`, and `Demo` seed profiles.
- Clean checkout defaults: runtime databases and local artifacts are not
  tracked.
- First-party Identity authentication, admin-created warehouse accounts,
  lockout/password reset, auditable authentication events, protected MVC
  routes, and explicit WinForms user sessions.
- Domain, service, and authorized HTTP slices cover inventory/ledger,
  receiving, work, allocation and picking, packing/shipping, returns, transfers,
  cycle counts, replenishment, workforce/slotting, reports, forecasting,
  anomaly detection, and governed recommendations. These are implementation
  statements, not a claim that all screens, provider transports, or release
  gates are complete.
- Stabilization issues #115–#133 are closed as bounded CI, runtime-wiring,
  data-fixture, PostgreSQL journey, browser, load, and recovery slices. Their
  exact revisions, provider/platform evidence, known exclusions, and remaining
  gates are tracked in `docs/implementation/EXECUTION_STATUS.md` and
  `docs/implementation/REMAINING_GATES.md`.

## Partial or qualification-dependent

- The retained Razor host has functional screens for a defined subset of the
  workflows. Many implemented modules are API-only; the coverage map names each
  view, route, permission, contract, test, and missing screen.
- PostgreSQL journey, browser, and recovery tests run against disposable
  resources. They verify the listed scenarios only; unsupported journey scope,
  physical handhelds, and production-like restoration remain explicit.
- Load qualification reports measured budget failures and currently classifies
  50-way contention as admitted and 100-way contention as unsupported. Do not
  describe the recorded workload thresholds as met.
- Reporting assistant, forecasting, anomaly detection, and recommendation
  services have runtime API boundaries. They have no finished operator UI;
  recommendation providers remain deterministic, disabled by default, and
  shadow-only.

## Planned or deferred

- New visual screens and a redesigned operator experience await the approved
  design phase. API availability is not screen completion.
- Forecast calibration and measured recommendation business lift need a
  separately agreed evaluation dataset and acceptance thresholds.
- A named ERP, marketplace, carrier, or EDI provider is needed before adding
  protocol, credential-resolution, and partner-specific code. The current
  transport inventory separates implemented local HTTP/SMTP behavior from
  contract-only adapters and external acceptance.
- The measured load-budget failures, unsupported 100-way contention, physical
  handheld/device acceptance, independent security/capacity review, real-data
  migration rehearsal, production backup/restore, and deployment/change-window
  approval remain release gates under #108.
- The untracked `Front-End/` package is preserved user work; it is not assumed
  to be wired to the current host or included in the backend evidence.
- The legacy `Warehouse Management System` WinForms source-folder name is
  retained for the current solution path; normalizing that path is deferred to
  a dedicated rename because it would rewrite the solution, smoke scripts, and
  designer/resource paths together.

The remaining issues are executed in the order defined by Master Plan #108;
issue completion is recorded only after implementation and proportional local
evidence are committed.
