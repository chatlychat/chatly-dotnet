using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// Customer-data-platform events — what a person did in your product.
/// Reached as <c>chatly.Events</c>.
/// </summary>
/// <remarks>
/// Tracked events show on a contact's journey, and workflow triggers can fire
/// on them, so this is how your product's own milestones become things Chatly
/// can act on.
/// </remarks>
public sealed class EventsResource
{
    private const string Collection = "/v1/cdp/events";

    private readonly Transport _transport;

    internal EventsResource(Transport transport) => _transport = transport;

    /// <summary>
    /// Record an event against a contact. <c>POST /v1/cdp/events</c>.
    /// </summary>
    /// <param name="contactId">The contact who did it.</param>
    /// <param name="name">The event name, e.g. <c>trial_started</c>.</param>
    /// <param name="properties">Optional properties. Keys are sent verbatim.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The stored event.</returns>
    public Task<JsonElement> TrackAsync(
        string contactId,
        string name,
        IDictionary<string, object?>? properties = null,
        CancellationToken cancellationToken = default)
        => TrackAsync(new { contactId, name, properties }, cancellationToken);

    /// <summary>
    /// Record an event from your own payload shape.
    /// <c>POST /v1/cdp/events</c>.
    /// </summary>
    /// <param name="request">Serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The stored event.</returns>
    public Task<JsonElement> TrackAsync(object request, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(HttpMethod.Post, Collection, request, null, cancellationToken);

    /// <summary>
    /// Read events back. <c>GET /v1/cdp/events</c>.
    /// </summary>
    /// <remarks>
    /// The same collection <see cref="TrackAsync(object, CancellationToken)"/>
    /// writes to, so "did my tracking call actually land?" has an answer
    /// outside the dashboard.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The first page of events.</returns>
    public Task<Page<JsonElement>> ListAsync(CancellationToken cancellationToken = default)
        => ListAsync(null, cancellationToken);

    /// <summary>One page of events, optionally for one contact.</summary>
    /// <param name="options">
    /// Cursor, limit, and <see cref="EventsListOptions.ContactId"/> to scope
    /// the stream to one person.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The requested page.</returns>
    public Task<Page<JsonElement>> ListAsync(
        EventsListOptions? options,
        CancellationToken cancellationToken = default)
        => _transport.GetPageAsync(Collection, ListOptions.QueryFor(options), cancellationToken);

    /// <summary>Every event, page by page.</summary>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of events.</returns>
    public IAsyncEnumerable<JsonElement> ListAllAsync(CancellationToken cancellationToken = default)
        => ListAllAsync(null, cancellationToken);

    /// <summary>Every event matching the options, page by page.</summary>
    /// <param name="options">Limit, contact filter, starting cursor.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of events.</returns>
    public IAsyncEnumerable<JsonElement> ListAllAsync(
        EventsListOptions? options,
        CancellationToken cancellationToken = default)
        => _transport.WalkPagesAsync(Collection, options, cancellationToken);
}
