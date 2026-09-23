# Secret scan workflow

The `security` job in `.github/workflows/quality.yml` checks out complete Git history and runs `scripts/verify-secret-scan.sh`. The scanner image is pinned to Gitleaks v8.30.0 by its GHCR manifest digest.

## Scan ranges

- A normal push scans `before..after`, including all newly reachable non-merge commits.
- A first push has no `before` commit, so it scans the complete history reachable from `after`.
- A pull request scans `merge-base(base, head)..head`. If that range is empty, it falls back to the full head history.
- A manual dispatch scans the complete history reachable from its selected ref.

The script rejects shallow checkouts, missing commit objects, unresolvable ranges, and empty scan ranges. It checks the commit count with Git before invoking Gitleaks, then requires Gitleaks to report the same scanned commit count. Gitleaks must also create a valid SARIF 2.1.0 report. A clean result requires all three checks, scanner exit code 0, and zero SARIF findings. Findings, scanner errors, and invalid or missing reports fail the job and appear as different statuses in its summary metadata.

Gitleaks runs with `--redact`; the SARIF report and scan metadata are uploaded on both success and failure when available. The scan job has read-only repository permission. It does not post pull request comments.

## Reviewed findings

The complete history scan found five `generic-api-key` matches in unit test fixtures. Review confirmed they are synthetic idempotency keys used by tests, not service credentials. `.gitleaksignore` suppresses only those exact commit, path, rule, and line fingerprints; it does not exclude a whole directory or rule.

## Safe range and finding qualification

Use a disposable repository outside this checkout. Never commit a real credential or put the synthetic fixture in the production repository.

1. Create a repository with a clean root commit and at least two subsequent commits. Run the scanner script as a `push` with the root SHA as `EVENT_BEFORE` and the latest commit as `EVENT_AFTER`; the metadata should show both new commits in the range.
2. Run it as a `pull_request`, with the root SHA as base and the latest commit as head; the metadata should show the merge-base-to-head range.
3. Run it as `workflow_dispatch` at the latest commit; the metadata should show full history.
4. Simulate a first push with `EVENT_BEFORE` set to 40 zeroes and `EVENT_AFTER` set to the latest commit; it should scan full history.
5. Make a shallow clone of that repository and run the script; it must fail before scanning with a shallow-checkout status.
6. In another disposable repository, commit a synthetic, non-secret API-key-shaped value assembled from fragments, run a full-history scan, and confirm the result is `findings`, scanner exit code 2, and a redacted SARIF artifact. The expected nonzero result is the test passing.

For local runs, set `GITHUB_WORKSPACE` to the disposable repository, set `GITHUB_EVENT_NAME`, `GITHUB_SHA`, `GITHUB_RUN_ID`, and `RUNNER_TEMP`, then set the event-specific `EVENT_BEFORE` / `EVENT_AFTER` or `PR_BASE_SHA` / `PR_HEAD_SHA` values. From the WareCommand checkout, run:

```bash
bash scripts/verify-secret-scan.sh
```

The script expects Bash (Git Bash on Windows), Git, Python 3, and Docker. It writes reports only below `RUNNER_TEMP`.
