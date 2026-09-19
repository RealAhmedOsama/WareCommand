# WareCommand baseline status

Date: 2026-09-19

This is the evidence baseline for issue #2. It describes the repository as it
was verified locally before modernization. It is not a production-readiness
claim.

## Environment and checkout

- Repository: `RealAhmedOsama/Warehouse-Management-System`
- Branch: `master`
- Remote: `https://github.com/RealAhmedOsama/Warehouse-Management-System.git`
- SDK: .NET SDK `10.0.401` / runtime `10.0.12`
- Solution projects: 5 production projects and 3 test projects
- Framework state: projects target `net8.0`/`net8.0-windows`; `Directory.Build.props`
  sets C# `12.0`, SQLite is the configured provider, and warnings are not errors.

The initial and final `git status --short --branch` showed the same pre-existing
untracked directory: `?? Front-End/`. It contains the supplied
`WareCommand-Landing-Page` package and was preserved. Therefore the checkout
did not satisfy the clean-working-tree part of issue #2 without destroying a
user change.

## Commands and results

All commands ran from the repository root against the current checkout:

```text
dotnet restore "Warehouse Management System.sln"                 PASS (exit 0)
dotnet build "Warehouse Management System.sln" -c Debug --no-restore   PASS (0 errors, 25 warnings)
dotnet build "Warehouse Management System.sln" -c Release --no-restore PASS (0 errors, 25 warnings)
dotnet test "Warehouse Management System.sln" -c Release --no-build --no-restore --logger "console;verbosity=normal" PASS
```

The Release test run discovered and passed 147 tests:

| Project | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| `Wms.Domain.Tests` | 89 | 0 | 0 |
| `Wms.Application.Tests` | 28 | 0 | 0 |
| `Wms.Infrastructure.Tests` | 30 | 0 | 0 |
| **Total** | **147** | **0** | **0** |

## Startup smoke tests

### ASP.NET Core MVC

The application was started with a disposable SQLite database supplied through
`ConnectionStrings__DefaultConnection`, using the HTTP launch profile on port
5232. These actual MVC requests returned HTTP 200:

```text
GET /          200
GET /Dashboard 200
GET /Items     200
GET /Inventory 200
```

The startup log also showed the current behavior: `EnsureCreatedAsync` creates
the schema and seeds sample data. HTTPS redirection logged that no HTTPS port
could be determined for the HTTP-only smoke run.

### WinForms

`Warehouse Management System/bin/Release/net8.0-windows/Wms.WinForms.exe`
was launched against its disposable local output database. After seven seconds
the process was live with a non-zero main-window handle and title
`Warehouse Management System`; the exact test process was then stopped.

## Known blockers and follow-up

These are baseline findings, not completed modernization work:

1. The solution is still .NET 8/C# 12 with SQLite and `EnsureCreated`; issues
   #3, #6, and #7 must establish the .NET 10/PostgreSQL/migration lifecycle.
2. The Release build has 25 warnings, including nullable initialization,
   unawaited WinForms calls, a duplicate using, a possible null dereference,
   and xUnit nullable test-data warnings. Issue #4 must make quality gates
   explicit and issue #3 must resolve the code-level warnings.
3. The test suite is primarily unit tests and EF Core InMemory/SQLite tests;
   it is not PostgreSQL, concurrency, browser, security, load, backup, or
   recovery qualification. Issues #94–#103 cover those missing gates.
4. The repository contains historical documents with unsupported completion
   and production-readiness language. Those claims were qualified in this
   baseline update; this file is the authoritative status until new evidence
   replaces it.
5. The supplied `Front-End/` directory is pre-existing and remains untracked;
   the exact visual integration gate belongs to issue #71 and its frontend
   prerequisites.

## Reproduction

Run `scripts/verify-baseline.ps1` from the repository root. The script repeats
restore, Debug/Release builds, Release tests, disposable MVC requests, and the
WinForms process smoke test. It was rerun successfully with
`pwsh -NoProfile -File .\scripts\verify-baseline.ps1 -WebPort 5236` on this
baseline. It writes transient logs/databases under the system temporary
directory and never uses the repository databases.
