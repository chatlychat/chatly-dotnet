using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// Customer cards — panels in the agent sidebar whose contents your own
/// backend serves, live, when an agent opens a conversation. Reached as
/// <c>chatly.CustomerCards</c>.
/// </summary>
/// <remarks>
/// <para>
/// A card is a URL plus a signing secret. Chatly POSTs a signed request to
/// that URL and renders the blocks you answer with, so the data never leaves
/// your systems to be stored here. <c>chatly.Contacts.CardsAsync</c> is the
/// same fetch, on demand.
/// </para>
/// <para>
/// No response on this resource ever carries a header <i>value</i> — only
/// header names — and the signing secret appears exactly twice: in the
/// create response and in a rotate response. If you lose it, rotate.
/// </para>
/// </remarks>
public sealed class CustomerCardsResource
{
    private const string Collection = "/v1/customer-cards";

    private readonly Transport _transport;

    internal CustomerCardsResource(Transport transport) => _transport = transport;

    /// <summary>Configured cards. <c>GET /v1/customer-cards</c>.</summary>
    /// <remarks>
    /// <b>A bare JSON array</b>, pagination-exempt. Header names only: no
    /// values and no secrets, so this is safe to log.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A JSON array of card configurations.</returns>
    public Task<JsonElement> ListAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(Collection, null, cancellationToken);

    /// <summary>Configure a card. <c>POST /v1/customer-cards</c>.</summary>
    /// <remarks>
    /// The only response that carries <c>secret</c> beside the card. Store it
    /// before you do anything else — there is no route that reads it back.
    /// </remarks>
    /// <param name="card">The card configuration, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The card, and <c>secret</c> beside it, at 201.</returns>
    public Task<JsonElement> CreateAsync(object card, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(HttpMethod.Post, Collection, card, null, cancellationToken);

    /// <summary>Change part of a card. <c>PATCH /v1/customer-cards/{id}</c>.</summary>
    /// <remarks>
    /// An absent field is unchanged — but <c>headers: {}</c> is not absent,
    /// and <b>clears every header</b>. The body is strict, so a misspelled
    /// field is a 400 rather than a silent no-op. The response carries no
    /// secret.
    /// </remarks>
    /// <param name="cardId">The card to change.</param>
    /// <param name="changes">The fields to change, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The card as stored.</returns>
    public Task<JsonElement> UpdateAsync(
        string cardId,
        object changes,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            new HttpMethod("PATCH"),
            $"{Collection}/{Route.Segment(cardId, nameof(cardId))}",
            changes,
            null,
            cancellationToken);

    /// <summary>
    /// Mint a new signing secret for one card.
    /// <c>POST /v1/customer-cards/{id}/rotate-secret</c>.
    /// </summary>
    /// <remarks>
    /// <b>No overlap window: the previous secret stops verifying
    /// immediately.</b> Update your endpoint first, or accept both briefly.
    /// For a rotation you can stage, give the URL an endpoint secret instead
    /// — see <c>chatly.CustomerCardEndpoints</c>, where minting and
    /// activating are two calls.
    /// </remarks>
    /// <param name="cardId">The card.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ secret }</c> — shown once.</returns>
    public Task<JsonElement> RotateSecretAsync(string cardId, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/{Route.Segment(cardId, nameof(cardId))}/rotate-secret",
            null,
            null,
            cancellationToken);

    /// <summary>Remove a card. <c>DELETE /v1/customer-cards/{id}</c>.</summary>
    /// <remarks>
    /// Answers <b>200 with <c>{ ok: true }</c></b>, not a 204. The agent
    /// sidebar stops calling your URL; nothing stored here is deleted,
    /// because nothing was stored.
    /// </remarks>
    /// <param name="cardId">The card to remove.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ ok: true }</c>.</returns>
    public Task<JsonElement> DeleteAsync(string cardId, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Delete,
            $"{Collection}/{Route.Segment(cardId, nameof(cardId))}",
            null,
            null,
            cancellationToken);
}

/// <summary>
/// Per-URL signing secrets for customer cards, with a staged rotation.
/// Reached as <c>chatly.CustomerCardEndpoints</c>.
/// </summary>
/// <remarks>
/// <para>
/// Several cards often point at one URL. By default each is signed with its
/// own card secret, which means a shared endpoint has to know several. An
/// <i>endpoint</i> secret replaces that: one secret for the URL, signing
/// every card that points at it.
/// </para>
/// <para>
/// The reason to prefer it is rotation. A card secret swaps instantly and
/// breaks any endpoint that has not been redeployed; an endpoint secret is
/// minted <i>pending</i> and starts signing only when you activate it, so you
/// can deploy in between. Mint, deploy, activate.
/// </para>
/// </remarks>
public sealed class CustomerCardEndpointsResource
{
    private const string Collection = "/v1/customer-card-endpoints";

    private readonly Transport _transport;

    internal CustomerCardEndpointsResource(Transport transport) => _transport = transport;

    /// <summary>
    /// Endpoint secrets, never the secrets themselves.
    /// <c>GET /v1/customer-card-endpoints</c>.
    /// </summary>
    /// <remarks>
    /// <b>A bare JSON array</b>, pagination-exempt. <c>cardKeys</c> lists the
    /// cards pointing at each URL — an <i>empty</i> one means the secret
    /// signs nothing, which is the usual explanation for a secret that looks
    /// configured and never arrives.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A JSON array of endpoint records.</returns>
    public Task<JsonElement> ListAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(Collection, null, cancellationToken);

    /// <summary>
    /// Give one card URL its own signing secret.
    /// <c>POST /v1/customer-card-endpoints</c>.
    /// </summary>
    /// <remarks>
    /// Takes effect at once, unlike a rotation: from this call on, every
    /// request to that URL is signed with the returned secret rather than the
    /// per-card ones. So create the endpoint only when your handler is
    /// already able to verify it.
    /// </remarks>
    /// <param name="endpoint">The endpoint to create, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The endpoint and <c>secret</c> beside it, at 201.</returns>
    public Task<JsonElement> CreateAsync(object endpoint, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(HttpMethod.Post, Collection, endpoint, null, cancellationToken);

    /// <summary>
    /// Mint the <i>next</i> secret, which signs nothing yet.
    /// <c>POST /v1/customer-card-endpoints/{id}/rotate-secret</c>.
    /// </summary>
    /// <remarks>
    /// Returns the new secret once, with <c>active: false</c>. The current
    /// secret keeps signing until
    /// <see cref="ActivateSecretAsync(string, CancellationToken)"/>, which is
    /// the whole point: deploy the pending secret, then promote it.
    /// </remarks>
    /// <param name="endpointId">The endpoint.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The pending secret, shown once, with <c>active: false</c>.</returns>
    public Task<JsonElement> RotateSecretAsync(string endpointId, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/{Route.Segment(endpointId, nameof(endpointId))}/rotate-secret",
            null,
            null,
            cancellationToken);

    /// <summary>
    /// Promote the pending secret — it starts signing now.
    /// <c>POST /v1/customer-card-endpoints/{id}/activate-secret</c>.
    /// </summary>
    /// <remarks>
    /// After this the pending secret is the only one that signs and the
    /// previous one is gone. <b>A 409 when there is no pending secret</b>, so
    /// this cannot be used as an idempotent "make sure it is active" — call
    /// it once per rotation.
    /// </remarks>
    /// <param name="endpointId">The endpoint.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ activated: true }</c>.</returns>
    /// <exception cref="ChatlyConflictException">There is no pending secret to promote.</exception>
    public Task<JsonElement> ActivateSecretAsync(string endpointId, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/{Route.Segment(endpointId, nameof(endpointId))}/activate-secret",
            null,
            null,
            cancellationToken);

    /// <summary>
    /// Remove the endpoint secret. <c>DELETE /v1/customer-card-endpoints/{id}</c>.
    /// </summary>
    /// <remarks>
    /// Signing <b>reverts to per-card secrets</b> rather than stopping:
    /// requests to that URL go back to being signed by the leading card of
    /// the group. So this is a configuration change your handler will notice,
    /// not a cleanup. Answers 200 with <c>{ ok: true }</c>.
    /// </remarks>
    /// <param name="endpointId">The endpoint to remove.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ ok: true }</c>.</returns>
    public Task<JsonElement> DeleteAsync(string endpointId, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Delete,
            $"{Collection}/{Route.Segment(endpointId, nameof(endpointId))}",
            null,
            null,
            cancellationToken);
}
