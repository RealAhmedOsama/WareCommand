# Contributing to WareCommand

WareCommand uses a lightweight, evidence-first workflow. The repository is a
.NET 10 modular monolith; a feature issue is not complete until its runtime
boundary, tests, documentation, and operational consequences are understood.

## Work order and branches

- The protected integration branch is `master`. Work should enter it through a
  pull request when branch protection is enabled.
- Use short-lived branches named `feature/<issue>-<slug>`,
  `fix/<issue>-<slug>`, `docs/<issue>-<slug>`, `chore/<issue>-<slug>`, or
  `release/v<version>`.
- Do not introduce GitFlow branches or long-lived parallel release lines
  without a separately approved support need.
- Keep one coherent issue scope per change. Record a dependency or blocker in
  the issue and in `docs/implementation/EXECUTION_STATUS.md` instead of
  silently expanding scope.
- Preserve unrelated working-tree changes. The supplied untracked
  `Front-End/` package is user work and must not be staged by backend tasks.

## Conventional commits

Use a Conventional Commits subject:

```text
<type>(optional-scope): imperative summary
```

Use `feat`, `fix`, `docs`, `refactor`, `test`, `build`, `ci`, `perf`,
`chore`, or `revert`. Keep the subject concise and explain non-obvious
reasoning in the body. Reference the issue with `(refs #123)` when the commit
implements it. Never add generated, agent, or tool attribution to a commit.

Examples:

```text
feat: add receiving quantity validation (refs #42)
docs: record execution status for issue 42 (refs #42)
```

Every completed issue receives a local Git commit. Nothing in this workflow
auto-pushes, auto-deploys, or creates a production release.

## Versioning and release rules

WareCommand uses Semantic Versioning 2.0.0 for supported product releases:

- `MAJOR` changes break a supported API, persisted data contract, or user
  workflow and require an explicit migration/upgrade note.
- `MINOR` adds backward-compatible product capability.
- `PATCH` fixes a backward-compatible defect or security issue.

The immutable annotated tag `vMAJOR.MINOR.PATCH` is the release identity. The
container image must carry the same product version plus the source revision in
its OCI labels. CI-only builds use a non-release version such as
`0.0.0-ci.<run-number>` and an immutable commit-SHA image tag.

Pre-releases use `vMAJOR.MINOR.PATCH-alpha.N`, `-beta.N`, or `-rc.N`. A
pre-release never replaces or moves a stable tag. Pre-releases are not covered
by the stable support promise.

Database migrations are forward-only release artifacts. A breaking schema
change uses an expand/contract sequence where possible: deploy additive schema
support, migrate/backfill with a backup and reconciliation evidence, switch
application reads/writes, then remove obsolete schema only in a later release.
The application must continue to refuse pending migrations; it must not apply
them blindly at startup. API or message-contract breaks require a major
version or an explicitly versioned compatibility boundary.

The supported line is the latest stable minor release. The immediately prior
minor line receives only critical security and data-recovery fixes until the
next stable minor is established; end-of-support dates are recorded in the
release notes. A major release resets the support window. This policy becomes
operational only after the first stable release; the current repository remains
pre-release.

## Changelog process

[`CHANGELOG.md`](CHANGELOG.md) follows a Keep a Changelog-style structure.
Add user-visible changes to `Unreleased` under `Added`, `Changed`, `Fixed`,
`Security`, or `Removed`, with issue references where useful. Move the section
to `MAJOR.MINOR.PATCH - YYYY-MM-DD` only when the release tag is approved.
Do not call local verification a release, and do not list an unverified
production deployment as shipped.

## Pull requests and issue closure

Use the repository pull-request template and select the smallest reviewable
scope. A completed issue needs evidence for the applicable items below:

- implementation commit(s) and the exact affected boundary;
- focused tests plus build/format/static checks proportional to the change;
- migration, backup, compatibility, and rollback notes when data or contracts
  change;
- documentation and configuration examples without real secrets;
- the exact verification commands and results, including intentional skips;
- an explicit statement of what was not deployed, pushed, or provider-tested.

Post that evidence on the issue before closing it. If work is partial, keep the
issue open and record the current state, dependency, blocker, and exact next
action. Update `docs/implementation/EXECUTION_STATUS.md` in the same local
checkpoint sequence.

## Local release/tag dry run

This validates a release plan without creating or publishing a tag:

```powershell
$version = '0.1.0-rc.1'
if ($version -notmatch '^\d+\.\d+\.\d+-(alpha|beta|rc)\.\d+$') {
    throw "Invalid pre-release version: $version"
}
git diff --check
dotnet restore '.\Warehouse Management System.sln'
dotnet build '.\Warehouse Management System.sln' -c Release --no-restore
dotnet test '.\Warehouse Management System.sln' -c Release --no-build --no-restore
dotnet format '.\Warehouse Management System.sln' --verify-no-changes --no-restore --severity error
pwsh -NoProfile -File '.\scripts\verify-migrations.ps1'
git tag --list "v$version"
```

The final command must return no existing tag. After human approval and the
release checklist is complete, the explicit release action is an annotated tag
such as `git tag --annotate v0.1.0-rc.1 --message "WareCommand v0.1.0-rc.1"`.
Pushing that tag is a separate, human-authorized action.
