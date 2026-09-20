# Localization and directionality

WareCommand supports the `en-US` and `ar-SA` cultures. English is the safe
default and Arabic uses right-to-left layout with the Cairo font.

## Culture precedence

For each request, the ASP.NET Core localization pipeline evaluates cultures in
this order:

1. `culture`/`ui-culture` query-string values for a temporary override.
2. The authenticated user's persisted `Locale` preference.
3. The ASP.NET Core request-culture cookie.
4. The typed global settings `Localization.Locale` value.
5. `en-US`.

The account language selector writes the cookie for anonymous users and writes
both the cookie and the Identity user's `Locale` for authenticated users. The
settings screen controls the optional system default; warehouse overrides are
validated as part of the typed settings contract.

## UI rules

- Shared resource keys live beside `WmsSharedResource` in
  `Wms.Application/Localization` and must exist in both `.resx` files.
- Views set document `lang` and `dir`, choose Bootstrap LTR/RTL assets, and
  use Cairo for Arabic controls.
- Operational labels, validation summaries, controller result messages,
  account flows, settings, audit, reports, and navigation use the shared
  resources.
- SKUs, barcodes, location codes, order numbers, correlation IDs, URLs, and
  other technical identifiers use isolated LTR styling inside either layout.
- Canonical values remain culture-neutral in storage. Dates, quantities, and
  currency-like values are formatted at the UI/report boundary using the
  selected culture; identifiers are never localized.
- The WinForms host loads the same resource assembly, applies the persisted
  system/user culture after login, and applies RTL layout to forms, dialogs,
  controls, and grid headers.

## Completeness and qualification

`Wms.Application.Tests/Localization/WmsLocalizationResourceTests.cs` verifies
supported cultures, directionality, and resource-key parity. The ASP tests in
`Wms.ASP.Tests/LocalizationFlowTests.cs` verify Arabic RTL/English LTR document
metadata, translated login content, Cairo asset wiring, and anonymous locale
cookie persistence. Production provider qualification, browser screenshot
capture, and deployment remain release-environment gates; this change does not
perform those operations.
