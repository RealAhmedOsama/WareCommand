# PWA and offline boundary

This document records the bounded implementation slice for issue #72. The
MVC application is progressively installable and resilient to a short network
loss, but it is deliberately not an offline inventory replica or command
queue.

## Current slice

- `manifest.webmanifest` defines the standalone WareCommand app, theme,
  orientation, scope, shortcuts, and install icons.
- `sw.js` uses a versioned same-origin static cache and a safe navigation
  fallback. It caches only the shell's static assets (`css`, `js`, `lib`, the
  manifest, icon, and `offline.html`). It never caches authenticated HTML,
  API responses, form submissions, inventory data, or authentication/session
  responses.
- `offline.html` is a static, data-free fallback. It explicitly tells the
  operator that no inventory command was accepted or queued while offline.
- The layout registers the service worker progressively and exposes online,
  offline, reconnecting, unsupported-mutation, and update-available states.
  The health probe uses `/health/live` with `no-store` and does not authorize
  work.
- Non-GET forms are blocked in the browser while offline unless a future form
  explicitly opts into `data-wms-offline-command="true"`. No current command
  opts in, because command IDs, idempotency, conflict resolution, and server
  confirmation are not yet implemented.
- The quick-scan draft is preserved only in tab-scoped `sessionStorage` and is
  removed after submit. No authentication, inventory snapshot, or master data
  is stored client-side.

## Update and cache contract

`WMS_CACHE_VERSION` is the deployment asset boundary. A deployment that changes
static shell behavior must advance the version, allowing activation to remove
older WareCommand caches. A waiting worker is announced in the shell and only
activated after the operator selects Refresh; the page then reloads under the
new controller.

## Remaining issue #72 gates

This is progress only. Closure still requires supported-browser/device
qualification for installability and icons, Playwright/mobile evidence for
offline transition and safe navigation fallback, session expiry, storage
cleanup, update flow, virtual keyboard/orientation/wake-lock behavior, and
reconnect conflicts. It also requires a separately designed and tested
idempotent command contract before any offline retry queue is enabled. No
offline mutation is currently claimed as supported.
