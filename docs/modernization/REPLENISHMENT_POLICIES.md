# Replenishment policies and signals

## Current local boundary

WareCommand now stores effective-dated replenishment policies per item and
warehouse, with an optional location override. A policy contains minimum,
maximum, safety-stock, reorder-point, and target quantities, a quantity basis
(on-hand, physical available, or available-to-promise), an optional preferred
source, and an optional lead time. Domain validation rejects negative values,
invalid quantity ordering, invalid effective ranges, and negative lead times.

`Wms.Infrastructure.Inventory.InventoryReplenishmentPolicyService` is the
authorized read/write boundary. It rejects overlapping active policies for the
same item/warehouse/location scope, keeps policy identity immutable on update,
and records policy changes through the shared immutable audit writer. Signals
are evaluated from canonical `InventoryBalances` using database aggregation;
non-allocatable or inactive status/item/location rows do not contribute to ATP.
Inbound and ordered quantities are explicit zero values until the inbound and
procurement models provide those facts.

The scheduled low-stock job and the dashboard now consume these signals. Job
notifications use the existing durable deduplication key with policy, signal
kind, and business date, so repeated runs do not create duplicate alerts.
Signal evaluation never mutates stock and never creates a purchase order.

The API surface is:

- `GET /api/inventory/replenishment-policies`
- `GET /api/inventory/replenishment-policies/signals`
- `POST /api/inventory/replenishment-policies`
- `PUT /api/inventory/replenishment-policies/{policyId}`

Reads require `inventory.read`; writes require `inventory.adjust` and are
warehouse-scoped. The migration is
`20260920183616_AddInventoryReplenishmentPolicies`.

## Evidence

- `Wms.Domain.Tests/Entities/InventoryReplenishmentPolicyTests.cs`
- `Wms.Infrastructure.Tests/Inventory/InventoryReplenishmentPolicyServiceTests.cs`
- `Wms.Infrastructure/Inventory/InventoryReplenishmentPolicyService.cs`
- `Wms.Infrastructure/Jobs/WmsOperationalJobHandlers.cs`
- `Wms.ASP/Controllers/InventoryReplenishmentPoliciesController.cs`
- `Wms.ASP/Controllers/DashboardController.cs`

## Remaining issue scope

This is a progress slice, not closure of backlog issue #40. Bulk CSV import and
export with row-level errors, activation/deactivation administration, alert
acknowledgement and snooze state, localized administration screens, richer
dashboard/report filters, inbound and ordered quantity projections, measured
PostgreSQL plans/indexes, high-volume qualification, and replenishment-plan
consumers remain open. Those later consumers must use the policy signal
contract and must not create purchase orders implicitly.
