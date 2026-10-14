using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// Tickets — the tracked, assignable, SLA'd form of a request. Reached as
/// <c>chatly.Tickets</c>.
/// </summary>
public sealed class TicketsResource
{
    private const string Collection = "/v1/tickets";

    private readonly Transport _transport;

    internal TicketsResource(Transport transport) => _transport = transport;

    /// <summary>One page of tickets. <c>GET /v1/tickets</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The first page.</returns>
    public Task<Page<JsonElement>> ListAsync(CancellationToken cancellationToken = default)
        => ListAsync(null, cancellationToken);

    /// <summary>One page of tickets, filtered. <c>GET /v1/tickets</c>.</summary>
    /// <param name="options">Cursor, limit, filters, sort.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The requested page.</returns>
    public Task<Page<JsonElement>> ListAsync(ListOptions? options, CancellationToken cancellationToken = default)
        => _transport.GetPageAsync(Collection, ListOptions.QueryFor(options), cancellationToken);

    /// <summary>Every ticket, page by page.</summary>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of tickets.</returns>
    public IAsyncEnumerable<JsonElement> ListAllAsync(CancellationToken cancellationToken = default)
        => ListAllAsync(null, cancellationToken);

    /// <summary>Every ticket matching the options, page by page.</summary>
    /// <param name="options">Limit, filters, sort, starting cursor.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of tickets.</returns>
    public IAsyncEnumerable<JsonElement> ListAllAsync(ListOptions? options, CancellationToken cancellationToken = default)
        => _transport.WalkPagesAsync(Collection, options, cancellationToken);

    /// <summary>
    /// The comment thread on a ticket, public and internal.
    /// <c>GET /v1/tickets/{id}/comments</c>.
    /// </summary>
    /// <param name="ticketId">The ticket.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The comments.</returns>
    public Task<JsonElement> CommentsAsync(string ticketId, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Collection}/{Route.Segment(ticketId, nameof(ticketId))}/comments",
            null,
            cancellationToken);

    /// <summary>
    /// The ticket raised from a conversation, if there is one.
    /// <c>GET /v1/tickets/for-conversation/{conversationId}</c>.
    /// </summary>
    /// <remarks>
    /// Useful when your integration knows the thread and needs the ticket:
    /// inbound email, for instance, auto-creates a ticket for a new thread.
    /// </remarks>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The ticket for that conversation.</returns>
    public Task<JsonElement> ForConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Collection}/for-conversation/{Route.Segment(conversationId, nameof(conversationId))}",
            null,
            cancellationToken);

    /// <summary>The workspace's ticket queues. <c>GET /v1/tickets/queues</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The queues.</returns>
    public Task<JsonElement> QueuesAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync($"{Collection}/queues", null, cancellationToken);

    /// <summary>
    /// Merge one ticket into another. <c>POST /v1/tickets/{id}/merge</c>.
    /// </summary>
    /// <param name="ticketId">The ticket that survives.</param>
    /// <param name="request">
    /// The request body naming the ticket to absorb. See the REST reference.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The surviving ticket.</returns>
    public Task<JsonElement> MergeAsync(
        string ticketId,
        object request,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/{Route.Segment(ticketId, nameof(ticketId))}/merge",
            request,
            null,
            cancellationToken);

    /// <summary>
    /// Split a ticket in two. <c>POST /v1/tickets/{id}/split</c>.
    /// </summary>
    /// <param name="ticketId">The ticket to split.</param>
    /// <param name="request">
    /// The request body describing the new ticket. See the REST reference.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The resulting tickets.</returns>
    public Task<JsonElement> SplitAsync(
        string ticketId,
        object request,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/{Route.Segment(ticketId, nameof(ticketId))}/split",
            request,
            null,
            cancellationToken);

    /// <summary>
    /// Apply one change to many tickets. <c>POST /v1/tickets/bulk</c>.
    /// </summary>
    /// <remarks>
    /// Partial success is normal: the response reports which ids were updated
    /// and which failed, rather than failing the batch on one.
    /// </remarks>
    /// <param name="request">
    /// The request body — the ids and the change. See the REST reference.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Per-id outcomes.</returns>
    public Task<JsonElement> BulkAsync(object request, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(HttpMethod.Post, $"{Collection}/bulk", request, null, cancellationToken);

    /// <summary>
    /// The conversations attached to a ticket.
    /// <c>GET /v1/tickets/{id}/conversations</c>.
    /// </summary>
    /// <remarks>
    /// <b>A bare JSON array</b>, pagination-exempt — the inverse of
    /// <see cref="ForConversationAsync(string, CancellationToken)"/>. Gated
    /// on <c>conversations:read</c>, not a ticket scope, because what it
    /// discloses is conversation data.
    /// </remarks>
    /// <param name="ticketId">The ticket.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A JSON array of linked conversations.</returns>
    public Task<JsonElement> ConversationsAsync(
        string ticketId,
        CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Collection}/{Route.Segment(ticketId, nameof(ticketId))}/conversations",
            null,
            cancellationToken);

    /// <summary>
    /// What this ticket was split from, and what was split out of it.
    /// <c>GET /v1/tickets/{id}/lineage</c>.
    /// </summary>
    /// <remarks>
    /// Carries the raw <c>splitFromTicketId</c> / <c>parentTicketId</c>
    /// columns <i>alongside</i> the resolved references, and that pairing is
    /// the point: <b>a null reference beside a non-null id is a dangling
    /// pointer</b> — neither column has a foreign key to prevent one — so
    /// handle it rather than assuming the reference is always populated.
    /// <c>splitIntoTruncated</c> says the children list was capped.
    /// </remarks>
    /// <param name="ticketId">The ticket.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The lineage object.</returns>
    public Task<JsonElement> LineageAsync(string ticketId, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Collection}/{Route.Segment(ticketId, nameof(ticketId))}/lineage",
            null,
            cancellationToken);

    /// <summary>
    /// How many open tickets a conversation has.
    /// <c>GET /v1/tickets/open-count/{conversationId}</c>.
    /// </summary>
    /// <remarks>
    /// Note the path parameter is a <b>conversation</b> id, not a ticket id,
    /// despite living under <c>/v1/tickets</c>. One object with the count —
    /// for deciding whether to file a new ticket or append to an existing
    /// one without pulling the whole list.
    /// </remarks>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The open-ticket count for that conversation.</returns>
    public Task<JsonElement> OpenCountAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Collection}/open-count/{Route.Segment(conversationId, nameof(conversationId))}",
            null,
            cancellationToken);

    /// <summary>
    /// A ticket's status transitions. <c>GET /v1/tickets/{id}/status-history</c>.
    /// </summary>
    /// <remarks>
    /// <b>Oldest first</b> — the opposite order from most lists here — and
    /// capped. <c>truncated</c> is what distinguishes a capped timeline from
    /// a short one, and there is no cursor to read past the cap.
    /// </remarks>
    /// <param name="ticketId">The ticket.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The status timeline, oldest first.</returns>
    public Task<JsonElement> StatusHistoryAsync(string ticketId, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Collection}/{Route.Segment(ticketId, nameof(ticketId))}/status-history",
            null,
            cancellationToken);

    /// <summary>
    /// Time logged against a ticket, with both totals.
    /// <c>GET /v1/tickets/{id}/time</c>.
    /// </summary>
    /// <remarks>
    /// <b>An object with an <c>items</c> key and no cursor</b> — it reads
    /// like a page and is not one, so there is no walker. Beside the entries
    /// it carries <c>totalMinutes</c> and <c>billableMinutes</c>:
    /// <c>billableMinutes</c> is the figure that reaches an invoice and is
    /// computed server-side, so do not re-derive it by summing the entries
    /// you can see.
    /// </remarks>
    /// <param name="ticketId">The ticket.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ items, totalMinutes, billableMinutes }</c>, newest first.</returns>
    public Task<JsonElement> TimeAsync(string ticketId, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Collection}/{Route.Segment(ticketId, nameof(ticketId))}/time",
            null,
            cancellationToken);
}
