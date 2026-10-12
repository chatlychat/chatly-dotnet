using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// Plans and usage. Reached as <c>chatly.Billing</c>.
/// </summary>
public sealed class BillingResource
{
    private readonly Transport _transport;

    internal BillingResource(Transport transport) => _transport = transport;

    /// <summary>The plans on offer. <c>GET /v1/billing/plans</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The plan catalogue.</returns>
    public Task<JsonElement> PlansAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/billing/plans", null, cancellationToken);

    /// <summary>
    /// This workspace's current usage against its plan.
    /// <c>GET /v1/billing/usage</c>.
    /// </summary>
    /// <remarks>
    /// Worth polling if your integration drives AI features: AI spend is
    /// metered per workspace and capped, and a capped workspace gets refusals
    /// rather than silently larger bills.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The usage figures.</returns>
    public Task<JsonElement> UsageAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/billing/usage", null, cancellationToken);
}
