# ADR 0001: Keep a capability-oriented modular monolith

- Status: Accepted
- Date: 2026-09-19

## Decision

Keep Domain, Application, Infrastructure, Web, and optional WinForms as the
assembly boundaries. Organize application workflows by business capability
inside the existing Application assembly. Do not create a microservice or a
new assembly for each capability.

## Why

The current product is a single warehouse workflow with shared inventory
invariants and a small testable codebase. A modular monolith gives the
capabilities explicit ownership without introducing distributed transactions,
deployment coordination, or duplicated contracts.

## Consequences

- New work starts in the owning capability folder and exposes application
  contracts rather than persistence types.
- Inventory remains a shared domain boundary for receiving, putaway, picking,
  and adjustments.
- An assembly split requires a separate decision backed by an actual ownership
  or deployment need.
