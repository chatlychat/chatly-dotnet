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

    /// <summary>
    /// Volume and resolution grouped by captured campaign data.
    /// <c>GET /v1/reports/attribution</c>.
    /// </summary>
    /// <remarks>
    /// <b>A bare JSON array</b>, not a page envelope. The row with
    /// <c>value: null</c> is the untagged remainder rather than a bad row —
    /// drop it and the totals stop adding up. Accepts <c>dimension</c> (which
    /// captured field to group by) and <c>days</c>.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The attribution rows.</returns>
    public Task<JsonElement> AttributionAsync(CancellationToken cancellationToken = default)
        => AttributionAsync(null, cancellationToken);

    /// <summary>Attribution rows, with query parameters.</summary>
    /// <param name="query">Parameters — <c>dimension</c> and <c>days</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The attribution rows.</returns>
    public Task<JsonElement> AttributionAsync(ChatlyQuery? query, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/reports/attribution", query, cancellationToken);

    /// <summary>
    /// Conversation volume grouped by channel of origin.
    /// <c>GET /v1/reports/sources</c>.
    /// </summary>
    /// <remarks>
    /// <b>A bare JSON array</b>, not a page. <c>channelId</c> is null for a
    /// conversation that carries no channel — again a real bucket, not a
    /// defect. Accepts <c>days</c>.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The per-source rows.</returns>
    public Task<JsonElement> SourcesAsync(CancellationToken cancellationToken = default)
        => SourcesAsync(null, cancellationToken);

    /// <summary>Per-source rows, with query parameters.</summary>
    /// <param name="query">Parameters such as <c>days</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The per-source rows.</returns>
    public Task<JsonElement> SourcesAsync(ChatlyQuery? query, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/reports/sources", query, cancellationToken);

    /// <summary>
    /// Weekly contact-retention cohort grid.
    /// <c>GET /v1/reports/cohorts</c>.
    /// </summary>
    /// <remarks>
    /// One object, <b>not</b> a bare array of cells. <c>weeks</c> in the
    /// response is the <i>clamped</i> window the report actually used, which
    /// may be narrower than the <c>weeks</c> you asked for — read it back
    /// rather than assuming your parameter was honoured.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The cohort grid.</returns>
    public Task<JsonElement> CohortsAsync(CancellationToken cancellationToken = default)
        => CohortsAsync(null, cancellationToken);

    /// <summary>The cohort grid, with query parameters.</summary>
    /// <param name="query">Parameters such as <c>weeks</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The cohort grid.</returns>
    public Task<JsonElement> CohortsAsync(ChatlyQuery? query, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/reports/cohorts", query, cancellationToken);

    /// <summary>
    /// Volume, resolution time and CSAT by label.
    /// <c>GET /v1/reports/labels</c>.
    /// </summary>
    /// <remarks>
    /// One object. <c>rows</c> is <b>capped at the 50 busiest labels</b> with
    /// the remainder counted in a separate field, so a workspace with a long
    /// tail of labels will not see them all — the cap is not a page boundary
    /// and there is no cursor past it. Accepts a date range plus
    /// <c>business_hours</c> and <c>group_by</c>.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The per-label report.</returns>
    public Task<JsonElement> LabelsAsync(CancellationToken cancellationToken = default)
        => LabelsAsync(null, cancellationToken);

    /// <summary>The per-label report, with query parameters.</summary>
    /// <param name="query">A date range, <c>business_hours</c>, <c>group_by</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The per-label report.</returns>
    public Task<JsonElement> LabelsAsync(ChatlyQuery? query, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/reports/labels", query, cancellationToken);

    /// <summary>
    /// Per-team volume, percentiles, CSAT and SLA breaches.
    /// <c>GET /v1/reports/teams</c>.
    /// </summary>
    /// <remarks>
    /// One object. Takes the same range parameters as
    /// <see cref="LabelsAsync(ChatlyQuery, CancellationToken)"/> — including
    /// <c>business_hours</c>, which changes what "first response in 20
    /// minutes" means and so changes the breach counts rather than merely
    /// relabelling them.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The per-team report.</returns>
    public Task<JsonElement> TeamsAsync(CancellationToken cancellationToken = default)
        => TeamsAsync(null, cancellationToken);

    /// <summary>The per-team report, with query parameters.</summary>
    /// <param name="query">A date range, <c>business_hours</c>, <c>group_by</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The per-team report.</returns>
    public Task<JsonElement> TeamsAsync(ChatlyQuery? query, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/reports/teams", query, cancellationToken);

    /// <summary>
    /// First-response and resolution percentiles with sample size.
    /// <c>GET /v1/reports/response-times</c>.
    /// </summary>
    /// <remarks>
    /// One object. Percentiles are <b>seconds</b>, and they are <b>null when
    /// <c>sampleSize</c> is 0</b> rather than zero — so a quiet window reads
    /// as "no data" instead of "instant replies". Check the sample size
    /// before showing a p95 to anybody. Accepts <c>days</c>.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The percentile report.</returns>
    public Task<JsonElement> ResponseTimesAsync(CancellationToken cancellationToken = default)
        => ResponseTimesAsync(null, cancellationToken);

    /// <summary>The percentile report, with query parameters.</summary>
    /// <param name="query">Parameters such as <c>days</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The percentile report.</returns>
    public Task<JsonElement> ResponseTimesAsync(ChatlyQuery? query, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/reports/response-times", query, cancellationToken);

    /// <summary>
    /// Voice analytics — volume, answer rate, talk time, heatmap, per-agent.
    /// <c>GET /v1/reports/voice</c>.
    /// </summary>
    /// <remarks>
    /// One object. <c>recordingStorage.exact: false</c> means
    /// <c>totalBytesUpperBound</c> is a bound and not a measurement — do not
    /// bill or alert on it as though it were exact. Accepts <c>days</c>.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The voice report.</returns>
    public Task<JsonElement> VoiceAsync(CancellationToken cancellationToken = default)
        => VoiceAsync(null, cancellationToken);

    /// <summary>The voice report, with query parameters.</summary>
    /// <param name="query">Parameters such as <c>days</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The voice report.</returns>
    public Task<JsonElement> VoiceAsync(ChatlyQuery? query, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/reports/voice", query, cancellationToken);

    /// <summary>
    /// The latest theme batch: what customers wrote about, clustered.
    /// <c>GET /v1/reports/themes</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One object, <b>not</b> a bare array of themes. Themes are computed in
    /// nightly batches, so <c>batchDate</c> names the snapshot every row was
    /// measured against — a null one means no batch has run yet, which is the
    /// normal state for a new workspace rather than an error.
    /// </para>
    /// <para>
    /// Pass <c>date</c> to read an older batch instead of the latest.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ batchDate, themes }</c>.</returns>
    public Task<JsonElement> ThemesAsync(CancellationToken cancellationToken = default)
        => ThemesAsync(null, cancellationToken);

    /// <summary>A theme batch, with query parameters.</summary>
    /// <param name="query">Parameters — <c>date</c> selects an older batch.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ batchDate, themes }</c>.</returns>
    public Task<JsonElement> ThemesAsync(ChatlyQuery? query, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/reports/themes", query, cancellationToken);

    /// <summary>
    /// The conversations in one theme, as the snapshot recorded them.
    /// <c>GET /v1/reports/themes/{id}/conversations</c>.
    /// </summary>
    /// <remarks>
    /// One object wrapping an id list — <c>{ conversationIds }</c> — so you
    /// fetch the threads yourself. <b>An unknown or purged theme id answers
    /// 200 with an empty list, not a 404</b>, because a theme batch is
    /// snapshot data that ages out; so an empty result does not tell you
    /// whether the theme was empty or is simply gone.
    /// </remarks>
    /// <param name="themeId">The theme, from <see cref="ThemesAsync(CancellationToken)"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ conversationIds }</c>.</returns>
    public Task<JsonElement> ThemeConversationsAsync(
        string themeId,
        CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"/v1/reports/themes/{Route.Segment(themeId, nameof(themeId))}/conversations",
            null,
            cancellationToken);
}
