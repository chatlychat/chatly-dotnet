using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Tests;

/// <summary>
/// The routes whose request or response shape is not what the rest of the
/// surface does — and which therefore each cost a client a live bug.
/// </summary>
/// <remarks>
/// Both cases here shipped broken in several SDKs at once, for the same
/// reason: the manifest's flags were taken as the API's behaviour, and the
/// fake transports were generous enough to agree. The fixtures below answer
/// what the handlers actually answer.
/// </remarks>
public static class RouteShapeChecks
{
    /// <summary>
    /// The macro library decodes as a bare array, not as a page.
    /// </summary>
    /// <remarks>
    /// <c>GET /v1/macros</c> carries <c>@PaginationExempt</c> and its service
    /// returns <c>MacroDto[]</c>, so there is no <c>items</c> and no cursor.
    /// Decoding it as a page threw <c>unexpected_response_shape</c> against
    /// the real API while passing here, because the fake answered every route
    /// with a page envelope.
    /// </remarks>
    public static async Task MacroLibraryIsABareArray()
    {
        var handler = new FakeHandler(FakeResponse.Array(
            "[{\"id\":\"m_1\",\"name\":\"Refund\"},{\"id\":\"m_2\",\"name\":\"Escalate\"}]"));
        using var client = Fake.Client(handler);

        var macros = await client.Macros.ListAsync(CancellationToken.None);

        Expect.Equal(JsonValueKind.Array, macros.ValueKind, "the macro library is a JSON array");
        Expect.Equal(2, macros.GetArrayLength(), "both rows decoded");
        Expect.Equal(
            "Refund",
            macros.EnumerateArray().First().GetStringOrNull("name"),
            "in the order the service ranked them");
        Expect.Equal("/v1/macros", handler.Last.Uri.AbsolutePath, "the route");
        Expect.True(
            handler.Last.Uri.Query.Length == 0,
            "no cursor or limit is sent to a pagination-exempt route");
    }

    /// <summary>
    /// The macro library's own query parameters are sent, and no paging
    /// parameters are invented for it.
    /// </summary>
    public static async Task MacroLibraryTakesItsOwnFilters()
    {
        var handler = new FakeHandler(FakeResponse.Array());
        using var client = Fake.Client(handler);

        await client.Macros.ListAsync(
            new MacroListOptions { Scope = "team", Team = "tm_1", Query = "refund", Language = "fr" },
            CancellationToken.None);

        var request = handler.Last;
        Expect.Equal("team", Fake.QueryValues(request, "scope").SingleOrDefault(), "scope");
        Expect.Equal("tm_1", Fake.QueryValues(request, "team").SingleOrDefault(), "team");
        Expect.Equal("refund", Fake.QueryValues(request, "query").SingleOrDefault(), "query");
        Expect.Equal("fr", Fake.QueryValues(request, "language").SingleOrDefault(), "language");
        Expect.Equal(0, Fake.QueryValues(request, "cursor").Count, "no cursor is sent");
        Expect.Equal(0, Fake.QueryValues(request, "limit").Count, "no limit is sent");
    }

    /// <summary>
    /// Billing usage requires an event type, and the SDK refuses the call
    /// before spending a request on a certain 400.
    /// </summary>
    /// <remarks>
    /// <c>billing.controller.ts</c> validates <c>eventType</c> against a
    /// closed list and throws before doing anything else, so the optional
    /// argument four SDKs shipped could never succeed.
    /// </remarks>
    public static async Task BillingUsageRequiresAnEventType()
    {
        var handler = new FakeHandler(FakeResponse.Object("{\"total\":0}"));
        using var client = Fake.Client(handler);

        await Expect.ThrowsAsync<ArgumentException>(
            () => client.Billing.UsageAsync(string.Empty, null, null, CancellationToken.None),
            "an empty event type is refused");
        await Expect.ThrowsAsync<ArgumentException>(
            () => client.Billing.UsageAsync(new ChatlyQuery().Add("from", "2026-10-01T00:00:00Z"), CancellationToken.None),
            "a hand-built query with no eventType is refused");
        Expect.Equal(0, handler.CallCount, "neither refusal spent a request");

        // And the four accepted values are the API's closed list.
        Expect.Equal(4, ChatlyUsageEventTypes.All.Count, "four metered event types");
        Expect.Equal(
            "conversation.opened,message.sent.agent,message.sent.bot,ai.tokens",
            string.Join(",", ChatlyUsageEventTypes.All),
            "the list the handler validates against");
    }

    /// <summary>Billing usage sends the event type and an ISO-8601 window.</summary>
    public static async Task BillingUsageSendsEventTypeAndWindow()
    {
        var handler = new FakeHandler(FakeResponse.Object("{\"eventType\":\"ai.tokens\",\"total\":120}"));
        using var client = Fake.Client(handler);

        var result = await client.Billing.UsageAsync(
            ChatlyUsageEventTypes.AiTokens,
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        var request = handler.Last;
        Expect.Equal("/v1/billing/usage", request.Uri.AbsolutePath, "the route");
        Expect.Equal("ai.tokens", Fake.QueryValues(request, "eventType").SingleOrDefault(), "eventType");
        Expect.Contains(
            Fake.QueryValues(request, "from").SingleOrDefault() ?? string.Empty,
            "2026-10-01T00:00:00",
            "from is an ISO-8601 timestamp");
        Expect.Contains(
            Fake.QueryValues(request, "to").SingleOrDefault() ?? string.Empty,
            "2026-11-01T00:00:00",
            "to is an ISO-8601 timestamp");
        Expect.Equal(120, result.GetProperty("total").GetInt32(), "the summary decodes");

        // The window is optional; omitting it sends neither bound and the API
        // defaults to the current calendar month.
        await client.Billing.UsageAsync(ChatlyUsageEventTypes.ConversationOpened, null, null, CancellationToken.None);
        Expect.Equal(0, Fake.QueryValues(handler.Last, "from").Count, "from is omitted when not given");
        Expect.Equal(0, Fake.QueryValues(handler.Last, "to").Count, "to is omitted when not given");
        Expect.Equal(
            "conversation.opened",
            Fake.QueryValues(handler.Last, "eventType").SingleOrDefault(),
            "eventType is still sent");
    }
}
