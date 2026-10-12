using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly;

/// <summary>
/// One page of a list endpoint, exactly as the API returned it.
/// </summary>
/// <typeparam name="T">The item type — <see cref="JsonElement"/> for the
/// methods on this SDK's resources.</typeparam>
public sealed class Page<T>
{
    /// <summary>Build a page.</summary>
    /// <param name="items">The items on this page, in server order.</param>
    /// <param name="nextCursor">The cursor for the next page, or null on the last one.</param>
    /// <param name="raw">The whole decoded response, for fields not modelled here.</param>
    public Page(IReadOnlyList<T> items, string? nextCursor, JsonElement raw)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        NextCursor = nextCursor;
        Raw = raw;
    }

    /// <summary>The items on this page, in the order the server returned them.</summary>
    /// <remarks>
    /// Keep that order. Do not re-sort by <c>id</c>: ids here are a mix of
    /// UUID v4 and v7, so id order is neither insertion order nor time order.
    /// </remarks>
    public IReadOnlyList<T> Items { get; }

    /// <summary>
    /// The cursor to pass as <see cref="ListOptions.Cursor"/> for the next
    /// page. Null means this was the last page.
    /// </summary>
    public string? NextCursor { get; }

    /// <summary>
    /// The entire page response as JSON, including any field this class does
    /// not model (a total, a facet count, an endpoint-specific extra).
    /// </summary>
    public JsonElement Raw { get; }

    /// <summary>True when <see cref="NextCursor"/> is set.</summary>
    public bool HasMore => NextCursor is not null;
}

/// <summary>
/// Walks a cursor-paginated endpoint page by page.
/// </summary>
/// <remarks>
/// Every <c>ListAllAsync</c> on this SDK's resources is this, bound to one
/// endpoint. It is public so the same behaviour is available for a paged route
/// reached through <see cref="ChatlyClient.RequestAsync(System.Net.Http.HttpMethod, string, ChatlyQuery, object, CancellationToken)"/>.
/// </remarks>
public static class Pager
{
    /// <summary>
    /// Yield every item from every page, fetching the next page only when the
    /// consumer asks for an item past the end of the current one.
    /// </summary>
    /// <remarks>
    /// <para>Two stopping rules, and both matter:</para>
    /// <list type="number">
    /// <item><description>
    /// A null <c>nextCursor</c> ends the walk. That is the server saying "last
    /// page".
    /// </description></item>
    /// <item><description>
    /// A cursor that has already been used ends the walk too. A server bug, a
    /// caching proxy or a mid-walk deletion can make an endpoint hand back a
    /// cursor it already gave out; without this check the loop is infinite and
    /// the symptom is a job that burns rate limit until someone notices.
    /// </description></item>
    /// </list>
    /// </remarks>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="fetchPage">
    /// Fetches one page given a cursor (null for the first page).
    /// </param>
    /// <param name="cancellationToken">Stops the walk between items.</param>
    /// <returns>An async stream over every item in every page.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fetchPage"/> is null.</exception>
    public static async IAsyncEnumerable<T> WalkAsync<T>(
        Func<string?, CancellationToken, Task<Page<T>>> fetchPage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (fetchPage is null)
        {
            throw new ArgumentNullException(nameof(fetchPage));
        }

        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await fetchPage(cursor, cancellationToken).ConfigureAwait(false);

            foreach (var item in page.Items)
            {
                yield return item;
            }

            var next = page.NextCursor;
            if (string.IsNullOrEmpty(next))
            {
                yield break;
            }

            if (!seenCursors.Add(next))
            {
                // Repeated cursor: stop rather than spin. See the remarks above.
                yield break;
            }

            cursor = next;
        }
    }

    /// <summary>
    /// Decode a <c>{ "items": [...], "nextCursor": "..." }</c> body.
    /// </summary>
    /// <remarks>
    /// A missing or non-array <c>items</c> throws rather than yielding an
    /// empty page. An empty list and "this response is not a page" are
    /// different facts, and conflating them turns a shape change into a job
    /// that silently processes nothing.
    /// </remarks>
    internal static Page<JsonElement> ParsePage(JsonElement root, string route)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            throw new ChatlyException(
                $"{route} did not return a page: expected an object with an \"items\" array, got "
                + $"{root.ValueKind}. Use RequestAsync for an endpoint that is not paginated.",
                200,
                "unexpected_response_shape",
                null,
                root.ValueKind == JsonValueKind.Undefined ? null : root.ToString());
        }

        var list = new List<JsonElement>(items.GetArrayLength());
        foreach (var item in items.EnumerateArray())
        {
            list.Add(item);
        }

        return new Page<JsonElement>(list, ReadCursor(root), root);
    }

    /// <summary>
    /// Read <c>nextCursor</c>, tolerating the snake_case spelling.
    /// </summary>
    /// <remarks>
    /// The API sends <c>nextCursor</c>. <c>next_cursor</c> is accepted because
    /// a wrong guess here does not fail loudly — it ends the walk one page in,
    /// and a partial result looks like a complete one.
    /// </remarks>
    private static string? ReadCursor(JsonElement root)
    {
        if (root.TryGetProperty("nextCursor", out var camel) && camel.ValueKind == JsonValueKind.String)
        {
            return camel.GetString();
        }

        if (root.TryGetProperty("next_cursor", out var snake) && snake.ValueKind == JsonValueKind.String)
        {
            return snake.GetString();
        }

        return null;
    }
}
