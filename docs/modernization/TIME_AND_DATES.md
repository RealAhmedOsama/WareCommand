# Time, time zones, and business dates

Issue #23 establishes one time contract across the Web host, WinForms host,
domain, reports, jobs, and audit paths.

## Canonical storage

- Operational instants are UTC. `IClock.UtcNow` is the injectable application
  boundary; `SystemClock` is the only production adapter that reads the host
  clock directly.
- PostgreSQL `timestamp with time zone` columns hold UTC instants. New domain
  objects are stamped at the persistence boundary with the injected clock, and
  movement events receive their UTC timestamp from `StockMovementService`.
- Manufacture and expiry values are business dates, not instants. They remain
  `timestamp without time zone` columns for compatibility, but are normalized
  to midnight with `DateTimeKind.Unspecified`. They must not be converted to a
  UTC timestamp.

## Time-zone contract

`WmsLocalizationSettings.TimeZone` and warehouse overrides use IANA identifiers
such as `UTC`, `Africa/Cairo`, and `America/New_York`. Known Windows aliases are
accepted only as legacy input and normalized when they are resolved. User
profiles retain a separate display preference; localization changes presentation
only and never changes persisted canonical values.

`WmsBusinessTime` is the shared conversion boundary. It converts a UTC instant
to a warehouse-local `DateOnly`, and converts local dates back to UTC range
boundaries. DST gaps are moved to the first valid local minute; an ambiguous
boundary uses the earliest UTC instant. Both choices are deterministic and are
covered by tests.

## Date ranges and expiry

Report inputs are inclusive business dates. Persistence queries use the
half-open UTC interval `[fromInclusiveUtc, toExclusiveUtc)`, so a record exactly
at the next day boundary belongs to the next report and cannot be counted twice.
The same conversion handles 23-hour and 25-hour DST days.

Lot expiry and FEFO decisions compare `DateOnly` values against the effective
warehouse business date. An expiry on today is not expired; it becomes expired
on the following business date. Warning dates use the configured warning-day
cutoff inclusively.

## Scheduled jobs and display

Every recurring Hangfire cron expression is explicitly declared as UTC in
`WmsJobCatalog` and registered with `TimeZoneInfo.Utc`. Handlers calculate
warehouse-local business dates separately when a rule is date-based; they never
depend on the server's local time zone.

Views and desktop controls convert UTC instants to the configured display or
warehouse time zone at the presentation boundary. CSV/report data retains the
canonical UTC timestamp unless a consumer explicitly formats it for display.

Qualification is covered by fake-clock, IANA/legacy normalization, expiry,
date-boundary, and New York DST transition tests. Live provider qualification
and production deployment remain separate release gates.
