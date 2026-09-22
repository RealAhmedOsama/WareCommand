# WareCommand security guidance

This repository is not a security certification. The document records the
current controls and the boundaries that remain open.

## Repository rules

- Do not commit passwords, tokens, certificates, production connection
  strings, user-secrets files, local `.env` files, runtime databases, logs, or
  generated reports.
- Use environment variables, the .NET user-secrets store, or the deployment
  secret manager for connection strings and credentials.
- The container Compose baseline maps `WARECOMMAND_POSTGRES_PASSWORD` into a
  runtime secret and the Web host reads the mounted password file; it does not
  commit a connection string or credential. Replace the local Compose secret
  with the platform secret manager for production.
- Keep the PostgreSQL data and ASP.NET Core Data Protection key volumes on
  protected persistent storage. Do not expose PostgreSQL beyond the private
  application network unless an approved operational need requires it.
- Keep backups and migration reports outside the repository unless they are
  sanitized test fixtures.
- Review dependency and source changes before release; local build/test success
  is not a vulnerability assessment.

## Authentication and account controls

- ASP.NET Core Identity stores user accounts, roles, lockout state, password
  hashes, and security stamps in the checked-in Identity migration.
- Warehouse operations require an authenticated user. MVC and WinForms
  adapters pass the explicit Identity user ID through `ICurrentUser`; there is
  no `WEB_USER` or `SYSTEM` operational fallback.
- Password policy, lockout, cookie lifetime, active-account validation, reset
  tokens, antiforgery, and account-management authorization are configured in
  the host. Bootstrap is disabled by default and accepts a password only from
  an explicit secret source.
- Authentication events are auditable in `WmsAuthenticationEvents` without
  storing passwords, reset tokens, or sensitive claims. Protect this table as
  operational security data.
- Persist ASP.NET Core Data Protection keys on protected storage. Rotate the
  bootstrap secret and SMTP credentials through the deployment secret manager,
  never through committed settings.

The implementation and operator procedure are recorded in
[`docs/modernization/AUTHENTICATION.md`](docs/modernization/AUTHENTICATION.md).
Permission policies, role defaults, warehouse assignments, and the access
management boundary are recorded in
[`docs/modernization/AUTHORIZATION.md`](docs/modernization/AUTHORIZATION.md).

The repository `.gitignore` covers runtime SQLite files, logs, local settings,
coverage, test results, and generated artifacts. A pre-commit or CI secret scan
is still required for release qualification.

## Web request controls

- All MVC state-changing actions receive the global
  `AutoValidateAntiforgeryTokenAttribute`; mutation endpoints also declare the
  attribute explicitly where the action contract is easy to audit. No current
  MVC action opts out of antiforgery validation.
- Responses receive a nonce-based Content Security Policy, clickjacking and
  MIME-sniffing protection, a strict referrer policy, a restrictive
  Permissions Policy, and Cross-Origin Opener Policy. Inline handlers and
  inline styles are not used by the MVC views. The external Bootstrap Icons
  stylesheet is the only explicitly allow-listed third-party asset.
- HSTS is enabled outside Development. HTTPS redirection and forwarded-header
  processing are deployment settings. Forwarded headers are trusted only when
  `ForwardedHeaders:Enabled` is true and at least one valid
  `ForwardedHeaders:KnownProxies` address is configured; malformed or empty
  trusted-proxy configuration fails startup instead of trusting arbitrary
  client headers.
- Kestrel and form limits reject oversized requests. There are currently no
  upload, import, scanning, or API endpoints in the Web host. Any future file
  endpoint must add a narrow request limit, content-type and size validation,
  server-generated storage names, path containment checks, and malware/content
  inspection before it is enabled.
- Fixed-window rate-limit policies are configured for all requests plus
  authentication, password reset, reports, API, scanning, and import policy
  names. Current MVC login/reset/report actions use the relevant policies;
  future endpoints must opt into the matching named policy.
- Mutation actions use explicit `[Bind]` allowlists and validation attributes.
  Display-only fields such as current quantity, warehouse option lists, and
  item metadata are excluded from mutation requests. Application use cases
  perform the authoritative item, location, warehouse, and permission checks;
  hidden form fields are never treated as authorization evidence.
- Razor output remains encoded; SQL access goes through EF Core/query
  repositories; local return URLs are checked with `Url.IsLocalUrl`; and the
  current MVC surface does not use `Html.Raw` for request data.

Rate-limit values and request limits are configured under `Security` in the
host settings. Tune them per deployment and preserve the named policy
boundaries when adding expensive operations.

## Secrets and logging

- Credentials, reset tokens, Data Protection keys, connection strings, SMTP
  passwords, and bootstrap passwords belong in the .NET user-secrets store,
  environment variables, mounted secret files, or the deployment secret
  manager. They must not be placed in tracked settings, source, container
  images, issue comments, or command history.
- Authentication and operational logs use user IDs, stable identifiers, and
  error codes where possible. They must never log passwords, reset tokens,
  cookie values, connection-string passwords, security stamps, or full
  authorization headers. Audit metadata is redacted before persistence and
  must be treated as security-sensitive data.
- The production example intentionally leaves forwarded-proxy trust disabled
  until an operator supplies the real proxy address. Do not copy placeholder
  values into a live environment.

## Database and migration controls

- PostgreSQL schema changes are explicit and use checked-in EF migrations.
- Application startup refuses pending PostgreSQL migrations.
- The data importer opens SQLite read-only, creates a source backup before
  apply, parameterizes target values, writes within one serializable
  transaction, and reconciles counts, quantities, movements, and relationships.
- Never pass an unreviewed production connection string to a local verification
  script.

## Open security scope

The controls above are locally implemented and tested, and the repository threat
model is recorded in [`docs/security/THREAT_MODEL.md`](docs/security/THREAT_MODEL.md),
but neither replaces an external security review. Remaining release gates include
threat-model follow-up, deep dependency/vulnerability scanning, deployment-specific
proxy and HTTPS verification, production secret rotation, backup/restore rehearsal,
and provider-specific qualification. Treat the application as an internal
development system until those gates are implemented and evidenced.

Report suspected vulnerabilities privately through the repository's
[GitHub Security Advisory form](https://github.com/RealAhmedOsama/Warehouse-Management-System/security/advisories/new)
or directly to the repository owner. Do not publish credentials, reset tokens,
personal data, or an uncoordinated exploit in a public issue. Include the
affected revision, a minimal reproduction, impact, and any safe mitigation
when disclosure will not expose sensitive data.
