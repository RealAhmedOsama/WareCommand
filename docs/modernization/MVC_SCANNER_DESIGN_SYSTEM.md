# MVC scanner-first design system

This document records the first implementation slice for issue #71. The
existing ASP.NET Core MVC views remain the operational client; the design
system is an additive shell that can be adopted by each workflow as its page
is rebuilt.

## Current slice

- `_Layout.cshtml` exposes a skip link, an authenticated command bar, a
  permission-filtered quick scan/search form, and links to existing receiving,
  picking, and inventory endpoints.
- `site.css` defines shared shell tokens, touch targets, command-bar states,
  page headers, cards, statistics, statuses, empty states, loading skeletons,
  focus treatment, responsive breakpoints, and reduced-motion behavior.
- `site.js` supports `/` and `Ctrl+K`/`Cmd+K` focus shortcuts, Escape clearing,
  and opt-in persistent scan focus on scanner routes. It does not interpret
  inventory commands or bypass server validation.
- The quick scan form submits `searchTerm` to the existing authorized
  `InventoryController.Index` action. Authorization, warehouse scoping,
  filtering, and error handling therefore remain server-owned.

## Interaction contract

The shell must remain usable with a keyboard, a wedge scanner, or touch:

1. A scanner can type into the focused quick-scan field and submit with Enter.
2. `/` and `Ctrl+K`/`Cmd+K` focus the field when the operator is not already
   typing in another control.
3. Scanner routes opt into initial focus through the body
   `data-wms-persistent-scan="true"` marker; focus is not stolen if the browser
   already focused another element.
4. Escape clears the quick-scan field without changing server state.
5. All new shell controls meet the 44px minimum touch target and expose an
   accessible name. The skip link and visible focus ring support keyboard-only
   navigation.

## Directionality and content

English (`en-US`) remains LTR and Arabic (`ar-SA`) remains RTL. New shell
strings are present in both `WmsSharedResource.resx` and
`WmsSharedResource.ar-SA.resx`. Layout direction comes from the selected
culture, while technical identifiers such as barcodes, SKUs, locations, and
correlation IDs continue to use isolated LTR presentation where a page renders
them.

## Remaining issue #71 gates

This is a progress slice, not closure. The following remain before issue #71
can be closed:

- migrate every dashboard, master-data, inventory, inbound, outbound, work,
  counting, replenishment, transfer, report, user, settings, and exception
  page to the shared primitives;
- replace the remaining ad-hoc page CSS and add the operator/management
  navigation model, warehouse context, notifications, recent-work context,
  and breadcrumbs consistently;
- provide real workflow-specific persistent scan fields and verify keyboard,
  wedge-scanner, touch, loading, empty, validation, and server-error journeys;
- execute Playwright visual/functional checks at mobile, tablet, and desktop
  breakpoints, including Arabic RTL and English LTR screenshots;
- qualify the full client/server journeys against permission-denied,
  warehouse-scope, localization, and error contracts.

The approved untracked `Front-End/` package is a separate landing-page asset
and is intentionally not part of this MVC shell change.
