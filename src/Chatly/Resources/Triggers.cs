using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// Triggers — rules that fire an action when a named event happens. Reached
/// as <c>chatly.Triggers</c>.
/// </summary>
/// <remarks>
/// A trigger is an <c>eventType</c>, a list of conditions over the event's
/// payload, and a list of actions. The event type is a <b>string matched by
/// exact equality</b> at dispatch, and it is persisted as one — a trigger
/// naming an event nothing publishes is accepted at the write and simply
/// never fires, so take the name from the catalogue rather than from memory.
/// </remarks>
public sealed class TriggersResource
{
    private const string Collection = "/v1/triggers";

    private readonly Transport _transport;

    internal TriggersResource(Transport transport) => _transport = transport;

    /// <summary>One page of triggers. <c>GET /v1/triggers</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The first page, with a cursor for the next.</returns>
    public Task<Page<JsonElement>> ListAsync(CancellationToken cancellationToken = default)
        => ListAsync(null, cancellationToken);

    /// <summary>
    /// One page of triggers. <c>GET /v1/triggers</c>.
    /// </summary>
    /// <remarks>
    /// Genuinely cursor-paginated, which is easy to get wrong: the manifest
    /// did not flag it for a while, and an SDK that treated the envelope as a
    /// bare array saw <c>items</c> and <c>nextCursor</c> where it expected
    /// rows. <c>?fields=</c> projects, so a narrowed row is missing keys
    /// rather than carrying them null.
    /// </remarks>
    /// <param name="options">Cursor, limit, filters, sort, fields.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The requested page.</returns>
    public Task<Page<JsonElement>> ListAsync(ListOptions? options, CancellationToken cancellationToken = default)
        => _transport.GetPageAsync(Collection, ListOptions.QueryFor(options), cancellationToken);

    /// <summary>Every trigger, page by page, fetched as you consume it.</summary>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of triggers.</returns>
    public IAsyncEnumerable<JsonElement> ListAllAsync(CancellationToken cancellationToken = default)
        => ListAllAsync(null, cancellationToken);

    /// <summary>Every trigger matching the options, page by page.</summary>
    /// <param name="options">Limit, filters, sort, and a starting cursor.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of triggers.</returns>
    public IAsyncEnumerable<JsonElement> ListAllAsync(
        ListOptions? options,
        CancellationToken cancellationToken = default)
        => _transport.WalkPagesAsync(Collection, options, cancellationToken);

    /// <summary>
    /// Create or update a trigger. <c>POST /v1/triggers</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An upsert, despite the verb and the name.</b> With an <c>id</c> in
    /// the body it updates that trigger and returns the updated row; without
    /// one it inserts. There is no PATCH route, so this is how an edit is
    /// made.
    /// </para>
    /// <para>
    /// Both branches answer <b>201</b> — Nest's default for a POST stands for
    /// the update case too. Do not branch on 200 versus 201 to tell a create
    /// from an update; compare the id you sent.
    /// </para>
    /// <para>
    /// Conditions are capped at 50. <c>actions[].type</c> is a closed enum,
    /// so a typo there is a 400 — unlike <c>eventType</c>, which is a free
    /// string.
    /// </para>
    /// </remarks>
    /// <param name="trigger">
    /// The trigger, serialized as the request body. Include <c>id</c> to
    /// update an existing one.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The created or updated trigger, at 201 either way.</returns>
    public Task<JsonElement> CreateAsync(object trigger, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(HttpMethod.Post, Collection, trigger, null, cancellationToken);

    /// <summary>Delete a trigger. <c>DELETE /v1/triggers/{id}</c>.</summary>
    /// <remarks>
    /// Answers <b>200 with an acknowledgement</b>, not a 204. Anything the
    /// trigger already queued still runs; this stops future firings only.
    /// </remarks>
    /// <param name="triggerId">The trigger to delete.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Acknowledgement that the trigger is gone.</returns>
    public Task<JsonElement> DeleteAsync(string triggerId, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Delete,
            $"{Collection}/{Route.Segment(triggerId, nameof(triggerId))}",
            null,
            null,
            cancellationToken);
}
