using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Tests;

/// <summary>
/// What the client puts ON THE WIRE for the writes whose only input is a path
/// parameter, and the two refusals one of them answers that are not about the
/// caller's key.
/// </summary>
/// <remarks>
/// <para>
/// Same partial class as <c>ContractChecks.cs</c>, split out when that file
/// went past CONTRACT.md §9's 650-line ceiling — the same split, for the same
/// reason, as <c>SurfaceTable.cs</c> out of <c>SurfaceChecks.cs</c>. A
/// separate type would have meant renaming every entry in
/// <see cref="CheckRegistry"/> for a file-length limit.
/// </para>
/// <para>
/// Everything here asserts on the REQUEST the SDK issued, never on the canned
/// response. A fake that answers with whatever the SDK asked for agrees with
/// any bug — that is how two sibling SDKs shipped a paginator over a
/// bare-array route with green suites — so the subject is always
/// <c>handler.Last</c>.
/// </para>
/// </remarks>
public static partial class ContractChecks
{
    /// <summary>
    /// Every write whose only input is a path parameter sends that path, an
    /// <c>Idempotency-Key</c>, and no caller data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This asserts on <b>what the SDK sent</b>, not on what a fake answered.
    /// A fake that replies with whatever shape the SDK asked for agrees with
    /// any bug, which is how two SDKs shipped a paginator over a bare-array
    /// route with a green suite — so the subject here is
    /// <c>handler.Last</c>: the verb, the interpolated path, the header, and
    /// the absence of anything derived from the arguments.
    /// </para>
    /// <para>
    /// On the body: this transport sends the literal <c>{}</c> for every
    /// bodyless POST/PUT/PATCH, deliberately and transport-wide — see
    /// <c>Transport.SerializeBody</c>, and
    /// <see cref="BodylessWriteSendsAnEmptyObject"/>, which pins it. So "no
    /// request body" is asserted as "exactly <c>{}</c>, carrying none of the
    /// arguments": the path parameter must not leak into a body, and no field
    /// may be invented. The handlers behind these seven routes declare no
    /// <c>@Body()</c> at all, so the API never reads it.
    /// </para>
    /// <para>
    /// <c>visitors.start_conversation</c> is the row that matters most here.
    /// It sits under its controller's class-level
    /// <c>@RequireIdempotencyKey()</c> with no opt-out, so unlike
    /// <c>channels.rotate_secret</c> — <c>@NoIdempotency()</c> — the header is
    /// required rather than honoured, and a client that stopped sending it
    /// would get 428 in production.
    /// </para>
    /// </remarks>
    public static async Task PathOnlyWritesSendNoBodyAndAnIdempotencyKey()
    {
        var cases = new (string Name, Func<ChatlyClient, Task> Run, string Path)[]
        {
            ("ai.test_provider", c => c.Ai.TestProviderAsync("openai", CancellationToken.None), "/v1/ai-providers/openai/test"),
            ("bots.rotate_external_secret", c => c.Bots.RotateExternalSecretAsync("b_1", CancellationToken.None), "/v1/bots/b_1/external/rotate-secret"),
            ("channels.rotate_secret", c => c.Channels.RotateSecretAsync("ch_1", CancellationToken.None), "/v1/channels/ch_1/secret/rotate"),
            ("customer_cards.rotate_secret", c => c.CustomerCards.RotateSecretAsync("cc_1", CancellationToken.None), "/v1/customer-cards/cc_1/rotate-secret"),
            ("docs_sources.webhook", c => c.DocsSources.WebhookAsync("ds_1", CancellationToken.None), "/v1/docs-sources/ds_1/webhook"),
            ("knowledge_gaps.draft", c => c.KnowledgeGaps.DraftAsync("kg_1", CancellationToken.None), "/v1/knowledge-gaps/kg_1/draft"),
            ("visitors.start_conversation", c => c.Visitors.StartConversationAsync("cont_1", CancellationToken.None), "/v1/visitors/cont_1/start-conversation"),
        };

        foreach (var (name, run, path) in cases)
        {
            var handler = new FakeHandler(FakeResponse.Ok());
            using var client = Fake.Client(handler);

            await run(client);

            Expect.Equal(1, handler.CallCount, $"{name} issued exactly one request");
            Expect.Equal("POST", handler.Last.Verb, $"{name} is a POST");
            Expect.Equal(path, handler.Last.Path, $"{name} interpolated its id into the path");
            Expect.Equal(string.Empty, handler.Last.Uri.Query, $"{name} sends no query parameters");
            Expect.Equal("{}", handler.Last.Body, $"{name} sends no caller data in the body");
            Expect.NotNull(handler.Last.Header("idempotency-key"), $"{name} carries an Idempotency-Key");
        }
    }

    /// <summary>
    /// The two refusals a caller of
    /// <c>visitors.start_conversation</c> meets that are not about its key.
    /// </summary>
    /// <remarks>
    /// The route is gated by a workspace opt-in that is OFF by default
    /// (<c>settings.proactiveOutreach.apiKeysMayStartConversations</c>) and by
    /// a limit of three per contact per hour. Neither changes the response
    /// shape, so nothing in the surface lock can see them; both must arrive as
    /// the typed exceptions a caller branches on, and the 403 must not be
    /// retried — retrying an opt-in refusal three times is three audit entries
    /// for one mistake.
    /// </remarks>
    public static async Task StartConversationSurfacesTheOptInAndTheContactLimit()
    {
        /*
         * The envelope is serialized rather than built by interpolation.
         * `FakeResponse.Error` splices its message into a JSON string
         * literal, so it cannot carry the real message here: that message
         * quotes the JSON patch an admin has to send, and the embedded double
         * quotes would make the fixture emit a body the SDK cannot parse —
         * which looks exactly like the SDK dropping the message. Worth
         * knowing before reusing that helper for anything quote-bearing.
         */
        var optInMessage =
            "This workspace does not allow an API key to open conversations. Opening one sends a "
            + "message to a contact who did not write first, so it is a workspace decision and off "
            + "by default: an owner or admin enables it with `PATCH /v1/workspace/settings` "
            + "`{ \"proactiveOutreach\": { \"apiKeysMayStartConversations\": true } }`. "
            + "Nothing was sent.";
        var forbidden = new FakeHandler(new FakeResponse(
            403,
            JsonSerializer.Serialize(new { error = new { code = "forbidden", message = optInMessage } }),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["x-request-id"] = "req_optin" }));
        using var client = Fake.Client(forbidden);

        var denied = await Expect.ThrowsAsync<ChatlyPermissionException>(
            () => client.Visitors.StartConversationAsync("cont_1", CancellationToken.None),
            "the workspace opt-in refusal is a permission error, not a generic one");
        Expect.Contains(
            denied.Message,
            "apiKeysMayStartConversations",
            "and the message naming the setting survives, so a 403 is diagnosable");
        Expect.Equal(1, forbidden.CallCount, "a 403 is not retried");

        /*
         * The per-contact 429 as the server really sends it: NO Retry-After
         * header, and the TTL in the body at
         * `error.details.retryAfterSeconds`.
         *
         * An earlier draft of this check faked a `retry-after: 1800` header
         * and asserted RetryAfter was 1800. It passed, and it was fiction:
         * `consumePerContactOutreachToken` throws `RateLimitedError(ttl)`,
         * whose details carry the seconds, and `app-exception.filter.ts` sends
         * `wireError(...)` and sets no headers at all. The only plain
         * `Retry-After` on this API is synthesised by a Fastify onSend hook
         * from the per-bucket headers `@nestjs/throttler` emits, and this
         * refusal does not come from the throttler. So a fixture with that
         * header invents the server's shape and the suite then agrees with the
         * client instead of with the server — the trap `wireError`'s own
         * docstring exists to warn about.
         *
         * The route's OTHER 429, from the global throttler buckets, does carry
         * the header; `RateLimitCarriesRetryAfter` covers that path.
         *
         * `RetryAfter` being null here is measured, not assumed, and it is a
         * fact about THIS client rather than about the route:
         * `Transport.ReadRetryAfter` reads the header and has no body
         * fallback, while the Ruby and Python clients do fall back to
         * `details.retryAfterSeconds` and report 1800 for the same response.
         * CONTRACT.md §10.4's "a 429 ... with retry_after populated" is
         * therefore language-dependent on this one route. If .NET ever gains
         * the fallback, this assertion is the thing that should fail.
         */
        var ttlBody = JsonSerializer.Serialize(new
        {
            error = new
            {
                code = "rate_limited",
                message = "Too many requests",
                details = new { retryAfterSeconds = 1800 },
            },
        });
        var throttled = new FakeHandler(new FakeResponse(
            429,
            ttlBody,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["x-request-id"] = "req_rl_contact" }));
        using var second = Fake.Client(
            throttled,
            new ChatlyClientOptions { ApiKey = "ck_test_key", Retry = ChatlyRetryPolicy.None });

        var busy = await Expect.ThrowsAsync<ChatlyRateLimitException>(
            () => second.Visitors.StartConversationAsync("cont_1", CancellationToken.None),
            "the per-contact hourly limit is a rate-limit error");
        Expect.True(
            busy.RetryAfter is null,
            "and RetryAfter is NULL on it, because this refusal sends no Retry-After header");
        Expect.Contains(
            busy.Body ?? string.Empty,
            "\"retryAfterSeconds\":1800",
            "the window's remaining TTL is only readable from the error body");
        Expect.Equal("rate_limited", busy.Code, "read from the nested envelope");
        Expect.Equal("req_rl_contact", busy.RequestId, "with the request id");
    }
}
