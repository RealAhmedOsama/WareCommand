# Webhook delivery transport

Webhook delivery is disabled unless `Wms:Integrations:Webhooks:Enabled` is explicitly set to `true`. When disabled, the registered transport reports `Disabled` and never reports a successful send. Enabling delivery requires at least one exact destination host, a scheme, and an allowed port; invalid enabled configuration fails host startup.

Example deployment configuration:

```json
{
  "Wms": {
    "Integrations": {
      "Webhooks": {
        "Enabled": true,
        "AllowedHosts": ["events.partner.example"],
        "AllowedSchemes": ["https"],
        "AllowedPorts": [443],
        "RequestTimeoutSeconds": 10,
        "ConnectTimeoutSeconds": 5,
        "MaximumRequestBodyBytes": 1000000,
        "MaximumResponseBytes": 8000
      }
    }
  }
}
```

Allowed hosts are exact names; wildcard entries are rejected. The transport also checks every resolved address and opens the socket directly to a checked address, preventing a DNS answer change between validation and connection from redirecting a request to a private target. Loopback, private, link-local, multicast, documentation, reserved, and metadata address ranges are rejected. Redirects and proxies are disabled. TLS certificate validation stays enabled.

Only HTTPS is allowed by default. A loopback HTTP receiver can be enabled in a `Development` environment by explicitly setting `AllowLocalHttpForDevelopment`, adding `http` to `AllowedSchemes`, allowlisting `localhost` or a loopback IP, and allowlisting the receiver's port. When `http` is allowed, every configured host must be loopback. Startup validation rejects that exception outside `Development`.

Request bodies are transmitted as the exact UTF-8 bytes of the stored payload JSON. Headers include the HMAC signature and timestamp plus stable `X-WareCommand-Event-Id`, `X-WareCommand-Delivery-Id`, `X-WareCommand-Event-Type`, and `X-WareCommand-Event-Version` identifiers. The delivery ID is the persisted webhook-delivery row key and remains stable across retries. Receivers should deduplicate on that identifier; HTTP delivery is at least once, not exactly once.

The request timeout is capped at 60 seconds, the connect timeout at 30 seconds, request bodies at 1 MB, and response reads at 8 KB. 2xx responses are delivered; 408, 425, 429, connection failures, timeouts, and 5xx responses are retryable. Other 4xx and redirects are permanent failures. `Retry-After` is honored when later than exponential backoff and is capped to one hour. Default `HttpClient` request loggers and URL spans are disabled for this client. Its bounded delivery span includes only subscription/event/delivery identifiers, event type, status, and a fixed error category; secrets, destination paths, signatures, payloads, and response bodies are not recorded. Response bodies are bounded while reading and discarded rather than persisted.

`GET /api/integrations/capabilities` reports webhook `Disabled`, `Configured`, or `Verified` state without returning endpoint secrets. A configured channel becomes verified after an active subscription has at least one successful delivery. This is local transport evidence; partner-side acceptance remains a separate deployment gate.
