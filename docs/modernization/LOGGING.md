# Structured logging and correlation

WareCommand configures Serilog in both the MVC and WinForms composition roots.
Both hosts emit compact JSON through the same safe formatter and the same event
ID/message-template vocabulary. The MVC host keeps console logging enabled for
container collection. WinForms enables a bounded daily rolling file by default;
the MVC production example exposes the same file sink behind
`Wms:Logging:File:Enabled` so a writable volume can be selected by deployment.

## Context contract

Every request or operation carries:

- `CorrelationId`: the end-to-end trace identifier;
- `OperationId`: the specific request, job, API, or business-operation ID;
- `RequestId`: the host request identifier where one exists;
- `Operation`, `ReferenceId`, `SourceClient`/`ClientType`, `UserId`, and
  `WarehouseId` when known.

HTTP adapters use `X-Correlation-ID`, `X-Operation-ID`, and `X-Reference-ID`.
`WmsOperationContextPropagation.ToHeaders` provides the same contract for
future API clients, integration messages, and PostgreSQL-backed jobs. Nested
application operations preserve the parent correlation and request IDs while
receiving their own operation ID.

## Event conventions

`Wms.Application.Logging.WmsLogEvents` owns stable event IDs. Current host
coverage includes host lifecycle, HTTP request start/completion/failure, slow
requests, database initialization/migration, and receipt, putaway, pick, and
adjustment completion. Job and external-call IDs are reserved for the durable
job and integration adapters; those adapters must create a child
`WmsOperationContext`, log start/completion/failure, and keep retry attempts
bounded and correlated.

## Safety and operations

`WmsSafeJsonFormatter` redacts password/token/secret/authorization/cookie/API
key/connection-string/barcode/body/payload and selected personal-data
properties, removes stack traces from emitted JSON, sanitizes exception text,
and bounds strings, sequences, structures, and dictionaries. Application code
must still avoid putting secrets or raw request bodies in log templates.

`Wms:Logging:MinimumLevel`, `Wms:Logging:Overrides`, console enablement, file
path, retention count, size limit, and slow-operation threshold are startup
configuration. Keep framework overrides at `Warning` unless a short diagnostic
window is explicitly required. Retain files only on an access-controlled
volume and ship console JSON to the approved container/host collector. Do not
log credentials, tokens, connection strings, barcode payloads, or complete
personal records to investigate an incident; use the correlation/reference ID
instead.
