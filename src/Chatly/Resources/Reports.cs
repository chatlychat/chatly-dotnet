using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// Reporting aggregates. Reached as <c>chatly.Reports</c>.
/// </summary>
/// <remarks>
/// Each method has an overload taking a <see cref="ChatlyQuery"/>, because the
/// accepted date and grouping parameters differ per report and this SDK does
/// not model them: it would be guessing at someone else's schema. See the REST
/// reference for what each endpoint accepts, and pass it through.
/// </remarks>
public sealed class ReportsResource
{
    private readonly Transport _transport;

    internal ReportsResource(Transport transport) => _transport = transport;

    /// <summary>Headline numbers. <c>GET /v1/reports/overview</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The overview.</returns>
    public Task<JsonElement> OverviewAsync(CancellationToken cancellationToken = default)
        => OverviewAsync(null, cancellationToken);

    /// <summary>Headline numbers, with query parameters.</summary>
    /// <param name="query">Parameters such as a date range.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The overview.</returns>
    public Task<JsonElement> OverviewAsync(ChatlyQuery? query, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/reports/overview", query, cancellationToken);

    /// <summary>Per-agent performance. <c>GET /v1/reports/agents</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The per-agent report.</returns>
    public Task<JsonElement> AgentsAsync(CancellationToken cancellationToken = default)
        => AgentsAsync(null, cancellationToken);

    /// <summary>Per-agent performance, with query parameters.</summary>
    /// <param name="query">Parameters such as a date range.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The per-agent report.</returns>
    public Task<JsonElement> AgentsAsync(ChatlyQuery? query, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/reports/agents", query, cancellationToken);

    /// <summary>
    /// Conversation volume by day.
    /// <c>GET /v1/reports/conversations-by-day</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The daily series.</returns>
    public Task<JsonElement> ConversationsByDayAsync(CancellationToken cancellationToken = default)
        => ConversationsByDayAsync(null, cancellationToken);

    /// <summary>Conversation volume by day, with query parameters.</summary>
    /// <param name="query">Parameters such as a date range.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The daily series.</returns>
    public Task<JsonElement> ConversationsByDayAsync(
        ChatlyQuery? query,
        CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/reports/conversations-by-day", query, cancellationToken);

    /// <summary>First-response and resolution against SLA. <c>GET /v1/reports/sla</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The SLA report.</returns>
    public Task<JsonElement> SlaAsync(CancellationToken cancellationToken = default)
        => SlaAsync(null, cancellationToken);

    /// <summary>First-response and resolution against SLA, with query parameters.</summary>
    /// <param name="query">Parameters such as a date range.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The SLA report.</returns>
    public Task<JsonElement> SlaAsync(ChatlyQuery? query, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/reports/sla", query, cancellationToken);
}
