using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// The rules that decide which AI agent answers a given visitor. Reached as
/// <c>chatly.BotRoutes</c>.
/// </summary>
/// <remarks>
/// A routing rule is a condition over the page the visitor is on and the
/// context the widget supplied, plus the bot to use when it matches. Rules
/// are evaluated <b>in order</b> and the first match wins, so position is
/// semantic rather than cosmetic — <see cref="ListAsync(CancellationToken)"/>
/// returns them in evaluation order, not by creation time.
/// </remarks>
public sealed class BotRoutesResource
{
    private const string Collection = "/v1/bot-routes";

    private readonly Transport _transport;

    internal BotRoutesResource(Transport transport) => _transport = transport;

    /// <summary>
    /// The routing table, in evaluation order. <c>GET /v1/bot-routes</c>.
    /// </summary>
    /// <remarks>
    /// <b>A bare JSON array</b>, pagination-exempt — the order <i>is</i> the
    /// answer, and a cursor would invite a caller to re-sort it. Do not
    /// re-order what comes back.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A JSON array of rules, first-match-wins order.</returns>
    public Task<JsonElement> ListAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(Collection, null, cancellationToken);

    /// <summary>
    /// The fields and operators a routing condition may use.
    /// <c>GET /v1/bot-routes/fields</c>.
    /// </summary>
    /// <remarks>
    /// Static catalogue, not workspace data: an object with the condition
    /// vocabulary. Read it rather than hard-coding field names — a condition
    /// naming a field that is not in here never matches, and the write that
    /// created it does not refuse.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The field and operator catalogue.</returns>
    public Task<JsonElement> FieldsAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync($"{Collection}/fields", null, cancellationToken);

    /// <summary>Add a routing rule. <c>POST /v1/bot-routes</c>.</summary>
    /// <remarks>
    /// Needs <c>triggers:write</c> — routing rules are grouped with triggers
    /// in the permission vocabulary, not with <c>ai:*</c>.
    /// </remarks>
    /// <param name="route">The rule to create, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The created rule, at 201.</returns>
    public Task<JsonElement> CreateAsync(object route, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(HttpMethod.Post, Collection, route, null, cancellationToken);

    /// <summary>Edit a routing rule. <c>PATCH /v1/bot-routes/{id}</c>.</summary>
    /// <remarks>
    /// A PATCH, so omitted fields are unchanged. Moving a rule's position
    /// changes which visitors reach every rule below it, so prefer
    /// <see cref="ResolveAsync(object, CancellationToken)"/> over reasoning
    /// about the table by eye.
    /// </remarks>
    /// <param name="routeId">The rule to change.</param>
    /// <param name="changes">The fields to change, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The updated rule.</returns>
    public Task<JsonElement> UpdateAsync(
        string routeId,
        object changes,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            new HttpMethod("PATCH"),
            $"{Collection}/{Route.Segment(routeId, nameof(routeId))}",
            changes,
            null,
            cancellationToken);

    /// <summary>Remove a routing rule. <c>DELETE /v1/bot-routes/{id}</c>.</summary>
    /// <remarks>
    /// Answers <b>200 with <c>{ deleted: true }</c></b>, not a 204. Rules
    /// below the removed one move up, which changes evaluation order.
    /// </remarks>
    /// <param name="routeId">The rule to remove.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ deleted: true }</c>.</returns>
    public Task<JsonElement> DeleteAsync(string routeId, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Delete,
            $"{Collection}/{Route.Segment(routeId, nameof(routeId))}",
            null,
            null,
            cancellationToken);

    /// <summary>
    /// Dry-run the routing table: which bot would answer, and why not the
    /// others. <c>POST /v1/bot-routes/resolve</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Changes nothing.</b> It is a POST because the input is a context
    /// object rather than a query string, and it is scoped to
    /// <c>triggers:read</c> accordingly. The response carries the selected
    /// route (or null) <i>and every route with its verdict</i>, in evaluation
    /// order — which is the part worth having, because "no bot answered" and
    /// "the wrong bot answered" look identical from the selected route alone.
    /// </para>
    /// <para>
    /// A note on the input: <c>page.url</c> and <c>page.path</c> are derived
    /// from each other when only one is given, so a rule keyed on the path
    /// matches whether you sent a URL or a path. The widget sends both.
    /// </para>
    /// </remarks>
    /// <param name="context">
    /// The simulated visitor context — <c>page</c>, and <c>context.verified</c>
    /// / <c>context.unverified</c> exactly as a condition reads them.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The selected route, every route's verdict, and the context used.</returns>
    public Task<JsonElement> ResolveAsync(object context, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/resolve",
            context,
            null,
            cancellationToken);
}
