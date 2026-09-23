# Deterministic non-production data generation

`Wms.Application.DataGeneration` provides versioned profiles and a deterministic
plan preview. The PostgreSQL integration fixture also writes a coherent WMS
journey into a fresh, isolated test schema and reports the data it actually
created. Planning and writing are separate operations: a plan never mutates a
database.

## Profiles and bounds

| Profile | Purpose | Planned warehouses | Planned locations | Planned items | Planned inventory rows | Maximum scale |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `minimal-development` | Local feature development | 1 | 8 | 25 | 25 | 10 |
| `full-demo` | Non-production demonstrations | 2 | 80 | 250 | 400 | 4 |
| `edge-cases` | Boundary and exception coverage | 1 | 12 | 40 | 80 | 8 |
| `integration-test` | Repeatable cross-module journeys | 2 | 40 | 100 | 200 | 5 |
| `large-performance` | Explicit load and capacity data shape | 10 | 5,000 | 10,000 | 100,000 | 1 |

Counts shown are the bounded plan dimensions; the writer's report contains
actual row counts for each persisted entity. A stable seed, generator version,
profile, and locale produce stable business keys and a logical dataset
fingerprint. English and Arabic names are supported. The writer uses a fixed
clock for repeatable document and inventory outcomes; Identity passwords are
random, ephemeral, and never included in the report.

Scale is restricted per profile. Only `Development`, `Testing`, `Test`, `CI`,
and `Local` environments and English or Arabic locales are accepted. Production,
staging, unknown environments, unsupported locales, and every reset request
are refused.

## PostgreSQL fixture

Run the disposable data-generation group from the repository root:

```powershell
pwsh -NoProfile -File scripts/verify-postgresql.ps1 -Group data-generation -Port 55445
```

The runner starts its own PostgreSQL 17 container. Each test fixture creates a
new random `wms_test_*` schema, applies all migrations, and drops only that
schema and the owned container during cleanup. The writer verifies the
Npgsql provider, exact schema search path, fixture-owned target, and empty
schema before inserting anything. It refuses populated targets and never
resets or reuses an existing database.

Reference dimensions (warehouses, locations, units, items, suppliers,
customers, and the seeded test actor) are inserted directly where no normal
operational command exists. The actor is provisioned through ASP.NET Identity,
assigned the warehouse-manager role, and scoped to the generated warehouses.
Purchase, receiving, putaway, sales, allocation, pick, pack, shipment, return,
cycle-count, and opening-balance changes use application services and use cases.
The writer fails if profile dimensions or inventory row targets exceed their
planned bounds.

The returned report contains generator/profile and seed fingerprints, target
schema identifier, elapsed time, actual entity counts, operation outcomes,
logical dataset fingerprint, and deep reconciliation status and transaction
count. The PostgreSQL runner includes both separate-schema reports in its
`dataGenerationReports` JSON field; the reports contain no credentials. The
test runs the same seed into two
separate schemas, compares logical fingerprints and counts, checks document
and inventory links, queries persisted data through application services, and
proves that a populated target, production environment, and reset request are
refused.

This group qualifies deterministic provider-backed fixture generation. It is
not a production seed path or a production/staging qualification result. Use
the separate browser and load qualification gates for those workflows; they
must consume a generated dataset before their evidence can be considered
complete.
