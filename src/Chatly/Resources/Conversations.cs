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
}
