using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Tests;

/// <summary>
/// The surface TABLE: one row per manifest entry, plus the auto-paginator
/// map. The assertions over it live in <c>SurfaceChecks.cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// Split out of that file when the surface grew from 55 routes to 159 and the
/// combined file went past CONTRACT.md §9's 650-line ceiling. The split is
/// data here, assertions there — not data-and-one-assertion here, which would
/// put the thing being checked and the check in the same file again.
/// </para>
/// <para>
/// Same partial class, deliberately: <c>SurfaceLock</c> and every check read
/// <c>SurfaceChecks.Entries</c>, and a separate type would have meant
/// renaming all of them for a file-length limit.
/// </para>
/// </remarks>
public static partial class SurfaceChecks
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
    /// The 162 entries of surface.json, in spec order, each with the response
    /// shape its handler's <c>@ZodResponse</c> schema produces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written out by hand rather than read from <c>surface.json</c>, and the
    /// shape column especially: a fixture derived from the manifest agrees
    /// with whatever the manifest says, including when it is wrong. Eight
    /// SDKs declare this column independently from the handlers.
    /// </para>
    /// <para>
    /// Rows that look wrong and are not. <c>contacts.remove_tag</c> is a
    /// DELETE that answers 200 with the surviving contact;
    /// <c>conversations.cancel_scheduled</c> is a DELETE that answers 200
    /// with the row it just cancelled; <c>macros.delete</c> is the only real
    /// 204 on the surface and six other DELETEs answer 200 with a body.
    /// <c>contacts.cards</c> and <c>bot_routes.resolve</c> and
    /// <c>docs_sources.discover</c> are POSTs that create nothing —
    /// <c>discover</c> answers a bare array. <c>ai.set_tone</c> is a PUT
    /// taking <c>{ rules }</c> and answering a bare array.
    /// <c>billing.invoices</c> is cursor-paginated and still <c>object</c>,
    /// because its envelope key is <c>data</c> rather than <c>items</c>.
    /// <c>conversations.linked</c> and <c>tickets.time</c> have an
    /// <c>items</c> key and no cursor, so they are objects too.
    /// </para>
    /// <para>
    /// The last three rows arrived after the other 159 and were derived from
    /// their handlers here a third time, independently of the manifest entry
    /// and of the note that asked for them. <c>webhooks.delete</c> is
    /// <c>z.object({ ok: z.literal(true) })</c> with no <c>@HttpCode</c>, so
    /// 200 and not 204. <c>root.me</c> is a
    /// <c>z.discriminatedUnion('principal', …)</c> of two objects, so an
    /// object whichever branch answers. <c>workspace.mine</c> is
    /// <c>z.array(workspaceListItemSchema)</c> — a bare array even for a key,
    /// where it holds exactly one entry.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<SurfaceEntry> Entries { get; } = new List<SurfaceEntry>
    {
        // ── ai ─────────────────────────────────────────────────────────────
        // Two controllers under one namespace, and their paths disagree:
        // credentials are /v1/ai-providers, everything else is /v1/ai/…
        new SurfaceEntry("ai.add_provider", "POST", "/v1/ai-providers", ResponseShape.Object, async (c, t) => await c.Ai.AddProviderAsync("openai", "sk-test", t)),
        new SurfaceEntry("ai.create_tool", "POST", "/v1/ai/tools", ResponseShape.Object, async (c, t) => await c.Ai.CreateToolAsync(new { name = "lookup_order" }, t)),
        new SurfaceEntry("ai.delete_tool", "DELETE", "/v1/ai/tools/tl_1", ResponseShape.Object, async (c, t) => await c.Ai.DeleteToolAsync("tl_1", t)),
        new SurfaceEntry("ai.generate_tone", "POST", "/v1/ai/tone/generate", ResponseShape.Object, async (c, t) => await c.Ai.GenerateToneAsync("Be brief and never apologise twice.", t)),
        new SurfaceEntry("ai.providers", "GET", "/v1/ai-providers", ResponseShape.Object, async (c, t) => await c.Ai.ProvidersAsync(t)),
        new SurfaceEntry("ai.quota", "GET", "/v1/ai/quota", ResponseShape.Object, async (c, t) => await c.Ai.QuotaAsync(t)),

        // A PUT whose body is `{ rules: [...] }` and whose response is the
        // bare array the GET returns. The asymmetry is the server's.
        new SurfaceEntry("ai.set_tone", "PUT", "/v1/ai/tone", ResponseShape.Array, async (c, t) => await c.Ai.SetToneAsync(new object[] { new { category = "greeting" } }, t)),

        new SurfaceEntry("ai.test_provider", "POST", "/v1/ai-providers/openai/test", ResponseShape.Object, async (c, t) => await c.Ai.TestProviderAsync("openai", t)),
        new SurfaceEntry("ai.tone", "GET", "/v1/ai/tone", ResponseShape.Array, async (c, t) => await c.Ai.ToneAsync(t)),
        new SurfaceEntry("ai.tone_preview", "GET", "/v1/ai/tone/preview", ResponseShape.Object, async (c, t) => await c.Ai.TonePreviewAsync(t)),
        new SurfaceEntry("ai.tools", "GET", "/v1/ai/tools", ResponseShape.Array, async (c, t) => await c.Ai.ToolsAsync(t)),
        new SurfaceEntry("ai.update_tool", "PATCH", "/v1/ai/tools/tl_1", ResponseShape.Object, async (c, t) => await c.Ai.UpdateToolAsync("tl_1", new { name = "lookup_order_v2" }, t)),

        // ── billing ────────────────────────────────────────────────────────
        // Cursor-paginated and NOT a page: its envelope key is `data`. A
        // walker keyed on `items` would report an empty page forever.
        new SurfaceEntry("billing.invoices", "GET", "/v1/billing/invoices", ResponseShape.Object, async (c, t) => await c.Billing.InvoicesAsync(null, null, t)),

        new SurfaceEntry("billing.plans", "GET", "/v1/billing/plans", ResponseShape.Array, async (c, t) => await c.Billing.PlansAsync(t)),

        // The collection root with no sub-path: /v1/billing, not /v1/billing/summary.
        new SurfaceEntry("billing.summary", "GET", "/v1/billing", ResponseShape.Object, async (c, t) => await c.Billing.SummaryAsync(t)),

        new SurfaceEntry("billing.usage", "GET", "/v1/billing/usage", ResponseShape.Object,
            async (c, t) => await c.Billing.UsageAsync(ChatlyUsageEventTypes.AiTokens, null, null, t)),
        new SurfaceEntry("billing.usage_daily", "GET", "/v1/billing/usage/daily", ResponseShape.Object,
            async (c, t) => await c.Billing.UsageDailyAsync(ChatlyUsageEventTypes.AiTokens, null, null, t)),
        new SurfaceEntry("billing.usage_monthly", "GET", "/v1/billing/usage/monthly", ResponseShape.Object, async (c, t) => await c.Billing.UsageMonthlyAsync(null, t)),

        // ── bot_routes ─────────────────────────────────────────────────────
        new SurfaceEntry("bot_routes.create", "POST", "/v1/bot-routes", ResponseShape.Object, async (c, t) => await c.BotRoutes.CreateAsync(new { botId = "b_1" }, t)),
        new SurfaceEntry("bot_routes.delete", "DELETE", "/v1/bot-routes/br_1", ResponseShape.Object, async (c, t) => await c.BotRoutes.DeleteAsync("br_1", t)),
        new SurfaceEntry("bot_routes.fields", "GET", "/v1/bot-routes/fields", ResponseShape.Object, async (c, t) => await c.BotRoutes.FieldsAsync(t)),
        new SurfaceEntry("bot_routes.list", "GET", "/v1/bot-routes", ResponseShape.Array, async (c, t) => await c.BotRoutes.ListAsync(t)),

        // A POST that changes nothing: a dry run of the routing table.
        new SurfaceEntry("bot_routes.resolve", "POST", "/v1/bot-routes/resolve", ResponseShape.Object, async (c, t) => await c.BotRoutes.ResolveAsync(new { page = new { path = "/pricing" } }, t)),

        new SurfaceEntry("bot_routes.update", "PATCH", "/v1/bot-routes/br_1", ResponseShape.Object, async (c, t) => await c.BotRoutes.UpdateAsync("br_1", new { enabled = false }, t)),

        // ── bots ───────────────────────────────────────────────────────────
        new SurfaceEntry("bots.analytics", "GET", "/v1/bots/b_1/analytics", ResponseShape.Object, async (c, t) => await c.Bots.AnalyticsAsync("b_1", null, t)),
        new SurfaceEntry("bots.create", "POST", "/v1/bots", ResponseShape.Object, async (c, t) => await c.Bots.CreateAsync(new { name = "Returns" }, t)),

        // A page, and it was unflagged in the manifest for a while:
        // buildPaginatedResponse, so items + nextCursor + hasMore.
        new SurfaceEntry("bots.deliveries", "GET", "/v1/bots/b_1/deliveries", ResponseShape.Page, async (c, t) => await c.Bots.DeliveriesAsync("b_1", null, null, t)),

        new SurfaceEntry("bots.delivery", "GET", "/v1/bots/b_1/deliveries/d_1", ResponseShape.Object, async (c, t) => await c.Bots.DeliveryAsync("b_1", "d_1", t)),
        new SurfaceEntry("bots.get", "GET", "/v1/bots/b_1", ResponseShape.Object, async (c, t) => await c.Bots.GetAsync("b_1", t)),

        // Also a late-flagged page.
        new SurfaceEntry("bots.list", "GET", "/v1/bots", ResponseShape.Page, async (c, t) => await c.Bots.ListAsync(t)),

        new SurfaceEntry("bots.redeliver", "POST", "/v1/bots/b_1/deliveries/d_1/redeliver", ResponseShape.Object, async (c, t) => await c.Bots.RedeliverAsync("b_1", "d_1", t)),
        new SurfaceEntry("bots.rotate_external_secret", "POST", "/v1/bots/b_1/external/rotate-secret", ResponseShape.Object, async (c, t) => await c.Bots.RotateExternalSecretAsync("b_1", t)),
        new SurfaceEntry("bots.test_delivery", "POST", "/v1/bots/b_1/test-delivery", ResponseShape.Object, async (c, t) => await c.Bots.TestDeliveryAsync("b_1", t)),
        new SurfaceEntry("bots.update", "PATCH", "/v1/bots/b_1", ResponseShape.Object, async (c, t) => await c.Bots.UpdateAsync("b_1", new { name = "Returns v2" }, t)),
        new SurfaceEntry("bots.update_external", "PATCH", "/v1/bots/b_1/external", ResponseShape.Object, async (c, t) => await c.Bots.UpdateExternalAsync("b_1", new { url = "https://example.com/agent" }, t)),

        // ── brands ─────────────────────────────────────────────────────────
        new SurfaceEntry("brands.create", "POST", "/v1/brands", ResponseShape.Object, async (c, t) => await c.Brands.CreateAsync(new { name = "Acme", slug = "acme" }, t)),

        // A soft delete that answers 200 with the id.
        new SurfaceEntry("brands.delete", "DELETE", "/v1/brands/bd_1", ResponseShape.Object, async (c, t) => await c.Brands.DeleteAsync("bd_1", t)),

        new SurfaceEntry("brands.list", "GET", "/v1/brands", ResponseShape.Array, async (c, t) => await c.Brands.ListAsync(t)),
        new SurfaceEntry("brands.update", "PATCH", "/v1/brands/bd_1", ResponseShape.Object, async (c, t) => await c.Brands.UpdateAsync("bd_1", new { name = "Acme Ltd" }, t)),

        // ── canvas_steps ───────────────────────────────────────────────────
        new SurfaceEntry("canvas_steps.get", "GET", "/v1/canvas-steps/cs_1", ResponseShape.Object, async (c, t) => await c.CanvasSteps.GetAsync("cs_1", t)),
        new SurfaceEntry("canvas_steps.resolve", "POST", "/v1/canvas-steps/cs_1/resolve", ResponseShape.Object, async (c, t) => await c.CanvasSteps.ResolveAsync("cs_1", new { blocks = System.Array.Empty<object>() }, t)),

        // ── channels ───────────────────────────────────────────────────────
        // The one route on this surface that answers an array of STRINGS.
        new SurfaceEntry("channels.adapters", "GET", "/v1/channels/adapters", ResponseShape.Array, async (c, t) => await c.Channels.AdaptersAsync(t)),

        new SurfaceEntry("channels.create", "POST", "/v1/channels", ResponseShape.Object, async (c, t) => await c.Channels.CreateAsync(new { kind = "web" }, t)),
        new SurfaceEntry("channels.get", "GET", "/v1/channels/ch_1", ResponseShape.Object, async (c, t) => await c.Channels.GetAsync("ch_1", t)),
        new SurfaceEntry("channels.list", "GET", "/v1/channels", ResponseShape.Array, async (c, t) => await c.Channels.ListAsync(t)),

        // /secret/rotate, not /rotate-secret as on the card routes.
        new SurfaceEntry("channels.rotate_secret", "POST", "/v1/channels/ch_1/secret/rotate", ResponseShape.Object, async (c, t) => await c.Channels.RotateSecretAsync("ch_1", t)),

        new SurfaceEntry("channels.update", "PATCH", "/v1/channels/ch_1", ResponseShape.Object, async (c, t) => await c.Channels.UpdateAsync("ch_1", new { name = "Website" }, t)),

        // ── contacts ───────────────────────────────────────────────────────
        new SurfaceEntry("contacts.add_tags", "POST", "/v1/contacts/c_1/tags", ResponseShape.Object, async (c, t) => await c.Contacts.AddTagsAsync("c_1", "vip", t)),
        new SurfaceEntry("contacts.bulk_upsert", "POST", "/v1/contacts/bulk", ResponseShape.Object, async (c, t) => await c.Contacts.BulkUpsertAsync(new[] { new ContactInput { Email = "a@example.com" } }, t)),

        // A POST that reads: the conversation context goes in the body.
        new SurfaceEntry("contacts.cards", "POST", "/v1/contacts/c_1/cards", ResponseShape.Object, async (c, t) => await c.Contacts.CardsAsync("c_1", null, t)),

        new SurfaceEntry("contacts.get", "GET", "/v1/contacts/c_1", ResponseShape.Object, async (c, t) => await c.Contacts.GetAsync("c_1", t)),
        new SurfaceEntry("contacts.journey", "GET", "/v1/contacts/c_1/journey", ResponseShape.Page, async (c, t) => await c.Contacts.JourneyAsync("c_1", t)),
        new SurfaceEntry("contacts.list", "GET", "/v1/contacts", ResponseShape.Page, async (c, t) => await c.Contacts.ListAsync(t)),
        new SurfaceEntry("contacts.merge", "POST", "/v1/contacts/c_1/merge", ResponseShape.Object, async (c, t) => await c.Contacts.MergeAsync("c_1", "c_2", t)),
        new SurfaceEntry("contacts.mood", "GET", "/v1/contacts/c_1/mood", ResponseShape.Object, async (c, t) => await c.Contacts.MoodAsync("c_1", null, t)),

        // A DELETE with a body: the surviving contact, the same object the tag
        // POST returns. Not a 204, whatever the verb suggests.
        new SurfaceEntry("contacts.remove_tag", "DELETE", "/v1/contacts/c_1/tags/vip", ResponseShape.Object, async (c, t) => await c.Contacts.RemoveTagAsync("c_1", "vip", t)),

        new SurfaceEntry("contacts.unsubscribe", "POST", "/v1/contacts/c_1/unsubscribe", ResponseShape.Object, async (c, t) => await c.Contacts.UnsubscribeAsync("c_1", true, null, t)),
        new SurfaceEntry("contacts.upsert", "POST", "/v1/contacts", ResponseShape.Object, async (c, t) => await c.Contacts.UpsertAsync(new ContactInput { Email = "a@example.com" }, t)),

        // ── conversations ──────────────────────────────────────────────────
        new SurfaceEntry("conversations.agent_handling", "GET", "/v1/conversations/k_1/agent-handling", ResponseShape.Object, async (c, t) => await c.Conversations.AgentHandlingAsync("k_1", t)),

        // Also a DELETE with a body: the row it just cancelled, so a caller
        // can show what it called off. `@ZodResponse(scheduledMessageSchema)`.
        new SurfaceEntry("conversations.cancel_scheduled", "DELETE", "/v1/conversations/scheduled-messages/s_1", ResponseShape.Object, async (c, t) => await c.Conversations.CancelScheduledAsync("s_1", t)),

        new SurfaceEntry("conversations.generate_reply", "POST", "/v1/conversations/k_1/generated-replies", ResponseShape.Object, async (c, t) => await c.Conversations.GenerateReplyAsync("k_1", null, t)),
        new SurfaceEntry("conversations.generated_replies", "GET", "/v1/conversations/k_1/generated-replies", ResponseShape.Array, async (c, t) => await c.Conversations.GeneratedRepliesAsync("k_1", t)),
        new SurfaceEntry("conversations.get", "GET", "/v1/conversations/k_1", ResponseShape.Object, async (c, t) => await c.Conversations.GetAsync("k_1", t)),

        // `items` and no cursor: an object, not a page. No walker for it.
        new SurfaceEntry("conversations.linked", "GET", "/v1/conversations/k_1/linked", ResponseShape.Object, async (c, t) => await c.Conversations.LinkedAsync("k_1", t)),

        new SurfaceEntry("conversations.list", "GET", "/v1/conversations", ResponseShape.Page, async (c, t) => await c.Conversations.ListAsync(t)),
        new SurfaceEntry("conversations.messages", "GET", "/v1/conversations/k_1/messages", ResponseShape.Page, async (c, t) => await c.Conversations.MessagesAsync("k_1", t)),
        new SurfaceEntry("conversations.outcomes", "GET", "/v1/conversations/k_1/outcomes", ResponseShape.Object, async (c, t) => await c.Conversations.OutcomesAsync("k_1", t)),
        new SurfaceEntry("conversations.record_outcome", "POST", "/v1/conversations/k_1/outcomes", ResponseShape.Object, async (c, t) => await c.Conversations.RecordOutcomeAsync("k_1", new { kind = "resolved" }, t)),
        new SurfaceEntry("conversations.return_to_bot", "POST", "/v1/conversations/k_1/return-to-bot", ResponseShape.Object, async (c, t) => await c.Conversations.ReturnToBotAsync("k_1", "b_1", t)),
        new SurfaceEntry("conversations.schedule_message", "POST", "/v1/conversations/k_1/scheduled-messages", ResponseShape.Object, async (c, t) => await c.Conversations.ScheduleMessageAsync("k_1", new { body = "later" }, t)),
        new SurfaceEntry("conversations.scheduled_messages", "GET", "/v1/conversations/k_1/scheduled-messages", ResponseShape.Array, async (c, t) => await c.Conversations.ScheduledMessagesAsync("k_1", t)),
        new SurfaceEntry("conversations.send", "POST", "/v1/conversations/k_1/messages", ResponseShape.Object, async (c, t) => await c.Conversations.SendAsync("k_1", new MessageInput { Body = "hi" }, t)),
        new SurfaceEntry("conversations.set_agent_handling", "PUT", "/v1/conversations/k_1/agent-handling", ResponseShape.Object, async (c, t) => await c.Conversations.SetAgentHandlingAsync("k_1", new { handledBy = "ai" }, t)),

        // A page, with a `total` beside `items` and `nextCursor` — the badge
        // number, which ignores the cursor. The doc comment on this method
        // claimed it was not paginated at all.
        new SurfaceEntry("conversations.unattended", "GET", "/v1/conversations/unattended", ResponseShape.Page, async (c, t) => await c.Conversations.UnattendedAsync(t)),

        // Answers 200 and is near-useless from a key: the counts are the
        // CALLER's, and a key's membership user id is the key's own.
        new SurfaceEntry("conversations.unread_counts", "GET", "/v1/conversations/unread-counts", ResponseShape.Object, async (c, t) => await c.Conversations.UnreadCountsAsync(null, t)),

        new SurfaceEntry("conversations.update_blocks", "PATCH", "/v1/conversations/k_1/messages/m_1/blocks", ResponseShape.Object, async (c, t) => await c.Conversations.UpdateBlocksAsync("k_1", "m_1", new { blocks = System.Array.Empty<object>() }, t)),

        // ── customer_card_endpoints ────────────────────────────────────────
        new SurfaceEntry("customer_card_endpoints.activate_secret", "POST", "/v1/customer-card-endpoints/ce_1/activate-secret", ResponseShape.Object, async (c, t) => await c.CustomerCardEndpoints.ActivateSecretAsync("ce_1", t)),
        new SurfaceEntry("customer_card_endpoints.create", "POST", "/v1/customer-card-endpoints", ResponseShape.Object, async (c, t) => await c.CustomerCardEndpoints.CreateAsync(new { url = "https://example.com/cards" }, t)),
        new SurfaceEntry("customer_card_endpoints.delete", "DELETE", "/v1/customer-card-endpoints/ce_1", ResponseShape.Object, async (c, t) => await c.CustomerCardEndpoints.DeleteAsync("ce_1", t)),
        new SurfaceEntry("customer_card_endpoints.list", "GET", "/v1/customer-card-endpoints", ResponseShape.Array, async (c, t) => await c.CustomerCardEndpoints.ListAsync(t)),
        new SurfaceEntry("customer_card_endpoints.rotate_secret", "POST", "/v1/customer-card-endpoints/ce_1/rotate-secret", ResponseShape.Object, async (c, t) => await c.CustomerCardEndpoints.RotateSecretAsync("ce_1", t)),

        // ── customer_cards ─────────────────────────────────────────────────
        new SurfaceEntry("customer_cards.create", "POST", "/v1/customer-cards", ResponseShape.Object, async (c, t) => await c.CustomerCards.CreateAsync(new { url = "https://example.com/card" }, t)),
        new SurfaceEntry("customer_cards.delete", "DELETE", "/v1/customer-cards/cc_1", ResponseShape.Object, async (c, t) => await c.CustomerCards.DeleteAsync("cc_1", t)),
        new SurfaceEntry("customer_cards.list", "GET", "/v1/customer-cards", ResponseShape.Array, async (c, t) => await c.CustomerCards.ListAsync(t)),
        new SurfaceEntry("customer_cards.rotate_secret", "POST", "/v1/customer-cards/cc_1/rotate-secret", ResponseShape.Object, async (c, t) => await c.CustomerCards.RotateSecretAsync("cc_1", t)),
        new SurfaceEntry("customer_cards.update", "PATCH", "/v1/customer-cards/cc_1", ResponseShape.Object, async (c, t) => await c.CustomerCards.UpdateAsync("cc_1", new { position = 1 }, t)),

        // ── docs_sources ───────────────────────────────────────────────────
        new SurfaceEntry("docs_sources.create", "POST", "/v1/docs-sources", ResponseShape.Object, async (c, t) => await c.DocsSources.CreateAsync(new { provider = "gitbook" }, t)),

        // 200 with `{ kept }`, not a 204: no article is deleted.
        new SurfaceEntry("docs_sources.delete", "DELETE", "/v1/docs-sources/ds_1", ResponseShape.Object, async (c, t) => await c.DocsSources.DeleteAsync("ds_1", t)),

        // A POST that answers a BARE ARRAY, and stores nothing.
        new SurfaceEntry("docs_sources.discover", "POST", "/v1/docs-sources/discover", ResponseShape.Array, async (c, t) => await c.DocsSources.DiscoverAsync(new { provider = "gitbook", token = "tk" }, t)),

        new SurfaceEntry("docs_sources.list", "GET", "/v1/docs-sources", ResponseShape.Array, async (c, t) => await c.DocsSources.ListAsync(t)),
        new SurfaceEntry("docs_sources.providers", "GET", "/v1/docs-sources/providers", ResponseShape.Array, async (c, t) => await c.DocsSources.ProvidersAsync(t)),
        new SurfaceEntry("docs_sources.sync", "POST", "/v1/docs-sources/ds_1/sync", ResponseShape.Object, async (c, t) => await c.DocsSources.SyncAsync("ds_1", false, t)),
        new SurfaceEntry("docs_sources.update", "PATCH", "/v1/docs-sources/ds_1", ResponseShape.Object, async (c, t) => await c.DocsSources.UpdateAsync("ds_1", new { status = "paused" }, t)),
        new SurfaceEntry("docs_sources.webhook", "POST", "/v1/docs-sources/ds_1/webhook", ResponseShape.Object, async (c, t) => await c.DocsSources.WebhookAsync("ds_1", t)),

        // ── email_templates ────────────────────────────────────────────────
        // A DELETE that restores the built-in copy, and HAS a body:
        // `{ restored: false }` at 200 when there was no override.
        new SurfaceEntry("email_templates.delete", "DELETE", "/v1/email-templates/workspace_invite", ResponseShape.Object, async (c, t) => await c.EmailTemplates.DeleteAsync("workspace_invite", t)),

        new SurfaceEntry("email_templates.list", "GET", "/v1/email-templates", ResponseShape.Array, async (c, t) => await c.EmailTemplates.ListAsync(t)),
        new SurfaceEntry("email_templates.preview", "POST", "/v1/email-templates/preview", ResponseShape.Object, async (c, t) => await c.EmailTemplates.PreviewAsync(new { kind = "workspace_invite" }, t)),
        new SurfaceEntry("email_templates.update", "PUT", "/v1/email-templates/workspace_invite", ResponseShape.Object, async (c, t) => await c.EmailTemplates.UpdateAsync("workspace_invite", new { subject = "Join us" }, t)),

        // ── events ─────────────────────────────────────────────────────────
        new SurfaceEntry("events.list", "GET", "/v1/cdp/events", ResponseShape.Page, async (c, t) => await c.Events.ListAsync(t)),
        new SurfaceEntry("events.track", "POST", "/v1/cdp/events", ResponseShape.Object, async (c, t) => await c.Events.TrackAsync("c_1", "trial_started", null, t)),

        // ── kb ─────────────────────────────────────────────────────────────
        new SurfaceEntry("kb.article", "GET", "/v1/kb/articles/getting-started", ResponseShape.Object, async (c, t) => await c.Kb.ArticleAsync("getting-started", t)),
        new SurfaceEntry("kb.articles", "GET", "/v1/kb/articles", ResponseShape.Page, async (c, t) => await c.Kb.ArticlesAsync(t)),
        new SurfaceEntry("kb.collections", "GET", "/v1/kb/collections", ResponseShape.Array, async (c, t) => await c.Kb.CollectionsAsync(t)),

        // ── knowledge_gaps ─────────────────────────────────────────────────
        new SurfaceEntry("knowledge_gaps.draft", "POST", "/v1/knowledge-gaps/kg_1/draft", ResponseShape.Object, async (c, t) => await c.KnowledgeGaps.DraftAsync("kg_1", t)),
        new SurfaceEntry("knowledge_gaps.list", "GET", "/v1/knowledge-gaps", ResponseShape.Array, async (c, t) => await c.KnowledgeGaps.ListAsync(null, null, t)),
        new SurfaceEntry("knowledge_gaps.update", "PATCH", "/v1/knowledge-gaps/kg_1", ResponseShape.Object, async (c, t) => await c.KnowledgeGaps.UpdateAsync("kg_1", "resolved", t)),

        // ── macros ─────────────────────────────────────────────────────────
        new SurfaceEntry("macros.actions", "GET", "/v1/macros/actions", ResponseShape.Object, async (c, t) => await c.Macros.ActionsAsync(t)),
        new SurfaceEntry("macros.create", "POST", "/v1/macros", ResponseShape.Object, async (c, t) => await c.Macros.CreateAsync(new { name = "Refund" }, t)),

        // The ONLY real 204 on this surface: @HttpCode(204), and the handler
        // returns nothing. The fake answers it with no body at all.
        new SurfaceEntry("macros.delete", "DELETE", "/v1/macros/m_1", ResponseShape.Empty, async (c, t) => await c.Macros.DeleteAsync("m_1", t)),

        new SurfaceEntry("macros.get", "GET", "/v1/macros/m_1", ResponseShape.Object, async (c, t) => await c.Macros.GetAsync("m_1", t)),

        // @PaginationExempt, bare array, capped at 200 by usage rank. The one
        // that cost this SDK a decode error in production.
        new SurfaceEntry("macros.list", "GET", "/v1/macros", ResponseShape.Array, async (c, t) => await c.Macros.ListAsync(t)),

        new SurfaceEntry("macros.template", "GET", "/v1/macros/templates/refund-request", ResponseShape.Object, async (c, t) => await c.Macros.TemplateAsync("refund-request", t)),

        // `{ items, total }` with no cursor: an object. `total` is
        // `items.length` over a frozen catalogue, not a hint that more exist.
        new SurfaceEntry("macros.templates", "GET", "/v1/macros/templates", ResponseShape.Object, async (c, t) => await c.Macros.TemplatesAsync(t)),

        new SurfaceEntry("macros.update", "PATCH", "/v1/macros/m_1", ResponseShape.Object, async (c, t) => await c.Macros.UpdateAsync("m_1", new { name = "Refund v2" }, t)),

        // ── reports ────────────────────────────────────────────────────────
        // Four of the thirteen are bare arrays and the rest are objects, so
        // this namespace is the reason a per-route column exists at all.
        new SurfaceEntry("reports.agents", "GET", "/v1/reports/agents", ResponseShape.Array, async (c, t) => await c.Reports.AgentsAsync(t)),
        new SurfaceEntry("reports.attribution", "GET", "/v1/reports/attribution", ResponseShape.Array, async (c, t) => await c.Reports.AttributionAsync(t)),
        new SurfaceEntry("reports.cohorts", "GET", "/v1/reports/cohorts", ResponseShape.Object, async (c, t) => await c.Reports.CohortsAsync(t)),
        new SurfaceEntry("reports.conversations_by_day", "GET", "/v1/reports/conversations-by-day", ResponseShape.Array, async (c, t) => await c.Reports.ConversationsByDayAsync(t)),
        new SurfaceEntry("reports.labels", "GET", "/v1/reports/labels", ResponseShape.Object, async (c, t) => await c.Reports.LabelsAsync(t)),
        new SurfaceEntry("reports.overview", "GET", "/v1/reports/overview", ResponseShape.Object, async (c, t) => await c.Reports.OverviewAsync(t)),
        new SurfaceEntry("reports.response_times", "GET", "/v1/reports/response-times", ResponseShape.Object, async (c, t) => await c.Reports.ResponseTimesAsync(t)),
        new SurfaceEntry("reports.sla", "GET", "/v1/reports/sla", ResponseShape.Object, async (c, t) => await c.Reports.SlaAsync(t)),
        new SurfaceEntry("reports.sources", "GET", "/v1/reports/sources", ResponseShape.Array, async (c, t) => await c.Reports.SourcesAsync(t)),
        new SurfaceEntry("reports.teams", "GET", "/v1/reports/teams", ResponseShape.Object, async (c, t) => await c.Reports.TeamsAsync(t)),
        new SurfaceEntry("reports.theme_conversations", "GET", "/v1/reports/themes/th_1/conversations", ResponseShape.Object, async (c, t) => await c.Reports.ThemeConversationsAsync("th_1", t)),
        new SurfaceEntry("reports.themes", "GET", "/v1/reports/themes", ResponseShape.Object, async (c, t) => await c.Reports.ThemesAsync(t)),
        new SurfaceEntry("reports.voice", "GET", "/v1/reports/voice", ResponseShape.Object, async (c, t) => await c.Reports.VoiceAsync(t)),

        // ── root ───────────────────────────────────────────────────────────
        // A discriminated union of two objects, so `object` either way. The
        // key branch is byte-identical to whoami's; the difference is that
        // this route is ungated, which is what makes it the liveness check.
        new SurfaceEntry("root.me", "GET", "/v1/me", ResponseShape.Object, async (c, t) => await c.MeAsync(t)),

        new SurfaceEntry("root.whoami", "GET", "/v1/api-keys/whoami", ResponseShape.Object, async (c, t) => await c.WhoAmIAsync(t)),

        // ── search ─────────────────────────────────────────────────────────
        // A page carrying `tier` and `facets` beside the two fields that
        // define it. The single-page form returns the WHOLE envelope.
        new SurfaceEntry("search.query", "GET", "/v1/search", ResponseShape.Page, async (c, t) => await c.Search.QueryAsync("refund", t)),

        // ── tickets ────────────────────────────────────────────────────────
        new SurfaceEntry("tickets.bulk", "POST", "/v1/tickets/bulk", ResponseShape.Object, async (c, t) => await c.Tickets.BulkAsync(new { ids = new[] { "t_1" } }, t)),
        new SurfaceEntry("tickets.comments", "GET", "/v1/tickets/t_1/comments", ResponseShape.Array, async (c, t) => await c.Tickets.CommentsAsync("t_1", t)),
        new SurfaceEntry("tickets.conversations", "GET", "/v1/tickets/t_1/conversations", ResponseShape.Array, async (c, t) => await c.Tickets.ConversationsAsync("t_1", t)),
        new SurfaceEntry("tickets.for_conversation", "GET", "/v1/tickets/for-conversation/k_1", ResponseShape.Array, async (c, t) => await c.Tickets.ForConversationAsync("k_1", t)),
        new SurfaceEntry("tickets.lineage", "GET", "/v1/tickets/t_1/lineage", ResponseShape.Object, async (c, t) => await c.Tickets.LineageAsync("t_1", t)),
        new SurfaceEntry("tickets.list", "GET", "/v1/tickets", ResponseShape.Page, async (c, t) => await c.Tickets.ListAsync(t)),
        new SurfaceEntry("tickets.merge", "POST", "/v1/tickets/t_1/merge", ResponseShape.Object, async (c, t) => await c.Tickets.MergeAsync("t_1", new { mergedId = "t_2" }, t)),

        // The path parameter is a CONVERSATION id, under /v1/tickets.
        new SurfaceEntry("tickets.open_count", "GET", "/v1/tickets/open-count/k_1", ResponseShape.Object, async (c, t) => await c.Tickets.OpenCountAsync("k_1", t)),

        new SurfaceEntry("tickets.queues", "GET", "/v1/tickets/queues", ResponseShape.Array, async (c, t) => await c.Tickets.QueuesAsync(t)),
        new SurfaceEntry("tickets.split", "POST", "/v1/tickets/t_1/split", ResponseShape.Object, async (c, t) => await c.Tickets.SplitAsync("t_1", new { subject = "part two" }, t)),
        new SurfaceEntry("tickets.status_history", "GET", "/v1/tickets/t_1/status-history", ResponseShape.Object, async (c, t) => await c.Tickets.StatusHistoryAsync("t_1", t)),

        // `items` plus two totals and no cursor: an object, not a page.
        new SurfaceEntry("tickets.time", "GET", "/v1/tickets/t_1/time", ResponseShape.Object, async (c, t) => await c.Tickets.TimeAsync("t_1", t)),

        // ── triggers ───────────────────────────────────────────────────────
        // An upsert that answers 201 on the update branch too.
        new SurfaceEntry("triggers.create", "POST", "/v1/triggers", ResponseShape.Object, async (c, t) => await c.Triggers.CreateAsync(new { name = "Welcome", eventType = "conversation.created", enabled = true }, t)),

        new SurfaceEntry("triggers.delete", "DELETE", "/v1/triggers/tg_1", ResponseShape.Object, async (c, t) => await c.Triggers.DeleteAsync("tg_1", t)),

        // The third late-flagged page.
        new SurfaceEntry("triggers.list", "GET", "/v1/triggers", ResponseShape.Page, async (c, t) => await c.Triggers.ListAsync(t)),

        // ── webhooks ───────────────────────────────────────────────────────
        // /v1/webhooks is the ENDPOINTS; /v1/webhooks/deliveries is the log,
        // and only the second one is a page.
        new SurfaceEntry("webhooks.create", "POST", "/v1/webhooks", ResponseShape.Object, async (c, t) => await c.Webhooks.CreateAsync(new { url = "https://example.com/hook", eventTypes = new[] { "conversation.created" } }, t)),
        new SurfaceEntry("webhooks.deliveries", "GET", "/v1/webhooks/deliveries", ResponseShape.Page, async (c, t) => await c.Webhooks.DeliveriesAsync(t)),

        // 200 with `{ ok: true }`, not a 204. Opened to key auth on
        // 2026-10-14; before that a key could add a delivery target and
        // never remove it.
        new SurfaceEntry("webhooks.delete", "DELETE", "/v1/webhooks/wh_1", ResponseShape.Object, async (c, t) => await c.Webhooks.DeleteAsync("wh_1", t)),
        new SurfaceEntry("webhooks.delivery", "GET", "/v1/webhooks/deliveries/d_1", ResponseShape.Object, async (c, t) => await c.Webhooks.DeliveryAsync("d_1", t)),
        new SurfaceEntry("webhooks.events", "GET", "/v1/webhooks/events", ResponseShape.Object, async (c, t) => await c.Webhooks.EventsAsync(t)),
        new SurfaceEntry("webhooks.list", "GET", "/v1/webhooks", ResponseShape.Array, async (c, t) => await c.Webhooks.ListAsync(t)),
        new SurfaceEntry("webhooks.redeliver", "POST", "/v1/webhooks/deliveries/d_1/redeliver", ResponseShape.Object, async (c, t) => await c.Webhooks.RedeliverAsync("d_1", t)),
        new SurfaceEntry("webhooks.schema_versions", "GET", "/v1/webhooks/schema-versions", ResponseShape.Object, async (c, t) => await c.Webhooks.SchemaVersionsAsync(t)),
        new SurfaceEntry("webhooks.set_schema_version", "PATCH", "/v1/webhooks/wh_1/schema-version", ResponseShape.Object, async (c, t) => await c.Webhooks.SetSchemaVersionAsync("wh_1", "2026-01-01", t)),

        // ── workspace ──────────────────────────────────────────────────────
        new SurfaceEntry("workspace.agent_skills", "GET", "/v1/agents/skills", ResponseShape.Object, async (c, t) => await c.Workspace.AgentSkillsAsync(t)),
        new SurfaceEntry("workspace.inboxes", "GET", "/v1/inboxes", ResponseShape.Array, async (c, t) => await c.Workspace.InboxesAsync(t)),

        // The SECOND key-accessible route on the api-keys controller, beside
        // whoami. A bare array of `{ value, group }`.
        new SurfaceEntry("workspace.key_scopes", "GET", "/v1/api-keys/scopes", ResponseShape.Array, async (c, t) => await c.Workspace.KeyScopesAsync(t)),

        new SurfaceEntry("workspace.labels", "GET", "/v1/labels", ResponseShape.Array, async (c, t) => await c.Workspace.LabelsAsync(t)),

        // A bare array even for a key, where it is a list of one.
        // @AllowWhenSuspended, so it answers when most routes do not.
        new SurfaceEntry("workspace.mine", "GET", "/v1/workspaces/me", ResponseShape.Array, async (c, t) => await c.Workspace.MineAsync(t)),
        new SurfaceEntry("workspace.secrets", "GET", "/v1/workspace/secrets", ResponseShape.Object, async (c, t) => await c.Workspace.SecretsAsync(t)),
        new SurfaceEntry("workspace.settings", "GET", "/v1/workspace/settings", ResponseShape.Object, async (c, t) => await c.Workspace.SettingsAsync(t)),
        new SurfaceEntry("workspace.team_members", "GET", "/v1/teams/tm_1/members", ResponseShape.Array, async (c, t) => await c.Workspace.TeamMembersAsync("tm_1", t)),
    };

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
    /// <para>
    /// THIRTEEN now. <c>bots.list</c>, <c>bots.deliveries</c> and
    /// <c>triggers.list</c> all end in <c>buildPaginatedResponse</c> and were
    /// unflagged in the manifest until 2026-10-14 — the same class of
    /// omission as the three above, found the same way: by reading the
    /// handlers rather than the manifest.
    /// </para>
    /// <para>
    /// What is deliberately NOT here: <c>billing.invoices</c>, which really
    /// is cursor-paginated but whose envelope key is <c>data</c>. A walker
    /// over it would read <c>items</c>, find nothing, and report an empty
    /// page forever — a wrong answer with a 200, which is worse than the
    /// decode error this file exists to prevent. Likewise
    /// <c>conversations.linked</c> and <c>tickets.time</c>, which have an
    /// <c>items</c> key and no cursor at all.
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

            // The three the manifest had not flagged. All `buildPaginatedResponse`.
            ["bots.list"] = (c, t) => c.Bots.ListAllAsync(t),
            ["bots.deliveries"] = (c, t) => c.Bots.DeliveriesAllAsync("b_1", null, null, t),
            ["triggers.list"] = (c, t) => c.Triggers.ListAllAsync(t),
        };
}
