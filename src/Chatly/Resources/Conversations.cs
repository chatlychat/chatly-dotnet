using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// Threads of messages, on any channel. Reached as <c>chatly.Conversations</c>.
/// </summary>
/// <remarks>
/// Messages sent with an API key are recorded as sent by an integration, not
/// by the person who created the key — a key outlives whoever issued it, so
/// attributing its writes to them would be wrong the moment they leave.
/// </remarks>
public sealed class ConversationsResource
{
    private const string Collection = "/v1/conversations";

    private const string UnattendedPath = Collection + "/unattended";

    private readonly Transport _transport;

    internal ConversationsResource(Transport transport) => _transport = transport;

    /// <summary>One page of conversations. <c>GET /v1/conversations</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The first page.</returns>
    public Task<Page<JsonElement>> ListAsync(CancellationToken cancellationToken = default)
        => ListAsync(null, cancellationToken);

    /// <summary>One page of conversations, filtered. <c>GET /v1/conversations</c>.</summary>
    /// <param name="options">Cursor, limit, filters, sort.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The requested page.</returns>
    public Task<Page<JsonElement>> ListAsync(ListOptions? options, CancellationToken cancellationToken = default)
        => _transport.GetPageAsync(Collection, ListOptions.QueryFor(options), cancellationToken);

    /// <summary>Every conversation, page by page.</summary>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of conversations.</returns>
    public IAsyncEnumerable<JsonElement> ListAllAsync(CancellationToken cancellationToken = default)
        => ListAllAsync(null, cancellationToken);

    /// <summary>Every conversation matching the options, page by page.</summary>
    /// <param name="options">Limit, filters, sort, starting cursor.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of conversations.</returns>
    public IAsyncEnumerable<JsonElement> ListAllAsync(ListOptions? options, CancellationToken cancellationToken = default)
        => _transport.WalkPagesAsync(Collection, options, cancellationToken);

    /// <summary>One conversation by id. <c>GET /v1/conversations/{id}</c>.</summary>
    /// <param name="conversationId">The conversation's id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The conversation.</returns>
    public Task<JsonElement> GetAsync(string conversationId, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Collection}/{Route.Segment(conversationId, nameof(conversationId))}",
            null,
            cancellationToken);

    /// <summary>
    /// One page of messages in a thread.
    /// <c>GET /v1/conversations/{id}/messages</c>.
    /// </summary>
    /// <remarks>Read the thread before replying to it.</remarks>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The first page of messages.</returns>
    public Task<Page<JsonElement>> MessagesAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
        => MessagesAsync(conversationId, null, cancellationToken);

    /// <summary>One page of messages in a thread, filtered.</summary>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="options">Cursor, limit, filters, sort.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The requested page of messages.</returns>
    public Task<Page<JsonElement>> MessagesAsync(
        string conversationId,
        ListOptions? options,
        CancellationToken cancellationToken = default)
        => _transport.GetPageAsync(
            MessagesPath(conversationId),
            ListOptions.QueryFor(options),
            cancellationToken);

    /// <summary>Every message in a thread, page by page.</summary>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of messages.</returns>
    public IAsyncEnumerable<JsonElement> MessagesAllAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
        => MessagesAllAsync(conversationId, null, cancellationToken);

    /// <summary>Every message in a thread matching the options, page by page.</summary>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="options">Limit, filters, sort, starting cursor.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of messages.</returns>
    public IAsyncEnumerable<JsonElement> MessagesAllAsync(
        string conversationId,
        ListOptions? options,
        CancellationToken cancellationToken = default)
        => _transport.WalkPagesAsync(MessagesPath(conversationId), options, cancellationToken);

    /// <summary>
    /// Post a message into a thread.
    /// <c>POST /v1/conversations/{id}/messages</c>.
    /// </summary>
    /// <param name="conversationId">The conversation to post into.</param>
    /// <param name="message">The message. <see cref="MessageInput.Body"/> is required.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The stored message.</returns>
    public Task<JsonElement> SendAsync(
        string conversationId,
        MessageInput message,
        CancellationToken cancellationToken = default)
        => SendAsync(conversationId, (object)message, cancellationToken);

    /// <summary>
    /// Post a message into a thread from any object.
    /// <c>POST /v1/conversations/{id}/messages</c>.
    /// </summary>
    /// <remarks>
    /// Use this overload for fields <see cref="MessageInput"/> does not model
    /// — attachments, rich blocks, an interactive canvas.
    /// </remarks>
    /// <param name="conversationId">The conversation to post into.</param>
    /// <param name="message">Serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The stored message.</returns>
    public Task<JsonElement> SendAsync(
        string conversationId,
        object message,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            MessagesPath(conversationId),
            message,
            null,
            cancellationToken);

    /// <summary>
    /// One page of threads nobody has answered.
    /// <c>GET /v1/conversations/unattended</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This route IS cursor-paginated.</b> It reads <c>cursor</c> and
    /// <c>limit</c> and answers <c>{ items, nextCursor, total }</c>; the
    /// remarks here used to say it was "a bounded operational list, not a
    /// feed, so it comes back whole", and a caller who believed that processed
    /// the first page of a triage queue and thought it had seen all of it.
    /// Use <see cref="UnattendedAllAsync(CancellationToken)"/> to walk it.
    /// </para>
    /// <para>
    /// The whole envelope is returned rather than just <c>items</c>, because
    /// <c>total</c> sits beside them and ignores the cursor — it is the badge
    /// number, and a caller that only ever saw the hits would lose it.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The first page: <c>items</c>, <c>nextCursor</c> and <c>total</c>.
    /// </returns>
    public Task<JsonElement> UnattendedAsync(CancellationToken cancellationToken = default)
        => UnattendedAsync(null, cancellationToken);

    /// <summary>
    /// One page of unattended threads, narrowed.
    /// <c>GET /v1/conversations/unattended</c>.
    /// </summary>
    /// <remarks>
    /// The wire names are <c>assignee</c>, <c>team</c> and <c>channel</c> —
    /// see <see cref="UnattendedOptions"/>, which until now no method accepted.
    /// </remarks>
    /// <param name="options">Assignee, team, channel, wait time, page size, cursor.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The requested page.</returns>
    public Task<JsonElement> UnattendedAsync(
        UnattendedOptions? options,
        CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(UnattendedPath, options?.ToQuery(), cancellationToken);

    /// <summary>
    /// Every unattended thread, page by page.
    /// </summary>
    /// <remarks>
    /// Yields the threads alone. If you need the queue's <c>total</c>, read it
    /// from <see cref="UnattendedAsync(CancellationToken)"/> — it is a
    /// property of the queue, not of an item.
    /// </remarks>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of unattended conversations.</returns>
    public IAsyncEnumerable<JsonElement> UnattendedAllAsync(CancellationToken cancellationToken = default)
        => UnattendedAllAsync(null, cancellationToken);

    /// <summary>Every unattended thread matching the options, page by page.</summary>
    /// <param name="options">Assignee, team, channel, wait time, page size, starting cursor.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of unattended conversations.</returns>
    public IAsyncEnumerable<JsonElement> UnattendedAllAsync(
        UnattendedOptions? options,
        CancellationToken cancellationToken = default)
        => _transport.WalkPagesAsync(
            UnattendedPath,
            cursor => (cursor is null
                    ? options ?? new UnattendedOptions()
                    : (options ?? new UnattendedOptions()) with { Cursor = cursor })
                .ToQuery(),
            cancellationToken);

    /// <summary>
    /// Who is handling this thread — a human, or an AI agent.
    /// <c>GET /v1/conversations/{id}/agent-handling</c>.
    /// </summary>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The current handling state.</returns>
    public Task<JsonElement> AgentHandlingAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(AgentHandlingPath(conversationId), null, cancellationToken);

    /// <summary>
    /// Hand this thread to an AI agent, or take it back.
    /// <c>PUT /v1/conversations/{id}/agent-handling</c>.
    /// </summary>
    /// <remarks>
    /// This is the switch a bring-your-own-agent integration flips when it
    /// takes over a thread, and flips back when it escalates to a human.
    /// </remarks>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="state">
    /// The new handling state, serialized as the request body. See the REST
    /// reference for the accepted fields.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The updated handling state.</returns>
    public Task<JsonElement> SetAgentHandlingAsync(
        string conversationId,
        object state,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Put,
            AgentHandlingPath(conversationId),
            state,
            null,
            cancellationToken);

    /// <summary>
    /// Replies an AI agent has drafted for this thread but not sent.
    /// <c>GET /v1/conversations/{id}/generated-replies</c>.
    /// </summary>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The pending drafts.</returns>
    public Task<JsonElement> GeneratedRepliesAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(GeneratedRepliesPath(conversationId), null, cancellationToken);

    /// <summary>
    /// Ask for a drafted reply to this thread.
    /// <c>POST /v1/conversations/{id}/generated-replies</c>.
    /// </summary>
    /// <remarks>
    /// Proposes a draft; it is not sent until it is approved. Sending and
    /// discarding a draft are separate routes with no typed method here —
    /// reach them through
    /// <see cref="ChatlyClient.RequestAsync(HttpMethod, string, ChatlyQuery, object, CancellationToken)"/>.
    /// </remarks>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="request">
    /// The request body, or null for the endpoint's defaults.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The proposed draft.</returns>
    public Task<JsonElement> GenerateReplyAsync(
        string conversationId,
        object? request = null,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            GeneratedRepliesPath(conversationId),
            request,
            null,
            cancellationToken);

    /// <summary>
    /// Messages queued to go out later on this thread.
    /// <c>GET /v1/conversations/{id}/scheduled-messages</c>.
    /// </summary>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The scheduled messages.</returns>
    public Task<JsonElement> ScheduledMessagesAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(ScheduledPath(conversationId), null, cancellationToken);

    /// <summary>
    /// Queue a message to send later.
    /// <c>POST /v1/conversations/{id}/scheduled-messages</c>.
    /// </summary>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="request">
    /// The request body — the message and when to send it. See the REST
    /// reference for the accepted fields.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The scheduled message, including the id to cancel it by.</returns>
    public Task<JsonElement> ScheduleMessageAsync(
        string conversationId,
        object request,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            ScheduledPath(conversationId),
            request,
            null,
            cancellationToken);

    /// <summary>
    /// Cancel a queued message before it goes out.
    /// <c>DELETE /v1/conversations/scheduled-messages/{scheduledId}</c>.
    /// </summary>
    /// <remarks>
    /// Note the path: the scheduled-message id is enough, so no conversation
    /// id is needed here.
    /// </remarks>
    /// <param name="scheduledMessageId">The id from the scheduling response.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The row it just cancelled. This route answers <b>200 with a body</b>,
    /// not 204 — <c>@ZodResponse(scheduledMessageSchema)</c> — so a caller can
    /// show what it called off. The only real 204 on this surface is
    /// <c>DELETE /v1/macros/{id}</c>; this doc comment used to claim this was
    /// one too, and the Node client acted on that belief by typing the method
    /// <c>void</c> and throwing the body away.
    /// </returns>
    public Task<JsonElement> CancelScheduledAsync(
        string scheduledMessageId,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Delete,
            $"{Collection}/scheduled-messages/{Route.Segment(scheduledMessageId, nameof(scheduledMessageId))}",
            null,
            null,
            cancellationToken);

    private static string MessagesPath(string conversationId)
        => $"{Collection}/{Route.Segment(conversationId, nameof(conversationId))}/messages";

    private static string AgentHandlingPath(string conversationId)
        => $"{Collection}/{Route.Segment(conversationId, nameof(conversationId))}/agent-handling";

    private static string GeneratedRepliesPath(string conversationId)
        => $"{Collection}/{Route.Segment(conversationId, nameof(conversationId))}/generated-replies";

    private static string ScheduledPath(string conversationId)
        => $"{Collection}/{Route.Segment(conversationId, nameof(conversationId))}/scheduled-messages";

    /// <summary>
    /// Conversations merged into or out of this one.
    /// <c>GET /v1/conversations/{id}/linked</c>.
    /// </summary>
    /// <remarks>
    /// <b>An object with an <c>items</c> key and no cursor</b> — it looks
    /// like a page and is not one, which is why there is no walker here. The
    /// set is bounded by how many times a thread has been merged. Each row's
    /// <c>relation</c> says which direction the merge went:
    /// <c>merged_into</c> or <c>merged_from</c>.
    /// </remarks>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ items: [...] }</c>.</returns>
    public Task<JsonElement> LinkedAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Collection}/{Route.Segment(conversationId, nameof(conversationId))}/linked",
            null,
            cancellationToken);

    /// <summary>
    /// The outcomes recorded on one conversation.
    /// <c>GET /v1/conversations/{id}/outcomes</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Newest first, and merged from three places — AI resolutions,
    /// escalations and outcomes reported through
    /// <see cref="RecordOutcomeAsync(string, object, CancellationToken)"/> —
    /// so a row's shape varies with where it came from. An object with an
    /// <c>items</c> key, not a page.
    /// </para>
    /// <para>
    /// <b>Needs <c>conversations:read</c>, which the route does not
    /// declare.</b> It carries no <c>@RequirePermission</c> of its own and
    /// resolves the conversation before reading outcomes, so a key without
    /// that scope gets a <b>404 on every conversation</b> rather than a 403 —
    /// which reads as "no such thread" and sends you looking for the wrong
    /// bug. Check your key with
    /// <see cref="ChatlyClient.WhoAmIAsync(CancellationToken)"/> first.
    /// </para>
    /// </remarks>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ items: [...] }</c>, newest first.</returns>
    public Task<JsonElement> OutcomesAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            OutcomesPath(conversationId),
            null,
            cancellationToken);

    /// <summary>
    /// Report an outcome on a conversation — resolved, escalated or
    /// purchased. <c>POST /v1/conversations/{id}/outcomes</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is how an integration closes the attribution loop: if the thread
    /// ended in a sale or a deflection your own systems know about, record it
    /// here and it appears in the attribution and resolution reports.
    /// </para>
    /// <para>
    /// <b>Not every 200 writes a row.</b> <c>created: false</c> means an
    /// equal outcome was already on record and this call changed nothing —
    /// which makes it naturally safe to retry, independently of the
    /// idempotency key.
    /// </para>
    /// <para>
    /// Needs <c>conversations:write</c> <i>and</i> the undeclared
    /// <c>conversations:read</c> described on
    /// <see cref="OutcomesAsync(string, CancellationToken)"/>; without the
    /// read scope this answers 404.
    /// </para>
    /// </remarks>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="outcome">The outcome, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The recorded outcome, with <c>created</c>.</returns>
    public Task<JsonElement> RecordOutcomeAsync(
        string conversationId,
        object outcome,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            OutcomesPath(conversationId),
            outcome,
            null,
            cancellationToken);

    /// <summary>
    /// Hand an open conversation back to an AI agent after a human took it.
    /// <c>POST /v1/conversations/{id}/return-to-bot</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reverse of an escalation: the thread leaves the human queue and
    /// belongs to the named bot. <paramref name="botId"/> is <b>required and
    /// not defaulted to whoever owned it before</b> — a thread three
    /// assistants have touched has no obvious "the" bot, and guessing means
    /// the visitor talks to the returns agent about a size exchange. List the
    /// choices with <c>chatly.Bots.ListAsync</c>.
    /// </para>
    /// <para>
    /// Needs <c>conversations:assign</c>, not <c>conversations:write</c>.
    /// Refused on a closed thread.
    /// </para>
    /// </remarks>
    /// <param name="conversationId">The conversation to hand back.</param>
    /// <param name="botId">The bot to assign it to. A UUID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The conversation, now owned by that bot.</returns>
    public Task<JsonElement> ReturnToBotAsync(
        string conversationId,
        string botId,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/{Route.Segment(conversationId, nameof(conversationId))}/return-to-bot",
            new { botId },
            null,
            cancellationToken);

    /// <summary>
    /// Per-user unread badge counts for the inbox sidebar.
    /// <c>GET /v1/conversations/unread-counts</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is near-useless from an API key, and it is here because it
    /// answers 200 rather than failing.</b> The counts are per <i>user</i>,
    /// read from the caller's membership — and for a key, that membership's
    /// user id is the key's own. So you get the unread state of a principal
    /// that never opens conversations: zeroes, or whatever the key's own
    /// read-markers happen to say. It is not the workspace's unread total and
    /// there is no parameter that makes it one.
    /// </para>
    /// <para>
    /// Shipped anyway, with this warning, because the alternative is an SDK
    /// that silently lacks a route the manifest lists — and because a 200
    /// with the wrong number is the failure a caller most needs told about.
    /// For workspace-wide backlog, use
    /// <see cref="UnattendedAsync(CancellationToken)"/>, whose <c>total</c>
    /// is a real figure.
    /// </para>
    /// <para>
    /// One object: four scalars plus three bucket arrays. Not a page and not
    /// a bare array.
    /// </para>
    /// </remarks>
    /// <param name="brand">Narrow to one brand. Null counts across all of them.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The caller's own unread counts.</returns>
    public Task<JsonElement> UnreadCountsAsync(
        string? brand = null,
        CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Collection}/unread-counts",
            new ChatlyQuery().Add("brand", brand),
            cancellationToken);

    /// <summary>
    /// Patch the rich blocks on a message that has already been sent.
    /// <c>PATCH /v1/conversations/{id}/messages/{messageId}/blocks</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For editing an interactive message in place — marking a card as paid,
    /// disabling a button someone already pressed — without posting a second
    /// message the visitor has to read. The widget re-renders the message
    /// where it stands.
    /// </para>
    /// <para>
    /// Needs <c>messages:write</c> <i>and</i> the undeclared
    /// <c>conversations:read</c> described on
    /// <see cref="OutcomesAsync(string, CancellationToken)"/>: the handler
    /// resolves the conversation first, so a key without the read scope sees
    /// a 404 here and not a 403.
    /// </para>
    /// </remarks>
    /// <param name="conversationId">The conversation the message is in.</param>
    /// <param name="messageId">The message to patch.</param>
    /// <param name="patch">The block patch, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The patch result.</returns>
    public Task<JsonElement> UpdateBlocksAsync(
        string conversationId,
        string messageId,
        object patch,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            new HttpMethod("PATCH"),
            $"{Collection}/{Route.Segment(conversationId, nameof(conversationId))}"
            + $"/messages/{Route.Segment(messageId, nameof(messageId))}/blocks",
            patch,
            null,
            cancellationToken);

    private static string OutcomesPath(string conversationId)
        => $"{Collection}/{Route.Segment(conversationId, nameof(conversationId))}/outcomes";
}
