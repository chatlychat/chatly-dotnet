using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// Documentation sources — a GitBook, Forumulate, Discourse, ReadMe or plain
/// website pulled into the knowledge base on a schedule. Reached as
/// <c>chatly.DocsSources</c>.
/// </summary>
/// <remarks>
/// <para>
/// This whole surface is reachable from an API key on purpose, so a partner
/// can provision a workspace, connect its documentation and have a useful
/// widget on day one without a human opening a browser. Connecting
/// documentation used to be the one step that could not be scripted.
/// </para>
/// <para>
/// Nothing here syncs inline. A space with hundreds of pages is hundreds of
/// upstream requests, so the work runs in the worker and
/// <see cref="SyncAsync(string, bool, CancellationToken)"/> only enqueues it
/// — the same job the 15-minute scan runs.
/// </para>
/// </remarks>
public sealed class DocsSourcesResource
{
    private const string Collection = "/v1/docs-sources";

    private readonly Transport _transport;

    internal DocsSourcesResource(Transport transport) => _transport = transport;

    /// <summary>
    /// Providers that can be connected. <c>GET /v1/docs-sources/providers</c>.
    /// </summary>
    /// <remarks>
    /// <b>A bare JSON array</b> and a static catalogue — it says which
    /// providers need a base URL and which need a token, which is what you
    /// need before building a connect form. Two accept an <i>empty</i> token:
    /// a public Discourse category and the sitemap crawler read only what any
    /// visitor can.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A JSON array of provider descriptors.</returns>
    public Task<JsonElement> ProvidersAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync($"{Collection}/providers", null, cancellationToken);

    /// <summary>Connected sources. <c>GET /v1/docs-sources</c>.</summary>
    /// <remarks>
    /// <b>A bare JSON array</b>, pagination-exempt. No row carries the stored
    /// token — no DTO field exists for it.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A JSON array of sources.</returns>
    public Task<JsonElement> ListAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(Collection, null, cancellationToken);

    /// <summary>
    /// Which spaces a token can reach, for a connect picker.
    /// <c>POST /v1/docs-sources/discover</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A POST that answers a bare JSON array</b> — the verb is a POST
    /// because the credential goes in the body rather than a query string,
    /// not because anything is created. Nothing is stored; this is a probe.
    /// </para>
    /// <para>
    /// Pass <c>organizationId</c> when you already know it. An account-wide
    /// token without one returns every space in every organisation, which is
    /// the right answer for someone who has not chosen yet and the wrong one
    /// for everybody else — it was 60 spaces across unrelated organisations
    /// in the case that made this optional.
    /// </para>
    /// </remarks>
    /// <param name="credentials">
    /// The provider, token and — where the provider is self-hosted — base
    /// URL, serialized as the request body.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A JSON array of reachable spaces.</returns>
    public Task<JsonElement> DiscoverAsync(object credentials, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/discover",
            credentials,
            null,
            cancellationToken);

    /// <summary>Connect a source. <c>POST /v1/docs-sources</c>.</summary>
    /// <remarks>
    /// <para>
    /// Returns the source <b>before its first sync has run</b>, so an empty
    /// article count straight after this call is expected rather than a
    /// failure. Call <see cref="SyncAsync(string, bool, CancellationToken)"/>
    /// if you do not want to wait for the scheduler.
    /// </para>
    /// <para>
    /// Two fields have non-obvious floors. <c>syncIntervalMinutes</c> is
    /// refused below 15, because that is the scan cadence and a shorter
    /// interval could not be honoured — only look honoured. And
    /// <c>includePaths</c> / <c>excludePaths</c> are accepted only for
    /// Forumulate and website sources; the other three <i>refuse</i> them
    /// rather than storing a filter that can never match. Prefixes match at
    /// a segment boundary, and <c>exclude</c> beats <c>include</c>.
    /// </para>
    /// </remarks>
    /// <param name="source">The source to connect, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The connected source, at 201.</returns>
    public Task<JsonElement> CreateAsync(object source, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(HttpMethod.Post, Collection, source, null, cancellationToken);

    /// <summary>
    /// Rename, pause, re-schedule, or rotate the token of a source.
    /// <c>PATCH /v1/docs-sources/{id}</c>.
    /// </summary>
    /// <remarks>
    /// A replacement <c>token</c> is <b>verified against the provider before
    /// it is stored</b>, so a token that authenticates but cannot see this
    /// source's space is refused and the working credential is left alone.
    /// The token is never echoed back in the response.
    /// </remarks>
    /// <param name="sourceId">The source to change.</param>
    /// <param name="changes">The fields to change, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The source as stored.</returns>
    public Task<JsonElement> UpdateAsync(
        string sourceId,
        object changes,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            new HttpMethod("PATCH"),
            $"{Collection}/{Route.Segment(sourceId, nameof(sourceId))}",
            changes,
            null,
            cancellationToken);

    /// <summary>
    /// Sync now. <c>POST /v1/docs-sources/{id}/sync</c>.
    /// </summary>
    /// <remarks>
    /// Enqueues the job and returns — it does not wait for the sync, so poll
    /// <see cref="ListAsync(CancellationToken)"/> for the outcome.
    /// </remarks>
    /// <param name="sourceId">The source to sync.</param>
    /// <param name="full">
    /// True drops the incremental watermark first, so every page is re-read.
    /// Defaults to false: a scheduled sync must stay a delta or every space
    /// is re-read on every pass.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Acknowledgement that the job is queued, at 201.</returns>
    public Task<JsonElement> SyncAsync(
        string sourceId,
        bool full = false,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/{Route.Segment(sourceId, nameof(sourceId))}/sync",
            new { full },
            null,
            cancellationToken);

    /// <summary>
    /// Subscribe a source to its provider's change events.
    /// <c>POST /v1/docs-sources/{id}/webhook</c>.
    /// </summary>
    /// <remarks>
    /// Turns a 15-minute poll into a push, where the provider supports it.
    /// <b>Not every failure is an error:</b> a token that cannot manage
    /// webhooks answers 200 with <c>subscribed: false</c> and a reason, so
    /// read the field rather than the status code.
    /// </remarks>
    /// <param name="sourceId">The source to subscribe.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ subscribed, … }</c> — check <c>subscribed</c>.</returns>
    public Task<JsonElement> WebhookAsync(string sourceId, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/{Route.Segment(sourceId, nameof(sourceId))}/webhook",
            null,
            null,
            cancellationToken);

    /// <summary>
    /// Disconnect a source, keeping the articles it created.
    /// <c>DELETE /v1/docs-sources/{id}</c>.
    /// </summary>
    /// <remarks>
    /// Answers <b>200 with <c>{ kept }</c></b>, not a 204 — <c>kept</c> is
    /// how many articles stayed behind, and none are deleted. The customer
    /// does see the source disappear from the help centre, which is why this
    /// is open to an API key where channel deletion is not: it destroys no
    /// conversation history.
    /// </remarks>
    /// <param name="sourceId">The source to disconnect.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ kept }</c>.</returns>
    public Task<JsonElement> DeleteAsync(string sourceId, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Delete,
            $"{Collection}/{Route.Segment(sourceId, nameof(sourceId))}",
            null,
            null,
            cancellationToken);
}
