# ADR 0002: Centralize registration and database initialization

- Status: Accepted
- Date: 2026-09-19

## Decision

Application and infrastructure registrations live in layer-owned extension
methods. PostgreSQL startup verifies that checked-in EF migrations are already
applied; explicit migration commands perform schema changes. SQLite local/demo
mode uses `EnsureCreated`. A single `WmsSeedService` owns deterministic
`None`, `Reference`, and `Demo` profiles for both hosts. Web and WinForms only
select the provider, connection string, seed profile, and presentation
services.

## Why

The two hosts previously duplicated repository, service, use-case, EF Core, and
seed registrations. That made drift likely and put persistence/entity
construction in presentation composition roots.

## Consequences

- A new host can reuse the same registration and initialization boundary.
- Development defaults to `Demo`; other environments default to `None`.
  Reference and Demo are explicit opt-ins outside Development.
- Demo seed data remains local qualification behavior, not a production data
  migration or deployment claim.
- Infrastructure remains an outer adapter; it is not introduced into Domain or
  Application.
