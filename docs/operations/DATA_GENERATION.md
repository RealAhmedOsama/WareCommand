# Deterministic data-generation boundary

The Application data-generation contracts define versioned, deterministic
scenario profiles for development, demos, integration tests, edge cases, and
explicit performance qualification. A plan contains a seed fingerprint,
environment/locale, estimated entity counts, and a small preview; it does not
silently reset or write a runtime database.

## Profiles

| Profile | Intended use |
| --- | --- |
| `minimal-development` | Small local feature development dataset |
| `full-demo` | Non-production demonstrations with multilingual/edge data |
| `edge-cases` | Boundary, shortage, expiry, and exception tests |
| `integration-test` | Repeatable cross-module journey setup |
| `large-performance` | Explicit load/capacity qualification only |

The same profile/version/seed/locale produces the same plan and preview. Arabic
and English labels are generated as data values so UI-independent journeys can
exercise both languages. The scale is bounded to 1–100 and the performance
profile must be requested explicitly.

## Safety boundary

Plans reject `Production` and `Staging` environments. A destructive reset also
requires the exact `RESET-NON-PRODUCTION` confirmation and must be implemented
by an explicitly non-production host/tool. No default password or runtime DB
file belongs in source control. Generated records must still enter WMS through
normal commands/seed adapters or be independently reconciled; a generator must
not bypass invariants merely to write faster.

The current slice provides the deterministic profile/plan contract and tests.
Database writers, bounded bulk insertion, reconciliation output, Playwright
fixture integration, and large-volume provider qualification remain follow-up
gates.
