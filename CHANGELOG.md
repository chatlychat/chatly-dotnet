# Chatly .NET SDK — changelog

## 0.1.0 — 2026-10-12

First release.

- `ChatlyClient` covering all 55 key-accessible routes in
  `packages/sdk-spec/surface.json`: contacts, conversations, tickets, macros,
  knowledge base, search, CDP events, webhook deliveries, reports, channels,
  billing and workspace lookups (settings, labels, inboxes, a team's members
  and the agent skills table), plus `WhoAmIAsync`.
- Base URL defaults to the production host, `https://api.chatlychat.com`.
- Typed errors: `ChatlyException` with `ChatlyAuthenticationException`,
  `ChatlyPermissionException`, `ChatlyNotFoundException`,
  `ChatlyConflictException`, `ChatlyValidationException`,
  `ChatlyRateLimitException` (with `RetryAfter`), `ChatlyServerException` and
  `ChatlyTransportException`. Each carries `Status`, `Code`, `RequestId` and
  the raw `Body`. The `Code` is read from the API's nested
  `{"error":{"code":…}}` envelope.
- Retries: 3 attempts by default, exponential backoff with full jitter,
  honouring `Retry-After`. Transport failures, 429 and 5xx only — a 4xx that is
  not 429 is never retried. A retried write re-sends the same
  `Idempotency-Key`.
- Idempotency: a fresh UUID per write, and `WithIdempotencyKey(key)` to pin one
  across writes for retrying the same logical operation.
- Auto-pagination as `IAsyncEnumerable<JsonElement>` on every paged route
  (`ListAllAsync` and friends), stopping on a null cursor and on a repeated
  one.
- Webhook verification: `ChatlyWebhooks.VerifySignature`, HMAC-SHA256 over
  `<t>.<raw body>`, constant-time compare, 300-second default tolerance, and
  `false` rather than an exception on anything malformed. `ChatlyWebhooks.Sign`
  builds a genuine header for testing your own handler.
- Widget identity verification: `ChatlyIdentity.Hash(secret, externalId)`.
- Escape hatch: `RequestAsync`, `RequestAsync<T>`, `RequestPageAsync` and
  `RequestAllAsync` for the 85 key-accessible routes with no typed method.
- Zero runtime dependencies; `net8.0`.
