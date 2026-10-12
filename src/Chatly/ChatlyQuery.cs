using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Chatly;

/// <summary>
/// An ordered list of query-string parameters, where a repeated name stays
/// repeated.
/// </summary>
/// <remarks>
/// <para>
/// This exists instead of a dictionary because the API's array parameters are
/// repeated keys, not comma-joined values: <c>kinds=kb&amp;kinds=ticket</c>
/// is two parameters. A <c>Dictionary&lt;string, string&gt;</c> cannot express
/// that, and joining with commas silently sends one value the API reads as a
/// single odd string.
/// </para>
/// <para>Values are percent-encoded on the way out; pass them raw.</para>
/// </remarks>
public sealed class ChatlyQuery : IEnumerable<KeyValuePair<string, string>>
{
    private readonly List<KeyValuePair<string, string>> _items = new List<KeyValuePair<string, string>>();

    /// <summary>Start an empty query.</summary>
    public ChatlyQuery()
    {
    }

    /// <summary>Start a query from existing name/value pairs, in order.</summary>
    /// <param name="items">The pairs to copy. Nulls are ignored.</param>
    public ChatlyQuery(IEnumerable<KeyValuePair<string, string>> items)
    {
        if (items is not null)
        {
            _items.AddRange(items);
        }
    }

    /// <summary>How many parameters are in the query, counting repeats separately.</summary>
    public int Count => _items.Count;

    /// <summary>True when there is nothing to append to the URL.</summary>
    public bool IsEmpty => _items.Count == 0;

    /// <summary>
    /// Append one parameter. A null <paramref name="value"/> is dropped, so
    /// optional arguments need no branching at the call site.
    /// </summary>
    /// <param name="name">Parameter name, e.g. <c>cursor</c> or <c>filter[]</c>.</param>
    /// <param name="value">Parameter value, raw and un-encoded.</param>
    /// <returns>This same query, for chaining.</returns>
    public ChatlyQuery Add(string name, string? value)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("Query parameter name cannot be empty.", nameof(name));
        }

        if (value is not null)
        {
            _items.Add(new KeyValuePair<string, string>(name, value));
        }

        return this;
    }

    /// <summary>Append one integer parameter, or nothing when it is null.</summary>
    /// <param name="name">Parameter name.</param>
    /// <param name="value">Parameter value.</param>
    /// <returns>This same query, for chaining.</returns>
    public ChatlyQuery Add(string name, int? value)
        => value.HasValue ? Add(name, value.Value.ToString(CultureInfo.InvariantCulture)) : this;

    /// <summary>Append one boolean parameter as <c>true</c>/<c>false</c>, or nothing when null.</summary>
    /// <param name="name">Parameter name.</param>
    /// <param name="value">Parameter value.</param>
    /// <returns>This same query, for chaining.</returns>
    public ChatlyQuery Add(string name, bool? value)
        => value.HasValue ? Add(name, value.Value ? "true" : "false") : this;

    /// <summary>
    /// Append the same name once per value — the repeated-key form the API's
    /// array parameters use.
    /// </summary>
    /// <param name="name">Parameter name, usually ending in <c>[]</c>.</param>
    /// <param name="values">The values. Null or empty adds nothing.</param>
    /// <returns>This same query, for chaining.</returns>
    public ChatlyQuery AddEach(string name, IEnumerable<string>? values)
    {
        if (values is null)
        {
            return this;
        }

        foreach (var value in values)
        {
            Add(name, value);
        }

        return this;
    }

    /// <summary>
    /// Append every parameter of another query, preserving order and repeats.
    /// </summary>
    /// <param name="other">The query to copy from. Null adds nothing.</param>
    /// <returns>This same query, for chaining.</returns>
    public ChatlyQuery AddRange(ChatlyQuery? other)
    {
        if (other is not null)
        {
            _items.AddRange(other._items);
        }

        return this;
    }

    /// <summary>
    /// Render as a URL query string <i>without</i> a leading <c>?</c>.
    /// Returns an empty string when there are no parameters.
    /// </summary>
    /// <returns>The percent-encoded query string.</returns>
    public override string ToString()
    {
        if (_items.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var item in _items)
        {
            if (sb.Length > 0)
            {
                sb.Append('&');
            }

            sb.Append(Uri.EscapeDataString(item.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(item.Value));
        }

        return sb.ToString();
    }

    /// <summary>Enumerate the parameters in order.</summary>
    /// <returns>An enumerator over name/value pairs.</returns>
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// The query parameters every list endpoint understands.
/// </summary>
/// <remarks>
/// <para>
/// Pagination is by <see cref="Cursor"/> and nothing else. Never page or sort
/// on <c>id</c>: ids in this product are a mix of UUID v4 and v7, so id order
/// is not time order.
/// </para>
/// <para>
/// Anything not modelled here goes in <see cref="Extra"/>, which is appended
/// verbatim.
/// </para>
/// </remarks>
public record ListOptions
{
    /// <summary>
    /// The opaque cursor from the previous page's <c>nextCursor</c>. Null for
    /// the first page.
    /// </summary>
    public string? Cursor { get; init; }

    /// <summary>How many items to return. The API applies its own ceiling.</summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Filters, sent as repeated <c>filter[]</c> parameters. The accepted
    /// expressions are per-endpoint; see the REST reference.
    /// </summary>
    public IReadOnlyList<string>? Filter { get; init; }

    /// <summary>Sort expression, sent as <c>sort</c>, e.g. <c>-createdAt</c>.</summary>
    public string? Sort { get; init; }

    /// <summary>
    /// Sparse-fieldset selection, sent as one comma-joined <c>fields</c>
    /// parameter.
    /// </summary>
    public IReadOnlyList<string>? Fields { get; init; }

    /// <summary>
    /// Extra parameters appended after the ones above — for an endpoint-specific
    /// filter this SDK does not model.
    /// </summary>
    public ChatlyQuery? Extra { get; init; }

    /// <summary>
    /// Render these options as query parameters. Override in a derived record
    /// to add endpoint-specific parameters; call <c>base.ToQuery()</c> first.
    /// </summary>
    /// <returns>A fresh query carrying every set option.</returns>
    protected internal virtual ChatlyQuery ToQuery()
    {
        var q = new ChatlyQuery()
            .Add("cursor", Cursor)
            .Add("limit", Limit)
            .AddEach("filter[]", Filter)
            .Add("sort", Sort);

        if (Fields is { Count: > 0 })
        {
            q.Add("fields", string.Join(",", Fields));
        }

        return q.AddRange(Extra);
    }

    /// <summary>Render options that may be null, as a query that may be empty.</summary>
    /// <param name="options">The options, or null.</param>
    /// <returns>A query; empty when <paramref name="options"/> is null.</returns>
    internal static ChatlyQuery QueryFor(ListOptions? options) => options?.ToQuery() ?? new ChatlyQuery();
}

/// <summary>
/// <see cref="ListOptions"/> plus the one filter the CDP event stream adds.
/// </summary>
public sealed record EventsListOptions : ListOptions
{
    /// <summary>
    /// Restrict the stream to one contact. Omit for the whole workspace.
    /// </summary>
    public string? ContactId { get; init; }

    /// <inheritdoc />
    protected internal override ChatlyQuery ToQuery() => base.ToQuery().Add("contactId", ContactId);
}

/// <summary>
/// Options for <c>GET /v1/search</c>.
/// </summary>
public sealed record SearchOptions : ListOptions
{
    /// <summary>
    /// Which kinds of record to search, sent as repeated <c>kinds</c>
    /// parameters. Omit to search everything the key can read.
    /// </summary>
    public IReadOnlyList<string>? Kinds { get; init; }

    /// <inheritdoc />
    /*
     * `kinds`, not `kinds[]`.
     *
     * `searchQuerySchema` is a zod object with a key named `kinds`, and a
     * query parser leaves `kinds[]` as a literal key — which the object
     * schema strips. The search then ran UNNARROWED and returned 200, so a
     * caller asking for help-centre articles got contacts, conversations,
     * messages and tickets with nothing to indicate the filter was ignored.
     * The schema folds the bracketed spelling now, for clients already
     * published; the plain repeated key is what it declares.
     */
    protected internal override ChatlyQuery ToQuery() => base.ToQuery().AddEach("kinds", Kinds);
}
