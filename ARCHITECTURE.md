# WareCommand architecture

WareCommand is a modular monolith. The repository keeps a small number of
assemblies and uses capability-oriented folders and namespaces inside those
assemblies. A new assembly is not created for every feature, and no service
boundary or network hop is implied by a module name.

## Runtime dependency direction

```text
Wms.Domain
    ^
Wms.Application  (use cases, DTOs, application contracts)
    ^
Wms.Infrastructure (EF Core persistence, repositories, operational adapters)
    ^
Wms.ASP / Wms.WinForms (presentation adapters and composition roots)
```

The hosts reference `Wms.Application` and `Wms.Infrastructure` so they can
compose the application. They do not reference the domain directly, own an EF
Core context, or construct domain entities for an operational request.

- `Wms.Domain` owns entities, value objects, enums, repository/service
  abstractions, and invariants. It has no project reference to an outer layer.
- `Wms.Application` owns use-case orchestration, request/response DTOs, and
  result handling. It depends on the domain and framework abstractions only.
- `Wms.Infrastructure` owns EF Core mappings, the SQLite adapter currently used
  for local qualification, repository implementations, stock movement
  persistence, and demo database initialization.
- `Wms.ASP` and `Wms.WinForms` translate UI input into application requests and
  render application results. Their `Program` files are composition roots only.

The executable architecture tests in `Wms.Architecture.Tests` enforce the
project-reference direction and scan presentation/domain source for forbidden
outer-layer dependencies and direct persistence/entity construction.

## Capability ownership

| Capability | Current owner | Boundary | Current status |
| --- | --- | --- | --- |
| Access | Reserved application capability | Identity, roles, and access policy contracts | No workflow is implemented yet |
| Catalog | `Wms.Application/UseCases/Items` and item domain types | Item identity, descriptions, barcodes, lot/serial requirements | Implemented |
| Warehouses | `Wms.Application/UseCases/Locations` and warehouse/location domain types | Warehouse topology and location capabilities | Implemented |
| Inventory | `Wms.Application/UseCases/Inventory`, stock domain types, and stock movement infrastructure | On-hand, reserved, and movement invariants | Implemented |
| Inbound | `Wms.Application/UseCases/Receiving` | Receiving and putaway workflows | Implemented |
| Outbound | `Wms.Application/UseCases/Picking` | Picking workflow and availability checks | Implemented |
| Execution | Reserved application capability | Work/task execution across warehouse operations | Extension point only |
| Counting | Reserved application capability | Cycle counts and count adjustments | Extension point only |
| Replenishment | Reserved application capability | Reorder and movement recommendations | Extension point only |
| Shipping | Reserved application capability | Shipment staging and dispatch | Extension point only |
| Reporting | `Wms.Application/UseCases/Reports` | Read-only movement/report projections | Implemented |
| Integrations | Reserved infrastructure capability | External providers and durable adapter contracts | Extension point only |

The existing `UseCases/<capability>` folders are the current application
module seams. Future capabilities should add a folder and application
contract within the existing assembly first; splitting an assembly is a later
decision requiring an independently deployable or ownership boundary.

## Composition and initialization

Layer registration is centralized in reusable extensions:

- `AddWmsApplication` groups registrations by Catalog, Warehouses, Inventory,
  Inbound, Outbound, and Reporting.
- `AddWmsInfrastructure` groups persistence, stock movement adapters, and
  database initialization.
- `WmsDatabaseInitializer` owns `EnsureCreated` and the existing Web/Desktop
  demo seed profiles. The two composition roots select a profile; they do not
  build entities or call `SaveChanges`.

Controllers and forms may perform presentation validation such as required
fields and parseable numbers. Business invariants and state transitions remain
in domain entities and application/infrastructure workflows.

## Testing and evolution rules

- `Wms.Domain.Tests` validates domain invariants and value objects.
- `Wms.Application.Tests` validates use-case behavior through contracts and
  mocks.
- `Wms.Infrastructure.Tests` validates persistence and stock movement adapters.
- `Wms.Architecture.Tests` validates project references, source boundaries,
  capability folders, and thin composition roots.
- Web and WinForms smoke qualification remains an explicit verification step;
  architecture conformance does not imply provider, deployment, or production
  readiness.

Major choices are recorded in [`docs/architecture/decisions/`](docs/architecture/decisions/).
