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
}
