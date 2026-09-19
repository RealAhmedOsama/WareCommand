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

The repository `.gitignore` covers runtime SQLite files, logs, local settings,
coverage, test results, and generated artifacts. A pre-commit or CI secret scan
is still required for release qualification.

## Database and migration controls

- PostgreSQL schema changes are explicit and use checked-in EF migrations.
- Application startup refuses pending PostgreSQL migrations.
- The data importer opens SQLite read-only, creates a source backup before
  apply, parameterizes target values, writes within one serializable
  transaction, and reconciles counts, quantities, movements, and relationships.
- Never pass an unreviewed production connection string to a local verification
  script.

## Open security scope

Tenant isolation, abuse controls, security headers, threat modeling, deep
dependency scanning, and production secret rotation are not closed by the
current local qualification. Treat the application as an internal development
system until those gates are implemented and evidenced.

Report suspected vulnerabilities privately to the repository owner rather than
publishing credentials or exploit details in a public issue.
