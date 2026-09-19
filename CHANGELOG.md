# Changelog

All notable WareCommand changes are recorded here. This file describes
committed product work; it does not imply a production deployment.

## [Unreleased]

### Added

- .NET 10/C# 14 modernization, PostgreSQL persistence, checked-in migrations,
  and a controlled SQLite-to-PostgreSQL migration lifecycle.
- Deterministic `None`, `Reference`, and `Demo` seed profiles with clean
  runtime-artifact defaults.
- Cross-platform CI quality gates, disposable PostgreSQL qualification,
  dependency checks, Docker image validation, and secret/dependency review
  jobs.
- Non-root Web plus PostgreSQL Compose baseline with real health/readiness
  checks, secret-file connection injection, persistent Data Protection keys,
  and explicit migration instructions.

### Changed

- The Web host now refuses pending PostgreSQL migrations instead of applying
  schema changes implicitly at startup.
- Repository and release evidence is recorded in the implementation tracker
  and must be paired with a local Git commit.

### Release boundary

No stable or production release has been tagged. The next release candidate
must pass [`docs/release/RELEASE_CHECKLIST.md`](docs/release/RELEASE_CHECKLIST.md)
and receive an explicit human-approved tag.
