using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// The help centre: published articles and the collections they sit in.
/// Reached as <c>chatly.Kb</c>.
/// </summary>
/// <remarks>
/// This is the same corpus the AI agent retrieves from, so what you read here
/// is what it will cite.
/// </remarks>
public sealed class KnowledgeBaseResource
{
    private const string Articles = "/v1/kb/articles";

    private readonly Transport _transport;

    internal KnowledgeBaseResource(Transport transport) => _transport = transport;

    /// <summary>One page of articles. <c>GET /v1/kb/articles</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The first page.</returns>
    public Task<Page<JsonElement>> ArticlesAsync(CancellationToken cancellationToken = default)
        => ArticlesAsync(null, cancellationToken);

    /// <summary>One page of articles, filtered. <c>GET /v1/kb/articles</c>.</summary>
    /// <param name="options">Cursor, limit, filters, sort.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The requested page.</returns>
    public Task<Page<JsonElement>> ArticlesAsync(ListOptions? options, CancellationToken cancellationToken = default)
        => _transport.GetPageAsync(Articles, ListOptions.QueryFor(options), cancellationToken);

    /// <summary>Every article, page by page.</summary>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of articles.</returns>
    public IAsyncEnumerable<JsonElement> ArticlesAllAsync(CancellationToken cancellationToken = default)
        => ArticlesAllAsync(null, cancellationToken);

    /// <summary>Every article matching the options, page by page.</summary>
    /// <param name="options">Limit, filters, sort, starting cursor.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of articles.</returns>
    public IAsyncEnumerable<JsonElement> ArticlesAllAsync(ListOptions? options, CancellationToken cancellationToken = default)
        => _transport.WalkPagesAsync(Articles, options, cancellationToken);

    /// <summary>
    /// One article by slug. <c>GET /v1/kb/articles/{slug}</c>.
    /// </summary>
    /// <remarks>
    /// By slug, not id — the slug is the stable, human-readable handle that
    /// also appears in the published help-centre URL.
    /// </remarks>
    /// <param name="slug">The article's slug.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The article.</returns>
    public Task<JsonElement> ArticleAsync(string slug, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Articles}/{Route.Segment(slug, nameof(slug))}",
            null,
            cancellationToken);

    /// <summary>
    /// The collections articles are grouped into.
    /// <c>GET /v1/kb/collections</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The collections.</returns>
    public Task<JsonElement> CollectionsAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/kb/collections", null, cancellationToken);
}
