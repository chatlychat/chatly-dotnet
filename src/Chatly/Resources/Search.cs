using System;
using System.Collections.Generic;
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

    /// <summary>
    /// One page of matches. <c>GET /v1/search</c>.
    /// </summary>
    /// <remarks>
    /// Cursor-paginated: the response is <c>{ items, nextCursor }</c> with
    /// <c>tier</c> and <c>facets</c> beside them.
    /// <see cref="QueryAllAsync(string, CancellationToken)"/> walks it.
    /// </remarks>
    /// <param name="query">The search text.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The whole envelope: matches, cursor, tier and facets.</returns>
    public Task<JsonElement> QueryAsync(string query, CancellationToken cancellationToken = default)
        => QueryAsync(query, null, cancellationToken);

    /// <summary>
    /// Search, narrowed by kind and the usual list options.
    /// <c>GET /v1/search</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="SearchOptions.Kinds"/> goes out as repeated
    /// <c>kinds</c> parameters — <c>kinds=kb&amp;kinds=ticket</c>, two
    /// parameters, never one comma-joined value.
    /// </remarks>
    /// <param name="query">The search text.</param>
    /// <param name="options">Kinds, limit, cursor.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The whole envelope: matches, cursor, tier and facets.</returns>
    /// <exception cref="ArgumentException">The query text is empty.</exception>
    public Task<JsonElement> QueryAsync(
        string query,
        SearchOptions? options,
        CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(Path, Parameters(query, options), cancellationToken);

    /// <summary>
    /// Every match, page by page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Yields the hits alone, which is the right trade for a loop — but it
    /// means <c>tier</c> and <c>facets</c> are not visible here. Read those
    /// from <see cref="QueryAsync(string, CancellationToken)"/>, which returns
    /// the whole envelope; they describe the result set rather than any one
    /// hit.
    /// </para>
    /// <para>
    /// This route carries <c>paged: true</c> in the shared manifest and had no
    /// walker in this SDK until now, which contradicted §5b of the contract —
    /// a paged route is promised both a single page and an auto-paginator.
    /// </para>
    /// </remarks>
    /// <param name="query">The search text.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of matches.</returns>
    /// <exception cref="ArgumentException">The query text is empty.</exception>
    public IAsyncEnumerable<JsonElement> QueryAllAsync(
        string query,
        CancellationToken cancellationToken = default)
        => QueryAllAsync(query, null, cancellationToken);

    /// <summary>Every match for the narrowed search, page by page.</summary>
    /// <param name="query">The search text.</param>
    /// <param name="options">Kinds, limit, starting cursor.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of matches.</returns>
    /// <exception cref="ArgumentException">The query text is empty.</exception>
    public IAsyncEnumerable<JsonElement> QueryAllAsync(
        string query,
        SearchOptions? options,
        CancellationToken cancellationToken = default)
    {
        // Validated here rather than inside the lazy walk: an
        // IAsyncEnumerable built from an async iterator would not run a guard
        // until the first MoveNext, so the ArgumentException would surface at
        // the `await foreach` instead of at the call.
        var first = Parameters(query, options);
        return _transport.WalkPagesAsync(
            Path,
            cursor => cursor is null
                ? first
                : Parameters(query, (options ?? new SearchOptions()) with { Cursor = cursor }),
            cancellationToken);
    }

    /// <summary>
    /// The query string for one request: <c>q</c>, then the options.
    /// </summary>
    /// <remarks>
    /// `SearchOptions` is NOT a `ListOptions` — the search route reads
    /// neither `filter[]`, `sort` nor `fields`, so it has its own
    /// `ToQuery()` and `ListOptions.QueryFor` does not accept it.
    /// </remarks>
    private static ChatlyQuery Parameters(string query, SearchOptions? options)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Search query cannot be empty.", nameof(query));
        }

        return new ChatlyQuery()
            .Add("q", query)
            .AddRange(options?.ToQuery() ?? new ChatlyQuery());
    }

    /// <summary>
    /// Search help-centre articles only — <c>GET /v1/search</c> with
    /// <c>kinds=kb</c>.
    /// </summary>
    /// <remarks>
    /// Worth its own method because "find the article that answers this" is
    /// the common case, and getting there otherwise means knowing the
    /// <c>kinds</c> parameter exists.
    /// </remarks>
    /// <param name="query">The search text.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Matching articles.</returns>
    public Task<JsonElement> KbAsync(string query, CancellationToken cancellationToken = default)
        => QueryAsync(query, new SearchOptions { Kinds = new[] { "kb" } }, cancellationToken);
}
