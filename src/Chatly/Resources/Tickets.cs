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
}
