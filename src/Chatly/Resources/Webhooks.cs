using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// Webhook endpoints and their delivery history. Reached as
/// <c>chatly.Webhooks</c>.
/// </summary>
/// <remarks>
/// For <i>verifying</i> a delivery that arrived at your endpoint, see
/// <see cref="ChatlyWebhooks"/> — that is a local HMAC check and makes no API
/// call.
/// </remarks>
public sealed class WebhooksResource
{
    private const string Endpoints = "/v1/webhooks";

    private const string Collection = Endpoints + "/deliveries";

    private readonly Transport _transport;

    internal WebhooksResource(Transport transport) => _transport = transport;

    /// <summary>
    /// One page of delivery attempts. <c>GET /v1/webhooks/deliveries</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The first page.</returns>
    public Task<Page<JsonElement>> DeliveriesAsync(CancellationToken cancellationToken = default)
        => DeliveriesAsync(null, cancellationToken);

    /// <summary>One page of delivery attempts, filtered.</summary>
    /// <param name="options">Cursor, limit, filters, sort.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The requested page.</returns>
    public Task<Page<JsonElement>> DeliveriesAsync(
        ListOptions? options,
        CancellationToken cancellationToken = default)
        => _transport.GetPageAsync(Collection, ListOptions.QueryFor(options), cancellationToken);

    /// <summary>Every delivery attempt, page by page.</summary>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of deliveries.</returns>
    public IAsyncEnumerable<JsonElement> DeliveriesAllAsync(CancellationToken cancellationToken = default)
        => DeliveriesAllAsync(null, cancellationToken);

    /// <summary>Every delivery attempt matching the options, page by page.</summary>
    /// <param name="options">Limit, filters, sort, starting cursor.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream of deliveries.</returns>
    public IAsyncEnumerable<JsonElement> DeliveriesAllAsync(
        ListOptions? options,
        CancellationToken cancellationToken = default)
        => _transport.WalkPagesAsync(Collection, options, cancellationToken);

    /// <summary>
    /// One delivery attempt in full, including the response your endpoint
    /// gave. <c>GET /v1/webhooks/deliveries/{id}</c>.
    /// </summary>
    /// <param name="deliveryId">The delivery.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The delivery record.</returns>
    public Task<JsonElement> DeliveryAsync(string deliveryId, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Collection}/{Route.Segment(deliveryId, nameof(deliveryId))}",
            null,
            cancellationToken);

    /// <summary>
    /// Send a past delivery again.
    /// <c>POST /v1/webhooks/deliveries/{id}/redeliver</c>.
    /// </summary>
    /// <remarks>
    /// It goes to the endpoint's URL <i>as it is now</i>, not the URL the
    /// original attempt used, and it is signed fresh — so the replayed
    /// delivery's <c>t</c> is current and passes your freshness window.
    /// </remarks>
    /// <param name="deliveryId">The delivery to resend.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new attempt.</returns>
    public Task<JsonElement> RedeliverAsync(string deliveryId, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/{Route.Segment(deliveryId, nameof(deliveryId))}/redeliver",
            null,
            null,
            cancellationToken);

    /// <summary>
    /// The webhook endpoints this workspace has registered.
    /// <c>GET /v1/webhooks</c>.
    /// </summary>
    /// <remarks>
    /// <b>A bare JSON array</b>, pagination-exempt, and no row carries the
    /// signing secret — that appears only in the response that created the
    /// endpoint. Note this is <c>/v1/webhooks</c>, the endpoints; the
    /// delivery history on the same resource is <c>/v1/webhooks/deliveries</c>
    /// and is a page.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A JSON array of endpoints.</returns>
    public Task<JsonElement> ListAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(Endpoints, null, cancellationToken);

    /// <summary>
    /// Register a webhook endpoint. <c>POST /v1/webhooks</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The only response that carries <c>secret</c>.</b> Store it from
    /// this call — it is what
    /// <see cref="ChatlyWebhooks"/>
    /// needs, and nothing reads it back.
    /// </para>
    /// <para>
    /// <c>eventTypes</c> is required and must name at least one event from
    /// the subscribable list —
    /// <see cref="EventsAsync(CancellationToken)"/>. An optional
    /// <c>schemaVersion</c> pins the payload shape; an unrecognised one is
    /// <b>refused here</b>, unlike at dispatch time where an unknown version
    /// resolves to the latest. Loud on the write, generous on the read: a
    /// typo now costs one 400, where refusing at dispatch would be an
    /// integration that goes quiet with no error anywhere.
    /// </para>
    /// <para>
    /// Pair it with <see cref="DeleteAsync(string, CancellationToken)"/>. An
    /// integration that registers an endpoint should be able to deregister
    /// it, and for a while this one could not — see the remarks there.
    /// </para>
    /// </remarks>
    /// <param name="endpoint">The endpoint to register, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The endpoint and its <c>secret</c>, at 201.</returns>
    public Task<JsonElement> CreateAsync(object endpoint, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(HttpMethod.Post, Endpoints, endpoint, null, cancellationToken);

    /// <summary>
    /// The event types an endpoint may subscribe to.
    /// <c>GET /v1/webhooks/events</c>.
    /// </summary>
    /// <remarks>
    /// An object with an <c>eventTypes</c> array — not a bare array, despite
    /// being a static vocabulary. This is the <i>subscribable</i> set, which
    /// is narrower than the set of events the platform publishes internally:
    /// subscribing to a name that is not in here is a 400, and that is the
    /// intended way to find out.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ eventTypes: [...] }</c>.</returns>
    public Task<JsonElement> EventsAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync($"{Endpoints}/events", null, cancellationToken);

    /// <summary>
    /// The payload schema versions an endpoint can be pinned to, oldest
    /// first. <c>GET /v1/webhooks/schema-versions</c>.
    /// </summary>
    /// <remarks>
    /// Dated identifiers, so "the latest" moves. Pinning is how your handler
    /// stops being broken by a field change; read this before pinning, and
    /// treat the newest entry as a moving target rather than a constant.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The pinnable versions, oldest first.</returns>
    public Task<JsonElement> SchemaVersionsAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync($"{Endpoints}/schema-versions", null, cancellationToken);

    /// <summary>
    /// Pin one endpoint to a schema version.
    /// <c>PATCH /v1/webhooks/{id}/schema-version</c>.
    /// </summary>
    /// <remarks>
    /// Takes effect on the next delivery, so a redelivery of a past attempt
    /// is signed and shaped by the <i>new</i> pin rather than the old one.
    /// The version must be one of
    /// <see cref="SchemaVersionsAsync(CancellationToken)"/>; the body is
    /// strict, so an unknown version or a stray field is a 400.
    /// </remarks>
    /// <param name="endpointId">The endpoint to pin.</param>
    /// <param name="schemaVersion">The dated version identifier.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Acknowledgement of the new pin.</returns>
    public Task<JsonElement> SetSchemaVersionAsync(
        string endpointId,
        string schemaVersion,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            new HttpMethod("PATCH"),
            $"{Endpoints}/{Route.Segment(endpointId, nameof(endpointId))}/schema-version",
            new { schemaVersion },
            null,
            cancellationToken);

    /// <summary>
    /// Remove a webhook subscription. <c>DELETE /v1/webhooks/{id}</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Answers <b>200 with <c>{ ok: true }</c></b>, not a 204 — the handler's
    /// <c>@ZodResponse</c> is <c>z.object({ ok: z.literal(true) })</c> and it
    /// carries no <c>@HttpCode</c>, so Nest's DELETE default of 200 stands.
    /// </para>
    /// <para>
    /// This method did not exist until 2026-10-14, because the route refused
    /// an API key while <see cref="CreateAsync(object, CancellationToken)"/>
    /// accepted one. That asymmetry is worse than closing the surface
    /// entirely: a deploy pipeline registering an endpoint per environment
    /// leaked one into the customer's workspace on every run with nothing
    /// able to clean up. The workaround this SDK used to recommend — point
    /// the endpoint at a dead URL — is not a deletion: the subscription stays
    /// alive and the delivery log fills with failures.
    /// </para>
    /// <para>
    /// Needs <c>integrations:write</c>, the same scope as the create, on the
    /// argument that a key which can add a delivery target can already do the
    /// thing with consequences. The handler declares no idempotency posture,
    /// so it inherits the controller's <c>@RequireIdempotencyKey()</c> and
    /// answers <b>428 in production without the header</b> — this SDK mints
    /// one per write, so that only bites if you reach the route through
    /// something else.
    /// </para>
    /// </remarks>
    /// <param name="endpointId">The endpoint to remove.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ ok: true }</c>.</returns>
    /// <exception cref="ChatlyNotFoundException">No such endpoint in this workspace.</exception>
    public Task<JsonElement> DeleteAsync(string endpointId, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Delete,
            $"{Endpoints}/{Route.Segment(endpointId, nameof(endpointId))}",
            null,
            null,
            cancellationToken);
}
