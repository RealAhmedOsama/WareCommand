# ADR 0002: Centralize registration and database initialization

- Status: Accepted
- Date: 2026-09-19

## Decision

Application and infrastructure registrations live in layer-owned extension
methods. Database creation and the existing local demo seed data live in
`WmsDatabaseInitializer` with an explicit Web or Desktop profile. The Web and
WinForms programs only select the connection string, profile, and presentation
services.

## Why

The two hosts previously duplicated repository, service, use-case, EF Core, and
seed registrations. That made drift likely and put persistence/entity
construction in presentation composition roots.

## Consequences

- A new host can reuse the same registration and initialization boundary.
- Demo seed data remains local qualification behavior, not a production data
  migration or deployment claim.
- Infrastructure remains an outer adapter; it is not introduced into Domain or
  Application.
