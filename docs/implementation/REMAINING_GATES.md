# Remaining Master-Plan Gates

This is the local follow-up inventory for master-plan issue #108. The original
issues #2–#107 have delivered implementation slices and are closed on GitHub,
but their comments identify follow-up qualification or integration work. This
file groups that work into independently verifiable issues without confusing
local evidence with production, live-provider, or external-review approval.

## Local workstreams

| Workstream | New issue | Affected original issues | Local completion evidence |
| --- | --- | --- | --- |
| Bounded PostgreSQL/provider matrix | #109 | #6, #25–#70, #78–#95 | Disposable PostgreSQL groups run with isolated schemas, bounded memory, provider-specific assertions, cleanup proof, and redacted result counts. |
| Browser/client qualification | #110 | #20, #46–#87, #88–#93, #96–#98, #104–#107 | Local Web/PWA smoke matrix covers authentication, scanner shell, EN/AR LTR/RTL, responsive/focus/accessibility assertions, and redacted artifacts. |
| Performance/load evidence | #111 | #18, #21, #34, #54, #75, #90–#99, #102 | Repeatable local workloads carry revision/dataset/environment metadata, bounded concurrency, PostgreSQL/query/worker metrics, and reconciliation results. |
| Non-production resilience | #112 | #21, #22, #34, #90, #94–#99 | Test-only timeout/conflict/restart/response-loss/dead-letter/restore scenarios prove no partial or duplicate business result and cannot activate in production. |
| Release/security/support packet | #113 | #6, #7, #9, #10, #14, #15, #18, #22, #82, #84, #85, #87, #94, #95, #100–#103 | Local preflight, migration, backup, security-boundary, redaction, compatibility, and GO/GO_WITH_RESTRICTIONS/NO_GO packet checks pass without secrets. |
| Remaining local module wiring | #114 | #82–#93, #96–#107 and dependent workflow owners | Missing local command/resource/handler/UI/job/audit wiring gets focused contract and flow tests, while real vendor transport/certification stays explicit. |

## Explicitly non-local gates

The following cannot be honestly completed by local code changes alone:

- production migration/deployment, production secrets, proxy/TLS/storage, and
  an approved change window;
- live provider throughput/acceptance, real carrier/email/SFTP/EDI credentials,
  partner certification, or vendor-specific device/printer qualification;
- independent security review, external penetration testing, or production
  capacity approval.

Those gates remain documented in #109–#114 comments and in the release packet;
they are not silently converted into a local completion claim.

## Closure rule

A local workstream issue may be closed only after its focused tests, relevant
full project tests, and evidence packet pass. Master-plan #108 may be closed
only when the explicit non-local gates have an authorized owner and fresh
evidence, not merely because all implementation slices are committed.
