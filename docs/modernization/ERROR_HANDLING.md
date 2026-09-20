# Error handling and cancellation

WareCommand uses one error vocabulary across application results and the Web
boundary. Expected failures are represented by `ResultError` values rather than
provider or exception-message parsing. Each error has a stable code, a typed
category, a safe user message, optional field errors, and a retryable flag.

The supported categories are validation, not-found, conflict, unauthorized,
forbidden, concurrency, business-rule, dependency, unexpected, and cancelled.
Use the `WmsErrors` factory methods when creating new expected failures. The
legacy `Result.Failure(string)` overload remains only as a compatibility bridge
and records a typed business-rule error.

## Web boundary

`WmsExceptionHandler` is the single ASP.NET Core exception boundary. It:

- preserves request cancellation instead of converting it into a generic error;
- maps argument, authorization, not-found, EF concurrency, unique-constraint,
  and other persistence failures to safe status codes and stable error codes;
- logs an unhandled exception once with the request correlation/error reference;
- returns RFC-style `application/problem+json` for `/api` or JSON-accepting
  requests without exception text, SQL, provider details, or internal IDs; and
- re-executes normal MVC failures through the friendly error page, which shows
  only the support reference.

MVC controllers should pass `HttpContext.RequestAborted` to application and
infrastructure calls and should not add broad exception-catching boilerplate.
Expected `Result` failures remain user-actionable; unexpected failures belong to
the global boundary.

## Retry and background work

Validation, authorization, not-found, business-rule, and unique-conflict errors
are non-retryable. Concurrency and transient dependency errors are retryable
only when the owning operation is idempotent and the worker has a bounded
backoff policy. The durable background-job runner preserves cancellation,
classifies the final failure, and records the stable job/correlation reference;
it does not retry cancellation or programming defects. Retryable handlers must
remain idempotent because Hangfire can redeliver work after a restart.

## UI contract

Model validation remains field-level through MVC validation tags and the
validation summary. Application failures use the shared alert surface and safe
`ResultError.Message` values. Field-level errors must use `FieldErrors` and be
copied into `ModelState`; support references are shown only for unexpected
failures.
