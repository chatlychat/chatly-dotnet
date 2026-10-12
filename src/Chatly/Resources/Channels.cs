using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// The workspace's channels — web, email, WhatsApp, SMS and the rest.
/// Reached as <c>chatly.Channels</c>.
/// </summary>
/// <remarks>
/// Read-only here. Channel <i>configuration</i> needs a scope no API key can
/// hold, so this SDK ships no write method for it rather than a method that
/// always returns 403.
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
}
