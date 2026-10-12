# Chatly for .NET

Server-side .NET client for the [Chatly](https://chatlychat.com) API — live
chat, shared inbox, tickets, help centre and AI agents.

It authenticates with a **workspace API key**, which is a bearer credential
for your entire workspace. That makes it a backend library: put it in your
API, your worker or your console app, and never in a desktop app, a mobile app
or anything that reaches a browser. (For the browser there is a separate
widget SDK, `@chatlychat/sdk-js`, which uses a public key instead.)

**No runtime dependencies.** `HttpClient`, `System.Text.Json` and
`System.Security.Cryptography`, all in the box. Installing this does not drag
our dependency tree into your build.

```bash
dotnet add package Chatly
```

Targets `net8.0`, so it runs on .NET 8, 9 and 10.

---

## Quick start

```csharp
using Chatly;

var chatly = new ChatlyClient(Environment.GetEnvironmentVariable("CHATLY_API_KEY")!);

// Always call this first — see below.
var me = await chatly.WhoAmIAsync();
Console.WriteLine(me.GetStringOrNull("workspaceId"));

var contact = await chatly.Contacts.UpsertAsync(new ContactInput
{
    ExternalId = "user_42",
    Email = "dana@example.com",
    Name = "Dana Example",
    Attributes = new Dictionary<string, object?> { ["plan"] = "pro" },
});

await foreach (var conversation in chatly.Conversations.ListAllAsync())
{
    Console.WriteLine(conversation.GetStringOrNull("id"));
}
```

Create a key under **Workspace → Developer → API keys**. The token is shown
once and stored only as a hash — if you lose it, revoke it and mint another.
Tick only the scopes your integration needs.

### Make `WhoAmIAsync` your first call

```csharp
var me = await chatly.WhoAmIAsync();
// { "workspaceId": "ws_…", "scopes": ["contacts:read", "messages:write", …] }
```

It returns the workspace the key belongs to and the scopes it actually holds,
so a mis-scoped key fails loudly during integration instead of surfacing later
as a puzzling 403 from an unrelated endpoint. It is also the only `api-keys`
route a key may call: minting or listing keys with a key would let a leaked
read-only key escalate itself.

---

## What the methods return

Every method returns the API's JSON as a `System.Text.Json.JsonElement` (or a
`Page<JsonElement>`), not a hand-written response class.

That is a deliberate trade. A response model written from outside the API is a
model that can quietly disagree with it, and a test suite built on invented
fixtures then agrees with the model rather than with the server — a bug this
product has already shipped once. So the SDK hands you what arrived and lets
you bind it to a type you control:

```csharp
public sealed record Contact(string Id, string? Email, string? Name);

var page = await chatly.Contacts.ListAsync();
foreach (var element in page.Items)
{
    Contact? contact = element.As<Contact>();   // camelCase-tolerant
}

// or read one field without declaring anything
string? email = page.Items[0].GetStringOrNull("email");
```

`As<T>()` and `GetStringOrNull()` are extension methods on `JsonElement` from
this package.

Request bodies work the other way round: the typed inputs (`ContactInput`,
`MessageInput`) are conveniences, and **every write also accepts a plain
object**, so you are never blocked by a field the SDK has not modelled:

```csharp
await chatly.Conversations.SendAsync(conversationId, new
{
    body = "Thanks — refund issued.",
    kind = "text",
    clientRef = "turn-42",
});
```

PascalCase members are serialized as camelCase. Dictionary keys are sent
exactly as written. Null members are omitted rather than sent as JSON null, so
an upsert only touches the fields you set.

---

## Every method

55 methods, one per key-accessible route we hand-wrote. Each takes an optional
`CancellationToken` last.

### Credential

| Method | Route |
| --- | --- |
| `chatly.WhoAmIAsync()` | `GET /v1/api-keys/whoami` |

### Contacts — `chatly.Contacts`

| Method | Route |
| --- | --- |
| `ListAsync(options?)` | `GET /v1/contacts` |
| `ListAllAsync(options?)` | auto-paginates `GET /v1/contacts` |
| `GetAsync(contactId)` | `GET /v1/contacts/{id}` |
| `UpsertAsync(contact)` | `POST /v1/contacts` |
| `BulkUpsertAsync(contacts)` | `POST /v1/contacts/bulk` |
| `MergeAsync(contactId, mergedContactId)` | `POST /v1/contacts/{id}/merge` |
| `AddTagsAsync(contactId, tag)` | `POST /v1/contacts/{id}/tags` |
| `RemoveTagAsync(contactId, tag)` | `DELETE /v1/contacts/{id}/tags/{tag}` |
| `UnsubscribeAsync(contactId, unsubscribed = true, reason?)` | `POST /v1/contacts/{id}/unsubscribe` |
| `JourneyAsync(contactId)` | `GET /v1/contacts/{id}/journey` |

`UpsertAsync` keys on `ExternalId`, so calling it twice with the same one
updates a single record. `BulkUpsertAsync` takes up to 5000 rows per call and
reports per-row outcomes instead of failing the batch on one bad row.

### Conversations — `chatly.Conversations`

| Method | Route |
| --- | --- |
| `ListAsync(options?)` | `GET /v1/conversations` |
| `ListAllAsync(options?)` | auto-paginates `GET /v1/conversations` |
| `GetAsync(conversationId)` | `GET /v1/conversations/{id}` |
| `MessagesAsync(conversationId, options?)` | `GET /v1/conversations/{id}/messages` |
| `MessagesAllAsync(conversationId, options?)` | auto-paginates the above |
| `SendAsync(conversationId, message)` | `POST /v1/conversations/{id}/messages` |
| `UnattendedAsync()` | `GET /v1/conversations/unattended` |
| `AgentHandlingAsync(conversationId)` | `GET /v1/conversations/{id}/agent-handling` |
| `SetAgentHandlingAsync(conversationId, state)` | `PUT /v1/conversations/{id}/agent-handling` |
| `GeneratedRepliesAsync(conversationId)` | `GET /v1/conversations/{id}/generated-replies` |
| `GenerateReplyAsync(conversationId, request?)` | `POST /v1/conversations/{id}/generated-replies` |
| `ScheduledMessagesAsync(conversationId)` | `GET /v1/conversations/{id}/scheduled-messages` |
| `ScheduleMessageAsync(conversationId, request)` | `POST /v1/conversations/{id}/scheduled-messages` |
| `CancelScheduledAsync(scheduledMessageId)` | `DELETE /v1/conversations/scheduled-messages/{id}` |

A message sent with an API key is recorded as sent by an integration, not by
whoever created the key — a key outlives the person who issued it, so
attributing its writes to them would be wrong the moment they leave.

### Tickets — `chatly.Tickets`

| Method | Route |
| --- | --- |
| `ListAsync(options?)` / `ListAllAsync(options?)` | `GET /v1/tickets` |
| `CommentsAsync(ticketId)` | `GET /v1/tickets/{id}/comments` |
| `ForConversationAsync(conversationId)` | `GET /v1/tickets/for-conversation/{id}` |
| `QueuesAsync()` | `GET /v1/tickets/queues` |
| `MergeAsync(ticketId, request)` | `POST /v1/tickets/{id}/merge` |
| `SplitAsync(ticketId, request)` | `POST /v1/tickets/{id}/split` |
| `BulkAsync(request)` | `POST /v1/tickets/bulk` |

### Macros — `chatly.Macros`

| Method | Route |
| --- | --- |
| `ListAsync(options?)` / `ListAllAsync(options?)` | `GET /v1/macros` |
| `CreateAsync(macro)` | `POST /v1/macros` |
| `UpdateAsync(macroId, changes)` | `PATCH /v1/macros/{id}` |
| `DeleteAsync(macroId)` | `DELETE /v1/macros/{id}` |

### Knowledge base — `chatly.Kb`

| Method | Route |
| --- | --- |
| `ArticlesAsync(options?)` / `ArticlesAllAsync(options?)` | `GET /v1/kb/articles` |
| `ArticleAsync(slug)` | `GET /v1/kb/articles/{slug}` |
| `CollectionsAsync()` | `GET /v1/kb/collections` |

### Search — `chatly.Search`

| Method | Route |
| --- | --- |
| `QueryAsync(query, options?)` | `GET /v1/search` |
| `KbAsync(query)` | `GET /v1/search?kinds[]=kb` |

### Events (CDP) — `chatly.Events`

| Method | Route |
| --- | --- |
| `TrackAsync(contactId, name, properties?)` | `POST /v1/cdp/events` |
| `ListAsync(options?)` / `ListAllAsync(options?)` | `GET /v1/cdp/events` |

`ListAsync` takes an `EventsListOptions`, whose `ContactId` scopes the stream
to one person.

### Webhook deliveries — `chatly.Webhooks`

| Method | Route |
| --- | --- |
| `DeliveriesAsync(options?)` / `DeliveriesAllAsync(options?)` | `GET /v1/webhooks/deliveries` |
| `DeliveryAsync(deliveryId)` | `GET /v1/webhooks/deliveries/{id}` |
| `RedeliverAsync(deliveryId)` | `POST /v1/webhooks/deliveries/{id}/redeliver` |

### Reports — `chatly.Reports`

| Method | Route |
| --- | --- |
| `OverviewAsync(query?)` | `GET /v1/reports/overview` |
| `AgentsAsync(query?)` | `GET /v1/reports/agents` |
| `ConversationsByDayAsync(query?)` | `GET /v1/reports/conversations-by-day` |
| `SlaAsync(query?)` | `GET /v1/reports/sla` |

Each accepts a `ChatlyQuery` for the date and grouping parameters, which differ
per report; see the REST reference for what each one takes.

### Channels, billing, workspace

| Method | Route |
| --- | --- |
| `chatly.Channels.ListAsync()` | `GET /v1/channels` |
| `chatly.Channels.GetAsync(channelId)` | `GET /v1/channels/{id}` |
| `chatly.Billing.PlansAsync()` | `GET /v1/billing/plans` |
| `chatly.Billing.UsageAsync()` | `GET /v1/billing/usage` |
| `chatly.Workspace.SettingsAsync()` | `GET /v1/workspace/settings` |
| `chatly.Workspace.LabelsAsync()` | `GET /v1/labels` |
| `chatly.Workspace.InboxesAsync()` | `GET /v1/inboxes` |
| `chatly.Workspace.TeamMembersAsync(teamId)` | `GET /v1/teams/{id}/members` |
| `chatly.Workspace.AgentSkillsAsync()` | `GET /v1/agents/skills` |

### Helpers, which make no HTTP call

| Function | What it does |
| --- | --- |
| `ChatlyWebhooks.VerifySignature(body, header, secret, tolerance?, now?)` | Verify a webhook delivery |
| `ChatlyWebhooks.Sign(body, secret, timestamp)` | Build a genuine signature header, for testing your own handler |
| `ChatlyIdentity.Hash(secret, externalId)` | The widget's `userHash` |

### Not included, and why

Reactions and channel/widget configuration have no methods here. A reaction
records *who* reacted and an API key is not a person, so those routes require
a signed-in user and answer a key with 403. Channel configuration needs a
scope no key can hold. Both used to be in our Node SDK and both failed for
every caller — shipping a method that always fails is worse than not shipping
it, because the failure arrives at runtime, in production, inside someone
else's integration.

---

## Pagination

List endpoints return `{ items, nextCursor }`, where `nextCursor` is null on
the last page. You get both forms: the page as sent, and an
`IAsyncEnumerable<T>` that fetches pages as you consume them.

```csharp
// one page
var page = await chatly.Contacts.ListAsync(new ListOptions { Limit = 50 });
Console.WriteLine($"{page.Items.Count} items, more: {page.HasMore}");

// then the next, explicitly
var next = await chatly.Contacts.ListAsync(new ListOptions { Cursor = page.NextCursor });

// or walk everything
await foreach (var contact in chatly.Contacts.ListAllAsync(cancellationToken))
{
    // ...
}
```

The walk stops on a null cursor, and also stops if the API ever hands back a
cursor it already gave out — so it cannot become an infinite loop that burns
your rate limit.

`ListOptions` carries `Cursor`, `Limit`, `Filter` (sent as repeated
`filter[]`), `Sort`, `Fields` and an `Extra` escape hatch for endpoint-specific
parameters. Repeated parameters stay repeated:
`kinds[]=kb&kinds[]=article` is two parameters, never one comma-joined value.

**Do not page or sort on `id`.** Ids in this product are a mix of UUID v4 and
v7, so id order is neither insertion order nor time order. The cursor is the
only correct sequence.

---

## Errors

Every failure is a typed exception deriving from `ChatlyException`, carrying
`Status`, `Code`, `RequestId` (from the `x-request-id` response header) and
`Body` (the raw response text).

| Exception | When |
| --- | --- |
| `ChatlyAuthenticationException` | 401 — the key is missing, malformed or revoked |
| `ChatlyPermissionException` | 403 — the key is valid but lacks the scope |
| `ChatlyNotFoundException` | 404 — no such record in this workspace |
| `ChatlyConflictException` | 409 — conflicting write, or an idempotency-key replay mismatch |
| `ChatlyValidationException` | 400 / 422 — the body or query was rejected |
| `ChatlyRateLimitException` | 429 — carries `RetryAfter` |
| `ChatlyServerException` | 5xx |
| `ChatlyTransportException` | never reached the API: DNS, connect, TLS, timeout. `Status` is 0 |

"My key is wrong" and "my key lacks a scope" are different types, so you never
have to parse a message string:

```csharp
try
{
    await chatly.Contacts.UpsertAsync(new ContactInput { Email = email });
}
catch (ChatlyValidationException ex)
{
    logger.LogWarning("Chatly rejected the contact: {Code} {Body}", ex.Code, ex.Body);
}
catch (ChatlyRateLimitException ex)
{
    logger.LogWarning("Rate limited, retry after {Delay}", ex.RetryAfter);
}
catch (ChatlyException ex)
{
    logger.LogError(ex, "Chatly call failed (request {RequestId})", ex.RequestId);
}
```

A 422's rejected fields are in `Body` under `error.details.issues`. The SDK
surfaces the raw body rather than modelling that shape.

---

## Retries and idempotency

Out of the box: 3 attempts, exponential backoff with full jitter, honouring
`Retry-After`. Only what is safe to repeat is retried — transport failures,
429 and 5xx. **A 4xx that is not 429 is never retried**; it would fail
identically and only spend your rate limit.

```csharp
var chatly = new ChatlyClient(new ChatlyClientOptions
{
    ApiKey = key,
    Timeout = TimeSpan.FromSeconds(10),
    Retry = new ChatlyRetryPolicy { MaxAttempts = 5, InitialBackoff = TimeSpan.FromMilliseconds(400) },
});

// or turn retries off entirely
Retry = ChatlyRetryPolicy.None
```

Every write (POST/PUT/PATCH/DELETE) carries an `Idempotency-Key`, minted fresh
per call, so an SDK-level retry cannot double-write — a retried attempt
re-sends the *same* key, which is the whole reason the header exists.

When **your** code is the thing retrying — a job that may run twice, a
queue with at-least-once delivery — pin the key so the API replays the first
response instead of applying the write again:

```csharp
await chatly.WithIdempotencyKey($"refund-{orderId}")
            .Contacts.UpsertAsync(new ContactInput { ExternalId = $"order-{orderId}" });
```

Same key + same body replays the first response. Same key + a *different* body
is a 409. `WithIdempotencyKey` returns a derived client over the same
`HttpClient`; the original is unchanged.

---

## Verifying webhooks

Chatly signs every outbound event webhook. The header is
**`x-livechat-signature`** and looks like `t=1700000000,v1=<hex>`, where `v1`
is HMAC-SHA256 over the bytes `` `<t>.<raw body>` ``. The timestamp is bound
*into* the digest, which is what makes the replay window enforceable.

**Verify against the raw bytes.** Re-serialising parsed JSON reorders keys and
changes whitespace, and the digest will not match. In ASP.NET Core that means
reading the body yourself rather than binding a model:

```csharp
using System.Text.Json;
using Chatly;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var secret = builder.Configuration["Chatly:WebhookSecret"]!;   // whsec_…

app.MapPost("/webhooks/chatly", async (HttpRequest request) =>
{
    // The RAW bytes, before any JSON parsing. Do not bind a model here:
    // a bound model has already been re-serialised and will never verify.
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);
    var raw = buffer.ToArray();

    var signature = request.Headers[ChatlyWebhooks.SignatureHeaderName].ToString();

    if (!ChatlyWebhooks.VerifySignature(raw, signature, secret))
    {
        return Results.Unauthorized();
    }

    // Only now is it safe to parse. The body is
    // { "id": "…", "type": "message.created", "payload": { … } },
    // and the event type is repeated in the `x-livechat-event` header for a
    // consumer that wants to dedupe or route before parsing JSON.
    using var document = JsonDocument.Parse(raw);
    var type = document.RootElement.GetStringOrNull("type");

    // Do the work out-of-band and answer fast; we retry on a non-2xx.
    return Results.Ok();
});

app.Run();
```

`VerifySignature` **never throws**. A missing or malformed header, a
non-numeric `t`, a bad hex digest, a stale timestamp and an empty secret all
return `false` — because a verifier that throws is an accepted webhook in a
handler that forgot to catch.

The default replay window is 300 seconds; pass a `tolerance` to change it, or
`ChatlyWebhooks.InfiniteTolerance` when re-verifying an archived delivery.

> **The header name is the trap worth knowing.** `x-chatly-signature` is also
> real, but it belongs to four other surfaces — external-agent deliveries,
> custom AI tool calls, customer cards, and the workflow `fire_webhook` action
> — and each builds its base string differently. Outbound event webhooks, the
> ones this verifier is for, use `x-livechat-signature`. Do not share one
> verifier across them without checking the construction.

To test your own handler without waiting for a real delivery, build a genuine
header with `ChatlyWebhooks.Sign`:

```csharp
var header = ChatlyWebhooks.Sign(rawBody, secret, DateTimeOffset.UtcNow);
```

---

## Widget identity verification

If a workspace turns identity verification on, the widget must prove that the
`externalId` it claims came from your backend — otherwise anyone can open a
chat as anyone else's account. Your server computes the digest; only the
digest reaches the browser.

```csharp
var userHash = ChatlyIdentity.Hash(
    Environment.GetEnvironmentVariable("CHATLY_IDV_SECRET")!,   // sek_…
    user.Id);
```

Render it into the page and hand it to the widget:

```js
Chatly('identify', { externalId: 'user_42', userHash: '<the digest>', email: 'dana@example.com' });
```

The secret lives in **Workspace → Settings → Widget → Identity verification**.
Never send it to a browser.

---

## Dependency injection

Register it as a typed client so `IHttpClientFactory` owns the handler
lifetime:

```csharp
builder.Services.AddSingleton(new ChatlyClientOptions
{
    ApiKey = builder.Configuration["Chatly:ApiKey"]!,
    Timeout = TimeSpan.FromSeconds(10),
});

builder.Services.AddHttpClient<ChatlyClient>();
```

Then inject `ChatlyClient` anywhere:

```csharp
app.MapGet("/contacts", async (ChatlyClient chatly, CancellationToken ct) =>
    await chatly.Contacts.ListAsync(ct));
```

The SDK never modifies an `HttpClient` you supply — not its `Timeout`, its
`BaseAddress` or its default headers — because it may be shared. The
per-attempt timeout is applied with a linked `CancellationTokenSource`
instead, and `Dispose()` is a no-op on a borrowed client.

Without DI, keep one long-lived instance (it is thread-safe). Creating a
`ChatlyClient` per request creates an `HttpClient` per request and exhausts
sockets.

---

## The escape hatch

140 of the API's routes accept an API key and 55 of them have a method here.
For the other 85, `RequestAsync` goes through the same authentication,
idempotency-key minting, retries and typed errors:

```csharp
var json = await chatly.RequestAsync(
    HttpMethod.Post,
    "/v1/tickets/t_1/time-entries",
    query: null,
    body: new { minutes = 15, note = "call with Dana" });

// typed, if you prefer
var entry = await chatly.RequestAsync<TimeEntry>(HttpMethod.Get, "/v1/tickets/t_1/time-entries");

// and paged routes, including ones with no method here
await foreach (var row in chatly.RequestAllAsync("/v1/audit-logs", new ListOptions { Limit = 100 }))
{
    // ...
}
```

A route that does *not* accept an API key answers 403 no matter what you send:
those need a signed-in person, and no header substitutes for one.

---

## Pointing at another environment

The base URL defaults to `https://api.chatlychat.com`. Override it to reach
another Chatly environment, or a test server in your own suite. It must be an
absolute URI, and a path prefix is honoured — every route is appended to the
path you give, so an API served under `/api` works:

```csharp
var chatly = new ChatlyClient(new ChatlyClientOptions
{
    ApiKey = key,
    BaseUrl = new Uri("https://chat.example.com/api"),
});
```

---

## Running the tests

Two runners, the same assertions. Nothing in either reaches the network, needs
an API key or needs a running API — every request goes through a fake
`HttpMessageHandler`.

```bash
# xunit (restores xunit from NuGet)
dotnet test Chatly.sln

# the same checks with no NuGet dependency at all
dotnet run --project tests/Verify/Verify.csproj
```

The suite covers all nine items of the cross-language SDK contract, including
both pinned vectors (the identity-hash digest and the exact webhook delivery),
and drives all 55 methods to assert the HTTP verb and path each one produces.

Running it also regenerates `surface.lock.json`, the committed record of the
request each method was *observed* to issue, and fails if that disagrees with
the shared manifest in `packages/sdk-spec/surface.json`. The lock is produced
by executing the client rather than by reading its source, so a path assembled
at runtime is still recorded and a doc comment cannot fake a method.

---

## License

MIT. See [LICENSE](LICENSE).

---

## About this repository

This is a **mirror**. `chatly-dotnet` is built from a private monorepo where the rest
of the platform, its migrations and its contract checks live, so a pull request here
cannot be merged directly — but a patch is still the fastest way to get something
fixed and we will port it with attribution.

What you can do here: read the source, run the tests, open an issue, or open a PR
and we will treat the diff as the proposal.

Issues: https://github.com/chatlychat/chatly-dotnet/issues · Docs: https://chatlychat.com/docs
