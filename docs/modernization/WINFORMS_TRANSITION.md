# WinForms transition and parity strategy

Issue #86 is a transition decision and evidence boundary. The Web host is the
primary product surface. The WinForms host remains an optional workstation
client during the parity period because scanner-heavy stations may still need
an embedded desktop shell. No WinForms file is removed or archived by this
document.

## Client support decision

| Client | Support position | Data and policy boundary | Removal condition |
| --- | --- | --- | --- |
| ASP.NET Core Web | Primary supported client | Shared Application contracts, Infrastructure services, Identity/RBAC, warehouse scope, audit, and checked-in migrations | None; this is the reference surface |
| WinForms | Optional retained workstation during transition | The same `Wms.Application` and `Wms.Infrastructure` registrations, explicit desktop Identity session, shared use cases, and the same database schema | Retain for scan-heavy stations only when its parity and support evidence justifies it |
| Legacy standalone forms/assets | Not a separate product | No independent persistence or business-rule boundary is permitted | Archive only after explicit approval, parity evidence, and rollback rehearsal |

The current release has no installer or auto-update contract. A retained
desktop build must be distributed as a versioned artifact with the matching
application revision, configuration template, supported database-provider
statement, and smoke evidence. PostgreSQL migrations are applied by the
approved deployment process before a host is started; a desktop binary must
not silently migrate production data.

## Capability inventory and disposition

The following table is the complete capability inventory of the current
WinForms source. `.Designer.cs` and `.resx` files are presentation artifacts,
not additional workflows.

| Form or file | Capability and behavior | Disposition | Shared boundary and authorization | Web parity target | Current gate |
| --- | --- | --- | --- | --- | --- |
| `LoginForm.cs` | Username/password sign-in, disabled/locked-account messages, localized session start | Retain optional workstation | `IDesktopAuthenticationService`, `DesktopUserSession`, Identity audit | `/Account/Login` | Local auth exists; desktop/web side-by-side evidence remains |
| `MainForm.cs` | Desktop shell, child-form host, F1–F8 navigation, Escape close | Retain optional workstation | DI-resolved forms; operation forms enforce application authorization | Web navigation and command bar | Shell exists; parity and permission-visibility matrix remains |
| `DashboardForm.cs` | KPI cards, low-stock and recent-movement views, refresh | Replace in Web after parity | `IGetStockUseCase`, `IGetItemsUseCase`, `IWmsSettingsService`, shared clock | `/Dashboard` | Both surfaces exist; side-by-side evidence remains |
| `ReceivingForm.cs` | Barcode/item lookup, quantity, lot, location, reference, notes, receive action | Retain optional workstation | `IReceiveItemUseCase`, `IGetItemsUseCase`, `ICurrentUser.RequireUserId()` | `/Receiving/Receive` and receiving API | Shared mutation boundary exists; scanner journey remains |
| `PutawayForm.cs` | Barcode/item lookup, quantity, from/to locations, putaway action | Retain optional workstation | `IPutawayUseCase`, settings, `ICurrentUser.RequireUserId()` | `/Receiving/Putaway` and putaway API | Shared mutation boundary exists; handheld journey remains |
| `PickingForm.cs` | Barcode/item lookup, quantity, source location, order, pick action | Retain optional workstation | `IPickOrderUseCase`, `IGetItemsUseCase`, `ICurrentUser.RequireUserId()` | `/Picking` and picking API | Shared mutation boundary exists; handheld journey remains |
| `InventoryForm.cs` | Scoped stock search, refresh/summary, adjustment dialog | Retain optional workstation | `IGetStockUseCase`, `IStockAdjustmentUseCase`, `ICurrentUser.RequireUserId()` | `/Inventory` | Shared mutation boundary exists; adjustment parity remains |
| `StockAdjustmentDialog.cs` | New quantity/reason validation, Enter-to-reason, F1 confirm, Escape cancel | Keep temporarily | Presentation-only dialog; parent sends real user ID to `IStockAdjustmentUseCase` | Inventory adjustment flow | Web equivalent and reason/approval UX remain |
| `ItemManagementForm.cs` | Search/list item master, open create/edit dialog | Replace in Web after parity | `IGetItemsUseCase`; dialogs use item use cases and real identity | `/Items` | Web CRUD exists; side-by-side and bulk parity remain |
| `ItemEditDialog.cs` | Typed item create/update validation and submission | Replace in Web after parity | `ICreateItemUseCase`, `IUpdateItemUseCase`, `ICurrentUser.RequireUserId()` | `/Items/Create`, `/Items/Edit` | Web field parity remains |
| `LocationManagementForm.cs` | Search/list locations, open create/edit dialog | Replace in Web after parity | `IGetLocationsUseCase`; dialogs use location use cases and real identity | `/Locations` | Web CRUD exists; warehouse-scope journey remains |
| `LocationEditDialog.cs` | Typed location create/update, warehouse/default-setting lookup | Replace in Web after parity | `ICreateLocationUseCase`, `IUpdateLocationUseCase`, settings, `ICurrentUser.RequireUserId()` | `/Locations/Create`, `/Locations/Edit` | Web field parity remains |
| `ReportsForm.cs` | Movement filters, server report query, CSV export, localized display | Replace in Web after parity | `IMovementReportUseCase`, settings, business clock | `/Reports` | Web report core exists; export/render parity remains |

## Desktop shortcuts and scanner behavior

| Workflow | Input order and scanner contract | Keyboard behavior |
| --- | --- | --- |
| Shell | Navigate dashboard, receiving, putaway, picking, inventory, items, locations, reports | F1 dashboard, F2 receiving, F3 putaway, F4 picking, F5 inventory, F6 items, F7 locations, F8 reports, Escape closes |
| Receiving | Barcode lookup; quantity; lot when required; location; reference/notes; receive | Enter advances barcode/quantity/lot; F1 submits when valid; F2 clears; Escape closes |
| Putaway | Barcode lookup; quantity; source location; destination location; notes; putaway | Enter advances scanner fields; F1 submits when valid; F2 clears; Escape closes |
| Picking | Barcode lookup; quantity; source location; order/notes; pick | Enter advances scanner fields; F1 submits when valid; F2 clears; Escape closes |
| Inventory adjustment | Select stock row; enter non-negative quantity and reason | Enter moves from quantity to reason; F1 confirms; Escape cancels |

The scanner contract is keyboard-wedge compatible: controls receive the
scanner text, the Enter terminator advances the workflow, and the form keeps
the next scanner field focused. The shared settings service supplies barcode
length, timeout, audio, and default-location policy; forms do not own a
second configuration source.

## Security and drift controls

- `Warehouse Management System/Program.cs` composes the same Infrastructure
  and Application registrations as Web, then requires the explicit desktop
  login before opening `MainForm`.
- `DesktopUserSession` is the only desktop `ICurrentUser` implementation. No
  `SYSTEM`, `WEB_USER`, or anonymous operational identity is allowed.
- Mutating desktop forms pass `ICurrentUser.RequireUserId()` into shared use
  cases. Shared services remain authoritative for RBAC, warehouse scope,
  idempotency, invariants, and audit; the desktop UI is not a second policy
  engine.
- WinForms source does not reference `WmsDbContext`, EF Core, repositories,
  or domain entities. Persistence and seed initialization remain in
  Infrastructure composition extensions.
- New desktop workflows must add a row to this inventory, use an existing
  Application contract or add one there, add authorization/audit tests, and
  add a Web parity target before implementation is considered complete.

## Parity gate before replacement or removal

For receiving, putaway, inventory, picking, items, locations, reports,
printing, and scanner shortcuts, parity means a side-by-side Web and desktop
journey using the same seeded data and user/warehouse scope. The evidence must
cover successful and rejected authorization, validation errors, duplicate or
retry behavior, audit output, Arabic/English display, RTL/LTR layout, and
keyboard/scanner timing. A source review or a successful unit test alone does
not satisfy this gate.

## Rollback and decommission rule

No removal is authorized by #86. When a future release proposes replacement,
the change must preserve the last supported desktop artifact, record the
parity evidence and owner approval, and document rollback as: restore the
approved database backup if schema/data changed, redeploy the prior compatible
Web/desktop revision, and re-run health, authorization, and scanner smoke
checks. An older desktop binary must never be pointed at a schema it does not
declare compatible with.
