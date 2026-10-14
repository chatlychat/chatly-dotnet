using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// The workspace's channels — web, email, WhatsApp, SMS and the rest.
/// Reached as <c>chatly.Channels</c>.
/// </summary>
/// <remarks>
/// <para>
/// Create, update and secret rotation are here; <b>delete is not</b>, and
/// deliberately. Removing a channel destroys conversation history, so that
/// route requires a signed-in person — a key gets 403 from it no matter what
/// it is scoped to, and a method that always fails is worse than no method.
/// </para>
/// <para>
/// This class was read-only, and said so for the wrong reason: the comment
/// claimed channel configuration "needs a scope no API key can hold", which
/// was never true of the write routes once they were opened — only of the
/// delete.
/// </para>
/// </remarks>
public sealed class ChannelsResource
{
    private const string Collection = "/v1/channels";

    private readonly Transport _transport;

    internal ChannelsResource(Transport transport) => _transport = transport;

    /// <summary>Every channel in the workspace. <c>GET /v1/channels</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The channels.</returns>
    public Task<JsonElement> ListAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(Collection, null, cancellationToken);

    /// <summary>One channel by id. <c>GET /v1/channels/{id}</c>.</summary>
    /// <param name="channelId">The channel.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The channel.</returns>
    public Task<JsonElement> GetAsync(string channelId, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Collection}/{Route.Segment(channelId, nameof(channelId))}",
            null,
            cancellationToken);

    /// <summary>
    /// The channel adapter names this deployment supports.
    /// <c>GET /v1/channels/adapters</c>.
    /// </summary>
    /// <remarks>
    /// <b>A bare JSON array of STRINGS</b>, not of objects — the only route
    /// on this SDK's surface that is, which is worth knowing if you are
    /// binding responses to a type. It is a static capability list, so read
    /// it before offering a channel type in your own UI: a type that is not
    /// in here cannot be created.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A JSON array of adapter names.</returns>
    public Task<JsonElement> AdaptersAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync($"{Collection}/adapters", null, cancellationToken);

    /// <summary>Create a channel. <c>POST /v1/channels</c>.</summary>
    /// <remarks>
    /// <para>
    /// Needs <c>channels:write</c>. Note what is <i>not</i> here: there is no
    /// delete. Removing a channel destroys conversation history, so that
    /// route requires a signed-in person and no API key can reach it — a
    /// method for it would always fail.
    /// </para>
    /// <para>
    /// The response carries the channel plus its registrations, which is how
    /// you find out whether an inbound address or webhook was actually
    /// provisioned upstream.
    /// </para>
    /// </remarks>
    /// <param name="channel">The channel to create, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The created channel and its registrations, at 201.</returns>
    public Task<JsonElement> CreateAsync(object channel, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(HttpMethod.Post, Collection, channel, null, cancellationToken);

    /// <summary>Change part of a channel. <c>PATCH /v1/channels/{id}</c>.</summary>
    /// <remarks>
    /// <para>
    /// A PATCH, so omitted fields are unchanged. One trap worth knowing: an
    /// <b>empty</b> allowed-origins list means <i>any</i> origin, not none,
    /// and an empty array is invisible in a JSON diff — read
    /// <c>originsRestricted</c> on the response rather than counting origins.
    /// </para>
    /// <para>
    /// Same shape back as <see cref="CreateAsync(object, CancellationToken)"/>.
    /// </para>
    /// </remarks>
    /// <param name="channelId">The channel to change.</param>
    /// <param name="changes">The fields to change, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The updated channel and its registrations.</returns>
    public Task<JsonElement> UpdateAsync(
        string channelId,
        object changes,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            new HttpMethod("PATCH"),
            $"{Collection}/{Route.Segment(channelId, nameof(channelId))}",
            changes,
            null,
            cancellationToken);

    /// <summary>
    /// Mint a new inbound verification secret for a channel.
    /// <c>POST /v1/channels/{id}/secret/rotate</c>.
    /// </summary>
    /// <remarks>
    /// Note the path: <c>/secret/rotate</c>, not <c>/rotate-secret</c> as on
    /// the customer-card routes. The new secret is in the response and
    /// nowhere else, and the old one stops verifying at once — so an upstream
    /// provider still signing with the old secret will start failing
    /// verification immediately. Declared <c>@NoIdempotency()</c> on the
    /// server: a replayed key will not replay the mint.
    /// </remarks>
    /// <param name="channelId">The channel.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ secret }</c> — shown once, at 201.</returns>
    public Task<JsonElement> RotateSecretAsync(string channelId, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/{Route.Segment(channelId, nameof(channelId))}/secret/rotate",
            null,
            null,
            cancellationToken);
}
