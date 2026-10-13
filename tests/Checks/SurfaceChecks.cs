using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Tests;

/// <summary>
/// Every method in <c>packages/sdk-spec/surface.json</c>, driven once, with
/// the HTTP verb, the path and the response shape it produced asserted against
/// the spec.
/// </summary>
/// <remarks>
/// <para>
/// The table below is written out by hand rather than read from
/// <c>surface.json</c>, so that this package is self-contained — and because
/// a check that reads the same file the implementation was written from can
/// only catch a typo, not a misreading. Keep it in step with the spec: 55
/// entries, and the count is asserted.
/// </para>
/// <para>
/// This is what catches a wrong path. A path typo is invisible to every other
/// test here — the fake transport answers anything — and arrives as a 404 in
/// someone else's integration.
/// </para>
/// </remarks>
public static class SurfaceChecks
{
    /// <summary>One expected call: the spec entry, and the SDK method that makes it.</summary>
    public sealed class SurfaceEntry
    {
        public SurfaceEntry(
            string name,
            string verb,
            string path,
            ResponseShape shape,
            Func<ChatlyClient, CancellationToken, Task<object?>> invoke)
        {
            Name = name;
            Verb = verb;
            Path = path;
            Shape = shape;
            Invoke = invoke;
        }

        public string Name { get; }

        public string Verb { get; }

        public string Path { get; }

        /// <summary>
        /// The shape this route answers with, which the fake transport is fed
        /// when the method is driven.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Declared per route, not assumed, and a <b>required</b> constructor
        /// argument. It used to default to <see cref="ResponseShape.Object"/>,
        /// and because that is the commonest value nobody noticed that
        /// thirteen routes answering a bare JSON array were silently declared
        /// objects — the fake fed them <c>{}</c> and the suite agreed with the
        /// fixture. A default that happens to be right most of the time is how
        /// wrong rows stay invisible, so there is none.
        /// </para>
        /// <para>
        /// Feeding every route a page envelope is what let
        /// <c>Macros.ListAsync</c> pass here while failing against the real
        /// API, which answers that route with a bare array.
        /// <see cref="DeclaredShapesMatchTheManifestAsync"/> diffs this whole
        /// column against the manifest's four-valued <c>shape</c>, and
        /// <see cref="EveryMethodHitsTheSpecifiedRoute"/> asserts the value
        /// each method hands back really has the shape declared here.
        /// </para>
        /// </remarks>
        public ResponseShape Shape { get; }

        /// <summary>
        /// Calls the method and returns what it handed back, boxed.
        /// </summary>
        /// <remarks>
        /// The return value is kept — it used to be discarded as a bare
        /// <c>Task</c> — because the request being right says nothing about
        /// the decode. <see cref="Shapes.Of"/> names the shape of whatever
        /// comes out of here.
        /// </remarks>
        public Func<ChatlyClient, CancellationToken, Task<object?>> Invoke { get; }
    }

    /// <summary>
    /// The 55 entries of surface.json, in spec order, each with the response
    /// shape its handler's <c>@ZodResponse</c> schema produces.
    /// </summary>
    /// <remarks>
    /// Three rows look wrong and are not. <c>contacts.remove_tag</c> is a
    /// DELETE that answers 200 with the surviving contact;
    /// <c>conversations.cancel_scheduled</c> is a DELETE that answers 200 with
    /// the row it just cancelled; <c>macros.delete</c> is the only real 204 on
    /// the surface. And two of the four <c>reports.*</c> routes are bare
    /// arrays while the other two are objects.
    /// </remarks>
    public static IReadOnlyList<SurfaceEntry> Entries { get; } = new List<SurfaceEntry>
    {
        new SurfaceEntry("root.whoami", "GET", "/v1/api-keys/whoami", ResponseShape.Object, async (c, t) => await c.WhoAmIAsync(t)),

        new SurfaceEntry("contacts.list", "GET", "/v1/contacts", ResponseShape.Page, async (c, t) => await c.Contacts.ListAsync(t)),
        new SurfaceEntry("contacts.get", "GET", "/v1/contacts/c_1", ResponseShape.Object, async (c, t) => await c.Contacts.GetAsync("c_1", t)),
        new SurfaceEntry("contacts.upsert", "POST", "/v1/contacts", ResponseShape.Object, async (c, t) => await c.Contacts.UpsertAsync(new ContactInput { Email = "a@example.com" }, t)),
        new SurfaceEntry("contacts.bulk_upsert", "POST", "/v1/contacts/bulk", ResponseShape.Object, async (c, t) => await c.Contacts.BulkUpsertAsync(new[] { new ContactInput { Email = "a@example.com" } }, t)),
        new SurfaceEntry("contacts.merge", "POST", "/v1/contacts/c_1/merge", ResponseShape.Object, async (c, t) => await c.Contacts.MergeAsync("c_1", "c_2", t)),
        new SurfaceEntry("contacts.add_tags", "POST", "/v1/contacts/c_1/tags", ResponseShape.Object, async (c, t) => await c.Contacts.AddTagsAsync("c_1", "vip", t)),

        // A DELETE with a body: the surviving contact, the same object the tag
        // POST returns. Not a 204, whatever the verb suggests.
        new SurfaceEntry("contacts.remove_tag", "DELETE", "/v1/contacts/c_1/tags/vip", ResponseShape.Object, async (c, t) => await c.Contacts.RemoveTagAsync("c_1", "vip", t)),

        new SurfaceEntry("contacts.unsubscribe", "POST", "/v1/contacts/c_1/unsubscribe", ResponseShape.Object, async (c, t) => await c.Contacts.UnsubscribeAsync("c_1", true, null, t)),
        new SurfaceEntry("contacts.journey", "GET", "/v1/contacts/c_1/journey", ResponseShape.Page, async (c, t) => await c.Contacts.JourneyAsync("c_1", t)),

        new SurfaceEntry("conversations.list", "GET", "/v1/conversations", ResponseShape.Page, async (c, t) => await c.Conversations.ListAsync(t)),
        new SurfaceEntry("conversations.get", "GET", "/v1/conversations/k_1", ResponseShape.Object, async (c, t) => await c.Conversations.GetAsync("k_1", t)),
        new SurfaceEntry("conversations.messages", "GET", "/v1/conversations/k_1/messages", ResponseShape.Page, async (c, t) => await c.Conversations.MessagesAsync("k_1", t)),
        new SurfaceEntry("conversations.send", "POST", "/v1/conversations/k_1/messages", ResponseShape.Object, async (c, t) => await c.Conversations.SendAsync("k_1", new MessageInput { Body = "hi" }, t)),

        // A page, with a `total` beside `items` and `nextCursor` — the badge
        // number, which ignores the cursor. The doc comment on this method
        // claimed it was not paginated at all.
        new SurfaceEntry("conversations.unattended", "GET", "/v1/conversations/unattended", ResponseShape.Page, async (c, t) => await c.Conversations.UnattendedAsync(t)),

        new SurfaceEntry("conversations.agent_handling", "GET", "/v1/conversations/k_1/agent-handling", ResponseShape.Object, async (c, t) => await c.Conversations.AgentHandlingAsync("k_1", t)),
        new SurfaceEntry("conversations.set_agent_handling", "PUT", "/v1/conversations/k_1/agent-handling", ResponseShape.Object, async (c, t) => await c.Conversations.SetAgentHandlingAsync("k_1", new { handledBy = "ai" }, t)),
        new SurfaceEntry("conversations.generated_replies", "GET", "/v1/conversations/k_1/generated-replies", ResponseShape.Array, async (c, t) => await c.Conversations.GeneratedRepliesAsync("k_1", t)),
        new SurfaceEntry("conversations.generate_reply", "POST", "/v1/conversations/k_1/generated-replies", ResponseShape.Object, async (c, t) => await c.Conversations.GenerateReplyAsync("k_1", null, t)),
        new SurfaceEntry("conversations.scheduled_messages", "GET", "/v1/conversations/k_1/scheduled-messages", ResponseShape.Array, async (c, t) => await c.Conversations.ScheduledMessagesAsync("k_1", t)),
        new SurfaceEntry("conversations.schedule_message", "POST", "/v1/conversations/k_1/scheduled-messages", ResponseShape.Object, async (c, t) => await c.Conversations.ScheduleMessageAsync("k_1", new { body = "later" }, t)),

        // Also a DELETE with a body: the row it just cancelled, so a caller
        // can show what it called off. `@ZodResponse(scheduledMessageSchema)`.
        new SurfaceEntry("conversations.cancel_scheduled", "DELETE", "/v1/conversations/scheduled-messages/s_1", ResponseShape.Object, async (c, t) => await c.Conversations.CancelScheduledAsync("s_1", t)),

        new SurfaceEntry("tickets.list", "GET", "/v1/tickets", ResponseShape.Page, async (c, t) => await c.Tickets.ListAsync(t)),
        new SurfaceEntry("tickets.comments", "GET", "/v1/tickets/t_1/comments", ResponseShape.Array, async (c, t) => await c.Tickets.CommentsAsync("t_1", t)),
        new SurfaceEntry("tickets.for_conversation", "GET", "/v1/tickets/for-conversation/k_1", ResponseShape.Array, async (c, t) => await c.Tickets.ForConversationAsync("k_1", t)),
        new SurfaceEntry("tickets.queues", "GET", "/v1/tickets/queues", ResponseShape.Array, async (c, t) => await c.Tickets.QueuesAsync(t)),
        new SurfaceEntry("tickets.merge", "POST", "/v1/tickets/t_1/merge", ResponseShape.Object, async (c, t) => await c.Tickets.MergeAsync("t_1", new { mergedId = "t_2" }, t)),
        new SurfaceEntry("tickets.split", "POST", "/v1/tickets/t_1/split", ResponseShape.Object, async (c, t) => await c.Tickets.SplitAsync("t_1", new { subject = "part two" }, t)),
        new SurfaceEntry("tickets.bulk", "POST", "/v1/tickets/bulk", ResponseShape.Object, async (c, t) => await c.Tickets.BulkAsync(new { ids = new[] { "t_1" } }, t)),

        // @PaginationExempt, bare array, capped at 200 by usage rank. The one
        // that cost this SDK a decode error in production.
        new SurfaceEntry("macros.list", "GET", "/v1/macros", ResponseShape.Array, async (c, t) => await c.Macros.ListAsync(t)),

        new SurfaceEntry("macros.create", "POST", "/v1/macros", ResponseShape.Object, async (c, t) => await c.Macros.CreateAsync(new { name = "Refund" }, t)),
        new SurfaceEntry("macros.update", "PATCH", "/v1/macros/m_1", ResponseShape.Object, async (c, t) => await c.Macros.UpdateAsync("m_1", new { name = "Refund v2" }, t)),

        // The ONLY real 204 on this surface: @HttpCode(204), and the handler
        // returns nothing. The fake answers it with no body at all.
        new SurfaceEntry("macros.delete", "DELETE", "/v1/macros/m_1", ResponseShape.Empty, async (c, t) => await c.Macros.DeleteAsync("m_1", t)),

        new SurfaceEntry("kb.articles", "GET", "/v1/kb/articles", ResponseShape.Page, async (c, t) => await c.Kb.ArticlesAsync(t)),
        new SurfaceEntry("kb.article", "GET", "/v1/kb/articles/getting-started", ResponseShape.Object, async (c, t) => await c.Kb.ArticleAsync("getting-started", t)),
        new SurfaceEntry("kb.collections", "GET", "/v1/kb/collections", ResponseShape.Array, async (c, t) => await c.Kb.CollectionsAsync(t)),

        // A page carrying `tier` and `facets` beside the two fields that
        // define it. The single-page form returns the WHOLE envelope.
        new SurfaceEntry("search.query", "GET", "/v1/search", ResponseShape.Page, async (c, t) => await c.Search.QueryAsync("refund", t)),

        new SurfaceEntry("events.track", "POST", "/v1/cdp/events", ResponseShape.Object, async (c, t) => await c.Events.TrackAsync("c_1", "trial_started", null, t)),
        new SurfaceEntry("events.list", "GET", "/v1/cdp/events", ResponseShape.Page, async (c, t) => await c.Events.ListAsync(t)),

        new SurfaceEntry("webhooks.deliveries", "GET", "/v1/webhooks/deliveries", ResponseShape.Page, async (c, t) => await c.Webhooks.DeliveriesAsync(t)),
        new SurfaceEntry("webhooks.delivery", "GET", "/v1/webhooks/deliveries/d_1", ResponseShape.Object, async (c, t) => await c.Webhooks.DeliveryAsync("d_1", t)),
        new SurfaceEntry("webhooks.redeliver", "POST", "/v1/webhooks/deliveries/d_1/redeliver", ResponseShape.Object, async (c, t) => await c.Webhooks.RedeliverAsync("d_1", t)),

        new SurfaceEntry("reports.overview", "GET", "/v1/reports/overview", ResponseShape.Object, async (c, t) => await c.Reports.OverviewAsync(t)),

        // Arrays, both of them — per-agent rows and per-day buckets, no
        // envelope. The other two report routes are objects.
        new SurfaceEntry("reports.agents", "GET", "/v1/reports/agents", ResponseShape.Array, async (c, t) => await c.Reports.AgentsAsync(t)),
        new SurfaceEntry("reports.conversations_by_day", "GET", "/v1/reports/conversations-by-day", ResponseShape.Array, async (c, t) => await c.Reports.ConversationsByDayAsync(t)),

        new SurfaceEntry("reports.sla", "GET", "/v1/reports/sla", ResponseShape.Object, async (c, t) => await c.Reports.SlaAsync(t)),

        new SurfaceEntry("channels.list", "GET", "/v1/channels", ResponseShape.Array, async (c, t) => await c.Channels.ListAsync(t)),
        new SurfaceEntry("channels.get", "GET", "/v1/channels/ch_1", ResponseShape.Object, async (c, t) => await c.Channels.GetAsync("ch_1", t)),

        new SurfaceEntry("billing.plans", "GET", "/v1/billing/plans", ResponseShape.Array, async (c, t) => await c.Billing.PlansAsync(t)),
        new SurfaceEntry("billing.usage", "GET", "/v1/billing/usage", ResponseShape.Object,
            async (c, t) => await c.Billing.UsageAsync(ChatlyUsageEventTypes.AiTokens, null, null, t)),

        new SurfaceEntry("workspace.settings", "GET", "/v1/workspace/settings", ResponseShape.Object, async (c, t) => await c.Workspace.SettingsAsync(t)),
        new SurfaceEntry("workspace.labels", "GET", "/v1/labels", ResponseShape.Array, async (c, t) => await c.Workspace.LabelsAsync(t)),
        new SurfaceEntry("workspace.inboxes", "GET", "/v1/inboxes", ResponseShape.Array, async (c, t) => await c.Workspace.InboxesAsync(t)),
        new SurfaceEntry("workspace.team_members", "GET", "/v1/teams/tm_1/members", ResponseShape.Array, async (c, t) => await c.Workspace.TeamMembersAsync("tm_1", t)),
        new SurfaceEntry("workspace.agent_skills", "GET", "/v1/agents/skills", ResponseShape.Object, async (c, t) => await c.Workspace.AgentSkillsAsync(t)),
    };

    /// <summary>
    /// The <see cref="ResponseShape.Empty"/> fixture really sends nothing: 204,
    /// no content object, no <c>Content-Type</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A check on the fake rather than on the SDK, and it is here because the
    /// fake is the thing that has lied before. If
    /// <see cref="FakeResponse.NoContent"/> quietly attached an empty JSON
    /// document, the <c>macros.delete</c> row would prove nothing — a decoder
    /// that parses unconditionally would succeed against <c>""</c> as readily
    /// as against <c>{}</c>, and the only route on this surface that answers
    /// no body would be tested with a body.
    /// </para>
    /// <para>
    /// Driven through <see cref="System.Net.Http.HttpClient"/> directly, not
    /// through the SDK, because what is being asserted is the HTTP message.
    /// </para>
    /// </remarks>
    public static async Task TheEmptyFixtureReallySendsNoBody()
    {
        var handler = new FakeHandler(FakeResponse.For(ResponseShape.Empty));
        using var http = new System.Net.Http.HttpClient(handler);
        using var response = await http.GetAsync(new Uri("https://api.test.invalid/v1/macros/m_1"));

        Expect.Equal(204, (int)response.StatusCode, "the empty fixture answers 204");

        /*
         * No entity: no Content-Type, and nothing to read.
         *
         * `HttpResponseMessage.Content` is NOT null here even though the
         * handler set it to null — HttpClient substitutes a zero-length
         * placeholder, exactly as it does for a real 204 off the wire. So the
         * assertion is about the entity, not the object: a response with a
         * Content-Type of application/json and a zero-length body is a
         * different thing from a 204, and this fixture must be the latter.
         */
        Expect.True(
            response.Content.Headers.ContentType is null,
            "the empty fixture carries no Content-Type, as a 204 does not");
        Expect.Equal(
            string.Empty,
            await response.Content.ReadAsStringAsync(),
            "the empty fixture carries no bytes to parse");

        // And the one route declared `empty` is the only one, so no other
        // method is ever handed this.
        var declaredEmpty = Entries.Where(entry => entry.Shape == ResponseShape.Empty).ToList();
        Expect.Equal(1, declaredEmpty.Count, "exactly one route is declared empty");
        Expect.Equal("macros.delete", declaredEmpty[0].Name, "and it is macros.delete");
    }

    /// <summary>The spec lists 55 methods, and this table must cover all of them.</summary>
    public static Task TableCoversTheWholeSpec()
    {
        Expect.Equal(55, Entries.Count, "surface.json has 55 entries and this table has one row each");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in Entries)
        {
            Expect.True(seen.Add(entry.Name), $"{entry.Name} appears once");
            Expect.True(entry.Path.StartsWith("/v1/", StringComparison.Ordinal), $"{entry.Name} path is a /v1 route");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The declared shape column really distinguishes routes: 14 arrays, 10
    /// pages, 30 objects, 1 empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Anti-vacuity, and not a hypothetical one. A column that said
    /// <c>Object</c> 55 times would satisfy every other assertion in this file
    /// — the fake would answer <c>{}</c> everywhere, every method would decode
    /// it, and the lock would be internally consistent — and it would be
    /// worthless. That is very close to the state this column was in while
    /// <see cref="ResponseShape.Object"/> was the constructor default: thirteen
    /// bare-array routes were declared objects and nothing noticed.
    /// </para>
    /// <para>
    /// The numbers come from reading all 55 <c>@ZodResponse</c> schemas on
    /// 2026-10-12. If the surface grows they change — but all four values must
    /// stay represented, and <c>empty</c> stays at exactly one until a second
    /// real 204 ships.
    /// </para>
    /// </remarks>
    public static Task DeclaredShapeDistributionIsNotFlat()
    {
        var counts = Entries
            .GroupBy(entry => entry.Shape)
            .ToDictionary(group => group.Key, group => group.Count());

        int count(ResponseShape shape) => counts.TryGetValue(shape, out var n) ? n : 0;

        Expect.Equal(14, count(ResponseShape.Array), "bare-array routes declared");
        Expect.Equal(10, count(ResponseShape.Page), "page routes declared");
        Expect.Equal(30, count(ResponseShape.Object), "object routes declared");
        Expect.Equal(1, count(ResponseShape.Empty), "204 routes declared (macros.delete, and only it)");

        return Task.CompletedTask;
    }

    /// <summary>
    /// The shape declared for every route is the shape the manifest records,
    /// in both directions — and agrees with the manifest's <c>paged</c> flag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the check that would have caught the macros bug. The fake used
    /// to answer <i>every</i> route with <c>{ items, nextCursor }</c>, so
    /// <c>Macros.ListAsync</c> decoded a page here and threw
    /// <c>unexpected_response_shape</c> against the real API, which answers
    /// that route with a bare array. The fixture had invented a shape the
    /// server never sends, and the suite then agreed with the fixture.
    /// </para>
    /// <para>
    /// It used to compare only against <c>paged</c>, which is two-valued: it
    /// could tell a page from a not-page and nothing else, so every bare-array
    /// route declared <c>Object</c> passed it. The manifest now carries a
    /// four-valued <c>shape</c> per entry and this diffs the whole column
    /// against it.
    /// </para>
    /// <para>
    /// The column itself stays hand-written. It is NOT read from
    /// <c>surface.json</c> — a fixture derived from the manifest agrees with
    /// whatever the manifest says, including when it is wrong, which is not
    /// hypothetical: <c>macros.list</c> was flagged <c>paged</c> for a day
    /// while its handler is <c>@PaginationExempt</c>. Eight SDKs declare this
    /// column independently, so a wrong manifest row needs eight authors to
    /// misread the same handler.
    /// </para>
    /// </remarks>
    public static Task DeclaredShapesMatchTheManifestAsync()
    {
        var manifest = Manifest.Load();
        var declared = Entries.ToDictionary(entry => entry.Name, entry => entry.Shape, StringComparer.Ordinal);

        var pagedInManifest = 0;
        foreach (var entry in manifest)
        {
            Expect.True(
                declared.ContainsKey(entry.Key),
                $"{entry.Key} has a row in the surface table");

            var shape = declared[entry.Key];

            // The whole four-valued field, not just page-or-not.
            Expect.Equal(
                entry.ResponseShape,
                shape,
                $"{entry.Key}: the manifest says the response shape is <{entry.Shape}>, and this "
                + "package's fixture and decoder must say the same");

            // And `paged` must not drift from `shape`: the two are spellings
            // of one fact, added eighteen months apart.
            if (entry.Paged)
            {
                pagedInManifest++;
                Expect.Equal(
                    ResponseShape.Page,
                    shape,
                    $"{entry.Key} is paged in the manifest, so the fake must answer it with a page");
            }
            else
            {
                Expect.True(
                    shape != ResponseShape.Page,
                    $"{entry.Key} is NOT paged in the manifest, so the fake must not answer it with a "
                    + "page envelope — that is the fixture inventing a shape the API does not send");
            }
        }

        // The other direction: no row here is absent from the manifest, so the
        // column cannot carry a route the contract has never heard of.
        var manifestKeys = new HashSet<string>(manifest.Select(entry => entry.Key), StringComparer.Ordinal);
        foreach (var entry in Entries)
        {
            Expect.True(
                manifestKeys.Contains(entry.Name),
                $"{entry.Name} is declared here and must exist in the manifest");
        }

        /*
         * Anti-vacuity floor. TEN, and it has been 8 and 7 — the manifest's
         * `paged` flag was wrong in both directions, which is why this
         * asserts the number at all.
         *
         * 8 -> 7: `macros.list` left, because its handler is
         * @PaginationExempt and answers a bare array capped at 200 rows by
         * usage rank. There was never a cursor to follow.
         *
         * 7 -> 10: `contacts.journey`, `conversations.unattended` and
         * `search.query` went in. All three answer real
         * `{ items, nextCursor }` envelopes — `unattended` carries a `total`
         * beside them and `search` carries `tier` and `facets` — and none had
         * been flagged.
         *
         * Both moves came from reading the handlers. Neither was a parser
         * dropping rows, which is the other reason this count changes and the
         * only one that is a bug. Check that surface.json parses before
         * believing the next move.
         */
        Expect.Equal(
            10,
            pagedInManifest,
            "the manifest marks 10 entries paged (macros.list is not one: @PaginationExempt, bare array)");

        // And the one route known to answer a bare array is declared as one.
        Expect.Equal(
            ResponseShape.Array,
            declared["macros.list"],
            "macros.list answers a bare JSON array");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Drive every method and assert the verb, the path, and the shape of the
    /// value it handed back.
    /// </summary>
    public static async Task EveryMethodHitsTheSpecifiedRoute()
    {
        foreach (var entry in Entries)
        {
            var handler = new FakeHandler(FakeResponse.For(entry.Shape));
            using var client = Fake.Client(handler);

            // Decoding happens inside Invoke, so a method that expects a page
            // from a route that answers an array throws here rather than
            // passing on a fixture nobody serves.
            var value = await entry.Invoke(client, CancellationToken.None);

            Expect.Equal(1, handler.CallCount, $"{entry.Name} makes exactly one request");
            Expect.Equal(entry.Verb, handler.Last.Verb, $"{entry.Name} verb");
            Expect.Equal(entry.Path, handler.Last.Uri.AbsolutePath, $"{entry.Name} path");
            Expect.Equal(
                "https://api.chatlychat.com",
                handler.Last.Uri.GetLeftPart(UriPartial.Authority),
                $"{entry.Name} host");

            /*
             * And the value handed back really has the declared shape.
             *
             * This is what catches a method that wraps or unwraps: a `list`
             * that returned `page.Items` from a page route, or one that
             * reached for `items` on a bare-array route, disagrees here even
             * though the request it issued was perfect. A decoder that ignores
             * the body entirely fails on `macros.delete`, whose fixture has no
             * body for it to ignore.
             */
            Expect.Equal(
                Shapes.Name(entry.Shape),
                Shapes.Of(value),
                $"{entry.Name} decoded to the declared {Shapes.Name(entry.Shape)}");

            var isWrite = entry.Verb is "POST" or "PUT" or "PATCH" or "DELETE";
            if (isWrite)
            {
                Expect.NotNull(handler.Last.Header("idempotency-key"), $"{entry.Name} carries an idempotency key");
            }
            else
            {
                Expect.True(handler.Last.Header("idempotency-key") is null, $"{entry.Name} is a read and carries none");
            }
        }
    }

    /// <summary>
    /// Every route declared <see cref="ResponseShape.Page"/> has an
    /// auto-paginator here, and nothing else does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written as a map keyed by route name rather than a count, so the set
    /// can be compared with the <c>page</c> rows of
    /// <see cref="Entries"/> in both directions. The count alone was the check
    /// for a while and it was satisfied by SEVEN walkers against TEN paged
    /// routes: <c>contacts.journey</c> had a walker and was simply missing
    /// from the map, while <c>conversations.unattended</c> and
    /// <c>search.query</c> had none at all, which contradicted §5b of the
    /// contract for a route the manifest marks paged.
    /// </para>
    /// </remarks>
    private static Dictionary<string, Func<ChatlyClient, CancellationToken, IAsyncEnumerable<JsonElement>>> Walkers()
        => new Dictionary<string, Func<ChatlyClient, CancellationToken, IAsyncEnumerable<JsonElement>>>(StringComparer.Ordinal)
        {
            ["contacts.list"] = (c, t) => c.Contacts.ListAllAsync(t),

            // Existed all along; the map was stale. Its cursor is an ISO-8601
            // instant rather than an opaque token, which is no reason not to
            // walk it.
            ["contacts.journey"] = (c, t) => c.Contacts.JourneyAllAsync("c_1", t),

            ["conversations.list"] = (c, t) => c.Conversations.ListAllAsync(t),
            ["conversations.messages"] = (c, t) => c.Conversations.MessagesAllAsync("k_1", t),

            // Written for this: the method had no walker, and its single-page
            // form was documented as "not cursor-paginated".
            ["conversations.unattended"] = (c, t) => c.Conversations.UnattendedAllAsync(t),

            ["tickets.list"] = (c, t) => c.Tickets.ListAllAsync(t),
            ["kb.articles"] = (c, t) => c.Kb.ArticlesAllAsync(t),

            // Also written for this. The single-page form still returns the
            // whole envelope, because `tier` and `facets` live beside `items`.
            ["search.query"] = (c, t) => c.Search.QueryAllAsync("refund", t),

            ["events.list"] = (c, t) => c.Events.ListAllAsync(t),
            ["webhooks.deliveries"] = (c, t) => c.Webhooks.DeliveriesAllAsync(t),
        };

    /// <summary>
    /// The paged entries of the spec expose both a single page and an
    /// auto-paginator.
    /// </summary>
    public static async Task EveryPagedEntryHasAnAutoPaginator()
    {
        var walkers = Walkers();

        /*
         * The walker set is exactly the `page` rows — not merely as numerous.
         * TEN, and it was SEVEN for months while the manifest said ten, which
         * a count-only check reported as a pass because the number it asserted
         * was the number it found.
         */
        var pageRoutes = Entries
            .Where(entry => entry.Shape == ResponseShape.Page)
            .Select(entry => entry.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Expect.Equal(10, pageRoutes.Count, "ten routes are declared `page` above");
        Expect.Equal(
            string.Join(", ", pageRoutes),
            string.Join(", ", walkers.Keys.OrderBy(name => name, StringComparer.Ordinal)),
            "every `page` route has an auto-paginator here, and nothing else does");

        foreach (var (name, walk) in walkers)
        {
            var handler = new FakeHandler((_, index) => index == 0
                ? FakeResponse.Page("[{\"id\":\"x_1\"}]", "cursor_2")
                : FakeResponse.Page("[{\"id\":\"x_2\"}]"));
            using var client = Fake.Client(handler);

            var count = 0;
            await foreach (var _ in walk(client, CancellationToken.None))
            {
                count++;
            }

            Expect.Equal(2, count, $"{name} walked both pages");
            Expect.Equal(2, handler.CallCount, $"{name} stopped on the null cursor");
        }
    }

    /// <summary>
    /// The negative direction: a paginator fed a bare array must fail loudly.
    /// </summary>
    /// <remarks>
    /// <see cref="EveryPagedEntryHasAnAutoPaginator"/> proves each walker
    /// copes with the shape the manifest says the route sends. It cannot prove
    /// the declaration matters — a decoder that read <c>items</c> with a
    /// shrug, or one that treated a bare array as an empty page, passes it.
    /// This feeds every walker a bare array where it expects
    /// <c>{ items, nextCursor }</c> and requires it to reject, which is
    /// exactly what the real API handed this client on <c>macros.list</c>.
    /// Reading a bare array as an EMPTY page, which is what the Rust client
    /// did, is worse than throwing: a wrong answer with a 200 rather than a
    /// missing one.
    /// </remarks>
    public static async Task EveryAutoPaginatorRejectsABareArray()
    {
        foreach (var (name, walk) in Walkers())
        {
            var handler = new FakeHandler(FakeResponse.Array("[{\"id\":\"x_1\"}]"));
            using var client = Fake.Client(handler);

            var threw = false;
            try
            {
                await foreach (var _ in walk(client, CancellationToken.None))
                {
                    break;
                }
            }
            catch (ChatlyException)
            {
                threw = true;
            }

            Expect.True(threw, $"{name}'s paginator rejected a bare array where a page is expected");
        }
    }

    /// <summary>
    /// The escape hatch can reach a route with no typed method, and
    /// auto-paginate it.
    /// </summary>
    public static async Task EscapeHatchReachesUnmodelledRoutes()
    {
        var handler = new FakeHandler((_, index) => index == 0
            ? FakeResponse.Page("[{\"id\":\"a_1\"}]", "cursor_2")
            : FakeResponse.Page("[{\"id\":\"a_2\"}]"));
        using var client = Fake.Client(handler);

        var ids = new List<string?>();
        await foreach (var item in client.RequestAllAsync("/v1/audit-logs", new ListOptions { Limit = 1 }, CancellationToken.None))
        {
            ids.Add(item.GetStringOrNull("id"));
        }

        Expect.Equal("a_1,a_2", string.Join(",", ids), "an unmodelled paged route auto-paginates");
        Expect.Equal("/v1/audit-logs", handler.Last.Uri.AbsolutePath, "the path is used verbatim");

        var page = await client.RequestPageAsync("/v1/audit-logs", null, CancellationToken.None);
        Expect.Equal(1, page.Items.Count, "RequestPageAsync returns one page");
    }
}
