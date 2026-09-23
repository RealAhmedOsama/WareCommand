# Permission-aware reporting assistant

## Current slice

Issue #104 currently has an application-owned, read-only planning boundary in
`Wms.Application/ReportingAssistant`. A bounded English or Arabic question is
mapped only to an allow-listed reporting tool. The plan carries the required
permissions, an optional warehouse scope, a row limit, a locale, and a
one-way question fingerprint; it never stores the raw question as an audit
payload and it never accepts a database query or provider prompt.

Supported planning tools are inventory balance, movement ledger, receiving
status, outbound status, exception summary, and warehouse KPI. A question that
matches multiple tools returns `NeedsClarification` instead of guessing. A
mutation request is rejected before planning, and missing permission or an
out-of-scope warehouse is rejected with a safe authorization result.

## Executable read-only API

`POST /api/reporting-assistant/query` is authorized by `reports.read` and uses
the configured report rate limit. Cookie-authenticated clients send the
antiforgery token in the `RequestVerificationToken` header. Its JSON body
accepts only `question`, `locale`, `warehouseId`, and `maximumRows`; permission
claims and warehouse assignments are read from the authenticated server
context. The executor checks
the report permission, derives the planner's permission set from current
claims, plans deterministically, then reauthorizes every permission and the
selected warehouse immediately before executing a tool. The six adapters call
the existing inventory inquiry, movement report, receipt list, sales order
list, inbound/outbound exception lists, and dashboard read services. They do
not accept SQL or write commands.

The response has a stable status, deterministic summary, typed data fields and
units, filters, limitations, and citations. Each returned field cites the
allow-listed source tool and a stable record reference with a cutoff timestamp.
Inventory and dashboard quantities are identified as canonical base units;
movement quantities retain their display unit. Receipt and sales-order sources
do not expose database snapshot tokens, so their response states that the
cutoff is the completed read time. Exception details omit free-text reasons and
notes. Empty, truncated, clarification, unsupported, unavailable, and timed-out
results are represented explicitly; cancellation propagates, and HTTP rate
rejection remains `429`.

Execution is bounded to 200 rows and eight seconds. The planner rejects
mutation requests and database query syntax, and sends no user-provided search
text to the report tools. No conversation state is persisted: a clarification
response asks the client to resend the complete question, so permissions and
warehouse scope are checked again. Safe logs contain only the selected tool,
warehouse, one-way question fingerprint, row count, or exception type; raw
questions, prompts, secrets, and warehouse payloads are not logged. No external
model/provider is configured or called.

## Remaining qualification

This slice does not include an assistant UI, stored conversation history, an
external model/provider, or production operational qualification. UI/RTL
accessibility, provider governance if separately approved, load qualification,
and production evidence remain separate work.
