using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// Plans and usage. Reached as <c>chatly.Billing</c>.
/// </summary>
public sealed class BillingResource
{
    private const string UsagePath = "/v1/billing/usage";

    private readonly Transport _transport;

    internal BillingResource(Transport transport) => _transport = transport;

    /// <summary>The plans on offer. <c>GET /v1/billing/plans</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The plan catalogue.</returns>
    public Task<JsonElement> PlansAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/billing/plans", null, cancellationToken);

    /// <summary>
    /// Month-to-date usage for one metered event type.
    /// <c>GET /v1/billing/usage</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="eventType"/> is <b>required</b>. The handler validates
    /// it against a closed list before doing anything else and answers a 400
    /// for anything absent or unrecognised, so there is no "usage overall"
    /// form of this call — ask for one metric at a time. The accepted values
    /// are on <see cref="ChatlyUsageEventTypes"/>.
    /// </para>
    /// <para>
    /// The window defaults to the current calendar month: <c>from</c> is the
    /// first of this month and <c>to</c> is the first of next, exclusive. The
    /// response echoes the resolved window back, along with <c>total</c> and
    /// <c>count</c>.
    /// </para>
    /// <para>
    /// Worth polling if your integration drives AI features: AI spend is
    /// metered per workspace and capped, and a capped workspace gets refusals
    /// rather than silently larger bills.
    /// </para>
    /// </remarks>
    /// <param name="eventType">
    /// Which metric to total — see <see cref="ChatlyUsageEventTypes"/>.
    /// </param>
    /// <param name="from">
    /// Start of the window, inclusive. Defaults to the first of this month.
    /// </param>
    /// <param name="to">
    /// End of the window, exclusive. Must be strictly after
    /// <paramref name="from"/>. Defaults to the first of next month.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The usage summary for that event type.</returns>
    /// <exception cref="ArgumentException"><paramref name="eventType"/> is empty.</exception>
    /// <exception cref="ChatlyValidationException">
    /// The API did not recognise the event type, or the window is backwards.
    /// </exception>
    public Task<JsonElement> UsageAsync(
        string eventType,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException(
                "Billing.UsageAsync: eventType is required. See ChatlyUsageEventTypes for the accepted values.",
                nameof(eventType));
        }

        var query = new ChatlyQuery()
            .Add("eventType", eventType)
            .Add("from", from?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))
            .Add("to", to?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));

        return _transport.GetJsonAsync(UsagePath, query, cancellationToken);
    }

    /// <summary>
    /// Month-to-date usage with query parameters you build yourself.
    /// <c>GET /v1/billing/usage</c>.
    /// </summary>
    /// <remarks>
    /// Still requires <c>eventType</c> — the parameter is checked here so the
    /// failure is an argument exception at the call site rather than a 400
    /// from the API.
    /// </remarks>
    /// <param name="query">The query, which must include <c>eventType</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The usage summary.</returns>
    /// <exception cref="ArgumentException"><c>eventType</c> is missing from the query.</exception>
    public Task<JsonElement> UsageAsync(ChatlyQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        var hasEventType = false;
        foreach (var parameter in query)
        {
            if (parameter.Key == "eventType" && parameter.Value.Length > 0)
            {
                hasEventType = true;
                break;
            }
        }

        if (!hasEventType)
        {
            throw new ArgumentException(
                "Billing.UsageAsync: the query must carry a non-empty eventType; the API rejects the call without one.",
                nameof(query));
        }

        return _transport.GetJsonAsync(UsagePath, query, cancellationToken);
    }
}
