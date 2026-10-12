using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// Webhook delivery history — what we sent to your endpoint, and a way to send
/// it again. Reached as <c>chatly.Webhooks</c>.
/// </summary>
/// <remarks>
/// For <i>verifying</i> a delivery that arrived at your endpoint, see
/// <see cref="ChatlyWebhooks"/> — that is a local HMAC check and makes no API
/// call.
/// </remarks>
public sealed class WebhooksResource
{
    private const string Collection = "/v1/webhooks/deliveries";

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
}
