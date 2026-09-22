# Bulk import and export framework

## Current boundary

The reusable bulk-exchange foundation now provides:

- versioned import types and mapping profiles with source-to-target columns,
  defaults, required fields, transformations, culture, timezone, and duplicate
  policy metadata;
- a CSV parser that handles quoted commas and multiline fields, reports header
  and row shape errors, enforces bounded rows/columns, and keeps row numbers;
- culture-aware decimal/date/boolean validation with actionable field errors;
- durable source-file, execution, mapping-profile, and row-result records with
  SHA-256 source identity, actor/warehouse/correlation/idempotency metadata, and
  preview/execute/cancel status;
- a dry-run boundary that persists diagnostics but never invokes a module
  handler, and blocks execution while preview errors remain;
- a reusable UTF-8 CSV exporter that escapes formula-like values before writing
  cells and formats values using the requested culture.

## Safety and transaction policy

The framework stores at most 5,000,000 characters in the current local source
adapter and limits previews to 10,000 rows. Module handlers are intentionally
pluggable: they must route valid rows through the owning application service,
which retains authorization, validation, idempotency, and transaction rules.
The framework never bulk-inserts business entities.

## Remaining qualification gates

This is a committed framework slice, not closure. The XLSX reader/upload and
attachment-backed large-file adapter, background job enqueue/lease processing,
correction-file download, complete item/supplier/customer/PO/ASN/sales/transfer
handlers, partial-versus-all-or-nothing policy per module, localized export
surfaces, browser progress/cancel UI, formula/content security qualification,
large-file/load/cancellation/retry evidence, and production storage/retention
gates remain open. The current Excel path fails closed until an approved adapter
is configured.

The PostgreSQL identity boundary is qualified for mapping profiles
`(importType, name, version)`, execution `(user, idempotencyKey)`, and result
rows `(execution, rowNumber)`. A later profile version and the same idempotency
key for a different user are accepted; duplicates in each protected scope are
rejected. The disposable PostgreSQL 17 harness passed 54/54 on 2026-09-22
(port 55508) and cleaned its test container. Large-file, job, handler, and
production-storage gates remain open.
