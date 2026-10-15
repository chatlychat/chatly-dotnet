using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// AI agents and externally-hosted agents, and the delivery log for the
/// latter. Reached as <c>chatly.Bots</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two different things share this resource because they share the route
/// prefix. A <i>bot</i> is a Chatly-hosted persona: a prompt, a KB scope and
/// a tool set, answered by our AI service. An <i>external agent</i> is a bot
/// whose replies come from <b>your</b> HTTP endpoint — Chatly signs a payload
/// and POSTs it to you, and the <c>external</c> methods configure that leg.
/// </para>
/// <para>
/// The two credentials an external agent gets point in opposite directions
/// and are easy to confuse: the <c>signingSecret</c> is what <i>you</i>
/// verify our deliveries with, and the <c>token</c> is what you authenticate
/// back to us with. Both are shown exactly once, at creation.
/// </para>
/// </remarks>
public sealed class BotsResource
{
    private const string Collection = "/v1/bots";

    private readonly Transport _transport;

    internal BotsResource(Transport transport) => _transport = transport;

    /// <summary>One page of configured bots. <c>GET /v1/bots</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The first page, with a cursor for the next.</returns>
    public Task<Page<JsonElement>> ListAsync(CancellationToken cancellationToken = default)
        => ListAsync(null, cancellationToken);

    /// <summary>
    /// One page of configured bots. <c>GET /v1/bots</c>.
    /// </summary>
    /// <remarks>
    /// <c>?fields=</c> projects here, and the server documents the items as
    /// partial for exactly that reason: a row from a narrowed request is
    /// missing the keys you did not ask for, rather than carrying them null.
    /// Read fields defensively unless you asked for all of them.
    /// </remarks>
    /// <param name="options">Cursor, limit, filters, sort, fields.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The requested page.</returns>
    public Task<Page<JsonElement>> ListAsync(ListOptions? options, CancellationToken cancellationToken = default)
        => _transport.GetPageAsync(Collection, ListOptions.QueryFor(options), cancellationToken);

    /// <summary>Every bot, page by page, fetched as you consume it.</summary>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of bots.</returns>
    public IAsyncEnumerable<JsonElement> ListAllAsync(CancellationToken cancellationToken = default)
        => ListAllAsync(null, cancellationToken);

    /// <summary>Every bot matching the options, page by page.</summary>
    /// <param name="options">Limit, filters, sort, and a starting cursor.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of bots.</returns>
    public IAsyncEnumerable<JsonElement> ListAllAsync(
        ListOptions? options,
        CancellationToken cancellationToken = default)
        => _transport.WalkPagesAsync(Collection, options, cancellationToken);

    /// <summary>One bot by id. <c>GET /v1/bots/{id}</c>.</summary>
    /// <remarks>
    /// The full row rather than the projected one a list page may carry, and
    /// still without either external credential — those exist only in the
    /// response that minted them.
    /// </remarks>
    /// <param name="botId">The bot.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The bot.</returns>
    /// <exception cref="ChatlyNotFoundException">No such bot in this workspace.</exception>
    public Task<JsonElement> GetAsync(string botId, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Collection}/{Route.Segment(botId, nameof(botId))}",
            null,
            cancellationToken);

    /// <summary>
    /// Create a bot, or an external push-delivered agent.
    /// <c>POST /v1/bots</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The response shape depends on the body: with <c>type: "external"</c>
    /// it carries the agent <b>plus both credentials and a
    /// <c>credentialId</c></b>, shown once and never again; otherwise it is
    /// the plain bot row. Capture the secrets from this call or rotate them.
    /// </para>
    /// <para>
    /// Needs <c>ai:configure</c>, which is a stronger scope than the
    /// <c>triggers:read</c> that lists bots — a key that can see your
    /// assistants cannot necessarily add one.
    /// </para>
    /// </remarks>
    /// <param name="bot">The bot to create, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The created bot, at 201, with credentials for an external agent.</returns>
    public Task<JsonElement> CreateAsync(object bot, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(HttpMethod.Post, Collection, bot, null, cancellationToken);

    /// <summary>Change part of a bot. <c>PATCH /v1/bots/{id}</c>.</summary>
    /// <remarks>
    /// A PATCH, so omitted fields keep their current values. This is the
    /// Chatly-side configuration — prompt, persona, KB scope, tools. The
    /// webhook leg of an external agent is
    /// <see cref="UpdateExternalAsync(string, object, CancellationToken)"/>,
    /// a different route with a different body.
    /// </remarks>
    /// <param name="botId">The bot to change.</param>
    /// <param name="changes">The fields to change, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The updated bot.</returns>
    public Task<JsonElement> UpdateAsync(
        string botId,
        object changes,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            new HttpMethod("PATCH"),
            $"{Collection}/{Route.Segment(botId, nameof(botId))}",
            changes,
            null,
            cancellationToken);

    /// <summary>
    /// How one bot has performed over a window.
    /// <c>GET /v1/bots/{id}/analytics</c>.
    /// </summary>
    /// <remarks>
    /// Takes a <c>days</c> window and <b>rejects a non-numeric one with a
    /// 400</b> rather than falling back to a default — unlike the
    /// <c>reports.*</c> routes next door, which clamp. The window is still
    /// clamped to 1–365, because asking for 10,000 days means "everything"
    /// rather than a mistake. Gated on <c>reports:read</c>, not
    /// <c>ai:configure</c>.
    /// </remarks>
    /// <param name="botId">The bot.</param>
    /// <param name="days">The window in days. Null leaves the server's default.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The analytics object.</returns>
    public Task<JsonElement> AnalyticsAsync(
        string botId,
        int? days = null,
        CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Collection}/{Route.Segment(botId, nameof(botId))}/analytics",
            new ChatlyQuery().Add("days", days),
            cancellationToken);

    /// <summary>
    /// One page of delivery attempts to an external agent's endpoint.
    /// <c>GET /v1/bots/{id}/deliveries</c>.
    /// </summary>
    /// <remarks>
    /// Newest first. <b>A list row does not carry the signed payload</b> —
    /// a page of fifty bodies would be enormous — so read one with
    /// <see cref="DeliveryAsync(string, string, CancellationToken)"/> when
    /// you need the bytes we actually sent.
    /// </remarks>
    /// <param name="botId">The external agent.</param>
    /// <param name="options">Cursor and limit.</param>
    /// <param name="status">
    /// Narrow to one delivery status. Unknown query keys are stripped by the
    /// server rather than refused, so a misspelled filter reads as no filter.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The requested page of deliveries.</returns>
    public Task<Page<JsonElement>> DeliveriesAsync(
        string botId,
        ListOptions? options = null,
        string? status = null,
        CancellationToken cancellationToken = default)
        => _transport.GetPageAsync(
            DeliveriesPath(botId),
            ListOptions.QueryFor(options).Add("status", status),
            cancellationToken);

    /// <summary>
    /// Every delivery attempt to an external agent, page by page.
    /// </summary>
    /// <param name="botId">The external agent.</param>
    /// <param name="options">Limit, and a starting cursor.</param>
    /// <param name="status">Narrow to one delivery status.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of deliveries, newest first.</returns>
    public IAsyncEnumerable<JsonElement> DeliveriesAllAsync(
        string botId,
        ListOptions? options = null,
        string? status = null,
        CancellationToken cancellationToken = default)
        => _transport.WalkPagesAsync(
            DeliveriesPath(botId),
            cursor => ListOptions
                .QueryFor(cursor is null ? options : (options ?? new ListOptions()) with { Cursor = cursor })
                .Add("status", status),
            cancellationToken);

    /// <summary>
    /// One delivery, with the exact body that was signed and POSTed.
    /// <c>GET /v1/bots/{id}/deliveries/{deliveryId}</c>.
    /// </summary>
    /// <remarks>
    /// <c>payload</c> is deliberately untyped on the server: it is the bytes
    /// we stored and signed, so a row written before a schema change carries
    /// the shape of that day. Verify a signature against it byte-for-byte;
    /// re-serialising it reorders keys and the digest will not match.
    /// </remarks>
    /// <param name="botId">The external agent.</param>
    /// <param name="deliveryId">The delivery.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The delivery, including its payload and your endpoint's answer.</returns>
    public Task<JsonElement> DeliveryAsync(
        string botId,
        string deliveryId,
        CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{DeliveriesPath(botId)}/{Route.Segment(deliveryId, nameof(deliveryId))}",
            null,
            cancellationToken);

    /// <summary>
    /// Queue one recorded delivery again, from attempt zero.
    /// <c>POST /v1/bots/{id}/deliveries/{deliveryId}/redeliver</c>.
    /// </summary>
    /// <remarks>
    /// The payload is the one we stored, but it goes to the agent's
    /// <i>current</i> URL and the retry ladder starts over. Returns as soon
    /// as the job is queued — <c>{ queued: true, deliveryId, url }</c> — so a
    /// 200 here is not evidence your endpoint answered.
    /// </remarks>
    /// <param name="botId">The external agent.</param>
    /// <param name="deliveryId">The delivery to send again.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ queued: true, deliveryId, url }</c>.</returns>
    public Task<JsonElement> RedeliverAsync(
        string botId,
        string deliveryId,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{DeliveriesPath(botId)}/{Route.Segment(deliveryId, nameof(deliveryId))}/redeliver",
            null,
            null,
            cancellationToken);

    /// <summary>
    /// Send a signed test delivery and report what came back.
    /// <c>POST /v1/bots/{id}/test-delivery</c>.
    /// </summary>
    /// <remarks>
    /// Synchronous, unlike
    /// <see cref="RedeliverAsync(string, string, CancellationToken)"/>: it
    /// waits for your endpoint and returns the status, headers and up to
    /// 2,000 characters of the body. The first call to make after wiring an
    /// external agent, because it turns a signature mismatch into a visible
    /// answer instead of a silently failing queue.
    /// </remarks>
    /// <param name="botId">The external agent.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Your endpoint's response, truncated.</returns>
    public Task<JsonElement> TestDeliveryAsync(string botId, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/{Route.Segment(botId, nameof(botId))}/test-delivery",
            null,
            null,
            cancellationToken);

    /// <summary>
    /// Change an external agent's webhook URL, timeout or fallback copy.
    /// <c>PATCH /v1/bots/{id}/external</c>.
    /// </summary>
    /// <remarks>
    /// The fallback copy is what a visitor sees when your endpoint does not
    /// answer in time, so it is part of the delivery configuration rather
    /// than the persona. Returns the bot row; it never carries the signing
    /// secret, so changing the URL does not re-reveal it.
    /// </remarks>
    /// <param name="botId">The external agent.</param>
    /// <param name="changes">The fields to change, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The updated bot.</returns>
    public Task<JsonElement> UpdateExternalAsync(
        string botId,
        object changes,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            new HttpMethod("PATCH"),
            $"{Collection}/{Route.Segment(botId, nameof(botId))}/external",
            changes,
            null,
            cancellationToken);

    /// <summary>
    /// Rotate an external agent's signing secret, effective immediately.
    /// <c>POST /v1/bots/{id}/external/rotate-secret</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This overload is the hard cut.</b> The old secret stops signing
    /// deliveries the moment this returns, so deploy the new one to your
    /// endpoint first, or accept both for a minute. To keep the old secret
    /// signing for a while, use the overload that takes an
    /// <c>activateAt</c>. The new secret is in the response and nowhere
    /// else.
    /// </para>
    /// <para>
    /// The request body is <c>{}</c>, as for every bodyless write on this
    /// transport — exactly what this method sent before the overlap window
    /// existed.
    /// </para>
    /// </remarks>
    /// <param name="botId">The external agent.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// <c>{ signingSecret, activatesAt }</c>. <c>signingSecret</c> is shown
    /// once. <c>activatesAt</c> is <c>null</c> here: the new secret is
    /// already signing.
    /// </returns>
    public Task<JsonElement> RotateExternalSecretAsync(
        string botId,
        CancellationToken cancellationToken = default)
        => RotateExternalSecretCoreAsync(botId, null, cancellationToken);

    /// <summary>
    /// Rotate an external agent's signing secret, optionally with an overlap
    /// window. <c>POST /v1/bots/{id}/external/rotate-secret</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With an <paramref name="activateAt"/>, the new secret is issued now but
    /// Chatly keeps signing every delivery with the <b>old</b> secret until
    /// that instant, then switches. There is one signature per delivery, never
    /// two, so during the window your verifier should accept <i>both</i>
    /// secrets (<see cref="Chatly.ChatlyWebhooks"/> verification against
    /// each in turn) and drop the old one once <c>activatesAt</c> has passed.
    /// </para>
    /// <para>
    /// The instant is sent as UTC, <c>yyyy-MM-ddTHH:mm:ss.fffZ</c>, whatever
    /// offset it carries. The server caps it at 168 hours ahead of <i>its</i>
    /// clock and answers 400 beyond that; the SDK deliberately does not
    /// re-check the bound against yours. An instant already in the past is a
    /// hard cut, not an error, and so is any deferred rotation on an agent
    /// that has no usable secret today — both answer
    /// <c>activatesAt: null</c>.
    /// </para>
    /// <para>
    /// <c>null</c> is the hard cut and sends exactly what the overload
    /// without <paramref name="activateAt"/> sends.
    /// </para>
    /// </remarks>
    /// <param name="botId">The external agent.</param>
    /// <param name="activateAt">When the new secret starts signing, or <c>null</c> for now.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// <c>{ signingSecret, activatesAt }</c>. <c>signingSecret</c> is shown
    /// once. <c>activatesAt</c> is <c>null</c> when the new secret is already
    /// signing, otherwise the ISO 8601 instant the switch happens.
    /// </returns>
    public Task<JsonElement> RotateExternalSecretAsync(
        string botId,
        DateTimeOffset? activateAt,
        CancellationToken cancellationToken = default)
        => RotateExternalSecretCoreAsync(
            botId,
            activateAt?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            cancellationToken);

    /// <summary>
    /// Rotate an external agent's signing secret with an overlap window given
    /// as an ISO 8601 string. <c>POST /v1/bots/{id}/external/rotate-secret</c>.
    /// </summary>
    /// <remarks>
    /// Same semantics as the <see cref="DateTimeOffset"/> overload. The string
    /// is sent verbatim — the server validates it, and requires a <c>Z</c> or
    /// an explicit offset. <c>null</c> is the hard cut.
    /// </remarks>
    /// <param name="botId">The external agent.</param>
    /// <param name="activateAt">An ISO 8601 instant, or <c>null</c> for now.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ signingSecret, activatesAt }</c>, as above.</returns>
    public Task<JsonElement> RotateExternalSecretAsync(
        string botId,
        string? activateAt,
        CancellationToken cancellationToken = default)
        => RotateExternalSecretCoreAsync(botId, activateAt, cancellationToken);

    private Task<JsonElement> RotateExternalSecretCoreAsync(
        string botId,
        string? activateAt,
        CancellationToken cancellationToken)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/{Route.Segment(botId, nameof(botId))}/external/rotate-secret",
            // A dictionary, not an anonymous type: the transport's camelCase
            // policy applies to property names and leaves dictionary keys as
            // given, so the wire key is exactly `activateAt` by construction.
            // No activateAt means no body object at all, so the transport
            // sends its usual `{}` — byte-identical to the hard cut it was.
            activateAt is null ? null : new Dictionary<string, string> { ["activateAt"] = activateAt },
            null,
            cancellationToken);

    private static string DeliveriesPath(string botId)
        => $"{Collection}/{Route.Segment(botId, nameof(botId))}/deliveries";
}
