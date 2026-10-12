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
/// <remarks>
/// Deliberately <b>not</b> a <see cref="ListOptions"/>. Search is paginated by
/// <see cref="Cursor"/> and <see cref="Limit"/>, but it reads none of
/// <c>filter[]</c>, <c>sort</c> or <c>fields</c> — its own schema declares the
/// narrowing parameters below instead, and anything else is stripped. Offering
/// three fields that are silently discarded is worse than not offering them:
/// the caller gets a 200 and a wrong answer.
/// </remarks>
public sealed record SearchOptions
{
    /// <summary>
    /// Which kinds of record to search, sent as repeated <c>kinds</c>
    /// parameters. Omit to search everything the key can read.
    /// </summary>
    /// <remarks>
    /// The accepted values are <c>contact</c>, <c>conversation</c>,
    /// <c>message</c>, <c>ticket</c> and <c>kb</c>; anything else is a 400.
    /// </remarks>
    public IReadOnlyList<string>? Kinds { get; init; }

    /// <summary>Only match records created at or after this instant.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Only match records created strictly before this instant.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Restrict to one channel.</summary>
    public string? ChannelId { get; init; }

    /// <summary>Restrict to conversations assigned to one agent.</summary>
    public string? AssigneeUserId { get; init; }

    /// <summary>Restrict to one status.</summary>
    public string? Status { get; init; }

    /// <summary>Page size, 1–100. The API defaults to 25.</summary>
    public int? Limit { get; init; }

    /// <summary>
    /// The cursor from the previous page's <c>nextCursor</c>. Null for the
    /// first page.
    /// </summary>
    public string? Cursor { get; init; }

    /// <summary>Extra parameters appended after the ones above.</summary>
    public ChatlyQuery? Extra { get; init; }

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
    internal ChatlyQuery ToQuery()
        => new ChatlyQuery()
            .AddEach("kinds", Kinds)
            .Add("from", Iso(From))
            .Add("to", Iso(To))
            .Add("channelId", ChannelId)
            .Add("assigneeUserId", AssigneeUserId)
            .Add("status", Status)
            .Add("limit", Limit)
            .Add("cursor", Cursor)
            .AddRange(Extra);

    internal static string? Iso(DateTimeOffset? value)
        => value?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
}

/// <summary>
/// Options for <c>GET /v1/contacts/{id}/journey</c>.
/// </summary>
/// <remarks>
/// A contact's timeline is cursor-paginated like any list, but its parameters
/// are its own: a lookback window, a page size and a cursor.
/// </remarks>
public sealed record JourneyOptions
{
    /// <summary>
    /// How far back to look, in days: 1–365, and the API defaults to 90.
    /// </summary>
    /// <remarks>
    /// This bounds the whole walk, not one page. A timeline older than the
    /// window is not reachable by paging further.
    /// </remarks>
    public int? Days { get; init; }

    /// <summary>Page size, 1–100. The API defaults to 25.</summary>
    public int? Limit { get; init; }

    /// <summary>
    /// The cursor from the previous page's <c>nextCursor</c>.
    /// </summary>
    /// <remarks>
    /// <b>This one is an ISO-8601 instant</b> rather than the opaque token the
    /// other routes use — the timeline merges five sources and pages by time,
    /// so the server hands back the timestamp it stopped at. It is still a
    /// cursor: pass back what the previous page returned, and never build one.
    /// A hand-made instant silently skips or repeats whatever shares that
    /// moment across the five sources.
    /// </remarks>
    public string? Cursor { get; init; }

    /// <summary>Extra parameters appended after the ones above.</summary>
    public ChatlyQuery? Extra { get; init; }

    /// <summary>Render these options as query parameters.</summary>
    internal ChatlyQuery ToQuery()
        => new ChatlyQuery()
            .Add("days", Days)
            .Add("limit", Limit)
            .Add("cursor", Cursor)
            .AddRange(Extra);
}

/// <summary>
/// Options for <c>GET /v1/conversations/unattended</c>.
/// </summary>
/// <remarks>
/// The triage queue's own narrowing parameters. Note the wire names are
/// <c>assignee</c>, <c>team</c> and <c>channel</c> — short, and not the
/// <c>…UserId</c> / <c>…Type</c> spellings used elsewhere.
/// </remarks>
public sealed record UnattendedOptions
{
    /// <summary>
    /// Only threads assigned to this agent, or <c>"unassigned"</c> for
    /// nobody's.
    /// </summary>
    public string? Assignee { get; init; }

    /// <summary>Only threads owned by this team.</summary>
    public string? Team { get; init; }

    /// <summary>Only threads on this channel type, e.g. <c>web</c>.</summary>
    public string? Channel { get; init; }

    /// <summary>
    /// Only threads waiting longer than this many minutes.
    /// </summary>
    /// <remarks>
    /// Measured from the conversation's creation — when the customer's first
    /// message landed — because that is the clock they are experiencing.
    /// </remarks>
    public int? OlderThanMinutes { get; init; }

    /// <summary>Page size. The API clamps it.</summary>
    public int? Limit { get; init; }

    /// <summary>The cursor from the previous page's <c>nextCursor</c>.</summary>
    public string? Cursor { get; init; }

    /// <summary>Extra parameters appended after the ones above.</summary>
    public ChatlyQuery? Extra { get; init; }

    /// <summary>Render these options as query parameters.</summary>
    internal ChatlyQuery ToQuery()
        => new ChatlyQuery()
            .Add("assignee", Assignee)
            .Add("team", Team)
            .Add("channel", Channel)
            .Add("olderThanMinutes", OlderThanMinutes)
            .Add("limit", Limit)
            .Add("cursor", Cursor)
            .AddRange(Extra);
}

/// <summary>
/// Options for <c>GET /v1/macros</c>.
/// </summary>
/// <remarks>
/// Not a <see cref="ListOptions"/>, because that route is not a list in the
/// paginated sense: it is pagination-exempt, capped at 200 rows and ordered by
/// usage rank, so it has no cursor, limit, sort or sparse-fieldset support.
/// These four parameters are what it actually reads.
/// </remarks>
public sealed record MacroListOptions
{
    /// <summary>
    /// Visibility scope to filter by — a macro is personal, team-wide or
    /// workspace-wide. Omit for everything the key can see.
    /// </summary>
    public string? Scope { get; init; }

    /// <summary>Restrict to one team's macros. A team id.</summary>
    public string? Team { get; init; }

    /// <summary>Free-text filter over the macro's name and body.</summary>
    public string? Query { get; init; }

    /// <summary>
    /// Rank macros written in this language first, e.g. <c>fr</c>.
    /// </summary>
    /// <remarks>
    /// Ranking, not filtering: an agent answering in French wants the French
    /// replies at the top but must still be able to reach an English one. An
    /// unrecognised value simply ranks nothing.
    /// </remarks>
    public string? Language { get; init; }

    /// <summary>Extra parameters appended after the ones above.</summary>
    public ChatlyQuery? Extra { get; init; }

    /// <summary>Render these options as query parameters.</summary>
    /// <returns>A fresh query carrying every set option.</returns>
    internal ChatlyQuery ToQuery()
        => new ChatlyQuery()
            .Add("scope", Scope)
            .Add("team", Team)
            .Add("query", Query)
            .Add("language", Language)
            .AddRange(Extra);
}
