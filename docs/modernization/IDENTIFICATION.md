# Global identification and GS1

The scanner boundary is `POST /api/scanning/resolve`. It requires the existing
`items.read` permission, antiforgery protection for cookie-authenticated MVC
sessions, the named `wms-scanning` rate-limit policy, and a bounded JSON body.
The endpoint returns a typed owner (`Item`, `Packaging`, `Location`, `Lot`,
`Serial`, `LicensePlate`, or `Document`) instead of guessing from a numeric
string.

## Normalization contract

- Generic scanner input is trimmed, bounded to 200 characters, upper-cased for
  lookup, and classified as `Internal` for numeric values or `Code128` for
  other printable values.
- EAN-8, UPC-A, EAN-13, and GTIN-14 validation is explicit and checks the GS1
  check digit. A numeric value is not treated as a product code merely because
  its length looks familiar.
- Original scanned input is retained on the dedicated identifier row when it
  is safe to persist. Resolution audit entries contain only kind, symbology,
  normalized length, correlation context, and the resolved entity key; they do
  not persist the raw payload.

## GS1 contract

The parser accepts parenthesized payloads and FNC1/group-separator payloads.
The default supported application identifiers are:

| AI | Meaning | Validation |
| --- | --- | --- |
| `00` | SSCC | 18 digits and check digit |
| `01` | GTIN | GTIN-14 and check digit |
| `10` | Lot | variable, maximum 20 characters |
| `17` | Expiry | `YYMMDD`, converted to `DateOnly` |
| `21` | Serial | variable, maximum 20 characters |
| `30`, `37` | Quantity | non-negative invariant decimal |

`Gs1Parser.Parse` accepts a definitions dictionary for deployments that
configure additional application identifiers. Unknown AIs and malformed,
over-length, invalid-date, and invalid-check-digit values fail closed with a
validation error.

## Persistence and migration

`WmsIdentifiers` is the globally indexed registry. It stores kind, symbology,
original/normalized values, owner key, alias metadata, active status, and a
validity window. A unique normalized-value index prevents an active identifier
from being assigned to two owners. Item barcodes, packaging barcode/GTIN,
location barcodes, lot numbers, and stock serials are backfilled by
`20260920114949_AddGlobalIdentificationSupport`; legacy fields remain readable
while existing data is reconciled. The resolver checks both the registry and
legacy values and returns an ambiguity conflict when they disagree.

New item and location management mutations synchronize the registry before the
business save. The old columns are deliberately retained until later workflow
issues can remove their compatibility surface.
