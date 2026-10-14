using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// The transactional emails Chatly sends on a workspace's behalf, and this
/// workspace's overrides of them. Reached as <c>chatly.EmailTemplates</c>.
/// </summary>
/// <remarks>
/// <para>
/// These are addressed by <i>kind</i> rather than by id, and the set of kinds
/// is closed: <c>workspace_invite</c>, <c>trial_ending_reminder</c>,
/// <c>conversation_cap_warning</c>, <c>hired_agent_engagement_invite</c> and
/// <c>hired_agent_engagement_started</c>. An unknown kind is a 400.
/// </para>
/// <para>
/// They are not conversation replies. Nothing here changes what an agent
/// sends a customer; it changes the platform's own notifications.
/// </para>
/// </remarks>
public sealed class EmailTemplatesResource
{
    private const string Collection = "/v1/email-templates";

    private readonly Transport _transport;

    internal EmailTemplatesResource(Transport transport) => _transport = transport;

    /// <summary>
    /// Every overridable email, with this workspace's override if there is
    /// one. <c>GET /v1/email-templates</c>.
    /// </summary>
    /// <remarks>
    /// <b>A bare JSON array of all five kinds</b> — it is the catalogue, not
    /// the override rows, so the length does not change with how much you
    /// have customised. <c>override</c> is null where the workspace is still
    /// on the built-in copy.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A JSON array of five entries.</returns>
    public Task<JsonElement> ListAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(Collection, null, cancellationToken);

    /// <summary>
    /// Render a template without saving it.
    /// <c>POST /v1/email-templates/preview</c>.
    /// </summary>
    /// <remarks>
    /// Returns the rendered subject, HTML and text — <b>and the tokens you
    /// wrote that this kind does not supply</b>, which is the field worth
    /// reading: a mistyped token renders as nothing rather than failing, so
    /// without this list a broken template looks fine.
    /// </remarks>
    /// <param name="template">The draft template, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Rendered subject, HTML, text, and unsupplied tokens.</returns>
    public Task<JsonElement> PreviewAsync(object template, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{Collection}/preview",
            template,
            null,
            cancellationToken);

    /// <summary>
    /// Replace this workspace's copy of one email.
    /// <c>PUT /v1/email-templates/{kind}</c>.
    /// </summary>
    /// <remarks>
    /// A PUT and a whole replacement, not a patch. The response is the same
    /// shape as one element of
    /// <see cref="ListAsync(CancellationToken)"/>. The write is audited, and
    /// a key linked to a machine user is attributed to it.
    /// </remarks>
    /// <param name="kind">
    /// One of the five kinds. Percent-encoded for you.
    /// </param>
    /// <param name="template">The override, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The kind as stored, with its new override.</returns>
    public Task<JsonElement> UpdateAsync(
        string kind,
        object template,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Put,
            $"{Collection}/{Route.Segment(kind, nameof(kind))}",
            template,
            null,
            cancellationToken);

    /// <summary>
    /// Go back to the built-in copy.
    /// <c>DELETE /v1/email-templates/{kind}</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This <b>deletes the override</b> rather than copying today's default
    /// into it, so the workspace keeps tracking the built-in copy as it
    /// changes. "Restore" is the better word for it than "delete".
    /// </para>
    /// <para>
    /// <b>It has a body, not a 204:</b> <c>{ restored: false }</c> with a 200
    /// when the workspace was already on the built-in copy, which makes it
    /// safe to call unconditionally.
    /// </para>
    /// </remarks>
    /// <param name="kind">The kind to restore.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ restored }</c>.</returns>
    public Task<JsonElement> DeleteAsync(string kind, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Delete,
            $"{Collection}/{Route.Segment(kind, nameof(kind))}",
            null,
            null,
            cancellationToken);
}
