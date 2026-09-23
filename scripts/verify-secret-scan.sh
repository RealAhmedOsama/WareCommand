#!/usr/bin/env bash
set -Eeuo pipefail

readonly GITLEAKS_IMAGE='ghcr.io/gitleaks/gitleaks:v8.30.0@sha256:691af3c7c5a48b16f187ce3446d5f194838f91238f27270ed36eef6359a574d9'
readonly ZERO_SHA='0000000000000000000000000000000000000000'

status='failed'
reason='preflight did not complete'
scan_range='unresolved'
head_sha="${GITHUB_SHA:-unknown}"
commit_count='0'
scanner_commit_count='not-reported'
scanner_exit='not-run'
report_path=''

workspace="${GITHUB_WORKSPACE:-$(git rev-parse --show-toplevel 2>/dev/null || pwd)}"
run_id="${GITHUB_RUN_ID:-local}"
report_dir="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/gitleaks-${run_id}"

mkdir -p "$report_dir"
report_path="$report_dir/gitleaks.sarif"
metadata_path="$report_dir/scan-metadata.txt"
rm -f "$report_path" "$metadata_path"

finish() {
  local exit_status=$?
  {
    printf 'Status: %s\n' "$status"
    printf 'Reason: %s\n' "$reason"
    printf 'Event: %s\n' "${GITHUB_EVENT_NAME:-unknown}"
    printf 'Scanned range: %s\n' "$scan_range"
    printf 'Head SHA: %s\n' "$head_sha"
    printf 'Non-merge commits in range: %s\n' "$commit_count"
    printf 'Gitleaks commits scanned: %s\n' "$scanner_commit_count"
    printf 'Scanner exit code: %s\n' "$scanner_exit"
    if [[ -f "$report_path" ]]; then
      printf 'Redacted SARIF report: available\n'
    else
      printf 'Redacted SARIF report: unavailable\n'
    fi
  } > "$metadata_path"

  if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
    {
      printf 'status=%s\n' "$status"
      printf 'reason=%s\n' "$reason"
      printf 'scan_range=%s\n' "$scan_range"
      printf 'head_sha=%s\n' "$head_sha"
      printf 'commit_count=%s\n' "$commit_count"
      printf 'scanner_commit_count=%s\n' "$scanner_commit_count"
      printf 'scanner_exit=%s\n' "$scanner_exit"
    } >> "$GITHUB_OUTPUT"
  fi

  return "$exit_status"
}
trap finish EXIT

fail() {
  status='failed'
  reason="$1"
  printf '::error::Secret scan failed: %s\n' "$reason" >&2
  exit 1
}

require_sha() {
  local label="$1"
  local value="$2"
  if [[ ! "$value" =~ ^[0-9a-f]{40}$ ]]; then
    fail "$label is missing or is not a 40-character commit SHA"
  fi
  if ! git -C "$workspace" cat-file -e "${value}^{commit}" 2>/dev/null; then
    fail "$label commit $value is unavailable in the checkout"
  fi
}

if [[ ! -d "$workspace/.git" && ! -f "$workspace/.git" ]]; then
  fail 'the Git checkout is unavailable'
fi

shallow_state="$(git -C "$workspace" rev-parse --is-shallow-repository 2>/dev/null)" || \
  fail 'could not determine whether the checkout is shallow'
if [[ "$shallow_state" != 'false' ]]; then
  fail 'the security checkout is shallow; fetch the complete history before scanning'
fi

cd "$workspace"

case "${GITHUB_EVENT_NAME:-}" in
  push)
    event_head="${EVENT_AFTER:-$head_sha}"
    if [[ "$event_head" != "$head_sha" ]]; then
      fail 'the push event head does not match GITHUB_SHA'
    fi
    require_sha 'push head' "$event_head"
    head_sha="$event_head"

    event_before="${EVENT_BEFORE:-}"
    if [[ "$event_before" == "$ZERO_SHA" ]]; then
      # The first push has no prior revision. Scan every reachable non-merge commit.
      scan_range="$head_sha"
    else
      require_sha 'push before' "$event_before"
      scan_range="${event_before}..${head_sha}"
    fi
    ;;
  pull_request)
    pr_base="${PR_BASE_SHA:-}"
    pr_head="${PR_HEAD_SHA:-}"
    require_sha 'pull request base' "$pr_base"
    require_sha 'pull request head' "$pr_head"
    head_sha="$pr_head"

    merge_base="$(git -C "$workspace" merge-base "$pr_base" "$pr_head" 2>/dev/null)" || \
      fail 'the pull request base and head have no available merge base'
    if [[ "$merge_base" == "$pr_head" ]]; then
      # An empty PR delta still gets a real full-history scan instead of a clean empty range.
      scan_range="$pr_head"
    else
      scan_range="${merge_base}..${pr_head}"
    fi
    ;;
  workflow_dispatch)
    require_sha 'manual dispatch head' "$head_sha"
    scan_range="$head_sha"
    ;;
  *)
    fail "unsupported event '${GITHUB_EVENT_NAME:-unset}'"
    ;;
esac

commit_list="$(git -C "$workspace" rev-list --no-merges "$scan_range" 2>/dev/null)" || \
  fail "Git could not enumerate the intended range '$scan_range'"
commit_count="$(printf '%s\n' "$commit_list" | awk 'NF { n++ } END { print n + 0 }')"

if [[ "$commit_count" == '0' && "$scan_range" != "$head_sha" ]]; then
  # Merge-only changes can have no non-merge commit patch. Scan the full head history.
  scan_range="$head_sha"
  commit_list="$(git -C "$workspace" rev-list --no-merges "$scan_range" 2>/dev/null)" || \
    fail 'Git could not enumerate the full head history'
  commit_count="$(printf '%s\n' "$commit_list" | awk 'NF { n++ } END { print n + 0 }')"
fi

if [[ "$commit_count" == '0' ]]; then
  fail "the resolved range '$scan_range' contains no scannable commits"
fi

log_options="--no-merges $scan_range"
printf 'Scanning %s non-merge commit(s), head %s, range %s\n' \
  "$commit_count" "$head_sha" "$scan_range"

docker_workspace="$workspace"
docker_report_dir="$report_dir"
scanner_log="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/gitleaks-${run_id}.log"
rm -f "$scanner_log"
case "${OSTYPE:-}" in
  msys*|cygwin*)
    docker_workspace="$(cygpath -m "$workspace")"
    docker_report_dir="$(cygpath -m "$report_dir")"
    # Prevent MSYS from rewriting the container's /repo and /tmp paths.
    export MSYS_NO_PATHCONV=1
    ;;
esac

if ! docker pull "$GITLEAKS_IMAGE"; then
  scanner_exit='125'
  fail 'could not pull the pinned Gitleaks image'
fi
set +e
docker run --rm \
  --network none \
  --read-only \
  --cap-drop=ALL \
  --security-opt=no-new-privileges \
  --user "$(id -u):$(id -g)" \
  --env GIT_CONFIG_COUNT=1 \
  --env GIT_CONFIG_KEY_0=safe.directory \
  --env GIT_CONFIG_VALUE_0=/repo \
  --tmpfs /tmp:rw,noexec,nosuid,size=64m \
  --volume "$docker_workspace:/repo:ro" \
  --volume "$docker_report_dir:/reports:rw" \
  --workdir /repo \
  "$GITLEAKS_IMAGE" \
  detect \
  --source /repo \
  --log-opts="$log_options" \
  --exit-code=2 \
  --report-format=sarif \
  --report-path=/reports/gitleaks.sarif \
  --redact \
  --no-banner \
  --no-color > "$scanner_log" 2>&1
scanner_exit=$?
set -e
cat "$scanner_log"

if [[ "$scanner_exit" != '0' && "$scanner_exit" != '2' ]]; then
  fail "Gitleaks scanner error (exit code $scanner_exit)"
fi

scanner_commit_count="$(sed -nE 's/.* ([0-9]+) commits? scanned\..*/\1/p' "$scanner_log" | tail -n 1)"
if [[ ! "$scanner_commit_count" =~ ^[0-9]+$ ]]; then
  fail 'Gitleaks did not report how many commits it scanned'
fi
if [[ "$scanner_commit_count" != "$commit_count" ]]; then
  fail "Gitleaks scanned $scanner_commit_count commit(s), but Git resolved $commit_count"
fi

if [[ ! -s "$report_path" ]]; then
  fail "Gitleaks exited $scanner_exit without producing a SARIF report"
fi

python_bin="$(command -v python3 || command -v python || true)"
if [[ -z "$python_bin" ]]; then
  fail 'Python 3 is required to validate the SARIF report'
fi

validation_report_path="$report_path"
case "${OSTYPE:-}" in
  msys*|cygwin*)
    validation_report_path="$(cygpath -m "$report_path")"
    ;;
esac

if ! report_findings="$("$python_bin" - "$validation_report_path" <<'PY'
import json
import sys

try:
    with open(sys.argv[1], encoding="utf-8") as report_file:
        report = json.load(report_file)
    runs = report.get("runs")
    if report.get("version") != "2.1.0" or not isinstance(runs, list) or not runs:
        raise ValueError("missing SARIF 2.1.0 runs")
    if any(not isinstance(run.get("results"), list) for run in runs):
        raise ValueError("invalid SARIF results")
    print(sum(len(run["results"]) for run in runs))
except Exception as error:
    print(f"Invalid Gitleaks SARIF report ({type(error).__name__})", file=sys.stderr)
    sys.exit(1)
PY
)"; then
  fail 'Gitleaks did not produce a valid SARIF 2.1.0 report'
fi

if [[ "$scanner_exit" == '0' && "$report_findings" == '0' ]]; then
  status='clean'
  reason='Gitleaks completed successfully with no findings'
  printf 'Secret scan status: clean (%s commits checked)\n' "$commit_count"
  exit 0
fi

if [[ "$scanner_exit" == '2' && "$report_findings" != '0' ]]; then
  status='findings'
  reason="Gitleaks reported $report_findings finding(s); review the redacted SARIF artifact"
  printf '::error::Secret scan findings detected; review the redacted SARIF artifact.\n' >&2
  exit 1
fi

fail "Gitleaks exit code $scanner_exit disagrees with $report_findings SARIF finding(s)"
