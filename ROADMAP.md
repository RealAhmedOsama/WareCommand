# WareCommand roadmap

The authoritative work order is Master Plan issue #108. This roadmap is a
human-readable summary; the compact local checkpoint is
[`docs/implementation/EXECUTION_STATUS.md`](docs/implementation/EXECUTION_STATUS.md).

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

## Partial or qualification-dependent

- Catalog, warehouse topology, inventory, receiving, putaway, picking,
  adjustments, and movement reporting have local domain/application/UI paths.
- MVC and WinForms smoke paths exist, but browser acceptance and complete
  cross-host UI qualification remain open.
- PostgreSQL integration is disposable local evidence, not production-provider
  readiness.
- Documentation and quality gates are present, but release operations and
  external-provider evidence are not complete.

## Planned or deferred

- Access control, identity, tenant isolation, and authorization.
- Execution/task workflows, counting, replenishment, shipping, and external
  integrations.
- Concurrency, performance/load, backup/restore rehearsal, security scanning,
  abuse controls, and production observability.
- Frontend integration and browser-visible acceptance for the supplied
  `Front-End/` package.
- The legacy `Warehouse Management System` WinForms source-folder name is
  retained for the current solution path; normalizing that path is deferred to
  a dedicated rename because it would rewrite the solution, smoke scripts, and
  designer/resource paths together.

The remaining issues are executed in the order defined by Master Plan #108;
issue completion is recorded only after implementation and proportional local
evidence are committed.
