# Labels and printing

Issue #76 now has a provider-neutral label boundary. Templates are stored as
versioned declarative definitions in `WmsLabelTemplates`; they contain bounded
text rows, declared fields, typed barcode elements, dimensions, language, and
warehouse/customer/supplier scope. A template is never executable ZPL, HTML,
Razor, JavaScript, or reflection over an EF entity.

## Rendering and validation

- `WmsLabelTemplateValidator` rejects undeclared placeholders, control
  characters, printer command prefixes, scripts, `${...}` expressions, oversized
  layouts, unsupported locales, and invalid scopes.
- Field kinds validate invariant numbers and dates, product-code check digits,
  SSCC check digits, and GS1 application identifiers before a payload is
  generated. The shared `BarcodeParser`/`Gs1Parser` remains the single
  validation boundary.
- ZPL is emitted by the renderer, with `^CI28`, bounded dimensions, escaped
  field values, and typed `^BC` elements. Template authors cannot inject raw
  printer commands.
- PDF output is a small valid PDF for portable text output. Arabic labels set
  `PreferBrowserPdf`, and the same render also exposes UTF-8 HTML with the
  correct `lang="ar-SA"` and `dir="rtl"` attributes so browser PDF generation
  performs shaping with the host's configured fonts. The built-in PDF writer
  intentionally does not claim embedded Arabic-font coverage; printer/font
  qualification remains an environment/provider gate.

## Routing and jobs

`WmsPrintRouteCatalog` selects station-specific routes before warehouse routes,
then falls back to a template's default browser-PDF route when the template is
PDF. ZPL requires an explicit configured adapter route. Adapter keys are
identifiers only; addresses and credentials remain outside the application
contract.

`WmsPrintJobs` stores the rendered payload, template version/hash context,
route, source reference, copy count, actor, reprint reason, status, attempts,
errors, and timestamps. Idempotency keys prevent duplicate submission after a
successful print. A failed adapter leaves a retryable failed job; retry sends
the stored immutable payload and does not re-run an inventory transaction.
Reprints require a reason and all submission, retry, and failure transitions
write immutable audit records.

## Host boundary

The current host exposes guarded `/api/labels` endpoints for template version
save/list/activation/rollback, preview, print, and retry. Template management
and preview require `settings.manage`; active-template printing uses the
authenticated `inventory.read` boundary. No printer vendor SDK or live printer
endpoint is bundled. Provider/client qualification, station administration,
and production font/printer validation remain deployment evidence gates.
