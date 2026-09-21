# Invariant and provider testing

Issue #94 owns fast business-rule qualification. Domain and application tests
are kept separate from EF/provider tests so the normal feedback loop can run
without a database, network, wall-clock dependency, or external service.

## Test boundaries

| Category | Projects / gate | Purpose |
| --- | --- | --- |
| Unit | `Wms.Domain.Tests`, `Wms.Application.Tests` | Entities, value objects, typed results, permissions, conversions, identifiers, state transitions, and use-case decisions |
| Relational integration | `Wms.Infrastructure.Tests` | SQLite transaction/query behavior and service persistence where provider behavior is part of the contract |
| PostgreSQL provider | `scripts/verify-postgresql.ps1` and the isolated-schema fixture | Production-engine mappings, migrations, constraints, precision, UTC, rollback, and contention |
| MVC/API/UI | `Wms.ASP.Tests` plus the browser gates recorded by the owning issue | Authorization, antiforgery, localization, response contracts, and rendered workflows |
| Migration/data contract | `Wms.DataMigration.Tests` and `scripts/verify-data-migration.ps1` | Upgrade/import compatibility and reconciliation |

The unit gate is repeatable with shuffled project order:

```powershell
pwsh -NoProfile -File scripts/verify-unit-tests.ps1 -Repetitions 2
```

The script treats the Domain and Application projects as the unit category,
runs each project without restore/build, and leaves failure output attached to
the exact repetition and project. xUnit handles test-level parallelization
within each project; tests that need a relational provider stay outside this
gate.

## Invariant coverage currently established

- quantity non-negativity, arithmetic, comparison, conversion, and rounding
  boundaries;
- barcode/GTIN/SSCC check digits and GS1 variable-field parsing;
- warehouse/location hierarchy and capacity rules;
- lot, serial, license-plate, inventory-status, receipt, quality, ASN, PO,
  receiving-session, and stock-ledger lifecycle behavior;
- typed `Result`/error categories and permission decisions;
- work assignment ownership, legal transitions, terminal-state guards,
  supervisor-reason requirements, planned/actual quantity bounds, creation-key
  idempotency, command replay, and optimistic concurrency.

Tests use fixed clocks where the time boundary is part of the contract and
builders retain the values that matter to the assertion. A test must not turn
an authorization, persistence, or handler dependency into unconditional
success merely to make a scenario green.

## Remaining qualification

This is an incremental foundation, not a blanket coverage claim. The later
functional issues still need tests for their allocation/putaway/replenishment/
count/pack/ship/return invariants, approval/exception paths, large-volume
properties, culture/time-zone matrices, and intentional corruption/repair
scenarios. Coverage collection in CI is diagnostic; it is not treated as
proof that every line or workflow is complete.
