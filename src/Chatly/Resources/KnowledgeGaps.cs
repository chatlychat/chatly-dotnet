using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// Questions customers asked that the knowledge base could not answer.
/// Reached as <c>chatly.KnowledgeGaps</c>.
/// </summary>
/// <remarks>
/// Recorded by the RAG path when retrieval comes back with nothing useful, so
/// a gap is evidence from real traffic rather than a guess at what to write
/// next. The triage loop is: list, draft, publish or dismiss.
/// </remarks>
public sealed class KnowledgeGapsResource
{
    private const string Collection = "/v1/knowledge-gaps";

    private readonly Transport _transport;

    internal KnowledgeGapsResource(Transport transport) => _transport = transport;

    /// <summary>
    /// Gaps, most-asked first. <c>GET /v1/knowledge-gaps</c>.
    /// </summary>
    /// <remarks>
    /// <b>A bare JSON array</b>, pagination-exempt — the recorder caps a
    /// workspace's open gaps, so there is a bounded set rather than a stream,
    /// and the ordering by frequency is the useful part.
    /// </remarks>
    /// <param name="status">
    /// Narrow to one triage status. Null returns the server's default
    /// selection.
    /// </param>
    /// <param name="limit">Cap the rows returned. Null leaves the server's cap.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A JSON array of gaps, most-asked first.</returns>
    public Task<JsonElement> ListAsync(
        string? status = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            Collection,
            new ChatlyQuery().Add("status", status).Add("limit", limit),
            cancellationToken);

    /// <summary>
    /// Triage one gap: resolved, dismissed, or back to open.
    /// <c>PATCH /v1/knowledge-gaps/{id}</c>.
    /// </summary>
    /// <remarks>
    /// The body is strict and <c>status</c> is a closed enum, so a
    /// near-miss value is a 400. A gap id that is not this workspace's is a
    /// 404 rather than a silent no-op.
    /// </remarks>
    /// <param name="gapId">The gap.</param>
    /// <param name="status">The new triage status.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The updated gap.</returns>
    /// <exception cref="ChatlyNotFoundException">No such gap in this workspace.</exception>
    public Task<JsonElement> UpdateAsync(
        string gapId,
        string status,
        CancellationToken cancellationToken = default)
        => UpdateAsync(gapId, (object)new { status }, cancellationToken);

    /// <summary>
    /// Triage one gap from your own request body.
    /// <c>PATCH /v1/knowledge-gaps/{id}</c>.
    /// </summary>
    /// <param name="gapId">The gap.</param>
    /// <param name="changes">The request body, which must carry <c>status</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The updated gap.</returns>
    public Task<JsonElement> UpdateAsync(
        string gapId,
        object changes,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            new HttpMethod("PATCH"),
            $"{Collection}/{Route.Segment(gapId, nameof(gapId))}",
            changes,
            null,
            cancellationToken);

    /// <summary>
    /// Draft a KB article from the conversation this gap was found in.
    /// <c>POST /v1/knowledge-gaps/{id}/draft</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The article is created as a <b>draft</b> — nothing is published and no
    /// visitor sees it until someone publishes it.
    /// </para>
    /// <para>
    /// <b>Not every 200 produces an article.</b> A gap recorded without a
    /// usable source conversation answers with a reason instead of a draft
    /// id, so read the id rather than the status. This is also a metered LLM
    /// call and the route requires an idempotency key, which this SDK mints.
    /// </para>
    /// </remarks>
    /// <param name="gapId">The gap to draft from.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The gap, plus the new draft article's id or a reason there is none.</returns>
    public Task<JsonElement> DraftAsync(string gapId, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/{Route.Segment(gapId, nameof(gapId))}/draft",
            null,
            null,
            cancellationToken);
}
