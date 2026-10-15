using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// Opening a thread with someone who has not written to you.
/// Reached as <c>chatly.Visitors</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>One method, and the other two routes on this controller are absent on
/// purpose.</b> <c>GET /v1/visitors/live</c> and
/// <c>GET /v1/visitors/{contactId}/pages</c> carry no <c>@AllowApiKey()</c>,
/// so an API key gets 403 from both however it is scoped. A method that
/// always fails is worse than a missing one: the failure arrives at runtime,
/// in production, in someone else's integration.
/// </para>
/// </remarks>
public sealed class VisitorsResource
{
    private const string Collection = "/v1/visitors";

    private readonly Transport _transport;

    internal VisitorsResource(Transport transport) => _transport = transport;

    /// <summary>
    /// Open a conversation with a contact who did <b>not</b> write first.
    /// <c>POST /v1/visitors/{contactId}/start-conversation</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Needs <c>conversations:write</c>. The contact id is the whole input:
    /// there is no request body and no query parameter, because the handler
    /// declares neither.
    /// </para>
    /// <para>
    /// <b>A 403 here is probably not your scopes.</b> This is off by default
    /// per workspace. The flag is
    /// <c>settings.proactiveOutreach.apiKeysMayStartConversations</c>; an
    /// owner or admin turns it on with <c>PATCH /v1/workspace/settings</c>,
    /// which needs a signed-in person and so is not reachable from this SDK,
    /// and you can read its current value with
    /// <see cref="WorkspaceResource.SettingsAsync(CancellationToken)"/>,
    /// which is. The scope is not the opt-in:
    /// <c>conversations:write</c> was already grantable and is already held
    /// by keys minted for ticket bulk actions and schedule-send, so opening
    /// this route on the permission alone would have handed cold outreach to
    /// credentials nobody re-reviewed. So a
    /// <see cref="Chatly.ChatlyPermissionException"/> from this method means
    /// one of two quite different things, and the workspace setting is the
    /// likelier — check it before you go looking at your key.
    /// </para>
    /// <para>
    /// <b>Three per contact per hour</b>, counted per workspace and per
    /// <i>contact</i> rather than per caller, because the abuse shape is N
    /// threads at one person, which a per-caller limit cannot see. The fourth
    /// call in an hour is a <see cref="Chatly.ChatlyRateLimitException"/> —
    /// and this route can produce <b>two different</b> 429s, which do not
    /// carry the same information:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>The per-contact limit sends no <c>Retry-After</c> header at all</b>,
    /// so <see cref="Chatly.ChatlyRateLimitException.RetryAfter"/> is
    /// <c>null</c> for it. The server throws <c>RateLimitedError(ttl)</c> and
    /// the exception filter sends only the JSON envelope, setting no headers —
    /// the window's remaining seconds are in the body, at
    /// <c>error.details.retryAfterSeconds</c>, readable from
    /// <see cref="Chatly.ChatlyException.Body"/>. Do not wait on
    /// <c>RetryAfter</c> here; it will be null and you will retry at once.
    /// </description></item>
    /// <item><description>
    /// The route also inherits the global throttler buckets (30/second and
    /// 100/15 minutes) from the API's throttler config, and a 429 from
    /// <i>those</i> does carry a <c>Retry-After</c>, because only the
    /// throttler emits the per-bucket headers the response hook reads.
    /// </description></item>
    /// </list>
    /// <para>
    /// There is no per-route burst bucket on this handler yet. And note that
    /// the first bullet is specific to <i>this</i> client: the absent header
    /// is a property of the route, but whether an SDK surfaces the wait is a
    /// property of its own error construction.
    /// <c>Transport.ReadRetryAfter</c> reads the <c>Retry-After</c> header and
    /// nothing else — it has no body fallback — whereas the Ruby and Python
    /// clients do fall back to <c>details.retryAfterSeconds</c> and so report
    /// the wait here. So CONTRACT.md §10.4's promise, that a 429 arrives "with
    /// <c>retry_after</c> populated", is language-dependent on exactly this
    /// route, and in .NET you must read it from
    /// <see cref="Chatly.ChatlyException.Body"/>.
    /// </para>
    /// <para>
    /// <b>An offline visitor is not notified.</b> The greeting is persisted
    /// and the widget renders it from history on their next page view: no
    /// email, no push, no provider message, and nothing queued or dropped.
    /// An integration that needs to reach someone who has left the site wants
    /// an email or SMS conversation, not this.
    /// </para>
    /// <para>
    /// Unlike
    /// <see cref="ChannelsResource.RotateSecretAsync(string, CancellationToken)"/>,
    /// which declares <c>@NoIdempotency()</c>, this route sits under its
    /// controller's class-level <c>@RequireIdempotencyKey()</c> with no
    /// opt-out — so the header is <b>required</b>, not merely honoured, and
    /// the API answers 428 without it in production. This client always sends
    /// one. Pin it with
    /// <see cref="ChatlyClient.WithIdempotencyKey(string)"/> if you want a
    /// retry of the same logical outreach to replay rather than open a second
    /// thread.
    /// </para>
    /// </remarks>
    /// <param name="contactId">The contact to open a thread with.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ conversationId }</c> — the thread that was opened.</returns>
    public Task<JsonElement> StartConversationAsync(
        string contactId,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/{Route.Segment(contactId, nameof(contactId))}/start-conversation",
            null,
            null,
            cancellationToken);
}
