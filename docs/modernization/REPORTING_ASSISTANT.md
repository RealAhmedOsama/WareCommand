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

## Execution boundary

The eventual service must re-check the authenticated user and warehouse scope
at execution time, then call existing application-owned report/inquiry use
cases. Tool adapters must return citations containing a source tool, stable
record reference, and data cutoff. No model or provider may issue arbitrary
SQL, commands, or external requests. Raw questions, prompts, secrets, and
warehouse payloads must not be written to logs.

## Remaining qualification

The current slice does not claim a production assistant. Remaining work is the
authorized planner/executor wiring, per-tool result adapters, audit-safe
conversation state, localized UI, browser/RTL accessibility, prompt/provider
governance if an external model is approved, rate limits, adversarial prompt
tests, provider/load qualification, and production operational evidence.
