using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// The people your agents talk to. Reached as <c>chatly.Contacts</c>.
/// </summary>
/// <remarks>
/// A contact is identified by your own <c>externalId</c> where you have one:
/// <see cref="UpsertAsync(ContactInput, CancellationToken)"/> keys on it, so
/// the same call twice updates one record rather than creating two.
/// </remarks>
public sealed class ContactsResource
{
    private const string Collection = "/v1/contacts";

    private readonly Transport _transport;

    internal ContactsResource(Transport transport) => _transport = transport;

    /// <summary>One page of contacts. <c>GET /v1/contacts</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The first page, with a cursor for the next.</returns>
    public Task<Page<JsonElement>> ListAsync(CancellationToken cancellationToken = default)
        => ListAsync(null, cancellationToken);

    /// <summary>One page of contacts, filtered. <c>GET /v1/contacts</c>.</summary>
    /// <param name="options">Cursor, limit, filters, sort.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The requested page.</returns>
    public Task<Page<JsonElement>> ListAsync(ListOptions? options, CancellationToken cancellationToken = default)
        => _transport.GetPageAsync(Collection, ListOptions.QueryFor(options), cancellationToken);

    /// <summary>
    /// Every contact, page by page, fetched as you consume it.
    /// </summary>
    /// <remarks>
    /// Stops on a null cursor, and also stops if the API ever repeats a cursor
    /// — so this cannot become an infinite loop that burns your rate limit.
    /// </remarks>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of contacts.</returns>
    public IAsyncEnumerable<JsonElement> ListAllAsync(CancellationToken cancellationToken = default)
        => ListAllAsync(null, cancellationToken);

    /// <summary>Every contact matching the options, page by page.</summary>
    /// <param name="options">Limit, filters, sort, and a starting cursor.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of contacts.</returns>
    public IAsyncEnumerable<JsonElement> ListAllAsync(ListOptions? options, CancellationToken cancellationToken = default)
        => _transport.WalkPagesAsync(Collection, options, cancellationToken);

    /// <summary>One contact by id. <c>GET /v1/contacts/{id}</c>.</summary>
    /// <param name="contactId">The contact's Chatly id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The contact.</returns>
    /// <exception cref="ChatlyNotFoundException">No such contact in this workspace.</exception>
    public Task<JsonElement> GetAsync(string contactId, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Collection}/{Route.Segment(contactId, nameof(contactId))}",
            null,
            cancellationToken);

    /// <summary>
    /// Create or update a contact. <c>POST /v1/contacts</c>.
    /// </summary>
    /// <remarks>
    /// Upserts on <see cref="ContactInput.ExternalId"/> when you supply one.
    /// Fields left null are not touched.
    /// </remarks>
    /// <param name="contact">The contact to write.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The stored contact.</returns>
    public Task<JsonElement> UpsertAsync(ContactInput contact, CancellationToken cancellationToken = default)
        => UpsertAsync((object)contact, cancellationToken);

    /// <summary>
    /// Create or update a contact from any object. <c>POST /v1/contacts</c>.
    /// </summary>
    /// <param name="contact">
    /// Serialized as the request body — an anonymous object, a dictionary, or
    /// a JSON string sent verbatim.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The stored contact.</returns>
    public Task<JsonElement> UpsertAsync(object contact, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(HttpMethod.Post, Collection, contact, null, cancellationToken);

    /// <summary>
    /// Import many contacts in one call. <c>POST /v1/contacts/bulk</c>.
    /// </summary>
    /// <remarks>
    /// Up to 5000 rows per request; more is a 409. Each row upserts on its
    /// <c>externalId</c>, and the response reports per-row outcomes rather
    /// than failing the batch on one bad row.
    /// </remarks>
    /// <param name="contacts">The rows to import.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Per-row outcome counts and up to 100 row errors.</returns>
    public Task<JsonElement> BulkUpsertAsync(
        IEnumerable<ContactInput> contacts,
        CancellationToken cancellationToken = default)
        => BulkUpsertAsync((object)new { contacts }, cancellationToken);

    /// <summary>
    /// Import many contacts from your own payload shape.
    /// <c>POST /v1/contacts/bulk</c>.
    /// </summary>
    /// <param name="body">
    /// The whole request body, which the API expects to carry a
    /// <c>contacts</c> array.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Per-row outcome counts.</returns>
    public Task<JsonElement> BulkUpsertAsync(object body, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(HttpMethod.Post, $"{Collection}/bulk", body, null, cancellationToken);

    /// <summary>
    /// Fold one contact into another. <c>POST /v1/contacts/{id}/merge</c>.
    /// </summary>
    /// <remarks>
    /// <paramref name="mergedContactId"/> is absorbed into
    /// <paramref name="contactId"/>, which survives. Not reversible.
    /// </remarks>
    /// <param name="contactId">The contact that survives.</param>
    /// <param name="mergedContactId">The contact that is absorbed.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The surviving contact.</returns>
    public Task<JsonElement> MergeAsync(
        string contactId,
        string mergedContactId,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/{Route.Segment(contactId, nameof(contactId))}/merge",
            new { mergedId = mergedContactId },
            null,
            cancellationToken);

    /// <summary>
    /// Add a tag to a contact. <c>POST /v1/contacts/{id}/tags</c>.
    /// </summary>
    /// <remarks>
    /// One tag per call — the endpoint's body is a single <c>tag</c> string,
    /// despite the plural route. Call it again for a second tag.
    /// </remarks>
    /// <param name="contactId">The contact to tag.</param>
    /// <param name="tag">The tag to add.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The updated contact.</returns>
    public Task<JsonElement> AddTagsAsync(
        string contactId,
        string tag,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/{Route.Segment(contactId, nameof(contactId))}/tags",
            new { tag },
            null,
            cancellationToken);

    /// <summary>
    /// Remove a tag from a contact.
    /// <c>DELETE /v1/contacts/{id}/tags/{tag}</c>.
    /// </summary>
    /// <param name="contactId">The contact.</param>
    /// <param name="tag">
    /// The tag to remove. Percent-encoded for you, so a tag with a space or a
    /// slash works.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The updated contact.</returns>
    public Task<JsonElement> RemoveTagAsync(
        string contactId,
        string tag,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Delete,
            $"{Collection}/{Route.Segment(contactId, nameof(contactId))}/tags/{Route.Segment(tag, nameof(tag))}",
            null,
            null,
            cancellationToken);

    /// <summary>
    /// Record that a contact does or does not want to be contacted.
    /// <c>POST /v1/contacts/{id}/unsubscribe</c>.
    /// </summary>
    /// <param name="contactId">The contact.</param>
    /// <param name="unsubscribed">
    /// True to unsubscribe (the default), false to re-subscribe them.
    /// </param>
    /// <param name="reason">Optional note recorded with the change.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The updated contact.</returns>
    public Task<JsonElement> UnsubscribeAsync(
        string contactId,
        bool unsubscribed = true,
        string? reason = null,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/{Route.Segment(contactId, nameof(contactId))}/unsubscribe",
            new { unsubscribed, reason },
            null,
            cancellationToken);

    /// <summary>
    /// What this person has done: conversations, events, tickets, in order.
    /// <c>GET /v1/contacts/{id}/journey</c>.
    /// </summary>
    /// <param name="contactId">The contact.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The journey timeline.</returns>
    public Task<JsonElement> JourneyAsync(string contactId, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Collection}/{Route.Segment(contactId, nameof(contactId))}/journey",
            null,
            cancellationToken);
}
