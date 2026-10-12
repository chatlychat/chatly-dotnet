using System.Collections.Generic;
using System.Text.Json;

namespace Chatly;

/// <summary>
/// A contact to create or update, for
/// <see cref="Resources.ContactsResource.UpsertAsync(ContactInput, System.Threading.CancellationToken)"/>.
/// </summary>
/// <remarks>
/// <para>
/// A convenience, not a requirement: every write method on this SDK also
/// accepts a plain <c>object</c>, so <c>new { email = "a@b.c" }</c> or a
/// <c>Dictionary&lt;string, object&gt;</c> works identically. The fields here
/// are the ones the API's own validation schema accepts; anything else it
/// rejects with a 422.
/// </para>
/// <para>
/// Null properties are omitted from the request body rather than sent as
/// JSON null, so an upsert only touches the fields you set.
/// </para>
/// </remarks>
public sealed record ContactInput
{
    /// <summary>
    /// Your system's id for this person. The upsert key: a second call with
    /// the same value updates the same contact rather than making another.
    /// </summary>
    public string? ExternalId { get; init; }

    /// <summary>Email address.</summary>
    public string? Email { get; init; }

    /// <summary>Phone number, in whatever format you store it (max 32 chars).</summary>
    public string? Phone { get; init; }

    /// <summary>Display name (max 160 chars).</summary>
    public string? Name { get; init; }

    /// <summary>Absolute URL of an avatar image.</summary>
    public string? AvatarUrl { get; init; }

    /// <summary>BCP-47 locale, e.g. <c>en-GB</c>. Used to pick the widget's language.</summary>
    public string? Locale { get; init; }

    /// <summary>IANA timezone, e.g. <c>Europe/London</c>. Used for business-hours logic.</summary>
    public string? Timezone { get; init; }

    /// <summary>
    /// Your own attributes — plan, MRR, signup date, anything you want agents
    /// and workflow conditions to see. Keys are sent exactly as written.
    /// </summary>
    public IDictionary<string, object?>? Attributes { get; init; }
}

/// <summary>
/// A message to post into a conversation, for
/// <see cref="Resources.ConversationsResource.SendAsync(string, MessageInput, System.Threading.CancellationToken)"/>.
/// </summary>
/// <remarks>
/// The conversation id is not here: it comes from the method's path argument,
/// and the API overwrites any id in the body with the one in the URL.
/// </remarks>
public sealed record MessageInput
{
    /// <summary>
    /// The plain-text body. Required, and still required when
    /// <see cref="BodyHtml"/> is set — it is what search, push previews, the
    /// email transcript and text-only channels show.
    /// </summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>
    /// One of <c>text</c>, <c>rich</c>, <c>note</c>, <c>system_event</c> or
    /// <c>attachment_only</c>. Defaults to <c>text</c> server-side.
    /// </summary>
    /// <remarks>
    /// <c>note</c> is the internal one: visible to agents, never delivered to
    /// the contact.
    /// </remarks>
    public string? Kind { get; init; }

    /// <summary>
    /// Rich-text HTML for an agent-authored reply. Sanitised server-side, and
    /// the plain-text rendition derived from it replaces whatever you put in
    /// <see cref="Body"/>, so the two halves cannot disagree.
    /// </summary>
    public string? BodyHtml { get; init; }

    /// <summary>
    /// Your own name for this turn, e.g. <c>"turn-42"</c>. A second send with
    /// the same value on the same conversation returns the message that
    /// already exists instead of posting a duplicate.
    /// </summary>
    /// <remarks>
    /// Narrower than <c>Idempotency-Key</c> and not a replacement for it: the
    /// header caches a response per request, while this is an identifier your
    /// process can recompute after a restart that lost whatever key it had
    /// generated.
    /// </remarks>
    public string? ClientRef { get; init; }

    /// <summary>Id of a message in this same conversation to quote.</summary>
    public string? ReplyToMessageId { get; init; }

    /// <summary>Arbitrary metadata stored with the message. Keys sent verbatim.</summary>
    public IDictionary<string, object?>? Metadata { get; init; }
}

/// <summary>
/// Helpers for the <see cref="JsonElement"/> values this SDK's methods return.
/// </summary>
/// <remarks>
/// <para>
/// Every resource method returns raw JSON rather than a hand-written response
/// class. That is a deliberate trade and worth knowing about: a response model
/// written from the outside is a model that can quietly disagree with the
/// server, and a test suite built on invented fixtures then agrees with the
/// model instead of the API. The API's own schemas are the source of truth, so
/// this SDK hands you what arrived and lets you bind it to your own type.
/// </para>
/// <para>
/// So: read a field with <see cref="JsonElement"/>'s own API, or declare the
/// shape you care about and call <see cref="As{T}(JsonElement)"/>.
/// </para>
/// </remarks>
public static class ChatlyJson
{
    /// <summary>
    /// Bind a returned JSON value to your own type, using the same
    /// camelCase-tolerant, case-insensitive settings the SDK sends with.
    /// </summary>
    /// <example>
    /// <code>
    /// public sealed record Contact(string Id, string? Email, string? Name);
    ///
    /// var page = await chatly.Contacts.ListAsync(ct);
    /// foreach (var element in page.Items)
    /// {
    ///     Contact? contact = element.As&lt;Contact&gt;();
    /// }
    /// </code>
    /// </example>
    /// <typeparam name="T">The type to bind to.</typeparam>
    /// <param name="element">The JSON returned by an SDK method.</param>
    /// <returns>The bound value, or null when the element is JSON null.</returns>
    /// <exception cref="JsonException">The JSON does not fit <typeparamref name="T"/>.</exception>
    public static T? As<T>(this JsonElement element)
        => element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined
            ? default
            : element.Deserialize<T>(Transport.SerializerOptions);

    /// <summary>
    /// Read a string property, or null when it is absent or not a string.
    /// A convenience for pulling an <c>id</c> out of a write's response.
    /// </summary>
    /// <param name="element">The JSON object to read from.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The string value, or null.</returns>
    public static string? GetStringOrNull(this JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
}

/// <summary>
/// The metered event types <c>GET /v1/billing/usage</c> accepts.
/// </summary>
/// <remarks>
/// The API validates against this closed list and answers a 400 for anything
/// else, so these constants are the difference between a typo caught at
/// compile time and one caught in production.
/// </remarks>
public static class ChatlyUsageEventTypes
{
    /// <summary>A conversation was opened.</summary>
    public const string ConversationOpened = "conversation.opened";

    /// <summary>A message was sent by a human agent.</summary>
    public const string MessageSentAgent = "message.sent.agent";

    /// <summary>A message was sent by a bot or AI agent.</summary>
    public const string MessageSentBot = "message.sent.bot";

    /// <summary>LLM tokens consumed.</summary>
    public const string AiTokens = "ai.tokens";

    /// <summary>All four, in the order the API lists them.</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        ConversationOpened,
        MessageSentAgent,
        MessageSentBot,
        AiTokens,
    };
}
