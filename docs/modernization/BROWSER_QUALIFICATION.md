# Web, scanner, PWA, and RTL browser qualification

## Automated PostgreSQL browser slice

`Wms.ASP.Tests/PostgreSqlBrowserJourneyTests.cs` runs Chromium against the MVC
application on a real loopback HTTP origin. The fixture provisions an isolated
PostgreSQL schema, real Identity actors, warehouse assignments, and controlled
inventory. It does not use `TestServer` for browser navigation or mocked server
responses.

The route-render matrix covers the listed screens in English LTR and Arabic RTL
at 390x844, 768x1024, and 1366x768. The separate Settings permission check is
an English authenticated journey.

| Existing route | Browser evidence |
| --- | --- |
| `/Dashboard` | Authenticated render, language/direction, landmark, and page overflow |
| `/Inventory` | Persisted item inquiry, warehouse switch, and cross-actor data isolation |
| `/Receiving/Receive` | Permission boundary, scanner focus, foreign-location rejection, durable scan result, offline queue, response-loss retry, and duplicate-operation idempotency |
| `/Receiving/Putaway` | Real form submission and source/destination PostgreSQL balances |
| `/Picking` | Existing form render; client-side offline mutation block announces that nothing was submitted; no successful order pick is claimed |
| `/Reports` | Authenticated render and responsive/localization contract |
| `/Warehouses` | Assigned warehouse render and responsive/localization contract |
| `/Settings` | Warehouse-manager navigation omission and direct access-denied journey |

The receiving test queues a keyboard-wedge scan while disconnected and verifies
that PostgreSQL remains unchanged. It then lets the real server commit the scan
while the browser loses the response, retries the same client operation ID,
and verifies one completed scan and one inventory increase. A pending scan is
kept under the first actor's browser-storage namespace; after logout, the
second actor sees neither that draft nor an automatic replay. The service
worker test verifies that authenticated routes and API paths are absent from
its static cache and that offline navigation renders the explicit offline
message.

Run this slice locally with Docker available:

```powershell
pwsh -NoProfile -File scripts/verify-postgresql.ps1 -Group browser -Port 55432
```

The default PostgreSQL verification includes this bounded browser group. The
full performance profile remains an explicit command. Playwright installs the
Chromium build paired with the pinned `Microsoft.Playwright` package before the
browser group runs.

On failure, CI uploads a bounded JSON route/status/error timeline and a
screenshot that masks form inputs. The evidence omits request/response bodies,
cookies, authorization headers, antiforgery values, storage state, and raw
Playwright traces. No browser artifacts are published for a passing run.

## Application-owned matrix

`Wms.Application/BrowserTesting` remains the policy source for smoke and full
regression cases, English/Arabic direction, viewports, scanner/reconnect intent,
server-outcome assertions, bounded artifacts, and sensitive-value redaction.
The PostgreSQL browser group is the first real-browser runner wired to that
application contract; its current bounded case set is listed above.

## Remaining qualification

- `/Dashboard` refresh and warehouse-scope behavior; the matrix currently
  qualifies rendering and responsive layout.
- `/Picking`: a successful browser pick against a persisted sales order. The
  current check only proves offline submission is blocked without a mutation.
- `/Receiving`: purchase-order/ASN receipts and full session correction,
  completion, and cancellation journeys.
- `/Reports` and `/Warehouses`: filter/refresh/export and management mutations;
  the current matrix only qualifies authorized rendering and layout.
- Browser mutation journeys for cycle counts, returns, shipping, transfers,
  inventory adjustment, imports, and other implemented screens not in the
  route matrix above. Use `docs/ui/UI_BACKEND_COVERAGE.md` to distinguish
  implemented UI, API-only routes, and absent surfaces.
- Cross-actor queued-scan denial against the API in a second browser context,
  repeated response-loss runs, and broader offline/reconnect cases across each
  currently idempotent UI command.
- Scanner hardware certification; the current test sends keyboard events and
  does not claim physical wedge-device behavior.
- Browser-based axe checks, screenshot baselines, screen-reader checks,
  high-DPI/zoom, 480px handheld and 1920px kiosk viewports, and repeated
  cross-browser flake measurements.
- Service-worker upgrade from an older cache version, installability on
  supported mobile devices, and manual stale-data/update review.
- Manual operator walkthroughs and full backend reconciliation after each
  journey remain separate from this bounded browser suite.
