# CI test and coverage evidence

The CI workflow collects one TRX result and one Cobertura report for each of
the six test projects. Files are kept under
`artifacts/test-results/<platform>/<run-id>/<project>/`; the validated,
de-duplicated Cobertura report and `test-summary.md` are in the run directory.

## Run locally

```powershell
$runId = "local-$([guid]::NewGuid().ToString('N'))"
dotnet tool restore
dotnet restore 'Warehouse Management System.sln' --nologo
pwsh -NoProfile -File './scripts/run-test-suite.ps1' -Platform windows -RunId $runId
pwsh -NoProfile -File './scripts/verify-test-artifacts.ps1' -Platform windows -ResultsRoot "artifacts/test-results/windows/$runId"
```

For the CI Windows sequence, build the solution first and pass `-NoBuild` to
the test runner. Use `-Platform linux` in a Linux environment. Use a fresh
`RunId` for each local run. The runner continues through all six projects and
returns the first test-command failure code after the run, so reports from
later projects are still available.

## Evidence rules

- TRX result rows provide separate passed, failed/aborted, and skipped totals.
  Skips whose recorded reason names `WARECOMMAND_TEST_POSTGRES_CONNECTION` are
  called out as provider-gated; skipped tests do not count as executed coverage.
- `coverlet.collector` is private to test projects. Cobertura includes Wms
  production assemblies while excluding test assemblies, generated files,
  compiler-generated code, and code marked generated or excluded from coverage.
- ReportGenerator merges the six per-project reports once per platform, so
  repeated source lines are counted once. Identical Cobertura attachments
  emitted in multiple SDK result locations are normalized to one canonical
  project report; conflicting copies fail evidence validation. The measured
  line count and rate are reported without a pass threshold.
- After the test step runs, evidence validation requires one parseable TRX and
  one parseable Cobertura report per project, plus a non-empty merged report.
  Missing or invalid evidence fails validation. CI uploads the available files
  even when tests or validation fail; test failure remains a failed test step.

The workflow summary records the actual test, evidence-validation, and upload
step outcomes and includes the validated file-derived counts when available.
