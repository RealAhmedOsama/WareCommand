# Secure attachments and evidence

The attachment boundary stores metadata in PostgreSQL/SQLite and file bytes through `IAttachmentStorage`. No uploaded bytes, client path, or client MIME value is persisted as trusted storage identity.

## Current contract

- Supported reference types are `receipt`, `damage`, `quality-inspection`, `inbound-exception`, `outbound-exception`, `return`, `approval`, `shipment`, and `audit-evidence`.
- The reference authorizer resolves the referenced row and warehouse before upload, list, download, evidence marking, or deletion. The attachment permission is checked in addition to the underlying module permission, so a guessed attachment id is not sufficient for access.
- Storage keys are generated as `warehouse/reference-type/random-id`; client file names are display metadata only. Local storage writes a random temporary file, flushes it, and atomically moves it beneath the configured root. Download never accepts a filesystem path.
- Uploads are bounded and staged outside the database. Allowed types are JPEG, PNG, GIF, WebP, PDF, plain text, and CSV. The extension, normalized content type, leading signature, actual byte count, filename, per-reference count, and SHA-256 duplicate key are checked server-side.
- Suspicious or failed scans are retained in `Quarantined` state and cannot be downloaded. If `RequireAntivirusScan` is enabled while no scanner adapter is registered, uploads fail closed.
- Immutable evidence cannot be replaced or deleted. Ordinary deletion is a soft `PendingDeletion` transition; physical purge belongs to the retention/backup work in issue #84, which preserves the audit trail and avoids a storage/metadata split during a protected command.
- Upload, download, evidence-mark, and deletion-request actions are recorded through the existing append-only audit writer.

## Configuration

Development defaults are local and bounded:

```json
"Wms": {
  "Attachments": {
    "Provider": "Local",
    "RootPath": "",
    "MaximumFileSizeBytes": 10000000,
    "MaximumAttachmentsPerReference": 20,
    "MaximumRequestBodyBytes": 12000000,
    "RequireAntivirusScan": false
  }
}
```

The production example selects `Provider: External` and `RequireAntivirusScan: true` deliberately. Startup fails until the deployment registers an object-storage implementation of `IAttachmentStorage` and an antivirus/quarantine implementation of `IAttachmentScanner`; the current repository does not pretend that a local filesystem or no-op scanner is production object storage/AV evidence.

The external adapter must provide private object access, short-lived authorized reads, retention/backup participation, and a restore procedure. It must not be placed in Domain or Application, and it must not expose a public bucket or accept a client-supplied object key.

## API surface

- `GET /api/attachments?referenceType=&referenceId=&warehouseId=` lists authorized metadata.
- `POST /api/attachments` accepts a bounded multipart upload and requires antiforgery plus attachment/module permissions.
- `GET /api/attachments/{id}/download?preview=` streams an authorized object with `nosniff`, private no-store caching, safe `Content-Disposition`, and range support.
- `POST /api/attachments/{id}/evidence` marks immutable evidence.
- `DELETE /api/attachments/{id}` requests retention-safe deletion; it does not remove the object synchronously.

Mobile camera capture, upload progress, attachment lists/previews embedded in every affected workflow, deep-link notifications, Arabic translations, and RTL/LTR browser evidence remain UI/product-wiring work in #83, #87, and the affected module issues. This API slice is not a claim that those screens are complete.

## Qualification evidence

`AttachmentServiceTests` covers traversal and MIME/signature mismatch, IDOR/reference authorization, duplicate and count limits, storage failure without metadata, quarantine, immutable evidence, retention, and safe download behavior. `LocalAttachmentStorageTests` covers generated-key storage and traversal rejection.
