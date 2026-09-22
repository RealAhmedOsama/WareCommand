# Web, scanner, PWA, and RTL browser qualification

## Current slice

Issue #97 currently has an application-owned browser qualification matrix in
`Wms.Application/BrowserTesting`. The default matrix covers English LTR and
Arabic RTL, handheld/phone/tablet/laptop/desktop viewports, smoke and full
regression suites, scanner/reconnect intent, server-outcome assertions, and
failure artifacts. Credential-like failure values are redacted and bounded.

The policy rejects fixed sleeps and cases that only assert a visible message;
the eventual browser runner must prove the server outcome and correlation ID.

## Remaining qualification

Add the Playwright test project and isolated PostgreSQL host/data setup, then
wire authentication, warehouse selection, CRUD, dashboards, inventory,
inbound/outbound/transfer/count/return flows, keyboard-wedge scans, focus and
duplicate/retry behavior, session/PWA/offline cases, accessibility checks,
visual baselines, console/network failure handling, and CI artifact publishing.
Run smoke/full suites repeatedly and track flakiness without fixed sleeps.
