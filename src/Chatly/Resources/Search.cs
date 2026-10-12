using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// Full-text search across contacts, conversations, messages, tickets and the
/// knowledge base. Reached as <c>chatly.Search</c>.
/// </summary>
public sealed class SearchResource
{
    private const string Path = "/v1/search";

    private readonly Transport _transport;

    internal SearchResource(Transport transport) => _transport = transport;

    /// <summary>Search everything the key can read. <c>GET /v1/search</c>.</summary>
    /// <param name="query">The search text.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Matches, grouped by kind.</returns>
    public Task<JsonElement> QueryAsync(string query, CancellationToken cancellationToken = default)
        => QueryAsync(query, null, cancellationToken);

    /// <summary>
    /// Search, narrowed by kind and the usual list options.
    /// <c>GET /v1/search</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="SearchOptions.Kinds"/> goes out as repeated
    /// <c>kinds[]</c> parameters — <c>kinds[]=kb&amp;kinds[]=article</c>, two
    /// parameters, never one comma-joined value.
    /// </remarks>
    /// <param name="query">The search text.</param>
    /// <param name="options">Kinds, limit, cursor.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Matches, grouped by kind.</returns>
    /// <exception cref="ArgumentException">The query text is empty.</exception>
    public Task<JsonElement> QueryAsync(
        string query,
        SearchOptions? options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Search query cannot be empty.", nameof(query));
        }

        var parameters = new ChatlyQuery().Add("q", query).AddRange(ListOptions.QueryFor(options));
        return _transport.GetJsonAsync(Path, parameters, cancellationToken);
    }

    /// <summary>
    /// Search help-centre articles only — <c>GET /v1/search</c> with
    /// <c>kinds[]=kb</c>.
    /// </summary>
    /// <remarks>
    /// Worth its own method because "find the article that answers this" is
    /// the common case, and getting there otherwise means knowing the
    /// <c>kinds[]</c> parameter exists.
    /// </remarks>
    /// <param name="query">The search text.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Matching articles.</returns>
    public Task<JsonElement> KbAsync(string query, CancellationToken cancellationToken = default)
        => QueryAsync(query, new SearchOptions { Kinds = new[] { "kb" } }, cancellationToken);
}
