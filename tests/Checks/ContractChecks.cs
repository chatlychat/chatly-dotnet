using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Tests;

/// <summary>
/// Items 1-9 of the ten CONTRACT.md §10 requires of every Chatly backend SDK,
/// plus the cases that caused real incidents in sibling SDKs. Item 10, the
/// response-shape lock, lives in <see cref="SurfaceChecks"/> instead, because
/// that is the file which writes surface.lock.json.
/// </summary>
public static class ContractChecks
{
    // ── §10.1 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The default base URL is the production host — asserted on the URL a
    /// request actually went to, not just on the property.
    /// </summary>
    /// <remarks>
    /// Four SDKs in this product shipped defaulting to <c>api.livechat.local</c>
    /// or <c>api.chatly.example</c>. Both resolve nowhere. No test caught it
    /// because every test passed an explicit URL, which is exactly the hole
    /// this check closes.
    /// </remarks>
    public static async Task DefaultBaseUrlIsProduction()
    {
        var handler = new FakeHandler(FakeResponse.Ok("{\"workspaceId\":\"ws_1\"}"));
        using var client = Fake.Client(handler, new ChatlyClientOptions { ApiKey = "ck_test_key" });

        Expect.Equal("https://api.chatlychat.com/", client.BaseUrl.ToString(), "default BaseUrl");

        await client.WhoAmIAsync(CancellationToken.None);

        Expect.Equal(
            "https://api.chatlychat.com/v1/api-keys/whoami",
            handler.Last.Uri.GetLeftPart(UriPartial.Path),
            "the URL the request was sent to");

        var host = ChatlyClientOptions.ProductionBaseUrl.Host;
        Expect.False(host.EndsWith(".local", StringComparison.Ordinal), $"host <{host}> is not a .local name");
        Expect.False(host.EndsWith(".example", StringComparison.Ordinal), $"host <{host}> is not an .example name");
        Expect.False(host.Contains("localhost", StringComparison.Ordinal), $"host <{host}> is not localhost");
    }

    /// <summary>A self-hosted base URL with a path prefix is preserved.</summary>
    public static async Task BaseUrlPathPrefixIsKept()
    {
        var handler = new FakeHandler(FakeResponse.Ok());
        using var client = Fake.Client(handler, new ChatlyClientOptions
        {
            ApiKey = "ck_test_key",
            BaseUrl = new Uri("https://chat.example.internal/chatly/"),
        });

        await client.Workspace.LabelsAsync(CancellationToken.None);

        Expect.Equal(
            "https://chat.example.internal/chatly/v1/labels",
            handler.Last.Uri.ToString(),
            "self-hosted prefix is preserved and not doubled");
    }

    /// <summary>An empty key fails at construction, not at the first call.</summary>
    public static Task EmptyApiKeyIsRejectedAtConstruction()
    {
        Expect.Throws<ArgumentException>(
            () => new ChatlyClient(string.Empty),
            "an empty API key throws from the constructor");
        Expect.Throws<ArgumentException>(
            () => new ChatlyClient(new ChatlyClientOptions { ApiKey = "   " }),
            "a whitespace API key throws from the constructor");
        return Task.CompletedTask;
    }

    // ── §10.2 ─────────────────────────────────────────────────────────────

    /// <summary>Every request carries the key as a bearer token, plus the SDK's own headers.</summary>
    public static async Task AuthorizationCarriesTheKey()
    {
        var handler = new FakeHandler(FakeResponse.Page());
        using var client = Fake.Client(handler, new ChatlyClientOptions { ApiKey = "ck_live_abc123" });

        await client.Contacts.ListAsync(CancellationToken.None);

        var request = handler.Last;
        Expect.Equal("Bearer ck_live_abc123", request.Header("authorization"), "Authorization header");
        Expect.Equal("application/json", request.Header("accept"), "Accept header");
        Expect.Contains(request.Header("user-agent") ?? string.Empty, "chatly-sdk-dotnet/", "User-Agent header");
    }

    /// <summary>A caller-supplied suffix is appended to the SDK's User-Agent, not replacing it.</summary>
    public static async Task UserAgentSuffixIsAppended()
    {
        var handler = new FakeHandler(FakeResponse.Page());
        using var client = Fake.Client(handler, new ChatlyClientOptions
        {
            ApiKey = "ck_test_key",
            UserAgentSuffix = "acme-billing/2.1",
        });

        await client.Contacts.ListAsync(CancellationToken.None);

        var agent = handler.Last.Header("user-agent") ?? string.Empty;
        Expect.Contains(agent, "chatly-sdk-dotnet/", "SDK identity survives a suffix");
        Expect.Contains(agent, "acme-billing/2.1", "caller suffix is present");
    }

    // ── §10.3 ─────────────────────────────────────────────────────────────

    /// <summary>A write carries an Idempotency-Key; a read does not.</summary>
    public static async Task WriteSendsAnIdempotencyKey()
    {
        var handler = new FakeHandler(FakeResponse.Ok());
        using var client = Fake.Client(handler);

        await client.Contacts.UpsertAsync(new ContactInput { Email = "a@example.com" }, CancellationToken.None);
        var write = handler.Last;
        Expect.NotNull(write.Header("idempotency-key"), "POST carries Idempotency-Key");
        Expect.True(
            Guid.TryParse(write.Header("idempotency-key"), out _),
            "the minted key is a UUID");

        await client.Contacts.GetAsync("c_1", CancellationToken.None);
        Expect.True(handler.Last.Header("idempotency-key") is null, "GET carries no Idempotency-Key");
    }

    /// <summary>Two separate writes mint two different keys.</summary>
    public static async Task TwoWritesSendDifferentIdempotencyKeys()
    {
        var handler = new FakeHandler(FakeResponse.Ok());
        using var client = Fake.Client(handler);

        await client.Contacts.UpsertAsync(new ContactInput { Email = "a@example.com" }, CancellationToken.None);
        var first = handler.Last.Header("idempotency-key");
        await client.Contacts.UpsertAsync(new ContactInput { Email = "b@example.com" }, CancellationToken.None);
        var second = handler.Last.Header("idempotency-key");

        Expect.NotNull(first, "first key");
        Expect.NotNull(second, "second key");
        Expect.NotEqual(first, second, "two writes must not share a minted key");
    }

    /// <summary>A pinned key is sent on every write, unchanged.</summary>
    public static async Task PinnedIdempotencyKeyRepeats()
    {
        var handler = new FakeHandler(FakeResponse.Ok());
        using var client = Fake.Client(handler);
        var pinned = client.WithIdempotencyKey("order-4471-refund");

        await pinned.Contacts.UpsertAsync(new ContactInput { Email = "a@example.com" }, CancellationToken.None);
        await pinned.Contacts.UpsertAsync(new ContactInput { Email = "a@example.com" }, CancellationToken.None);

        Expect.Equal("order-4471-refund", handler.Requests[0].Header("idempotency-key"), "first pinned write");
        Expect.Equal("order-4471-refund", handler.Requests[1].Header("idempotency-key"), "second pinned write");

        // The original client is untouched, and its resources still route
        // through its own transport — the Node SDK's derive() forgot to rebind
        // one namespace and that bug was invisible until it mattered.
        await client.Contacts.UpsertAsync(new ContactInput { Email = "a@example.com" }, CancellationToken.None);
        Expect.NotEqual(
            "order-4471-refund",
            handler.Last.Header("idempotency-key"),
            "the parent client still mints its own key");
        Expect.True(client.PinnedIdempotencyKey is null, "the parent client was not mutated");

        Expect.Throws<ArgumentException>(
            () => client.WithIdempotencyKey(string.Empty),
            "an empty pinned key is refused");
    }

    /// <summary>
    /// Every namespace on a derived client goes through the derived transport.
    /// </summary>
    /// <remarks>
    /// The Node SDK rebuilt its namespaces by hand when deriving a client and
    /// missed <c>search</c>, so a derived client's search calls went through
    /// the parent. Here the pinned key is the observable difference, so one
    /// write per namespace proves the binding.
    /// </remarks>
    public static async Task DerivedClientRebindsEveryWriteNamespace()
    {
        var handler = new FakeHandler(FakeResponse.Ok());
        using var client = Fake.Client(handler);
        var pinned = client.WithIdempotencyKey("pinned-key");

        var writes = new List<Func<Task>>
        {
            () => pinned.Contacts.UpsertAsync(new ContactInput(), CancellationToken.None),
            () => pinned.Conversations.SendAsync("k_1", new MessageInput { Body = "hi" }, CancellationToken.None),
            () => pinned.Tickets.BulkAsync(new { ids = Array.Empty<string>() }, CancellationToken.None),
            () => pinned.Macros.CreateAsync(new { name = "m" }, CancellationToken.None),
            () => pinned.Events.TrackAsync("c_1", "trial_started", null, CancellationToken.None),
            () => pinned.Webhooks.RedeliverAsync("d_1", CancellationToken.None),
            () => pinned.RequestAsync(HttpMethod.Post, "/v1/tickets/t_1/time-entries", null, new { minutes = 1 }, CancellationToken.None),
        };

        foreach (var write in writes)
        {
            await write();
            Expect.Equal("pinned-key", handler.Last.Header("idempotency-key"), $"{handler.Last.Path} used the derived transport");
        }
    }

    // ── §10.4 ─────────────────────────────────────────────────────────────

    /// <summary>Each status maps to its own exception type, carrying the request id.</summary>
    public static async Task StatusCodesMapToTypedErrors()
    {
        await AssertStatusMaps<ChatlyAuthenticationException>(401, "unauthorized");
        await AssertStatusMaps<ChatlyPermissionException>(403, "forbidden");
        await AssertStatusMaps<ChatlyNotFoundException>(404, "not_found");
        await AssertStatusMaps<ChatlyConflictException>(409, "conflict");
        await AssertStatusMaps<ChatlyValidationException>(422, "validation_failed");
        await AssertStatusMaps<ChatlyValidationException>(400, "validation_failed");
        await AssertStatusMaps<ChatlyServerException>(500, "unknown");
        await AssertStatusMaps<ChatlyServerException>(503, "service_unavailable");
    }

    /// <summary>A 429 is a rate-limit error with Retry-After parsed, and a request id.</summary>
    public static async Task RateLimitCarriesRetryAfter()
    {
        var response = FakeResponse.Error(429, "rate_limited", "Slow down", "req_rl_1");
        response.Headers["retry-after"] = "7";
        var handler = new FakeHandler(response);
        using var client = Fake.Client(
            handler,
            new ChatlyClientOptions { ApiKey = "ck_test_key", Retry = ChatlyRetryPolicy.None });

        var error = await Expect.ThrowsAsync<ChatlyRateLimitException>(
            () => client.Contacts.ListAsync(CancellationToken.None),
            "a 429 raises the rate-limit type");

        Expect.Equal(429, error.Status, "status");
        Expect.Equal("rate_limited", error.Code, "code, read from the nested error envelope");
        Expect.Equal("req_rl_1", error.RequestId, "request id from x-request-id");
        Expect.Equal(TimeSpan.FromSeconds(7), error.RetryAfter, "Retry-After parsed from delay-seconds");
        Expect.Contains(error.Body ?? string.Empty, "Slow down", "the raw body is kept");
    }

    /// <summary>Retry-After as an HTTP-date becomes a delay from now.</summary>
    public static async Task RetryAfterAcceptsAnHttpDate()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var response = FakeResponse.Error(429, "rate_limited", "Slow down");
        response.Headers["retry-after"] = now.AddSeconds(30).ToString("R");
        var handler = new FakeHandler(response);
        using var client = Fake.Client(handler, new ChatlyClientOptions
        {
            ApiKey = "ck_test_key",
            Retry = ChatlyRetryPolicy.None,
            Clock = () => now,
        });

        var error = await Expect.ThrowsAsync<ChatlyRateLimitException>(
            () => client.Contacts.ListAsync(CancellationToken.None),
            "a 429 raises the rate-limit type");

        Expect.Equal(TimeSpan.FromSeconds(30), error.RetryAfter, "Retry-After parsed from an HTTP-date");
    }

    /// <summary>A dead host is a transport error, not an HTTP status.</summary>
    public static async Task TransportFailureIsItsOwnType()
    {
        var handler = new ThrowingHandler(() => new HttpRequestException("no such host"));
        using var client = Fake.Client(
            handler,
            new ChatlyClientOptions { ApiKey = "ck_test_key", Retry = ChatlyRetryPolicy.None });

        var error = await Expect.ThrowsAsync<ChatlyTransportException>(
            () => client.Contacts.ListAsync(CancellationToken.None),
            "a connect failure raises the transport type");

        Expect.Equal(0, error.Status, "a transport failure has no HTTP status");
        Expect.True(error.IsRetryable, "a transport failure is retryable");
        Expect.NotNull(error.InnerException, "the underlying exception is kept");
    }

    /// <summary>A caller's cancellation is not dressed up as a transport error.</summary>
    public static async Task CallerCancellationPropagates()
    {
        var handler = new FakeHandler(FakeResponse.Ok());
        using var client = Fake.Client(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Expect.ThrowsAsync<OperationCanceledException>(
            () => client.Contacts.ListAsync(cts.Token),
            "a cancelled token surfaces as OperationCanceledException");
        Expect.Equal(0, handler.CallCount, "nothing was sent");
    }

    // ── §10.5 ─────────────────────────────────────────────────────────────

    /// <summary>A 429 is retried and then succeeds; the retry reuses the same key.</summary>
    public static async Task RateLimitIsRetriedThenSucceeds()
    {
        var handler = FakeHandler.Sequence(
            FakeResponse.Error(429, "rate_limited", "Slow down"),
            FakeResponse.Ok("{\"id\":\"c_1\"}"));
        using var client = Fake.Client(handler);

        var result = await client.Contacts.UpsertAsync(
            new ContactInput { Email = "a@example.com" },
            CancellationToken.None);

        Expect.Equal(2, handler.CallCount, "one retry after the 429");
        Expect.Equal("c_1", result.GetStringOrNull("id"), "the second attempt's body is returned");
        Expect.Equal(
            handler.Requests[0].Header("idempotency-key"),
            handler.Requests[1].Header("idempotency-key"),
            "a retried write reuses the Idempotency-Key — the whole reason the header exists");
    }

    /// <summary>A 5xx is retried.</summary>
    public static async Task ServerErrorIsRetried()
    {
        var handler = FakeHandler.Sequence(
            FakeResponse.Error(503, "service_unavailable", "Down"),
            FakeResponse.Page());
        using var client = Fake.Client(handler);

        await client.Contacts.ListAsync(CancellationToken.None);
        Expect.Equal(2, handler.CallCount, "one retry after the 503");
    }

    /// <summary>A 400 is never retried.</summary>
    public static async Task ValidationErrorIsNotRetried()
    {
        var handler = new FakeHandler(FakeResponse.Error(400, "validation_failed", "Invalid request body"));
        using var client = Fake.Client(handler);

        await Expect.ThrowsAsync<ChatlyValidationException>(
            () => client.Contacts.UpsertAsync(new ContactInput(), CancellationToken.None),
            "a 400 raises the validation type");

        Expect.Equal(1, handler.CallCount, "a 400 is not retried — it would fail identically");
    }

    /// <summary>A 404 is not retried either.</summary>
    public static async Task NotFoundIsNotRetried()
    {
        var handler = new FakeHandler(FakeResponse.Error(404, "not_found", "No such contact"));
        using var client = Fake.Client(handler);

        await Expect.ThrowsAsync<ChatlyNotFoundException>(
            () => client.Contacts.GetAsync("c_missing", CancellationToken.None),
            "a 404 raises the not-found type");
        Expect.Equal(1, handler.CallCount, "a 404 is not retried");
    }

    /// <summary>Retries stop at MaxAttempts and then throw the last error.</summary>
    public static async Task RetriesStopAtMaxAttempts()
    {
        var handler = new FakeHandler(FakeResponse.Error(503, "service_unavailable", "Down"));
        using var client = Fake.Client(handler);

        await Expect.ThrowsAsync<ChatlyServerException>(
            () => client.Contacts.ListAsync(CancellationToken.None),
            "the last 503 is thrown");
        Expect.Equal(3, handler.CallCount, "three attempts by default, not more");
    }

    // ── §10.6 ─────────────────────────────────────────────────────────────

    /// <summary>The auto-paginator walks two pages and stops on a null cursor.</summary>
    public static async Task PaginatorWalksTwoPagesAndStops()
    {
        var handler = new FakeHandler((request, index) => index switch
        {
            0 => FakeResponse.Page("[{\"id\":\"c_1\"},{\"id\":\"c_2\"}]", "cursor_2"),
            _ => FakeResponse.Page("[{\"id\":\"c_3\"}]"),
        });
        using var client = Fake.Client(handler);

        var ids = new List<string?>();
        await foreach (var contact in client.Contacts.ListAllAsync(CancellationToken.None))
        {
            ids.Add(contact.GetStringOrNull("id"));
        }

        Expect.Equal(3, ids.Count, "every item from both pages");
        Expect.Equal("c_1,c_2,c_3", string.Join(",", ids), "in server order");
        Expect.Equal(2, handler.CallCount, "two requests, then it stopped on the null cursor");
        Expect.Equal(
            "cursor_2",
            Fake.QueryValues(handler.Requests[1], "cursor").SingleOrDefault(),
            "the second request carried the first page's cursor");
        Expect.True(
            Fake.QueryValues(handler.Requests[0], "cursor").Count == 0,
            "the first request carried no cursor");
    }

    /// <summary>A server that repeats a cursor ends the walk instead of looping forever.</summary>
    public static async Task PaginatorStopsOnARepeatedCursor()
    {
        // Every page hands back the same cursor. Without the repeated-cursor
        // guard this is an infinite loop that burns the caller's rate limit.
        var handler = new FakeHandler(FakeResponse.Page("[{\"id\":\"c_1\"}]", "same_cursor"));
        using var client = Fake.Client(handler);

        var count = 0;
        await foreach (var _ in client.Contacts.ListAllAsync(CancellationToken.None))
        {
            count++;
            if (count > 50)
            {
                throw new CheckFailedException("the paginator did not stop on a repeated cursor");
            }
        }

        Expect.Equal(2, count, "the repeated cursor was followed once, then the walk stopped");
        Expect.Equal(2, handler.CallCount, "two requests only");
    }

    /// <summary>The single-page call returns the page as the server sent it.</summary>
    public static async Task SinglePageIsReturnedAsIs()
    {
        var handler = new FakeHandler(FakeResponse.Page("[{\"id\":\"c_1\"}]", "cursor_2"));
        using var client = Fake.Client(handler);

        var page = await client.Contacts.ListAsync(new ListOptions { Limit = 1 }, CancellationToken.None);

        Expect.Equal(1, page.Items.Count, "one item");
        Expect.Equal("cursor_2", page.NextCursor, "the cursor is exposed");
        Expect.True(page.HasMore, "HasMore follows the cursor");
        Expect.Equal("1", Fake.QueryValues(handler.Last, "limit").SingleOrDefault(), "limit was sent");
    }

    /// <summary>A response that is not a page fails loudly rather than reading as empty.</summary>
    public static async Task NonPageResponseThrows()
    {
        var handler = new FakeHandler(FakeResponse.Ok("{\"total\":3}"));
        using var client = Fake.Client(handler);

        var error = await Expect.ThrowsAsync<ChatlyException>(
            () => client.Contacts.ListAsync(CancellationToken.None),
            "a non-page body raises");
        Expect.Equal("unexpected_response_shape", error.Code, "the shape error is identifiable");
    }

    /// <summary>Repeated query keys stay repeated; they are never comma-joined.</summary>
    public static async Task RepeatedQueryKeysStayRepeated()
    {
        // The search route answers an object; the contacts route below answers
        // a page. One responder, two shapes, so the check can assert query
        // encoding on both a plain GET and a paged one.
        var handler = new FakeHandler((request, _) => request.Path == "/v1/contacts"
            ? FakeResponse.Page()
            : FakeResponse.Ok("{}"));
        using var client = Fake.Client(handler);

        await client.Search.QueryAsync(
            "refund policy",
            // `ticket`, not `article`: SEARCH_KINDS is contact|conversation|
            // message|ticket|kb, so `article` is a 400.
            new SearchOptions { Kinds = new[] { "kb", "ticket" }, Limit = 5 },
            CancellationToken.None);

        // PLAIN `kinds`. This asserted the bracketed spelling, which the API's
        // zod object schema strips — so the assertion held the bug in place:
        // the search ran unnarrowed and answered 200.
        var kinds = Fake.QueryValues(handler.Last, "kinds");
        Expect.Equal(2, kinds.Count, "kinds appears twice");
        Expect.Equal("kb,ticket", string.Join(",", kinds), "both values, as separate parameters");
        Expect.Equal("refund policy", Fake.QueryValues(handler.Last, "q").SingleOrDefault(), "q is encoded, not mangled");
        Expect.Contains(handler.Last.Uri.Query, "kinds=kb", "sent as a plain repeated key");
        Expect.Equal(0, Fake.QueryValues(handler.Last, "kinds[]").Count, "no bracketed key is sent");

        await client.Contacts.ListAsync(
            new ListOptions { Filter = new[] { "status:open", "channel:web" }, Sort = "-createdAt", Fields = new[] { "id", "email" } },
            CancellationToken.None);

        Expect.Equal(2, Fake.QueryValues(handler.Last, "filter[]").Count, "filter[] repeats");
        Expect.Equal("-createdAt", Fake.QueryValues(handler.Last, "sort").SingleOrDefault(), "sort is sent");
        Expect.Equal("id,email", Fake.QueryValues(handler.Last, "fields").SingleOrDefault(), "fields is comma-joined");
    }

    // ── §8: the escape hatch ──────────────────────────────────────────────

    /// <summary>The escape hatch gets the same auth, idempotency and error handling.</summary>
    public static async Task EscapeHatchUsesTheSamePipeline()
    {
        var handler = FakeHandler.Sequence(
            FakeResponse.Error(429, "rate_limited", "Slow down"),
            FakeResponse.Ok("{\"ok\":true}"));
        using var client = Fake.Client(handler);

        var result = await client.RequestAsync(
            HttpMethod.Post,
            "/v1/tickets/t_1/time-entries",
            new ChatlyQuery().Add("notify", true),
            new { minutes = 15, note = "call" },
            CancellationToken.None);

        Expect.Equal(2, handler.CallCount, "the escape hatch is retried like any other call");
        Expect.Equal("Bearer ck_test_key", handler.Last.Header("authorization"), "it carries the key");
        Expect.NotNull(handler.Last.Header("idempotency-key"), "a POST through it carries an idempotency key");
        Expect.Equal(
            handler.Requests[0].Header("idempotency-key"),
            handler.Requests[1].Header("idempotency-key"),
            "and reuses it on the retry");
        Expect.Equal("true", Fake.QueryValues(handler.Last, "notify").SingleOrDefault(), "query parameters are sent");
        Expect.Contains(handler.Last.Body ?? string.Empty, "\"minutes\":15", "the body is serialized camelCase");
        Expect.True(result.GetProperty("ok").GetBoolean(), "the decoded response comes back");

        var typed = await client.RequestAsync<Dictionary<string, bool>>(
            HttpMethod.Get,
            "/v1/labels",
            null,
            null,
            CancellationToken.None);
        Expect.NotNull(typed, "the generic overload deserializes");

        Expect.Throws<ArgumentException>(
            () => client.RequestAsync(HttpMethod.Get, "v1/labels").GetAwaiter().GetResult(),
            "a path without a leading slash is refused");
    }

    /// <summary>A 204 or an empty body decodes as JSON null, not an exception.</summary>
    public static async Task EmptyResponseBodyDecodesAsNull()
    {
        var handler = new FakeHandler(new FakeResponse(204, string.Empty));
        using var client = Fake.Client(handler);

        var result = await client.Macros.DeleteAsync("m_1", CancellationToken.None);

        Expect.Equal(JsonValueKind.Null, result.ValueKind, "a 204 decodes as JSON null");
        Expect.Equal("DELETE", handler.Last.Verb, "it was a DELETE");
        Expect.True(handler.Last.Body is null or "", "a DELETE with no body sends no content");
    }

    /// <summary>A write with no body still sends {} so the API's JSON parser is happy.</summary>
    public static async Task BodylessWriteSendsAnEmptyObject()
    {
        var handler = new FakeHandler(FakeResponse.Ok());
        using var client = Fake.Client(handler);

        await client.Webhooks.RedeliverAsync("d_1", CancellationToken.None);

        Expect.Equal("POST", handler.Last.Verb, "redeliver is a POST");
        Expect.Equal("{}", handler.Last.Body, "a bodyless POST sends an empty JSON object, not nothing");
        Expect.Contains(
            handler.Last.Header("content-type") ?? string.Empty,
            "application/json",
            "with a JSON content type");
    }

    /// <summary>Null fields are omitted from a serialized body rather than sent as null.</summary>
    public static async Task NullFieldsAreOmittedFromBodies()
    {
        var handler = new FakeHandler(FakeResponse.Ok());
        using var client = Fake.Client(handler);

        await client.Contacts.UpsertAsync(
            new ContactInput { Email = "a@example.com", ExternalId = "u_7" },
            CancellationToken.None);

        var body = handler.Last.Body ?? string.Empty;
        Expect.Contains(body, "\"email\":\"a@example.com\"", "set fields are sent camelCase");
        Expect.Contains(body, "\"externalId\":\"u_7\"", "externalId is camelCase");
        Expect.False(body.Contains("\"phone\"", StringComparison.Ordinal), "unset fields are omitted, not nulled");
    }

    /// <summary>A path parameter is percent-encoded, and an empty one is refused.</summary>
    public static async Task PathParametersAreEncoded()
    {
        var handler = new FakeHandler(FakeResponse.Ok());
        using var client = Fake.Client(handler);

        await client.Contacts.RemoveTagAsync("c_1", "needs follow-up/urgent", CancellationToken.None);

        Expect.Equal(
            "/v1/contacts/c_1/tags/needs%20follow-up%2Furgent",
            handler.Last.Uri.AbsolutePath,
            "the tag is encoded so the slash cannot change the route");

        await Expect.ThrowsAsync<ArgumentException>(
            () => client.Contacts.GetAsync(string.Empty, CancellationToken.None),
            "an empty id is refused before a request is sent");
    }

    private static async Task AssertStatusMaps<TException>(int status, string code)
        where TException : ChatlyException
    {
        var handler = new FakeHandler(FakeResponse.Error(status, code, $"status {status}", "req_" + status));
        using var client = Fake.Client(
            handler,
            new ChatlyClientOptions { ApiKey = "ck_test_key", Retry = ChatlyRetryPolicy.None });

        var error = await Expect.ThrowsAsync<TException>(
            () => client.Contacts.GetAsync("c_1", CancellationToken.None),
            $"HTTP {status} raises {typeof(TException).Name}");

        Expect.Equal(status, error.Status, $"{status}: status");
        Expect.Equal(code, error.Code, $"{status}: code from the nested envelope");
        Expect.Equal("req_" + status, error.RequestId, $"{status}: request id");
        Expect.NotNull(error.Body, $"{status}: raw body is kept");
    }
}
