using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Tests;

/// <summary>
/// Every method in <c>packages/sdk-spec/surface.json</c>, driven once, with
/// the HTTP verb and path it produced asserted against the spec.
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
            Func<ChatlyClient, CancellationToken, Task> invoke,
            ResponseShape shape = ResponseShape.Object)
        {
            Name = name;
            Verb = verb;
            Path = path;
            Invoke = invoke;
            Shape = shape;
        }

        public string Name { get; }

        public string Verb { get; }

        public string Path { get; }

        public Func<ChatlyClient, CancellationToken, Task> Invoke { get; }

        /// <summary>
        /// The shape this route answers with, which the fake transport is fed
        /// when the method is driven.
        /// </summary>
        /// <remarks>
        /// Declared per route, not assumed. Feeding every route a page
        /// envelope is what let <c>Macros.ListAsync</c> pass here while
        /// failing against the real API, which answers that route with a bare
        /// array. <see cref="DeclaredShapesMatchTheManifestAsync"/> ties every
        /// <see cref="ResponseShape.Page"/> below back to the manifest's own
        /// <c>paged</c> flag, so this column cannot drift from the contract.
        /// </remarks>
        public ResponseShape Shape { get; }
    }

    /// <summary>The 55 entries of surface.json, in spec order.</summary>
    public static IReadOnlyList<SurfaceEntry> Entries { get; } = new List<SurfaceEntry>
    {
        new SurfaceEntry("root.whoami", "GET", "/v1/api-keys/whoami", (c, t) => c.WhoAmIAsync(t)),

        new SurfaceEntry("contacts.list", "GET", "/v1/contacts", (c, t) => c.Contacts.ListAsync(t), ResponseShape.Page),
        new SurfaceEntry("contacts.get", "GET", "/v1/contacts/c_1", (c, t) => c.Contacts.GetAsync("c_1", t)),
        new SurfaceEntry("contacts.upsert", "POST", "/v1/contacts", (c, t) => c.Contacts.UpsertAsync(new ContactInput { Email = "a@example.com" }, t)),
        new SurfaceEntry("contacts.bulk_upsert", "POST", "/v1/contacts/bulk", (c, t) => c.Contacts.BulkUpsertAsync(new[] { new ContactInput { Email = "a@example.com" } }, t)),
        new SurfaceEntry("contacts.merge", "POST", "/v1/contacts/c_1/merge", (c, t) => c.Contacts.MergeAsync("c_1", "c_2", t)),
        new SurfaceEntry("contacts.add_tags", "POST", "/v1/contacts/c_1/tags", (c, t) => c.Contacts.AddTagsAsync("c_1", "vip", t)),
        new SurfaceEntry("contacts.remove_tag", "DELETE", "/v1/contacts/c_1/tags/vip", (c, t) => c.Contacts.RemoveTagAsync("c_1", "vip", t)),
        new SurfaceEntry("contacts.unsubscribe", "POST", "/v1/contacts/c_1/unsubscribe", (c, t) => c.Contacts.UnsubscribeAsync("c_1", true, null, t)),
        new SurfaceEntry("contacts.journey", "GET", "/v1/contacts/c_1/journey", (c, t) => c.Contacts.JourneyAsync("c_1", t)),

        new SurfaceEntry("conversations.list", "GET", "/v1/conversations", (c, t) => c.Conversations.ListAsync(t), ResponseShape.Page),
        new SurfaceEntry("conversations.get", "GET", "/v1/conversations/k_1", (c, t) => c.Conversations.GetAsync("k_1", t)),
        new SurfaceEntry("conversations.messages", "GET", "/v1/conversations/k_1/messages", (c, t) => c.Conversations.MessagesAsync("k_1", t), ResponseShape.Page),
        new SurfaceEntry("conversations.send", "POST", "/v1/conversations/k_1/messages", (c, t) => c.Conversations.SendAsync("k_1", new MessageInput { Body = "hi" }, t)),
        new SurfaceEntry("conversations.unattended", "GET", "/v1/conversations/unattended", (c, t) => c.Conversations.UnattendedAsync(t)),
        new SurfaceEntry("conversations.agent_handling", "GET", "/v1/conversations/k_1/agent-handling", (c, t) => c.Conversations.AgentHandlingAsync("k_1", t)),
        new SurfaceEntry("conversations.set_agent_handling", "PUT", "/v1/conversations/k_1/agent-handling", (c, t) => c.Conversations.SetAgentHandlingAsync("k_1", new { handledBy = "ai" }, t)),
        new SurfaceEntry("conversations.generated_replies", "GET", "/v1/conversations/k_1/generated-replies", (c, t) => c.Conversations.GeneratedRepliesAsync("k_1", t)),
        new SurfaceEntry("conversations.generate_reply", "POST", "/v1/conversations/k_1/generated-replies", (c, t) => c.Conversations.GenerateReplyAsync("k_1", null, t)),
        new SurfaceEntry("conversations.scheduled_messages", "GET", "/v1/conversations/k_1/scheduled-messages", (c, t) => c.Conversations.ScheduledMessagesAsync("k_1", t)),
        new SurfaceEntry("conversations.schedule_message", "POST", "/v1/conversations/k_1/scheduled-messages", (c, t) => c.Conversations.ScheduleMessageAsync("k_1", new { body = "later" }, t)),
        new SurfaceEntry("conversations.cancel_scheduled", "DELETE", "/v1/conversations/scheduled-messages/s_1", (c, t) => c.Conversations.CancelScheduledAsync("s_1", t)),

        new SurfaceEntry("tickets.list", "GET", "/v1/tickets", (c, t) => c.Tickets.ListAsync(t), ResponseShape.Page),
        new SurfaceEntry("tickets.comments", "GET", "/v1/tickets/t_1/comments", (c, t) => c.Tickets.CommentsAsync("t_1", t)),
        new SurfaceEntry("tickets.for_conversation", "GET", "/v1/tickets/for-conversation/k_1", (c, t) => c.Tickets.ForConversationAsync("k_1", t)),
        new SurfaceEntry("tickets.queues", "GET", "/v1/tickets/queues", (c, t) => c.Tickets.QueuesAsync(t)),
        new SurfaceEntry("tickets.merge", "POST", "/v1/tickets/t_1/merge", (c, t) => c.Tickets.MergeAsync("t_1", new { mergedId = "t_2" }, t)),
        new SurfaceEntry("tickets.split", "POST", "/v1/tickets/t_1/split", (c, t) => c.Tickets.SplitAsync("t_1", new { subject = "part two" }, t)),
        new SurfaceEntry("tickets.bulk", "POST", "/v1/tickets/bulk", (c, t) => c.Tickets.BulkAsync(new { ids = new[] { "t_1" } }, t)),

        new SurfaceEntry("macros.list", "GET", "/v1/macros", (c, t) => c.Macros.ListAsync(t), ResponseShape.Array),
        new SurfaceEntry("macros.create", "POST", "/v1/macros", (c, t) => c.Macros.CreateAsync(new { name = "Refund" }, t)),
        new SurfaceEntry("macros.update", "PATCH", "/v1/macros/m_1", (c, t) => c.Macros.UpdateAsync("m_1", new { name = "Refund v2" }, t)),
        new SurfaceEntry("macros.delete", "DELETE", "/v1/macros/m_1", (c, t) => c.Macros.DeleteAsync("m_1", t)),

        new SurfaceEntry("kb.articles", "GET", "/v1/kb/articles", (c, t) => c.Kb.ArticlesAsync(t), ResponseShape.Page),
        new SurfaceEntry("kb.article", "GET", "/v1/kb/articles/getting-started", (c, t) => c.Kb.ArticleAsync("getting-started", t)),
        new SurfaceEntry("kb.collections", "GET", "/v1/kb/collections", (c, t) => c.Kb.CollectionsAsync(t)),

        new SurfaceEntry("search.query", "GET", "/v1/search", (c, t) => c.Search.QueryAsync("refund", t)),

        new SurfaceEntry("events.track", "POST", "/v1/cdp/events", (c, t) => c.Events.TrackAsync("c_1", "trial_started", null, t)),
        new SurfaceEntry("events.list", "GET", "/v1/cdp/events", (c, t) => c.Events.ListAsync(t), ResponseShape.Page),

        new SurfaceEntry("webhooks.deliveries", "GET", "/v1/webhooks/deliveries", (c, t) => c.Webhooks.DeliveriesAsync(t), ResponseShape.Page),
        new SurfaceEntry("webhooks.delivery", "GET", "/v1/webhooks/deliveries/d_1", (c, t) => c.Webhooks.DeliveryAsync("d_1", t)),
        new SurfaceEntry("webhooks.redeliver", "POST", "/v1/webhooks/deliveries/d_1/redeliver", (c, t) => c.Webhooks.RedeliverAsync("d_1", t)),

        new SurfaceEntry("reports.overview", "GET", "/v1/reports/overview", (c, t) => c.Reports.OverviewAsync(t)),
        new SurfaceEntry("reports.agents", "GET", "/v1/reports/agents", (c, t) => c.Reports.AgentsAsync(t)),
        new SurfaceEntry("reports.conversations_by_day", "GET", "/v1/reports/conversations-by-day", (c, t) => c.Reports.ConversationsByDayAsync(t)),
        new SurfaceEntry("reports.sla", "GET", "/v1/reports/sla", (c, t) => c.Reports.SlaAsync(t)),

        new SurfaceEntry("channels.list", "GET", "/v1/channels", (c, t) => c.Channels.ListAsync(t)),
        new SurfaceEntry("channels.get", "GET", "/v1/channels/ch_1", (c, t) => c.Channels.GetAsync("ch_1", t)),

        new SurfaceEntry("billing.plans", "GET", "/v1/billing/plans", (c, t) => c.Billing.PlansAsync(t)),
        new SurfaceEntry("billing.usage", "GET", "/v1/billing/usage",
            (c, t) => c.Billing.UsageAsync(ChatlyUsageEventTypes.AiTokens, null, null, t)),

        new SurfaceEntry("workspace.settings", "GET", "/v1/workspace/settings", (c, t) => c.Workspace.SettingsAsync(t)),
        new SurfaceEntry("workspace.labels", "GET", "/v1/labels", (c, t) => c.Workspace.LabelsAsync(t)),
        new SurfaceEntry("workspace.inboxes", "GET", "/v1/inboxes", (c, t) => c.Workspace.InboxesAsync(t)),
        new SurfaceEntry("workspace.team_members", "GET", "/v1/teams/tm_1/members", (c, t) => c.Workspace.TeamMembersAsync("tm_1", t)),
        new SurfaceEntry("workspace.agent_skills", "GET", "/v1/agents/skills", (c, t) => c.Workspace.AgentSkillsAsync(t)),
    };

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
    /// Every route the fake answers with a page is a route the manifest marks
    /// paged, and every paged manifest entry is fed a page.
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
    /// So the page-shaped fixtures are no longer a choice this file makes:
    /// they are derived from the manifest's own <c>paged</c> flag, in both
    /// directions. Declaring <see cref="ResponseShape.Page"/> for a route the
    /// manifest does not mark paged fails, and so does leaving a paged route
    /// on any other shape.
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

        // Anti-vacuity floor. SEVEN paged entries, not eight: `macros.list`
        // left the set when the manifest was corrected, because that handler is
        // @PaginationExempt and returns a bare array capped at 200 rows by
        // usage rank, so there is no cursor to follow.
        Expect.Equal(
            7,
            pagedInManifest,
            "the manifest marks 7 entries paged (macros.list is not one: @PaginationExempt, bare array)");

        // And the one route known to answer a bare array is declared as one.
        Expect.Equal(
            ResponseShape.Array,
            declared["macros.list"],
            "macros.list answers a bare JSON array");

        return Task.CompletedTask;
    }

    /// <summary>Drive every method and assert the verb and path it produced.</summary>
    public static async Task EveryMethodHitsTheSpecifiedRoute()
    {
        foreach (var entry in Entries)
        {
            var handler = new FakeHandler(FakeResponse.For(entry.Shape));
            using var client = Fake.Client(handler);

            // Decoding happens inside Invoke, so a method that expects a page
            // from a route that answers an array throws here rather than
            // passing on a fixture nobody serves.
            await entry.Invoke(client, CancellationToken.None);

            Expect.Equal(1, handler.CallCount, $"{entry.Name} makes exactly one request");
            Expect.Equal(entry.Verb, handler.Last.Verb, $"{entry.Name} verb");
            Expect.Equal(entry.Path, handler.Last.Uri.AbsolutePath, $"{entry.Name} path");
            Expect.Equal(
                "https://api.chatlychat.com",
                handler.Last.Uri.GetLeftPart(UriPartial.Authority),
                $"{entry.Name} host");

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
    /// The paged entries of the spec expose both a single page and an
    /// auto-paginator.
    /// </summary>
    public static async Task EveryPagedEntryHasAnAutoPaginator()
    {
        var walkers = new Dictionary<string, Func<ChatlyClient, CancellationToken, IAsyncEnumerable<System.Text.Json.JsonElement>>>
        {
            ["contacts.list"] = (c, t) => c.Contacts.ListAllAsync(t),
            ["conversations.list"] = (c, t) => c.Conversations.ListAllAsync(t),
            ["conversations.messages"] = (c, t) => c.Conversations.MessagesAllAsync("k_1", t),
            ["tickets.list"] = (c, t) => c.Tickets.ListAllAsync(t),
            ["kb.articles"] = (c, t) => c.Kb.ArticlesAllAsync(t),
            ["events.list"] = (c, t) => c.Events.ListAllAsync(t),
            ["webhooks.deliveries"] = (c, t) => c.Webhooks.DeliveriesAllAsync(t),
        };

        // Anti-vacuity floor: the entries carrying "paged": true in
        // surface.json. SEVEN, not eight — `macros.list` left this set when
        // the manifest was corrected, because that handler is
        // @PaginationExempt and answers a bare array, so there is no cursor
        // for an auto-paginator to follow. If this number drops again,
        // something stopped being walked and the check below says which.
        Expect.Equal(
            7,
            walkers.Count,
            "all 7 paged manifest entries have an auto-paginator here (macros.list is not paged: "
            + "@PaginationExempt, bare array, capped at 200 by usage rank)");

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
