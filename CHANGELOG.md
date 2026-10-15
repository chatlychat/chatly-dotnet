# Chatly .NET SDK — changelog

## Unreleased

- **`ChatlyWebhooks.VerifyChatlySignature(ChatlySignatureInput)`** — one
  verifier for all eight signed surfaces (`unified_v2`, `agent_delivery`,
  `event_webhook`, `ai_tool_call`, `canvas_step`, `customer_card`,
  `workflow_webhook`, `workflow_webhook_legacy`), ported from sdk-node's
  `verifyChatlySignature`. Returns a `ChatlySignatureResult` with `Ok`,
  `Surface`, `Base`, `TimestampMs`, `SecretIndex`, and on refusal one of
  eleven stable `Reason` strings plus a printable `Message`, instead of a
  bare `false`. Supports secret arrays for rotation, multi-`v1` headers, the
  unified v2 header with `Prefer` / `Require` / `Ignore`, and the `b=` base
  marker. Never throws. New public types: `ChatlySignatureInput`,
  `ChatlySignatureResult`, `ChatlySignatureSurfaces`, `ChatlySignatureFailure`,
  `ChatlySignatureBase`, `ChatlyV2Policy`; new constants
  `ChatlyWebhooks.ChatlySignatureToleranceSeconds` (300) and
  `ChatlyWebhooks.ChatlyV2SignatureHeader`. `ChatlyWebhooks` is now `partial`;
  `VerifySignature` and `Sign` are unchanged.
- Proven against the shared cross-SDK corpus
  (`tests/Fixtures/signature_vectors.json`, byte-identical in every SDK): every
  vector, every expected field. Where .NET defaults would have disagreed with
  the reference — `Regex` `\d` matching non-ASCII digits, `Trim()`'s
  whitespace set — the checks are written by hand. One deliberate divergence:
  a timestamp too large for a `long` (or a seconds value whose ×1000
  overflows) is `malformed_timestamp`, where Node reads it as a far-future
  `timestamp_outside_tolerance`; and a `NaN` tolerance refuses rather than
  accepts.

- **`Bots.RotateExternalSecretAsync` takes an optional `activateAt`** — new
  overloads `(botId, DateTimeOffset? activateAt, ct)` and
  `(botId, string? activateAt, ct)`. With it, the new secret is issued now and
  the old one keeps signing deliveries until that instant (at most 168h ahead,
  enforced by the server); the body is exactly `{"activateAt":"…"}`, a
  `DateTimeOffset` converted to UTC `yyyy-MM-ddTHH:mm:ss.fffZ`. The response
  now carries `activatesAt` (`null` = already signing). Existing calls —
  `(botId)`, `(botId, ct)` and `(botId, default)` — compile unchanged and
  still send `{}`, the hard cut they always were.
- **The whole manifest is covered: 162 methods, up from 55.** The 104 added
  are `ai`, `bots`, `bot_routes`, `brands`, `canvas_steps`,
  `customer_cards`, `customer_card_endpoints`, `docs_sources`,
  `email_templates`, `knowledge_gaps` and `triggers` in full, plus the rest of
  `billing`, `channels`, `contacts`, `conversations`, `macros`, `reports`,
  `tickets`, `webhooks` and `workspace`. New properties on `ChatlyClient`:
  `Ai`, `Bots`, `BotRoutes`, `Brands`, `CanvasSteps`, `CustomerCards`,
  `CustomerCardEndpoints`, `DocsSources`, `EmailTemplates`, `KnowledgeGaps`,
  `Triggers`.
- Every one of the 104 was verified against its handler before it was written
  — the `@AllowApiKey()` decorator, the `@ZodResponse` schema and the
  path — rather than taken from the manifest. The manifest agreed on every
  row this time, including the three `paged` flags added for this work.
- **Auto-pagination is on all thirteen paged routes now**, up from ten:
  `Bots.ListAllAsync`, `Bots.DeliveriesAllAsync` and
  `Triggers.ListAllAsync` joined. All three end in the API's
  `buildPaginatedResponse` and were unflagged in the manifest until
  2026-10-14.
- **`Billing.InvoicesAsync` deliberately has no walker.** It really is
  cursor-paginated, but its envelope key is `data` rather than `items`, so a
  `Page<T>` over it would decode an empty page and hand it back with a 200 —
  a wrong answer rather than a missing one, which is the exact failure the
  shape column in `surface.lock.json` exists to prevent. Same reasoning for
  `Conversations.LinkedAsync` and `Tickets.TimeAsync`, which have an `items`
  key and no cursor at all.
- `Conversations.UnreadCountsAsync` ships with a warning rather than being
  omitted: it answers 200, but the counts are the CALLER's, and an API key's
  membership user id is the key's own — so the figures describe a principal
  that never opens a conversation. `Conversations.UnattendedAsync`'s `total`
  is the real backlog number.
- Documented the three routes that need a scope they do not declare.
  `Conversations.OutcomesAsync`, `RecordOutcomeAsync` and `UpdateBlocksAsync`
  resolve the conversation first, so a key without `conversations:read`
  gets a **404 on every conversation**, not a 403.
- Three routes the manifest caught up with after the first 104, each verified
  against its handler here rather than taken from the manifest row:
  - `Webhooks.DeleteAsync` — `DELETE /v1/webhooks/{id}`, **200 with
    `{ ok: true }`** rather than a 204, which is exactly the row a defaulted
    shape column would have called `empty`. This package previously told
    callers to work around its absence by pointing the endpoint at a dead
    URL; that is not a deletion — the subscription stays alive and the
    delivery log fills with failures. The route had refused a key while the
    create accepted one, so an integration could add a delivery target and
    never remove it. Its handler declares no idempotency posture, so it
    inherits the controller's `@RequireIdempotencyKey()` and is a 428 in
    production without the header; this SDK mints one per write.
  - `ChatlyClient.MeAsync` — `GET /v1/me`, a
    `z.discriminatedUnion('principal', …)` of two objects, so `object` on
    either branch. From a key it returns the same object as `WhoAmIAsync`,
    and the reason to have both is the gate: this route carries **no
    `@RequirePermission` at all** where `whoami` wants `workspace:read`,
    which makes it the liveness check — a key with no useful scope still gets
    a 200, and that answer is true.
  - `Workspace.MineAsync` — `GET /v1/workspaces/me`,
    `z.array(workspaceListItemSchema)`, so a **bare array** even for a key,
    where it holds exactly one entry: the cheapest way to confirm which
    tenant a credential points at. `@AllowWhenSuspended`, so it answers when
    the active workspace is suspended. Its `role` is the synthetic
    `restricted` minted for every key, not a stored membership role.
- Shape distribution is 33 `array` / 13 `page` / 115 `object` / 1 `empty`,
  asserted, so a column of 162 `object`s cannot pass.
- Corrected two stale claims in the doc comments. `GET /v1/api-keys/whoami`
  was described as the only key-accessible route on the api-keys controller
  and `GET /v1/api-keys/scopes` is a second one; `ChannelsResource` was
  described as read-only because "channel configuration needs a scope no API
  key can hold", and create, update and secret rotation are all open — only
  the delete is not.
- `tests/Checks/SurfaceTable.cs` splits the 162-row surface table out of
  `SurfaceChecks.cs`, which together went past CONTRACT.md §9's 650-line
  ceiling. Same partial class, so `SurfaceChecks.Entries` still resolves.

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
- Auto-pagination as `IAsyncEnumerable<JsonElement>` on **all ten** paged
  routes (`ListAllAsync` and friends), stopping on a null cursor and on a
  repeated one. This read "the seven paged routes" and shipped with seven:
  `Contacts.JourneyAllAsync` existed but was left out of the suite's walker
  map, and `Conversations.UnattendedAllAsync` and `Search.QueryAllAsync` did
  not exist at all, so two routes the manifest marks `paged` had no walker —
  and `UnattendedAsync` was documented as "not cursor-paginated", which it is.
  `GET /v1/macros` is deliberately still not among them: it is
  pagination-exempt and answers a bare JSON array capped at 200 rows by usage
  rank, so `Macros.ListAsync` returns that array and there is no
  `Macros.ListAllAsync`.
- `surface.lock.json` records the top-level response shape this SDK's decoder
  expects per route — `array`, `page`, `object` or `empty` — declared by hand
  in `tests/Checks/SurfaceChecks.cs` and diffed against
  `packages/sdk-spec/surface.json` by
  `scripts/check-sdk-surface-parity.mjs`. Thirteen bare-array routes had been
  declared `object` because `ResponseShape.Object` was the constructor
  default; the argument is required now.
- `Billing.UsageAsync(eventType, from?, to?)` takes the event type as a
  required argument, with `ChatlyUsageEventTypes` for the four values the API
  accepts. The endpoint validates it against a closed list and 400s without
  one, so an optional argument could never have worked.
- `Search` sends a plain repeated `kinds` parameter, which is the spelling the
  API's own schema keys on.
- Webhook verification: `ChatlyWebhooks.VerifySignature`, HMAC-SHA256 over
  `<t>.<raw body>`, constant-time compare, 300-second default tolerance, and
  `false` rather than an exception on anything malformed. `ChatlyWebhooks.Sign`
  builds a genuine header for testing your own handler.
- Widget identity verification: `ChatlyIdentity.Hash(secret, externalId)`.
- Escape hatch: `RequestAsync`, `RequestAsync<T>`, `RequestPageAsync` and
  `RequestAllAsync` for the 85 key-accessible routes with no typed method.
- Zero runtime dependencies; `net8.0`.
