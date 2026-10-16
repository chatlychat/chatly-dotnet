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

163 methods, one per entry of the shared SDK manifest, each verified against
its handler. Each takes an optional `CancellationToken` last.

### Credential

| Method                 | Route                     |
| ---------------------- | ------------------------- |
| `chatly.MeAsync()`     | `GET /v1/me`              |
| `chatly.WhoAmIAsync()` | `GET /v1/api-keys/whoami` |

From an API key both return the same object — `MeAsync` is discriminated on
`principal` and the key branch is byte-identical to `whoami`'s, because there
is no user behind a key. The difference is the gate: **`GET /v1/me` carries no
permission requirement at all** where `whoami` wants `workspace:read`, which
makes `MeAsync` **the liveness check**. A key with no useful scope still gets a
200 from it, and that answer is true. It is also `@AllowWhenSuspended`, so it
answers for a suspended workspace.

Only that one handler on the `/v1/me` controller accepts a key; the profile
patch, the session list and the notification preferences each write or
enumerate a `users` row, so they answer 403 and have no methods here.

### Contacts — `chatly.Contacts`

| Method                                                      | Route                                 |
| ----------------------------------------------------------- | ------------------------------------- |
| `ListAsync(options?)`                                       | `GET /v1/contacts`                    |
| `ListAllAsync(options?)`                                    | auto-paginates `GET /v1/contacts`     |
| `GetAsync(contactId)`                                       | `GET /v1/contacts/{id}`               |
| `UpsertAsync(contact)`                                      | `POST /v1/contacts`                   |
| `BulkUpsertAsync(contacts)`                                 | `POST /v1/contacts/bulk`              |
| `MergeAsync(contactId, mergedContactId)`                    | `POST /v1/contacts/{id}/merge`        |
| `AddTagsAsync(contactId, tag)`                              | `POST /v1/contacts/{id}/tags`         |
| `RemoveTagAsync(contactId, tag)`                            | `DELETE /v1/contacts/{id}/tags/{tag}` |
| `UnsubscribeAsync(contactId, unsubscribed = true, reason?)` | `POST /v1/contacts/{id}/unsubscribe`  |
| `JourneyAsync(contactId, options?)`                         | `GET /v1/contacts/{id}/journey`       |
| `JourneyAllAsync(contactId, options?)`                      | auto-paginates the above              |
| `CardsAsync(contactId, conversationId?)`                    | `POST /v1/contacts/{id}/cards`        |
| `MoodAsync(contactId, days?)`                               | `GET /v1/contacts/{id}/mood`          |

`CardsAsync` is a POST that **reads**: it fetches the contact's customer cards
live from your own backends, so the optional conversation context goes in the
body. It is as slow as the slowest of your endpoints and is capped at 25
cards; a card with no blocks is not an error — read its `state`.

`UpsertAsync` keys on `ExternalId`, so calling it twice with the same one
updates a single record. `BulkUpsertAsync` takes up to 5000 rows per call and
reports per-row outcomes instead of failing the batch on one bad row.

### Conversations — `chatly.Conversations`

| Method                                                | Route                                               |
| ----------------------------------------------------- | --------------------------------------------------- |
| `ListAsync(options?)`                                 | `GET /v1/conversations`                             |
| `ListAllAsync(options?)`                              | auto-paginates `GET /v1/conversations`              |
| `GetAsync(conversationId)`                            | `GET /v1/conversations/{id}`                        |
| `MessagesAsync(conversationId, options?)`             | `GET /v1/conversations/{id}/messages`               |
| `MessagesAllAsync(conversationId, options?)`          | auto-paginates the above                            |
| `SendAsync(conversationId, message)`                  | `POST /v1/conversations/{id}/messages`              |
| `UnattendedAsync(options?)`                           | `GET /v1/conversations/unattended`                  |
| `UnattendedAllAsync(options?)`                        | auto-paginates the above                            |
| `AgentHandlingAsync(conversationId)`                  | `GET /v1/conversations/{id}/agent-handling`         |
| `SetAgentHandlingAsync(conversationId, state)`        | `PUT /v1/conversations/{id}/agent-handling`         |
| `GeneratedRepliesAsync(conversationId)`               | `GET /v1/conversations/{id}/generated-replies`      |
| `GenerateReplyAsync(conversationId, request?)`        | `POST /v1/conversations/{id}/generated-replies`     |
| `ScheduledMessagesAsync(conversationId)`              | `GET /v1/conversations/{id}/scheduled-messages`     |
| `ScheduleMessageAsync(conversationId, request)`       | `POST /v1/conversations/{id}/scheduled-messages`    |
| `CancelScheduledAsync(scheduledMessageId)`            | `DELETE /v1/conversations/scheduled-messages/{id}`  |
| `LinkedAsync(conversationId)`                         | `GET /v1/conversations/{id}/linked`                 |
| `OutcomesAsync(conversationId)`                       | `GET /v1/conversations/{id}/outcomes`               |
| `RecordOutcomeAsync(conversationId, outcome)`         | `POST /v1/conversations/{id}/outcomes`              |
| `ReturnToBotAsync(conversationId, botId)`             | `POST /v1/conversations/{id}/return-to-bot`         |
| `UnreadCountsAsync(brand?)`                           | `GET /v1/conversations/unread-counts`               |
| `UpdateBlocksAsync(conversationId, messageId, patch)` | `PATCH /v1/conversations/{id}/messages/{id}/blocks` |

Three of these need a **second scope the route does not declare**.
`OutcomesAsync`, `RecordOutcomeAsync` and `UpdateBlocksAsync` all resolve the
conversation before doing their own work, so a key without
`conversations:read` gets a **404 on every conversation** rather than a 403 —
which reads as "no such thread" and sends you looking for the wrong bug.
`chatly.WhoAmIAsync()` is the check.

`UnreadCountsAsync` is **near-useless from an API key** and is here because it
answers 200 rather than failing. The counts are the caller's own, read from
its membership — and a key's membership user id is the key's own id, so you
get the unread state of a principal that never opens a conversation. There is
no parameter that makes it workspace-wide; `UnattendedAsync`'s `total` is the
real backlog figure.

`LinkedAsync` has an `items` key and no cursor, so it is not a page and has no
walker. `RecordOutcomeAsync` reports `created: false` when an equal outcome
was already on record, which makes it naturally safe to retry.
`ReturnToBotAsync` requires the bot id and does not default to whoever owned
the thread before — a thread three assistants have touched has no obvious
"the" bot.

`UnattendedAsync` is cursor-paginated, despite reading like a bounded
operational list — it answers `{ items, nextCursor, total }`, and `total`
ignores the cursor, which is why the single-page form hands back the whole
envelope rather than just the threads. `UnattendedAllAsync` walks it.
`CancelScheduledAsync` answers **200 with the row it cancelled**, not 204; the
only route on this surface that really answers 204 is `Macros.DeleteAsync`.

A message sent with an API key is recorded as sent by an integration, not by
whoever created the key — a key outlives the person who issued it, so
attributing its writes to them would be wrong the moment they leave.

### Tickets — `chatly.Tickets`

| Method                                           | Route                                   |
| ------------------------------------------------ | --------------------------------------- |
| `ListAsync(options?)` / `ListAllAsync(options?)` | `GET /v1/tickets`                       |
| `CommentsAsync(ticketId)`                        | `GET /v1/tickets/{id}/comments`         |
| `ForConversationAsync(conversationId)`           | `GET /v1/tickets/for-conversation/{id}` |
| `QueuesAsync()`                                  | `GET /v1/tickets/queues`                |
| `MergeAsync(ticketId, request)`                  | `POST /v1/tickets/{id}/merge`           |
| `SplitAsync(ticketId, request)`                  | `POST /v1/tickets/{id}/split`           |
| `BulkAsync(request)`                             | `POST /v1/tickets/bulk`                 |
| `ConversationsAsync(ticketId)`                   | `GET /v1/tickets/{id}/conversations`    |
| `LineageAsync(ticketId)`                         | `GET /v1/tickets/{id}/lineage`          |
| `StatusHistoryAsync(ticketId)`                   | `GET /v1/tickets/{id}/status-history`   |
| `TimeAsync(ticketId)`                            | `GET /v1/tickets/{id}/time`             |
| `OpenCountAsync(conversationId)`                 | `GET /v1/tickets/open-count/{id}`       |

`OpenCountAsync`'s path parameter is a **conversation** id, despite living
under `/v1/tickets`. `LineageAsync` returns the raw `splitFromTicketId` /
`parentTicketId` columns beside the resolved references, and that pairing
matters: a null reference beside a non-null id is a dangling pointer, which
neither column has a foreign key to prevent. `StatusHistoryAsync` is **oldest
first** — the opposite of most lists here — and capped, with `truncated`
distinguishing a capped timeline from a short one. `TimeAsync` has an `items`
key and no cursor, plus `totalMinutes` and `billableMinutes`; the billable
figure is computed server-side, so do not re-derive it from the entries you
can see.

### Macros — `chatly.Macros`

| Method                          | Route                             |
| ------------------------------- | --------------------------------- |
| `ListAsync(options?)`           | `GET /v1/macros`                  |
| `CreateAsync(macro)`            | `POST /v1/macros`                 |
| `UpdateAsync(macroId, changes)` | `PATCH /v1/macros/{id}`           |
| `DeleteAsync(macroId)`          | `DELETE /v1/macros/{id}`          |
| `GetAsync(macroId)`             | `GET /v1/macros/{id}`             |
| `ActionsAsync()`                | `GET /v1/macros/actions`          |
| `TemplatesAsync()`              | `GET /v1/macros/templates`        |
| `TemplateAsync(slug)`           | `GET /v1/macros/templates/{slug}` |

`ActionsAsync` is a static catalogue, and each entry names the **permission**
its action needs — which is what decides whether the agent applying the macro
can run it, and that failure arrives at apply time rather than at save time.
`TemplatesAsync` answers `{ items, total }` with no cursor: `total` is just
`items.length` over a frozen catalogue, not a hint that more exist. Templates
are addressed by **slug**, not id, because they are a shipped catalogue rather
than workspace rows.

`ListAsync` is the one list method here that is **not** paginated and does not
return a page. The endpoint answers a bare JSON array — the service caps it at
200 rows and orders them by usage rank, which is what makes the list useful to
an agent picking a reply — so enumerate the array directly and expect no
cursor:

```csharp
var macros = await chatly.Macros.ListAsync(new MacroListOptions { Scope = "team", Language = "fr" });
foreach (var macro in macros.EnumerateArray())
{
    Console.WriteLine(macro.GetStringOrNull("name"));
}
```

There is no `ListAllAsync` for macros, and `MacroListOptions` takes `Scope`,
`Team`, `Query` and `Language` rather than a cursor or a limit — those are the
parameters the route actually reads.

### Knowledge base — `chatly.Kb`

| Method                                                   | Route                        |
| -------------------------------------------------------- | ---------------------------- |
| `ArticlesAsync(options?)` / `ArticlesAllAsync(options?)` | `GET /v1/kb/articles`        |
| `ArticleAsync(slug)`                                     | `GET /v1/kb/articles/{slug}` |
| `CollectionsAsync()`                                     | `GET /v1/kb/collections`     |

### Search — `chatly.Search`

| Method                           | Route                     |
| -------------------------------- | ------------------------- |
| `QueryAsync(query, options?)`    | `GET /v1/search`          |
| `QueryAllAsync(query, options?)` | auto-paginates the above  |
| `KbAsync(query)`                 | `GET /v1/search?kinds=kb` |

`QueryAsync` returns the whole envelope, not just the hits: `tier` and `facets`
sit beside `items` and a caller that only ever saw matches would lose both.
`QueryAllAsync` yields the hits alone, which is the right trade for a loop —
read the facets from a single page.

### Events (CDP) — `chatly.Events`

| Method                                           | Route                 |
| ------------------------------------------------ | --------------------- |
| `TrackAsync(contactId, name, properties?)`       | `POST /v1/cdp/events` |
| `ListAsync(options?)` / `ListAllAsync(options?)` | `GET /v1/cdp/events`  |

`ListAsync` takes an `EventsListOptions`, whose `ContactId` scopes the stream
to one person.

### Webhooks — `chatly.Webhooks`

| Method                                                       | Route                                         |
| ------------------------------------------------------------ | --------------------------------------------- |
| `ListAsync()`                                                | `GET /v1/webhooks`                            |
| `CreateAsync(endpoint)`                                      | `POST /v1/webhooks`                           |
| `EventsAsync()`                                              | `GET /v1/webhooks/events`                     |
| `SchemaVersionsAsync()`                                      | `GET /v1/webhooks/schema-versions`            |
| `SetSchemaVersionAsync(endpointId, schemaVersion)`           | `PATCH /v1/webhooks/{id}/schema-version`      |
| `DeliveriesAsync(options?)` / `DeliveriesAllAsync(options?)` | `GET /v1/webhooks/deliveries`                 |
| `DeliveryAsync(deliveryId)`                                  | `GET /v1/webhooks/deliveries/{id}`            |
| `RedeliverAsync(deliveryId)`                                 | `POST /v1/webhooks/deliveries/{id}/redeliver` |
| `DeleteAsync(endpointId)`                                    | `DELETE /v1/webhooks/{id}`                    |

`/v1/webhooks` is the endpoints and `/v1/webhooks/deliveries` is the log; only
the second is a page. `CreateAsync` is the only response that carries
`secret`, which is what `ChatlyWebhooks.VerifySignature` needs. An
unrecognised `schemaVersion` is **refused here**, unlike at dispatch time
where an unknown version resolves to the latest: loud on the write, generous
on the read.

`DeleteAsync` answers **200 with `{ ok: true }`**, not a 204. It is new, and
earlier versions of this README told you to work around its absence by
pointing the endpoint at a dead URL — which is not a deletion: the
subscription stays alive and the delivery log fills with failures. The route
refused an API key while `CreateAsync` accepted one, so a deploy pipeline
registering an endpoint per environment leaked one into the customer's
workspace on every run with nothing able to clean up. Both need
`integrations:write`, on the argument that a key which can add a delivery
target can already do the thing with consequences.

One wrinkle: the delete handler declares no idempotency posture, so it
inherits the controller's `@RequireIdempotencyKey()` and answers **428 in
production without the header**. This SDK mints one per write, so that only
bites if you reach the route some other way.

### Reports — `chatly.Reports`

| Method                             | Route                                       |
| ---------------------------------- | ------------------------------------------- |
| `OverviewAsync(query?)`            | `GET /v1/reports/overview`                  |
| `AgentsAsync(query?)`              | `GET /v1/reports/agents`                    |
| `ConversationsByDayAsync(query?)`  | `GET /v1/reports/conversations-by-day`      |
| `SlaAsync(query?)`                 | `GET /v1/reports/sla`                       |
| `AttributionAsync(query?)`         | `GET /v1/reports/attribution`               |
| `SourcesAsync(query?)`             | `GET /v1/reports/sources`                   |
| `CohortsAsync(query?)`             | `GET /v1/reports/cohorts`                   |
| `LabelsAsync(query?)`              | `GET /v1/reports/labels`                    |
| `TeamsAsync(query?)`               | `GET /v1/reports/teams`                     |
| `ResponseTimesAsync(query?)`       | `GET /v1/reports/response-times`            |
| `VoiceAsync(query?)`               | `GET /v1/reports/voice`                     |
| `ThemesAsync(query?)`              | `GET /v1/reports/themes`                    |
| `ThemeConversationsAsync(themeId)` | `GET /v1/reports/themes/{id}/conversations` |

Each accepts a `ChatlyQuery` for the date and grouping parameters, which differ
per report; see the REST reference for what each one takes.

Four of the thirteen answer a **bare array** — `AgentsAsync`,
`ConversationsByDayAsync`, `AttributionAsync`, `SourcesAsync` — and the rest
answer an object. A few have a null that means something: `AttributionAsync`'s
`value: null` row is the untagged remainder and `SourcesAsync`'s null
`channelId` is a real bucket, so dropping either stops the totals adding up,
and `ResponseTimesAsync` returns **null** percentiles when `sampleSize` is 0
rather than zeros, so a quiet window does not read as instant replies.
`LabelsAsync` caps `rows` at the 50 busiest labels with the remainder counted
separately, and there is no cursor past the cap. `ThemesAsync` is nightly
snapshot data: a null `batchDate` means no batch has run, and
`ThemeConversationsAsync` answers 200 with an empty list for a purged theme
rather than a 404.

### Channels — `chatly.Channels`

| Method                            | Route                                  |
| --------------------------------- | -------------------------------------- |
| `ListAsync()`                     | `GET /v1/channels`                     |
| `GetAsync(channelId)`             | `GET /v1/channels/{id}`                |
| `AdaptersAsync()`                 | `GET /v1/channels/adapters`            |
| `CreateAsync(channel)`            | `POST /v1/channels`                    |
| `UpdateAsync(channelId, changes)` | `PATCH /v1/channels/{id}`              |
| `RotateSecretAsync(channelId)`    | `POST /v1/channels/{id}/secret/rotate` |

`AdaptersAsync` is the only route on this surface that answers an array of
**strings** rather than objects. Note the rotate path is `/secret/rotate`, not
`/rotate-secret` as on the card routes. An **empty** allowed-origins list on
`UpdateAsync` means _any_ origin, not none, and an empty array is invisible in
a JSON diff — read `originsRestricted` on the response instead of counting
origins. There is no delete: removing a channel destroys conversation history,
so that route needs a signed-in person.

### Billing — `chatly.Billing`

| Method                                   | Route                           |
| ---------------------------------------- | ------------------------------- |
| `PlansAsync()`                           | `GET /v1/billing/plans`         |
| `SummaryAsync()`                         | `GET /v1/billing`               |
| `UsageAsync(eventType, from?, to?)`      | `GET /v1/billing/usage`         |
| `UsageDailyAsync(eventType, from?, to?)` | `GET /v1/billing/usage/daily`   |
| `UsageMonthlyAsync(month?)`              | `GET /v1/billing/usage/monthly` |
| `InvoicesAsync(cursor?, limit?)`         | `GET /v1/billing/invoices`      |

`SummaryAsync` is the collection root, `/v1/billing`, with no sub-path.
`UsageMonthlyAsync` is the one usage route needing no `eventType` — it returns
all four, plus a grand total — and its `month` is `YYYY-MM` or a 400, never a
coerced free-form date. `UsageDailyAsync` returns a **dense** series, one
entry per UTC day including zeros.

`InvoicesAsync` is genuinely cursor-paginated and still returns a plain
`JsonElement`, because **its envelope key is `data`, not `items`**. That is
why there is no `InvoicesAllAsync`: a walker reading `items` would report an
empty page forever, which is a wrong answer with a 200 rather than a visible
failure. Read `data` and `nextCursor` and pass the cursor back.

### Workspace — `chatly.Workspace`

| Method                     | Route                        |
| -------------------------- | ---------------------------- |
| `SettingsAsync()`          | `GET /v1/workspace/settings` |
| `LabelsAsync()`            | `GET /v1/labels`             |
| `InboxesAsync()`           | `GET /v1/inboxes`            |
| `TeamMembersAsync(teamId)` | `GET /v1/teams/{id}/members` |
| `AgentSkillsAsync()`       | `GET /v1/agents/skills`      |
| `KeyScopesAsync()`         | `GET /v1/api-keys/scopes`    |
| `MineAsync()`              | `GET /v1/workspaces/me`      |
| `SecretsAsync()`           | `GET /v1/workspace/secrets`  |

`KeyScopesAsync` is the second of only two routes on the api-keys controller a
key may call, alongside `whoami`; everything else there stays closed so a
leaked read-only key cannot mint itself a better one. It is deliberately
_not_ the whole permission vocabulary — key auth is opt-in per route, and this
endpoint used to list all eighty permissions, which let a customer mint a key
scoped to something the guard was never going to honour. Reach for it when a
call returns 403 and you want to know which scope it wanted.

`SecretsAsync` is a census: one entry per stored credential across nine
tables, reporting existence and timestamps and **never a value**.

`MineAsync` is a bare array, and for a key it holds **exactly one entry** —
the workspace the key was minted in — which makes it the cheapest way to
confirm which tenant a credential actually points at. Its `role` is the
synthetic `restricted` the API mints for every key, not a stored membership
role: what a key may do is its scope set, which `MeAsync` answers, and
reporting `owner` because the person who created the key was one would
overstate the credential. It is `@AllowWhenSuspended`, so it answers when the
active workspace is suspended — that is how a caller discovers the others, and
if everything else is failing while this still works, suspension is the thing
to check.

### AI — `chatly.Ai`

| Method                               | Route                                   |
| ------------------------------------ | --------------------------------------- |
| `ProvidersAsync()`                   | `GET /v1/ai-providers`                  |
| `AddProviderAsync(provider, apiKey)` | `POST /v1/ai-providers`                 |
| `TestProviderAsync(provider)`        | `POST /v1/ai-providers/{provider}/test` |
| `QuotaAsync()`                       | `GET /v1/ai/quota`                      |
| `ToneAsync()`                        | `GET /v1/ai/tone`                       |
| `SetToneAsync(rules)`                | `PUT /v1/ai/tone`                       |
| `GenerateToneAsync(styleGuide)`      | `POST /v1/ai/tone/generate`             |
| `TonePreviewAsync()`                 | `GET /v1/ai/tone/preview`               |
| `ToolsAsync()`                       | `GET /v1/ai/tools`                      |
| `CreateToolAsync(tool)`              | `POST /v1/ai/tools`                     |
| `UpdateToolAsync(toolId, changes)`   | `PATCH /v1/ai/tools/{id}`               |
| `DeleteToolAsync(toolId)`            | `DELETE /v1/ai/tools/{id}`              |

Two controllers sit behind one property and their paths disagree: credentials
are `/v1/ai-providers`, everything else is `/v1/ai/…`. Nothing here ever
returns a provider key — a provider row carries a four-character `keyHint` and
no field for the key itself.

`SetToneAsync` is a **whole replacement**: rules you omit are deleted, and
there is no per-rule route. Its body is `{ rules }` and its response is the
same bare array `ToneAsync` returns. `GenerateToneAsync` saves nothing — it
proposes rules for a person to approve, and it is metered AI spend, so
`QuotaAsync` can explain why it suddenly refuses.

### Bots and external agents — `chatly.Bots`

| Method                                           | Route                                          |
| ------------------------------------------------ | ---------------------------------------------- |
| `ListAsync(options?)` / `ListAllAsync(options?)` | `GET /v1/bots`                                 |
| `GetAsync(botId)`                                | `GET /v1/bots/{id}`                            |
| `CreateAsync(bot)`                               | `POST /v1/bots`                                |
| `UpdateAsync(botId, changes)`                    | `PATCH /v1/bots/{id}`                          |
| `AnalyticsAsync(botId, days?)`                   | `GET /v1/bots/{id}/analytics`                  |
| `DeliveriesAsync(botId, options?, status?)`      | `GET /v1/bots/{id}/deliveries`                 |
| `DeliveriesAllAsync(botId, options?, status?)`   | auto-paginates the above                       |
| `DeliveryAsync(botId, deliveryId)`               | `GET /v1/bots/{id}/deliveries/{deliveryId}`    |
| `RedeliverAsync(botId, deliveryId)`              | `POST /v1/bots/{id}/deliveries/{id}/redeliver` |
| `TestDeliveryAsync(botId)`                       | `POST /v1/bots/{id}/test-delivery`             |
| `UpdateExternalAsync(botId, changes)`            | `PATCH /v1/bots/{id}/external`                 |
| `RotateExternalSecretAsync(botId, activateAt?)`  | `POST /v1/bots/{id}/external/rotate-secret`    |

A _bot_ is a Chatly-hosted persona. An _external agent_ is a bot whose replies
come from **your** endpoint: Chatly signs a payload and POSTs it to you. The
two credentials an external agent gets point in opposite directions —
`signingSecret` is what you verify our deliveries with, `token` is what you
authenticate back to us with — and both are shown exactly once, by
`CreateAsync`.

`TestDeliveryAsync` is the first call to make after wiring one up: it waits
for your endpoint and returns the status and up to 2,000 characters of the
body, where `RedeliverAsync` only reports that a job was queued. A delivery
_list_ row does not carry the signed payload; `DeliveryAsync` does.

`RotateExternalSecretAsync(botId)` is a hard cut: the old signing secret stops
signing the moment it returns. Pass an `activateAt` (a `DateTimeOffset`, sent
as UTC, or an ISO 8601 string with `Z` or an offset, sent verbatim) and the
rotation has an overlap window instead: the new secret is issued now, and
Chatly keeps signing every delivery with the old one until that instant. Each
delivery carries one signature, so hold **both** secrets in your verifier for
the window. The response is `{ signingSecret, activatesAt }` — `activatesAt`
is `null` when the new secret is already signing (no `activateAt`, an instant
already in the past, or an agent with no usable secret yet), otherwise the
instant of the switch. More than 168 hours ahead is a 400, measured on the
server's clock.

### Bot routing — `chatly.BotRoutes`

| Method                          | Route                         |
| ------------------------------- | ----------------------------- |
| `ListAsync()`                   | `GET /v1/bot-routes`          |
| `FieldsAsync()`                 | `GET /v1/bot-routes/fields`   |
| `CreateAsync(route)`            | `POST /v1/bot-routes`         |
| `UpdateAsync(routeId, changes)` | `PATCH /v1/bot-routes/{id}`   |
| `DeleteAsync(routeId)`          | `DELETE /v1/bot-routes/{id}`  |
| `ResolveAsync(context)`         | `POST /v1/bot-routes/resolve` |

Rules are evaluated in order and the first match wins, so `ListAsync` returns
them in evaluation order and you should not re-sort it. `ResolveAsync` is a
POST that **changes nothing** — a dry run that reports the selected route
_and_ every route's verdict, which is the part worth having: "no bot answered"
and "the wrong bot answered" look identical from the selection alone.

### Brands — `chatly.Brands`

| Method                          | Route                    |
| ------------------------------- | ------------------------ |
| `ListAsync()`                   | `GET /v1/brands`         |
| `CreateAsync(brand)`            | `POST /v1/brands`        |
| `UpdateAsync(brandId, changes)` | `PATCH /v1/brands/{id}`  |
| `DeleteAsync(brandId)`          | `DELETE /v1/brands/{id}` |

`DeleteAsync` is a soft delete answering 200 with the id; everything that
referenced the brand keeps referencing it. `domain` is the brand's help-centre
hostname and `replyDomain` is where notification replies are addressed — they
are usually different, and pointing an MX at the first is the mistake the
split exists to prevent.

### Interactive canvas steps — `chatly.CanvasSteps`

| Method                         | Route                                |
| ------------------------------ | ------------------------------------ |
| `GetAsync(stepId)`             | `GET /v1/canvas-steps/{id}`          |
| `ResolveAsync(stepId, canvas)` | `POST /v1/canvas-steps/{id}/resolve` |

While a step is pending the visitor is watching a progress line, so this is a
latency-sensitive path. The canvas you supply **replaces that line in place**
rather than posting a new message. A step id is single-use, which is what
makes a retry after a timeout safe: exactly one resolve wins and the loser is
told the step is already resolved.

### Customer cards — `chatly.CustomerCards`

| Method                         | Route                                        |
| ------------------------------ | -------------------------------------------- |
| `ListAsync()`                  | `GET /v1/customer-cards`                     |
| `CreateAsync(card)`            | `POST /v1/customer-cards`                    |
| `UpdateAsync(cardId, changes)` | `PATCH /v1/customer-cards/{id}`              |
| `RotateSecretAsync(cardId)`    | `POST /v1/customer-cards/{id}/rotate-secret` |
| `DeleteAsync(cardId)`          | `DELETE /v1/customer-cards/{id}`             |

A card is a URL plus a signing secret; Chatly POSTs a signed request to it and
renders what you answer with, so the data never leaves your systems. No
response carries a header _value_ — only names — and the secret appears only
in the create and rotate responses. On `UpdateAsync`, an absent field is
unchanged but `headers: {}` **clears every header**.

### Card endpoint secrets — `chatly.CustomerCardEndpoints`

| Method                            | Route                                                   |
| --------------------------------- | ------------------------------------------------------- |
| `ListAsync()`                     | `GET /v1/customer-card-endpoints`                       |
| `CreateAsync(endpoint)`           | `POST /v1/customer-card-endpoints`                      |
| `RotateSecretAsync(endpointId)`   | `POST /v1/customer-card-endpoints/{id}/rotate-secret`   |
| `ActivateSecretAsync(endpointId)` | `POST /v1/customer-card-endpoints/{id}/activate-secret` |
| `DeleteAsync(endpointId)`         | `DELETE /v1/customer-card-endpoints/{id}`               |

One secret per URL instead of one per card, and the reason to prefer it is
rotation: a card secret swaps instantly and breaks any endpoint not yet
redeployed, while an endpoint secret is minted _pending_ and starts signing
only when you activate it. Mint, deploy, activate. `ActivateSecretAsync` is a
409 when there is nothing pending, so it is not an idempotent "make sure".
`DeleteAsync` reverts signing to per-card secrets rather than stopping it.

### Documentation sources — `chatly.DocsSources`

| Method                           | Route                                |
| -------------------------------- | ------------------------------------ |
| `ProvidersAsync()`               | `GET /v1/docs-sources/providers`     |
| `ListAsync()`                    | `GET /v1/docs-sources`               |
| `DiscoverAsync(credentials)`     | `POST /v1/docs-sources/discover`     |
| `CreateAsync(source)`            | `POST /v1/docs-sources`              |
| `UpdateAsync(sourceId, changes)` | `PATCH /v1/docs-sources/{id}`        |
| `SyncAsync(sourceId, full)`      | `POST /v1/docs-sources/{id}/sync`    |
| `WebhookAsync(sourceId)`         | `POST /v1/docs-sources/{id}/webhook` |
| `DeleteAsync(sourceId)`          | `DELETE /v1/docs-sources/{id}`       |

`DiscoverAsync` is a POST that answers a **bare array** and stores nothing —
the credential goes in the body rather than a query string. Nothing here syncs
inline: `SyncAsync` enqueues the same job the 15-minute scan runs.
`WebhookAsync` answers 200 with `subscribed: false` and a reason when the
token cannot manage webhooks, so read the field and not the status.
`DeleteAsync` answers 200 with `{ kept }` — no article is deleted.

### Transactional emails — `chatly.EmailTemplates`

| Method                        | Route                               |
| ----------------------------- | ----------------------------------- |
| `ListAsync()`                 | `GET /v1/email-templates`           |
| `PreviewAsync(template)`      | `POST /v1/email-templates/preview`  |
| `UpdateAsync(kind, template)` | `PUT /v1/email-templates/{kind}`    |
| `DeleteAsync(kind)`           | `DELETE /v1/email-templates/{kind}` |

Addressed by _kind_, and the set is closed: `workspace_invite`,
`trial_ending_reminder`, `conversation_cap_warning`,
`hired_agent_engagement_invite`, `hired_agent_engagement_started`. These are
the platform's own notifications, not conversation replies.

`PreviewAsync` reports the tokens you wrote that the kind does not supply,
which is the field to read: a mistyped token renders as nothing rather than
failing. `DeleteAsync` deletes the _override_ rather than copying today's
default, so the workspace goes back to tracking the built-in copy — and it has
a body, `{ restored: false }` at 200 when there was no override, so it is safe
to call unconditionally.

### Knowledge gaps — `chatly.KnowledgeGaps`

| Method                       | Route                                |
| ---------------------------- | ------------------------------------ |
| `ListAsync(status?, limit?)` | `GET /v1/knowledge-gaps`             |
| `UpdateAsync(gapId, status)` | `PATCH /v1/knowledge-gaps/{id}`      |
| `DraftAsync(gapId)`          | `POST /v1/knowledge-gaps/{id}/draft` |

Questions real traffic asked that retrieval could not answer, most-asked
first. `DraftAsync` creates a **draft** article — nothing is published — and
not every 200 produces one: a gap recorded without a usable source
conversation answers with a reason instead of an id.

### Triggers — `chatly.Triggers`

| Method                                           | Route                      |
| ------------------------------------------------ | -------------------------- |
| `ListAsync(options?)` / `ListAllAsync(options?)` | `GET /v1/triggers`         |
| `CreateAsync(trigger)`                           | `POST /v1/triggers`        |
| `DeleteAsync(triggerId)`                         | `DELETE /v1/triggers/{id}` |

`CreateAsync` is an **upsert**: with an `id` in the body it updates, without
one it inserts, and there is no PATCH route. Both branches answer **201**, so
do not branch on the status to tell a create from an update. `eventType` is a
free string matched by exact equality at dispatch, so a trigger naming an
event nothing publishes is accepted and simply never fires.

### Visitors — `chatly.Visitors`

| Method                              | Route                                              |
| ----------------------------------- | -------------------------------------------------- |
| `StartConversationAsync(contactId)` | `POST /v1/visitors/{contactId}/start-conversation` |

Opens a thread with a contact who did **not** write first. No body and no
query parameters: the contact id is the whole input. Answers
`{ conversationId }`.

**A 403 here is probably not your scopes.** It is off by default per
workspace: the flag is
`settings.proactiveOutreach.apiKeysMayStartConversations`, an owner or admin
turns it on with `PATCH /v1/workspace/settings` (session-only, so not
reachable from this SDK), and you can read its current value with
`chatly.Workspace.SettingsAsync()`, which is. The scope is not the opt-in —
`conversations:write` was already grantable and is already held by keys minted
for ticket bulk actions and schedule-send — so check the setting before you go
looking at your key.

**Three per contact per hour**, counted per workspace and per contact rather
than per caller. That 429 carries **no `Retry-After` header** — the server
puts the wait in the error body at `error.details.retryAfterSeconds` — and
this client reads only the header, so `ChatlyRateLimitException.RetryAfter` is
`null` on it and you must read the wait from `.Body`. (The Ruby and Python
clients do fall back to the body, so they report it; the missing header is a
property of the route, the null is a property of this client.) A 429 from the
API's global throttler buckets does carry the header, so do not assume one or
the other.

**An offline visitor is not notified.** The greeting is persisted and the
widget renders it from history on their next page view — no email, no push, no
provider message. To reach someone who has left the site, use an email or SMS
conversation instead.

The two sibling routes on that controller, `GET /v1/visitors/live` and
`GET /v1/visitors/{contactId}/pages`, have no methods here: neither carries
`@AllowApiKey()`, so both answer a key with 403.

### Helpers, which make no HTTP call

| Function                                                                 | What it does                                                        |
| ------------------------------------------------------------------------ | ------------------------------------------------------------------- |
| `ChatlyWebhooks.VerifySignature(body, header, secret, tolerance?, now?)` | Verify a webhook delivery                                           |
| `ChatlyWebhooks.Sign(body, secret, timestamp)`                           | Build a genuine signature header, for testing your own handler      |
| `ChatlyWebhooks.VerifyChatlySignature(input)`                            | Verify a request on ANY of the eight signed surfaces, with a reason |
| `ChatlyIdentity.Hash(secret, externalId)`                                | The widget's `userHash`                                             |

### Not included, and why

Reactions, contact block/unblock and channel deletion have no methods here.
Each of those routes records _who_ did it, or destroys history a person should
be named for, so each needs a signed-in user and answers a key with 403.
Reactions used to be in our Node SDK and failed for every caller — shipping a
method that always fails is worse than not shipping it, because the failure
arrives at runtime, in production, inside someone else's integration.

Channel _configuration_ is included now, and an earlier version of this
section said it was not. `CreateAsync`, `UpdateAsync` and `RotateSecretAsync`
were opened to key auth; only the delete stays closed.

`DELETE /v1/webhooks/{id}` used to be listed here as key-accessible with no
method, because it was opened after the shared manifest was frozen and a
160th lock entry would have failed the cross-SDK parity gate. The manifest
lists it now, and `Webhooks.DeleteAsync` is the method.

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
parameters. Repeated parameters stay repeated: `kinds=kb&kinds=ticket` is two
parameters, never one comma-joined value.

Not every list endpoint is paginated. `GET /v1/macros` is pagination-exempt and
returns a bare array (see above); the paginated ones are contacts,
conversations, a conversation's messages, tickets, KB articles, CDP events and
webhook deliveries.

**Do not page or sort on `id`.** Ids in this product are a mix of UUID v4 and
v7, so id order is neither insertion order nor time order. The cursor is the
only correct sequence.

---

## Errors

Every failure is a typed exception deriving from `ChatlyException`, carrying
`Status`, `Code`, `RequestId` (from the `x-request-id` response header) and
`Body` (the raw response text).

| Exception                       | When                                                             |
| ------------------------------- | ---------------------------------------------------------------- |
| `ChatlyAuthenticationException` | 401 — the key is missing, malformed or revoked                   |
| `ChatlyPermissionException`     | 403 — the key is valid but lacks the scope                       |
| `ChatlyNotFoundException`       | 404 — no such record in this workspace                           |
| `ChatlyConflictException`       | 409 — conflicting write, or an idempotency-key replay mismatch   |
| `ChatlyValidationException`     | 400 / 422 — the body or query was rejected                       |
| `ChatlyRateLimitException`      | 429 — carries `RetryAfter`                                       |
| `ChatlyServerException`         | 5xx                                                              |
| `ChatlyTransportException`      | never reached the API: DNS, connect, TLS, timeout. `Status` is 0 |

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
re-sends the _same_ key, which is the whole reason the header exists.

When **your** code is the thing retrying — a job that may run twice, a
queue with at-least-once delivery — pin the key so the API replays the first
response instead of applying the write again:

```csharp
await chatly.WithIdempotencyKey($"refund-{orderId}")
            .Contacts.UpsertAsync(new ContactInput { ExternalId = $"order-{orderId}" });
```

Same key + same body replays the first response. Same key + a _different_ body
is a 409. `WithIdempotencyKey` returns a derived client over the same
`HttpClient`; the original is unchanged.

---

## Metered usage

`UsageAsync` needs an event type: the API validates it against a closed list
and rejects the call without one, so there is no "usage overall" form. Ask for
one metric at a time, and use the constants rather than typing the strings:

```csharp
var usage = await chatly.Billing.UsageAsync(ChatlyUsageEventTypes.AiTokens);
Console.WriteLine(usage.GetProperty("total").GetInt32());
```

`ChatlyUsageEventTypes` holds the four the API accepts:
`conversation.opened`, `message.sent.agent`, `message.sent.bot` and
`ai.tokens`. The window defaults to the current calendar month; pass `from` and
`to` for any other range, and the response echoes the resolved window back
alongside `total` and `count`.

---

## Verifying webhooks

Chatly signs every outbound event webhook. The header is
**`x-livechat-signature`** and looks like `t=1700000000,v1=<hex>`, where `v1`
is HMAC-SHA256 over the bytes `` `<t>.<raw body>` ``. The timestamp is bound
_into_ the digest, which is what makes the replay window enforceable.

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

### Every other signed surface: `VerifyChatlySignature`

Event webhooks are one of **eight** signed requests Chatly sends, and the
header name does not tell you which construction you have — three surfaces
send `x-chatly-signature` and sign three different strings, and two send
**milliseconds**. `ChatlyWebhooks.VerifyChatlySignature` covers all eight, and
instead of `false` it tells you why:

```csharp
app.MapPost("/chatly/tool", async (HttpRequest request, ILogger<Program> logger) =>
{
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);

    var result = ChatlyWebhooks.VerifyChatlySignature(new ChatlySignatureInput
    {
        Surface = ChatlySignatureSurfaces.AiToolCall,
        Body = buffer.ToArray(),                                    // the RAW bytes
        Headers = ChatlySignatureInput.HeadersFrom(request.Headers),
        Secret = toolSecret,
        Method = request.Method,
        Path = request.Path + request.QueryString,                  // never the host
    });

    if (!result.Ok)
    {
        logger.LogWarning("{Reason}: {Message}", result.Reason, result.Message);
        return Results.Unauthorized();
    }

    return Results.Ok();
});
```

| `Surface`                 | Header                      | Signs                               | `t` unit |
| ------------------------- | --------------------------- | ----------------------------------- | -------- |
| `unified_v2`              | `x-chatly-signature-v2`     | `<t>.<METHOD>.<path>.<body>`        | seconds  |
| `agent_delivery`          | `x-chatly-signature`        | `<t>.<body>`                        | seconds  |
| `event_webhook`           | `x-livechat-signature`      | `<t>.<body>`                        | seconds  |
| `ai_tool_call`            | `x-chatly-signature`        | `<t>.<METHOD>.<path>.<body>`        | seconds  |
| `canvas_step`             | `x-chatly-canvas-signature` | `<ts>.<body>`, ts in its own header | **ms**   |
| `customer_card`           | `x-chatly-signature`        | `<ts>.<body>`, ts in its own header | **ms**   |
| `workflow_webhook`        | `x-chatly-signature-v2`     | `<t>.<body>` (or the unified base)  | seconds  |
| `workflow_webhook_legacy` | `x-chatly-signature`        | `<body>` alone — **no timestamp**   | n/a      |

What the result carries:

- **`Ok`**, and on success `Surface` (what actually verified — `unified_v2` when
  the v2 header was preferred), `Base`, `TimestampMs` (always milliseconds, so
  the two units compare; `null` on the legacy workflow surface) and
  `SecretIndex`.
- On failure, `Reason` — one of eleven stable strings in
  `ChatlySignatureFailure`, the same in every Chatly SDK — and a `Message`
  written to be logged.

**Pass `Method` and `Path` on every surface.** Every surface is moving to the
unified `x-chatly-signature-v2` header, sent alongside the legacy one. With the
default `V2 = ChatlyV2Policy.Prefer`, a receiver that passes both gets that
stronger check the day its sender starts emitting it, with no code change.
Once v2 is chosen it is the only answer: a bad v2 digest beside a good legacy
one is refused, not retried. `Require` refuses a request with no v2 header;
`Ignore` verifies only the surface's own header.

**Rotation works from both ends.** `Secrets` takes every secret live on the
surface and `SecretIndex` says which one matched — index into the list as you
passed it, empty entries included — which is how you learn the old secret is
finally unused. A header carrying two `v1=` digests verifies when either
matches. Set `Secret` or `Secrets`, and `Body` or `BodyText`, not both: both is
refused rather than guessed at.

It **never throws**. A header that arrived twice (under one name or two
casings) is refused as `duplicate_signature_header` rather than trial-verified;
a non-hex digest is `malformed_signature_header`, not a mismatch; a timestamp
that is not ASCII digits or does not fit in a `long` is `malformed_timestamp`;
stale and far-future timestamps are both `timestamp_outside_tolerance` (300 s
either way by default, exactly 300 s allowed). `workflow_webhook_legacy` is
refused as `replay_unprotected` unless you set `AllowUnprotectedReplay = true`,
because nothing can bound a replay of a request with no timestamp.

`HeadersFrom` copies ASP.NET Core's `request.Headers` or an
`HttpRequestMessage`'s headers into the shape `Headers` takes. Lookup walks
every key case-insensitively itself, so it does not matter what comparer your
dictionary uses.

### Minting one, to test your own handler: `SignChatlyV2`

The other direction, for a test that stands up a fake Chatly and fires a signed
request at your own endpoint. Until this existed you had to re-derive
`<t>.<METHOD>.<path>.<rawBody>` by hand from the documentation — and a
hand-derived base string is the one thing a signature test must not have: get
it wrong the same way in the test and nowhere else, and the test passes while
production refuses every delivery.

```csharp
var header = ChatlyWebhooks.SignChatlyV2(new ChatlyV2MintInput
{
    Base = ChatlyV2MintBase.Target,     // Target: <t>.<METHOD>.<path>.<body>
    Secret = secret,                    // Body:   <t>.<body>
    TimestampSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),   // SECONDS
    Method = "POST",
    Path = "/hooks/chatly?run=7",       // pathname + query, never the host
    Body = rawBody,                     // or BodyText, not both
});
// t=1760000000,v1=<64 hex>,b=t
```

`Base` has no sensible default: the two constructions are indistinguishable
from the value alone, which is precisely why the `b=` marker exists, so a
caller has to say which one they mean. `Target` emits `b=t` and `Body` emits
`b=b`; this never emits an absent marker, which on the wire means `Body`.
`ChatlyWebhooks.ChatlyV2BaseString(input)` returns the exact bytes the MAC is
taken over, so a test can pin the construction and not only the digest.

A record with named properties rather than six positional arguments, because
the construction is three strings in a row and
`SignChatlyV2(secret, t, method, path, body)` compiles with `path` and `body`
swapped — it then mints a digest nobody can reproduce, which arrives at the
receiver as "signature mismatch" and sends whoever wrote it hunting their own
secret. For the same reason this one **throws** on an input it cannot sign
faithfully (an undefined `Base`, an empty `Secret`, a negative
`TimestampSeconds`, no body, both bodies, or a `Target` with no `Method` or
`Path`), where the verifier never throws: a verifier that throws is an
accepted webhook in a handler that forgot to catch, but a mint that quietly
signs the wrong string is a test that passes while production fails.

The one wire value it will not produce on its own is the workflow action's
current **dual-digest** form, `t=<T>,v1=<target>,v1=<body>` with no marker.
Build it from two mints by taking the `v1=` field out of each — which is
exactly what the producer does, and what `tests/Checks/SignatureMintChecks.cs`
asserts.

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

163 key-accessible routes have a method here, and the API has more — the
live count was 174 open out of 909 on 2026-10-14, and it grows as routes are
opened to key auth. For the rest, `RequestAsync` goes through the same authentication,
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

A route that does _not_ accept an API key answers 403 no matter what you send:
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
and drives all 163 methods to assert the HTTP verb and path each one produces,
plus the response shape its decoder expects.

Running it also regenerates `surface.lock.json`, the committed record of the
request each method was _observed_ to issue, and fails if that disagrees with
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
