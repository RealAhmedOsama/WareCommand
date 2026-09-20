# Typed business settings

WareCommand stores business behavior in two explicit tables:

- `WmsGlobalSettings` is the single global profile. It owns company identity,
  warehouse defaults, numbering sequences, inventory safety rules, dashboard
  thresholds, expiry policy, scanner behavior, labels, reports, localization,
  and non-secret integration routing.
- `WmsWarehouseSettingsOverrides` stores nullable, named overrides keyed by
  `WarehouseId`. A null field means inherit the global value; it is not a
  generic key/value bag.

## Precedence and safety

The effective value is `warehouse override -> global value`. Values are merged
in `WmsSettingsPrecedence` and validated again after merging. Structural rules
cover ranges, barcode min/max ordering, locale/time-zone validity, numbering
sequences, and HTTP(S) integration endpoints. Relational validation rejects a
missing/inactive default warehouse and requires the configured receiving
location to be active and receivable in the target warehouse. Shipping
locations are validated when configured.

The Settings MVC controller and the settings service both require
`settings.manage`; warehouse-scoped writes also require warehouse scope. Every
create, update, removal, and import is recorded through the shared immutable
`IAuditWriter` in the same database transaction. The service uses optimistic
revision tokens and invalidates the effective-value cache after commit. Cache
entries have a bounded five-minute lifetime as a recovery guard for out-of-band
changes.

## Secret boundary

The schema contains no passwords, API keys, tokens, connection strings, or
credential values. `WmsIntegrationSettings` contains only enabled state,
endpoint routing, and timeout. Credentials must be resolved from deployment
configuration or a secret provider by the integration owner. Settings export
and import therefore contain only the typed non-secret document.

## Reload contract

The following are live-reload settings after a successful save/import: dashboard
thresholds and refresh interval, warehouse/location defaults, scanner behavior,
expiry warning policy, report limits, localization, labels, and non-secret
integration routing. The settings service invalidates its cache before the next
read; Web dashboard requests and newly opened WinForms workflows read the new
effective values.

Database connection strings, Data Protection keys, host limits, logging sinks,
OTLP exporter configuration, and deployment secret-provider settings remain
host configuration and require a restart. They are intentionally not part of
the business settings document.

The initial database seed creates one valid global profile. A seeded warehouse
is selected as the initial default only when the global row is first created;
later administrator changes are never silently overwritten by reseeding.
