# WareCommand authorization

WareCommand uses database-backed permission policies and warehouse assignments inside the modular monolith. Authentication establishes the user identity; authorization is evaluated from the active database state for every policy and application use-case call.

## Permission model

The permission catalog is defined in `Wms.Application/Identity/WmsAuthorization.cs`. Roles are named bundles of permission claims and direct user permission claims are additive. The seeded role matrix is:

| Role | Default capabilities |
| --- | --- |
| Administrator | All catalog permissions and global warehouse scope |
| WarehouseManager | Master data, inventory, inbound, outbound, counting, allocation, reports, and warehouse management |
| Receiver | Read-only item/location/inventory access plus receiving and putaway |
| Picker | Read-only item/location/inventory access plus picking |
| Packer | Read-only item/location/inventory access plus packing |
| InventoryController | Inventory read/adjust, counting, allocation, and reports |
| Auditor / Viewer | Read-only item/location/inventory and reports |
| WarehouseStaff | Existing compatibility bundle for receiving, putaway, and picking |

The role catalog is additive on startup: missing roles and default permission claims are created, while administrator-managed direct grants are preserved.

## Warehouse scope

`WmsUserWarehouseAssignments` stores a user-to-warehouse assignment and an optional default assignment. Non-administrator users can query or mutate only active assigned warehouses. The default warehouse is a preference and is never treated as authorization by itself. Administrators have global warehouse scope through the administrator role's wildcard permission.

Locations, stock, and movements use scoped repository queries. Current receiving, putaway, picking, inventory adjustment, location management, and movement-report use cases also authorize the operation and the target warehouse. A cross-warehouse location identifier therefore returns no visible entity and cannot be used to mutate stock.

## Web and desktop enforcement

MVC policies are registered for every permission in the catalog. Controllers use policies for direct URL access, while application use cases repeat the permission and warehouse check so retained WinForms paths and future API/job adapters use the same boundary. The policy handler logs denied user/permission pairs without logging credentials or sensitive request data.

Administrators use Account → Manage access to assign roles, direct permissions, assigned warehouses, and an optional default warehouse. The server validates catalog values, active warehouse IDs, default membership, administrator self-protection, and preservation of at least one active administrator. Changes rotate the target security stamp and are read from the database immediately by the authorization service.

## Verification

- `Wms.Infrastructure.Tests/Identity/WmsAuthorizationTests.cs` verifies permission grants, immediate grant removal, warehouse filtering, cross-warehouse denial, and safe administrator access updates.
- `Wms.ASP.Tests/AuthorizationFlowTests.cs` verifies direct MVC denial, existing-session permission removal, and warehouse-scoped inventory rendering.
- The authorization migration is `20260920000339_AddWarehouseAuthorization`.
