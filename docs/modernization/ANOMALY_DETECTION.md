# Explainable operational anomaly detection

## Current slice

Issue #106 currently has an application-owned deterministic signal boundary in
`Wms.Application/AnomalyDetection`. It accepts warehouse-scoped observations,
compares them with an explicit expected value and threshold, emits stable
deduplicated finding fingerprints, assigns a bounded severity, and preserves
the rule version, source reference, observed/expected values, timestamp, and a
plain-language explanation.

The lifecycle supports New, Investigating, Explained, ConfirmedIssue,
FalsePositive, Resolved, and Suppressed. Suppression is comment-bearing,
time-bounded, and validated separately from detection. Personal actor
references are rejected at this boundary; finding output has no authority to
mutate inventory or block a worker, and findings are investigation signals—not
proof of fraud, error, or misconduct.

## Remaining qualification

Remaining work includes adapters for ledger, counts, scans, receiving,
shipping, work, documents, and integrations; durable versioned rule/threshold
configuration; scheduled idempotent persistence; assignment/comments/evidence
UI; alerts and notification links; normal inquiry/audit/reconciliation
drill-down; false-positive feedback; localized/browser qualification; and
provider/load/production evidence. Threshold changes must create new versions
and never rewrite historical findings.
