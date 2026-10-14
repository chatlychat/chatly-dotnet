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

    /// <summary>
    /// The workspace's plan, seat count and trial state.
    /// <c>GET /v1/billing</c>.
    /// </summary>
    /// <remarks>
    /// The collection root, with no sub-path — <c>/v1/billing</c>, not
    /// <c>/v1/billing/summary</c>. Gated on <c>workspace:billing:read</c>,
    /// which is a narrower scope than <c>workspace:read</c>: a key that can
    /// list labels cannot necessarily see what the customer pays.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The billing summary.</returns>
    public Task<JsonElement> SummaryAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/billing", null, cancellationToken);

    /// <summary>
    /// Invoices, newest first. <c>GET /v1/billing/invoices</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Cursor-paginated, but its envelope key is <c>data</c>, not
    /// <c>items</c>.</b> That is why this returns a plain
    /// <see cref="JsonElement"/> and not a <see cref="Page{T}"/>, and why
    /// there is no auto-paginator: the page type here reads <c>items</c>, so
    /// pointing it at this route would report an empty page forever — a
    /// wrong answer with a 200 rather than a visible failure. Read
    /// <c>data</c> and <c>nextCursor</c> yourself and pass the cursor back
    /// through <paramref name="cursor"/>.
    /// </para>
    /// <para>
    /// The envelope predates the shared pagination helper. The difference is
    /// load-bearing for anyone already reading it, so it is documented rather
    /// than quietly normalised here.
    /// </para>
    /// </remarks>
    /// <param name="cursor">The cursor from a previous response's <c>nextCursor</c>.</param>
    /// <param name="limit">Page size. Null leaves the server's default.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ data, nextCursor }</c> — mirrored from Stripe.</returns>
    public Task<JsonElement> InvoicesAsync(
        string? cursor = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            "/v1/billing/invoices",
            new ChatlyQuery().Add("cursor", cursor).Add("limit", limit),
            cancellationToken);

    /// <summary>
    /// Every metered event type, totalled for one month.
    /// <c>GET /v1/billing/usage/monthly</c>.
    /// </summary>
    /// <remarks>
    /// The one usage route that needs <i>no</i> <c>eventType</c>: it returns
    /// an entry for each known type, always all four, plus a grand
    /// <c>total</c>. Use it instead of four calls to
    /// <see cref="UsageAsync(string, System.DateTimeOffset?, System.DateTimeOffset?, CancellationToken)"/>.
    /// </remarks>
    /// <param name="month">
    /// The month as <c>YYYY-MM</c>, UTC. Null means the current month.
    /// Anything else is a 400 rather than a coercion — the server refuses to
    /// parse a free-form date, because <c>"June 2026"</c> would resolve in
    /// the host's local timezone and shift the whole window.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One entry per usage event type, plus the total.</returns>
    public Task<JsonElement> UsageMonthlyAsync(
        string? month = null,
        CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{UsagePath}/monthly",
            new ChatlyQuery().Add("month", month),
            cancellationToken);

    /// <summary>
    /// A day-by-day usage series for one metered event type.
    /// <c>GET /v1/billing/usage/daily</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="eventType"/> is <b>required</b>, as it is on
    /// <see cref="UsageAsync(string, System.DateTimeOffset?, System.DateTimeOffset?, CancellationToken)"/>,
    /// and checked here so the failure is an argument exception at the call
    /// site rather than a 400 from the API. The accepted values are on
    /// <see cref="ChatlyUsageEventTypes"/>.
    /// </para>
    /// <para>
    /// The series is <b>dense</b>: one entry per UTC day in
    /// <c>[from, to)</c>, zeros included, so you can chart it without
    /// filling gaps. The window defaults to the current calendar month and is
    /// capped at 365 days by the service.
    /// </para>
    /// </remarks>
    /// <param name="eventType">Which metric to series — see <see cref="ChatlyUsageEventTypes"/>.</param>
    /// <param name="from">Start of the window, inclusive. Defaults to the first of this month.</param>
    /// <param name="to">End of the window, exclusive. Must be strictly after <paramref name="from"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The dense daily series.</returns>
    /// <exception cref="ArgumentException"><paramref name="eventType"/> is empty.</exception>
    public Task<JsonElement> UsageDailyAsync(
        string eventType,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException(
                "Billing.UsageDailyAsync: eventType is required. See ChatlyUsageEventTypes for the accepted values.",
                nameof(eventType));
        }

        var query = new ChatlyQuery()
            .Add("eventType", eventType)
            .Add("from", from?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))
            .Add("to", to?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));

        return _transport.GetJsonAsync($"{UsagePath}/daily", query, cancellationToken);
    }
}
