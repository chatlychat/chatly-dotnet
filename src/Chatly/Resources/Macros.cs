using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// Macros — saved replies an agent can apply in one click. Reached as
/// <c>chatly.Macros</c>.
/// </summary>
public sealed class MacrosResource
{
    private const string Collection = "/v1/macros";

    private readonly Transport _transport;

    internal MacrosResource(Transport transport) => _transport = transport;

    /// <summary>One page of macros. <c>GET /v1/macros</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The first page.</returns>
    public Task<Page<JsonElement>> ListAsync(CancellationToken cancellationToken = default)
        => ListAsync(null, cancellationToken);

    /// <summary>One page of macros, filtered. <c>GET /v1/macros</c>.</summary>
    /// <param name="options">Cursor, limit, filters, sort.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The requested page.</returns>
    public Task<Page<JsonElement>> ListAsync(ListOptions? options, CancellationToken cancellationToken = default)
        => _transport.GetPageAsync(Collection, ListOptions.QueryFor(options), cancellationToken);

    /// <summary>Every macro, page by page.</summary>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of macros.</returns>
    public IAsyncEnumerable<JsonElement> ListAllAsync(CancellationToken cancellationToken = default)
        => ListAllAsync(null, cancellationToken);

    /// <summary>Every macro matching the options, page by page.</summary>
    /// <param name="options">Limit, filters, sort, starting cursor.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of macros.</returns>
    public IAsyncEnumerable<JsonElement> ListAllAsync(ListOptions? options, CancellationToken cancellationToken = default)
        => _transport.WalkPagesAsync(Collection, options, cancellationToken);

    /// <summary>Create a macro. <c>POST /v1/macros</c>.</summary>
    /// <param name="macro">The macro to create, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The created macro.</returns>
    public Task<JsonElement> CreateAsync(object macro, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(HttpMethod.Post, Collection, macro, null, cancellationToken);

    /// <summary>
    /// Change part of a macro. <c>PATCH /v1/macros/{id}</c>.
    /// </summary>
    /// <remarks>
    /// A PATCH, so send only the fields you are changing — fields you omit
    /// keep their current values.
    /// </remarks>
    /// <param name="macroId">The macro to change.</param>
    /// <param name="changes">The fields to change, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The updated macro.</returns>
    public Task<JsonElement> UpdateAsync(
        string macroId,
        object changes,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            new HttpMethod("PATCH"),
            $"{Collection}/{Route.Segment(macroId, nameof(macroId))}",
            changes,
            null,
            cancellationToken);

    /// <summary>Delete a macro. <c>DELETE /v1/macros/{id}</c>.</summary>
    /// <param name="macroId">The macro to delete.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The API's response, which is JSON null when it answers 204.</returns>
    public Task<JsonElement> DeleteAsync(string macroId, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Delete,
            $"{Collection}/{Route.Segment(macroId, nameof(macroId))}",
            null,
            null,
            cancellationToken);
}
