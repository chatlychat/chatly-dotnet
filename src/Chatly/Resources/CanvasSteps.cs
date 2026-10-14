using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// Interactive widget steps — a form or a choice rendered inside the
/// conversation, waiting on your backend to supply what comes next. Reached
/// as <c>chatly.CanvasSteps</c>.
/// </summary>
/// <remarks>
/// <para>
/// The flow: a canvas you sent shows the visitor a control; when they use it,
/// Chatly holds a <i>pending step</i> and the visitor sees a progress line.
/// Your backend reads the step, decides, and resolves it with the next
/// canvas. Until it does, the visitor is watching a spinner — so this is a
/// latency-sensitive path, not a batch one.
/// </para>
/// <para>
/// A step id is single-use, which is what makes the resolve safe to retry:
/// the claiming update carries <c>status = 'pending'</c>, so exactly one
/// resolve wins and a second one is told the step is already resolved.
/// </para>
/// </remarks>
public sealed class CanvasStepsResource
{
    private const string Collection = "/v1/canvas-steps";

    private readonly Transport _transport;

    internal CanvasStepsResource(Transport transport) => _transport = transport;

    /// <summary>
    /// Read a step: still pending, already resolved, or expired.
    /// <c>GET /v1/canvas-steps/{id}</c>.
    /// </summary>
    /// <remarks>
    /// The question this answers is "is it still worth replying?" — an
    /// expired step cannot be resolved, and resolving a resolved one is a
    /// 409. Reading first turns both into a decision instead of an error.
    /// Needs <c>messages:read</c>.
    /// </remarks>
    /// <param name="stepId">The step id, from the webhook or canvas payload that announced it.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The step and its status.</returns>
    public Task<JsonElement> GetAsync(string stepId, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Collection}/{Route.Segment(stepId, nameof(stepId))}",
            null,
            cancellationToken);

    /// <summary>
    /// Resolve a pending step with the next canvas.
    /// <c>POST /v1/canvas-steps/{id}/resolve</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The canvas you supply <b>replaces the progress line the visitor is
    /// watching</b>, in place — it is not a new message in the thread. So an
    /// empty or malformed canvas leaves them staring at nothing.
    /// </para>
    /// <para>
    /// Idempotency is optional here rather than required, because the step id
    /// is already a single-use key: a retry after a timeout is answered
    /// correctly whether or not you sent a header. This SDK sends one anyway.
    /// </para>
    /// </remarks>
    /// <param name="stepId">The pending step.</param>
    /// <param name="canvas">
    /// The canvas to show next. Wrapped as <c>{ canvas: … }</c> for you; the
    /// server rejects unknown keys, so pass the canvas itself.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The resolved step.</returns>
    /// <exception cref="ChatlyConflictException">The step was already resolved, or has expired.</exception>
    public Task<JsonElement> ResolveAsync(
        string stepId,
        object canvas,
        CancellationToken cancellationToken = default)
        => ResolveBodyAsync(stepId, new { canvas }, cancellationToken);

    /// <summary>
    /// Resolve a pending step with your own request body.
    /// <c>POST /v1/canvas-steps/{id}/resolve</c>.
    /// </summary>
    /// <param name="stepId">The pending step.</param>
    /// <param name="body">The whole request body, which must carry a <c>canvas</c> key.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The resolved step.</returns>
    public Task<JsonElement> ResolveBodyAsync(
        string stepId,
        object body,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/{Route.Segment(stepId, nameof(stepId))}/resolve",
            body,
            null,
            cancellationToken);
}
