using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// The workspace itself: its settings and the things agents are organised by.
/// Reached as <c>chatly.Workspace</c>.
/// </summary>
/// <remarks>
/// These are the lookups an integration needs before it can write anything
/// meaningful — an inbox id to route into, a label id to apply, a team's
/// members to pick an assignee from.
/// </remarks>
public sealed class WorkspaceResource
{
    private readonly Transport _transport;

    internal WorkspaceResource(Transport transport) => _transport = transport;

    /// <summary>Workspace settings. <c>GET /v1/workspace/settings</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The settings.</returns>
    public Task<JsonElement> SettingsAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/workspace/settings", null, cancellationToken);

    /// <summary>Conversation labels. <c>GET /v1/labels</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The labels.</returns>
    public Task<JsonElement> LabelsAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/labels", null, cancellationToken);

    /// <summary>Inboxes. <c>GET /v1/inboxes</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The inboxes.</returns>
    public Task<JsonElement> InboxesAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/inboxes", null, cancellationToken);

    /// <summary>
    /// The members of one team. <c>GET /v1/teams/{id}/members</c>.
    /// </summary>
    /// <remarks>
    /// Team ids come from the dashboard, or from a workflow or webhook payload
    /// that names one.
    /// </remarks>
    /// <param name="teamId">The team.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The team's members.</returns>
    public Task<JsonElement> TeamMembersAsync(string teamId, CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(
            $"/v1/teams/{Route.Segment(teamId, nameof(teamId))}/members",
            null,
            cancellationToken);

    /// <summary>
    /// Every agent's routing skills, which is what routing decisions are made
    /// from. <c>GET /v1/agents/skills</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Skills per agent.</returns>
    public Task<JsonElement> AgentSkillsAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/agents/skills", null, cancellationToken);

    /// <summary>
    /// The permission scopes an API key can usefully be granted.
    /// <c>GET /v1/api-keys/scopes</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A bare JSON array</b>, static, and deliberately <i>not</i> the
    /// whole permission vocabulary. Key auth is opt-in per route, so most of
    /// the eighty permissions have no key-accessible route behind them; this
    /// endpoint used to return all of them, which let a customer mint a key
    /// scoped to something the guard was never going to honour and then watch
    /// every call fail. <c>group</c> is the part of the name before the
    /// colon.
    /// </para>
    /// <para>
    /// Reach for it when a call returns 403 and you want to know which scope
    /// it wanted, rather than learning the vocabulary by provoking refusals.
    /// Pair it with <see cref="ChatlyClient.WhoAmIAsync(CancellationToken)"/>,
    /// which says what your key actually holds.
    /// </para>
    /// <para>
    /// This is the second route on the api-keys controller that an API key
    /// may call, alongside <c>whoami</c>. Everything else there stays closed:
    /// minting or listing keys with a key would let a leaked read-only key
    /// escalate itself. Safe to open because the response is a vocabulary of
    /// permission <i>names</i> — no workspace data and no key material.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A JSON array of <c>{ value, group }</c>.</returns>
    public Task<JsonElement> KeyScopesAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/api-keys/scopes", null, cancellationToken);

    /// <summary>
    /// A census of every credential the workspace holds.
    /// <c>GET /v1/workspace/secrets</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One entry per stored credential across nine tables — provider keys,
    /// channel secrets, card secrets, webhook secrets and the rest. <b>No
    /// entry carries a value.</b> It reports existence, ownership and
    /// timestamps, which is what you need to answer "what would we have to
    /// rotate?" without being able to read any of it.
    /// </para>
    /// <para>
    /// Gated on <c>workspace:write</c> rather than a read scope, because
    /// knowing which credentials exist and when they were last touched is
    /// itself sensitive.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The credential census — existence and timestamps, never a value.</returns>
    public Task<JsonElement> SecretsAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/workspace/secrets", null, cancellationToken);

    /// <summary>
    /// Every workspace the caller can reach. <c>GET /v1/workspaces/me</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A bare JSON array</b>, pagination-exempt. For a <c>ck_</c> key it
    /// holds <b>exactly one entry</b> — the workspace the key was minted in —
    /// which makes it the cheapest way to confirm which tenant a key actually
    /// points at. The route is the cross-workspace one for a signed-in
    /// person; for a credential scoped to a single workspace it is a list of
    /// one, and it is an array rather than an object because the shape is the
    /// same either way.
    /// </para>
    /// <para>
    /// <c>role</c> on a key's entry is the <b>synthetic <c>restricted</c></b>
    /// the API mints for every key, not a stored membership role. That is the
    /// honest value: what a key may do is its scope set, which
    /// <see cref="ChatlyClient.MeAsync(CancellationToken)"/> answers.
    /// Reporting <c>owner</c> because the person who created the key was one
    /// would overstate the credential.
    /// </para>
    /// <para>
    /// Declared <c>@AllowWhenSuspended</c>, so it <b>answers even when the
    /// active workspace is suspended</b> — it is how a caller discovers the
    /// others. Most routes do not, so if everything else is failing and this
    /// still works, suspension is the thing to check.
    /// </para>
    /// <para>
    /// It is also the only open route on its controller. Creating a workspace
    /// makes the <i>caller</i> its owner, which has no meaning without a
    /// person, so <c>POST /v1/workspaces</c> answers a key with 403.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A JSON array of <c>{ id, name, slug, plan, role, createdAt }</c>.</returns>
    public Task<JsonElement> MineAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/workspaces/me", null, cancellationToken);
}
