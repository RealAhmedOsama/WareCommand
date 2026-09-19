---
name: Release request
about: Prepare a versioned release with backup, migration, monitoring, and rollback evidence.
title: "[Release] v"
labels: "release"
assignees: ""
---

## Release identity

- Version/tag:
- Release type: [ ] alpha [ ] beta [ ] release candidate [ ] stable
- Source commit:
- Changelog section:

## Scope and compatibility

- Included issues/commits:
- Breaking API/schema/user-workflow changes:
- Supported upgrade path and minimum previous version:

## Required evidence

- [ ] Release checklist completed.
- [ ] CI and focused local verification passed.
- [ ] Backup and restore evidence recorded.
- [ ] Migration script reviewed and applied only in the approved window.
- [ ] Smoke and health/readiness checks passed.
- [ ] Monitoring/logging/alert owner identified.
- [ ] Rollback decision, revision, and data strategy recorded.
- [ ] Data Protection keys and deployment secrets remain available.

## Approval boundary

- Deployment environment/change window:
- Human approver:
- Explicit push/tag/deploy action still required:
- Known gaps or follow-up issues:
