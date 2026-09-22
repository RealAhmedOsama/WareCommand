# Administration and operations console

Issue #85 is being delivered as a bounded console boundary while the owning module screens continue to mature. The console is not a generic database editor.

## Delivered in the first slice

- `/Administration` is protected by `access.manage` and only renders catalog entries authorized for the current user.
- `/api/administration/catalog` exposes typed module metadata: English and Arabic labels, route, required permission, warehouse scope, dependencies, supported actions, and surface maturity.
- `/api/administration/readiness` performs server-side checks for global settings, active warehouses, inbound operational locations, outbound operational locations, immutable audit storage, retention policy registration, and deployment health.
- `/api/administration/history` delegates to the existing paged audit query. The page intentionally shows safe scalar audit fields only; before/after payloads are not rendered in the console.
- Deployment-managed settings and credentials are represented as boundaries. Secret values are never returned by this catalog or page.
- Existing typed module screens remain the owners of validation, mutation, warehouse scope, and audit behavior. The catalog links to those screens and APIs instead of bypassing them.
- English/Arabic labels and RTL/LTR-safe layout are included in the new surface. Tables have captions, scoped headers, status text, and responsive wrappers.

## Readiness semantics

The summary is diagnostic and does not activate a workflow. A blocked inbound or outbound check means that the required active warehouse or operational location roles are missing. A warning means the system can still be inspected but requires an operational decision, such as enabling a configured workflow or registering a retention policy.

The deployment health check links to `/health/ready`; the administration service does not claim backup, storage, or job-runner health from configuration alone.

The readiness query is provider-qualified against PostgreSQL. With a
warehouse-scoped access context it returns only the authorized active
warehouse, while reading the global settings row, operational-location roles,
immutable audit table, and enabled retention-policy state from the same
database. The disposable PostgreSQL 17 harness passed 51/51 on 2026-09-22
(port 55505) and cleaned its test container. This is persistence and scope
evidence only; complete module CRUD/import/export, provider load, browser and
handheld accessibility, and production qualification remain open.

## Remaining #85 work

The issue remains open for the complete cross-module acceptance matrix: unified create/edit/activate/deactivate and bulk/import/export experiences for every configurable feature, high-impact preview and rollback/version restore workflows where supported, device/printer administration, integrations/API setup, full browser responsive/accessibility evidence, and end-to-end localization verification.
