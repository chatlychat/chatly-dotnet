using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// Macros — saved replies an agent can apply in one click. Reached as
/// <c>chatly.Macros</c>.
/// </summary>
public sealed class MacrosResource
{
    private const string Collection = "/v1/macros";

    private readonly Transport _transport;

    internal MacrosResource(Transport transport) => _transport = transport;

    /// <summary>
    /// The macro library. <c>GET /v1/macros</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This endpoint is not paginated and does not return a page.</b> It
    /// answers a bare JSON array, so the returned element's
    /// <c>ValueKind</c> is <see cref="JsonValueKind.Array"/> — enumerate it
    /// directly; there is no <c>items</c> property and no cursor. The handler
    /// is marked pagination-exempt on purpose: the service caps the result at
    /// 200 rows and orders them by usage rank, which is what makes the list
    /// useful to an agent picking a reply.
    /// </para>
    /// <para>
    /// There is deliberately no <c>ListAllAsync</c> here. An auto-paginator
    /// over a route with no cursor would either return one page and look
    /// complete, or spin; and the 200-row cap is the real answer to "give me
    /// everything", not a page size.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A JSON array of macros, newest-ranked first.</returns>
    public Task<JsonElement> ListAsync(CancellationToken cancellationToken = default)
        => ListAsync(null, cancellationToken);

    /// <summary>
    /// The macro library, narrowed. <c>GET /v1/macros</c>.
    /// </summary>
    /// <remarks>
    /// Takes <see cref="MacroListOptions"/> rather than
    /// <see cref="ListOptions"/>: this route accepts <c>scope</c>,
    /// <c>team</c>, <c>query</c> and <c>language</c>, and has no use for a
    /// cursor, a limit or a sort.
    /// </remarks>
    /// <param name="options">Scope, team, text filter, preferred language.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A JSON array of macros.</returns>
    public Task<JsonElement> ListAsync(
        MacroListOptions? options,
        CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(Collection, options?.ToQuery(), cancellationToken);

    /// <summary>Create a macro. <c>POST /v1/macros</c>.</summary>
    /// <param name="macro">The macro to create, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The created macro.</returns>
    public Task<JsonElement> CreateAsync(object macro, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(HttpMethod.Post, Collection, macro, null, cancellationToken);

    /// <summary>
    /// Change part of a macro. <c>PATCH /v1/macros/{id}</c>.
    /// </summary>
    /// <remarks>
    /// A PATCH, so send only the fields you are changing — fields you omit
    /// keep their current values.
    /// </remarks>
    /// <param name="macroId">The macro to change.</param>
    /// <param name="changes">The fields to change, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The updated macro.</returns>
    public Task<JsonElement> UpdateAsync(
        string macroId,
        object changes,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            new HttpMethod("PATCH"),
            $"{Collection}/{Route.Segment(macroId, nameof(macroId))}",
            changes,
            null,
            cancellationToken);

    /// <summary>Delete a macro. <c>DELETE /v1/macros/{id}</c>.</summary>
    /// <param name="macroId">The macro to delete.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The API's response, which is JSON null when it answers 204.</returns>
    public Task<JsonElement> DeleteAsync(string macroId, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Delete,
            $"{Collection}/{Route.Segment(macroId, nameof(macroId))}",
            null,
            null,
            cancellationToken);

    /// <summary>One macro in full. <c>GET /v1/macros/{id}</c>.</summary>
    /// <remarks>
    /// The list route caps at 200 rows by usage rank, so this is how you
    /// reach a macro that did not make the cut — and the only way to see the
    /// full action list on one, which a list row abbreviates.
    /// </remarks>
    /// <param name="macroId">The macro.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The macro.</returns>
    /// <exception cref="ChatlyNotFoundException">No such macro in this workspace.</exception>
    public Task<JsonElement> GetAsync(string macroId, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Collection}/{Route.Segment(macroId, nameof(macroId))}",
            null,
            cancellationToken);

    /// <summary>
    /// The actions a macro may perform. <c>GET /v1/macros/actions</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A static catalogue, not workspace data: an object carrying
    /// <c>actions</c> and a <c>limit</c>. Read it before building a macro —
    /// each entry names the <b>permission</b> its action needs, which is what
    /// decides whether the agent applying the macro can actually run it. A
    /// macro whose action the applier lacks permission for fails at apply
    /// time, not at save time.
    /// </para>
    /// <para>
    /// <c>limit</c> is the maximum actions one macro may carry. It is typed
    /// as a number rather than a constant on purpose; do not hard-code
    /// today's value.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ limit, actions }</c>.</returns>
    public Task<JsonElement> ActionsAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync($"{Collection}/actions", null, cancellationToken);

    /// <summary>
    /// Installable macro templates, in a lite shape.
    /// <c>GET /v1/macros/templates</c>.
    /// </summary>
    /// <remarks>
    /// <b>An object — <c>{ items, total }</c> — not a page and not a bare
    /// array.</b> There is no cursor: <c>total</c> is simply
    /// <c>items.length</c> over a frozen in-process catalogue, so it is the
    /// same number on every call and is not a hint that more exist. Rows are
    /// summaries; fetch a body with
    /// <see cref="TemplateAsync(string, CancellationToken)"/>.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ items, total }</c>.</returns>
    public Task<JsonElement> TemplatesAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync($"{Collection}/templates", null, cancellationToken);

    /// <summary>
    /// One macro template, with its full body.
    /// <c>GET /v1/macros/templates/{slug}</c>.
    /// </summary>
    /// <remarks>
    /// Addressed by <b>slug</b>, not by id — the templates are a shipped
    /// catalogue rather than workspace rows, so the identifier is stable
    /// across workspaces and deployments. Percent-encoded for you. Feed what
    /// comes back to
    /// <see cref="CreateAsync(object, CancellationToken)"/> to install it.
    /// </remarks>
    /// <param name="slug">The template's slug, from the list above.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The template, including its body.</returns>
    /// <exception cref="ChatlyNotFoundException">No template with that slug.</exception>
    public Task<JsonElement> TemplateAsync(string slug, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"{Collection}/templates/{Route.Segment(slug, nameof(slug))}",
            null,
            cancellationToken);
}
