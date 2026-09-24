# Accessibility, responsive quality, and operator usability

Issue #87 is being delivered in bounded evidence waves. The acceptance
contract below applies to Web screens and to the scanner-first journeys that
remain in the optional WinForms workstation.

## Baseline contract

- Every document has a language and direction (`en-US`/LTR or `ar-SA`/RTL), a
  viewport declaration, a skip link, and a focusable `main` landmark.
- Every interactive control has visible text, an accessible label, or an
  explicit relationship to a visible label. Decorative icons use
  `aria-hidden="true"`.
- Keyboard focus is visible with a high-contrast outline. The skip link moves
  focus to `#main-content`; validation and dialog flows must restore focus to
  the first invalid or next scanner field.
- Tables use responsive wrappers, captions, and scoped headers. Statuses must
  include text or a semantic attribute; color is never the only signal.
- Primary controls and scanner actions use at least the shared 2.75rem touch
  target. Long text, Arabic translations, identifiers, and numbers may wrap or
  scroll inside their bounded region without causing page-level horizontal
  overflow.
- Reduced-motion users do not receive skeleton or shell transitions. Print
  output hides navigation and connectivity controls while retaining the
  report content.

## Current automated evidence

`Wms.ASP.Tests/AccessibilityFlowTests.cs` checks the rendered anonymous shell
in English and Arabic, landmark/skip-link/viewport semantics, representative
dashboard/report semantics for an authorized user, and the shared CSS
focus/touch/reduced-motion contract. Existing `LocalizationFlowTests`,
`MvcShellFlowTests`, `PwaFlowTests`, and module flow tests provide additional
RTL/LTR, scanner-shell, PWA, and responsive markup evidence.

`Wms.ASP.Tests/PostgreSqlBrowserJourneyTests.cs` now runs Chromium against the
real MVC HTTP origin and an isolated PostgreSQL schema. Its current responsive
matrix covers 390x844 phone, 768x1024 tablet, and 1366x768 laptop widths in
English LTR and Arabic RTL, with route semantics, page overflow, receiving
scanner focus, online/offline behavior, and durable mutation assertions. This
is focused browser evidence, not full accessibility or visual-regression
qualification. Axe checks and screenshot baselines remain open for:

| Viewport | English | Arabic |
| --- | --- | --- |
| 390x844 phone | login, inventory, receiving, picking | login, inventory, receiving, picking |
| 768x1024 tablet | dashboard, reports, settings | dashboard, reports, settings |
| 1366x768 laptop | admin, reports, labels | admin, reports, labels |
| 1920x1080 kiosk/high DPI | receiving, picking, print preview | receiving, picking, print preview |

## Operator-friction measures

The browser/device wave records scan count, focus losses, clicks/taps to
complete, accidental submissions, validation recovery, and time-to-retry for
receiving, putaway, picking, inventory adjustment, and label preview. A
failure is a product defect unless a narrow exception is documented with the
affected device, locale, reason, workaround, and owner.

## Known remaining gates

- Keyboard-only traversal and scanner simulation across the full matrix above;
  the current browser slice exercises keyboard scanning at 390x844 only.
- Axe-based automated browser checks and visual baselines in CI.
- Manual screen-reader spot checks, high-DPI/zoom checks, long-translation and
  large-dataset checks, and print-preview validation.
- Focus restoration after every module validation/dialog/drawer flow and
  complete RTL/LTR acceptance for the later administration, labels,
  attachments, notifications, and integration surfaces.
