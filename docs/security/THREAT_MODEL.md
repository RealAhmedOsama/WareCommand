# WareCommand threat model

This is the repository-level threat model for the current WMS boundary. It is
an engineering control and release input, not a penetration-test or compliance
certification. The model is updated when a new external entry point, privileged
workflow, storage boundary, or provider adapter is added.

## Assets and actors

| Asset | Security property | Primary actors |
| --- | --- | --- |
| Inventory quantities, reservations, ledger, lots, serials, LPNs | Integrity, ordering, warehouse scope | Operators, managers, jobs |
| Documents, integration messages, control numbers, acknowledgements | Integrity, idempotency, traceability | Partners, adapters, jobs |
| Identity accounts, sessions, API clients, credential references | Confidentiality, authentication, revocation | Users, administrators, providers |
| Attachments, canonical import/B2B payloads, reports | Confidentiality, retention, malware safety | Users, support operators |
| Audit, logs, traces, health and support evidence | Integrity, redaction, availability | Administrators, support |
| PostgreSQL, backups, Data Protection keys, storage | Confidentiality, recoverability, availability | Deployment operators |

Actors include unauthenticated browsers, authenticated warehouse users, API
clients, connector/B2B partners, background workers, administrators, support
operators, deployment infrastructure, and a compromised external provider.

## Trust boundaries and data flow

1. Browser/PWA/WinForms input crosses authentication, antiforgery, request-size,
   rate-limit, input-validation, and warehouse-authorization boundaries.
2. The ASP.NET host calls Application contracts. Application code owns command,
   permission, idempotency, and invariant decisions; vendor SDKs are not allowed
   in the domain or core application modules.
3. EF Core is the persistence boundary. Inventory mutations, audit entries,
   integration outbox rows, B2B documents, and job state commit in explicit
   transactions; PostgreSQL is the production provider.
4. Connectors and B2B adapters cross into untrusted external systems through
   credential references, bounded requests, outbox/inbox, control-number and
   payload-hash checks, retry/dead-letter state, and transport-specific gates.
5. Files, attachments, reports, backups, logs, and Data Protection keys cross
   storage boundaries and require path, type, size, retention, encryption, and
   redaction controls.

## Threats and controls

| Threat | Control/evidence | Remaining gate |
| --- | --- | --- |
| Unauthenticated or cross-warehouse mutation | Identity, RBAC, `IWarehouseAccessService`, API-client scopes, negative authorization tests | Full browser/API journey matrix and provider deployment review |
| CSRF, XSS, overposting, open redirect | Global antiforgery, encoded Razor output, bind allowlists, local return URL checks, CSP/security headers | Reverse-proxy/TLS/header verification |
| Credential or token leakage | Hash-only API client secrets, deployment-managed references, redacted audit/logging, no raw connector/B2B secret acceptance | Secret scanner/dependency/container and production secret rotation |
| Webhook replay or duplicate integration message | Timestamped HMAC signatures, replay window, inbox dedup/hash reuse rejection, outbox delivery state | Real transport/provider and crash/restart qualification |
| Duplicate EDI interchange/document | Partner/control/message/idempotency unique keys, canonical payload hashes, quarantine and replay state | X12/EDIFACT golden samples and live transport certification |
| Malicious or oversized files/imports | Size/content boundaries, safe CSV formula export, attachment validation and storage containment | Full upload/antivirus/provider qualification |
| Injection through query or stored text | EF parameterization, validated mappings, encoded output, approved read models | Threat-specific fuzzing and external review |
| SSRF or compromised provider | No default network transports, explicit adapter ports, endpoint/credential deployment boundary, allowlisted modes | Live HTTP/SFTP/webhook adapter hardening and egress policy |
| Partial mutation or ambiguous retry | EF transaction boundaries, idempotency keys, immutable ledger/audit, job/outbox/inbox leases | PostgreSQL deadlock/crash and full journey qualification |
| Sensitive support artifact exposure | Redacted diagnostics/runbook boundary and bounded DTOs with no raw payloads | Support-bundle implementation and expiry/access audit |

## Automated boundary checks

Run the focused security tests and the read-only static gate:

```powershell
dotnet test Wms.Application.Tests/Wms.Application.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~SecurityBoundaryTests'
pwsh -NoProfile -File .\scripts\verify-security-boundaries.ps1
```

The static gate checks the presence of the host controls and fails if tracked
MVC views introduce an unreviewed `Html.Raw` use (the existing JSON-serialization
and fixed localized badge cases are allowlisted), if the connector/B2B secret
boundary is absent, or if the checked-in security guidance loses its explicit
external-review boundary.

## Launch blockers

No claim of completion is allowed until threat-model findings are triaged,
deep dependency/container/secret scans are evidenced, deployment proxy/TLS and
secret rotation are verified, upload/provider egress controls are qualified,
support artifacts are redacted and expiring, and a scoped external security
review is completed where required by the deployment owner.
