using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// The AI layer's configuration: provider keys, the spend meter, the tone
/// rules every customer-facing prompt is built from, and custom tools.
/// Reached as <c>chatly.Ai</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two controllers live behind this one property, and their paths do not
/// match: provider credentials are <c>/v1/ai-providers</c> (a hyphen, no
/// sub-path) while everything else is <c>/v1/ai/…</c>. The manifest keeps
/// both under one <c>ai</c> namespace, so the method names here are the only
/// hint — do not infer a path from one.
/// </para>
/// <para>
/// Nothing on this resource ever returns a provider API key. The provider
/// rows carry a four-character <c>keyHint</c> and no field for the key
/// itself, which is what keeps a leaked response from being a leaked
/// credential.
/// </para>
/// </remarks>
public sealed class AiResource
{
    private const string ProvidersPath = "/v1/ai-providers";

    private const string TonePath = "/v1/ai/tone";

    private const string ToolsPath = "/v1/ai/tools";

    private readonly Transport _transport;

    internal AiResource(Transport transport) => _transport = transport;

    /// <summary>
    /// Which AI providers this workspace has a usable key for.
    /// <c>GET /v1/ai-providers</c>.
    /// </summary>
    /// <remarks>
    /// Answers an object with a <c>providers</c> array, not a bare array —
    /// and the array lists <i>every supported provider</i>, configured or
    /// not, so <c>configured: false</c> rows are the normal case rather than
    /// an error. <c>configured</c> means the resolver would find a usable
    /// key, which is stronger than "a row exists": a row whose encryption
    /// envelope has gone is listed and unconfigured.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ providers: [...] }</c> — never a key value.</returns>
    public Task<JsonElement> ProvidersAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(ProvidersPath, null, cancellationToken);

    /// <summary>
    /// Store an AI provider key, replacing any key already held for that
    /// provider. <c>POST /v1/ai-providers</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The key is validated against the provider <i>during</i> this call, and
    /// the outcome comes back as <c>validation.status</c>. A non-success
    /// validation does not necessarily mean the key was rejected: only an
    /// authentication-class answer refuses the save, so a provider that is
    /// rate-limiting or down leaves the key stored as <c>unverified</c>
    /// rather than losing it. Re-check later with
    /// <see cref="TestProviderAsync(string, CancellationToken)"/>.
    /// </para>
    /// <para>
    /// <c>reindexQueued</c> in the response counts the KB articles this save
    /// queued for embedding — zero for every provider but OpenAI, and capped,
    /// so the rest index on their next publish. It is reported rather than
    /// implied because "all of them" is usually wrong.
    /// </para>
    /// </remarks>
    /// <param name="provider">The provider key, e.g. <c>openai</c> or <c>anthropic</c>.</param>
    /// <param name="apiKey">The provider's own API key. Stored encrypted; never echoed back.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ provider, validation, reindexQueued }</c>, at 201.</returns>
    public Task<JsonElement> AddProviderAsync(
        string provider,
        string apiKey,
        CancellationToken cancellationToken = default)
        => AddProviderAsync((object)new { provider, apiKey }, cancellationToken);

    /// <summary>
    /// Store an AI provider key from your own payload shape.
    /// <c>POST /v1/ai-providers</c>.
    /// </summary>
    /// <param name="body">The request body, which must carry <c>provider</c> and <c>apiKey</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ provider, validation, reindexQueued }</c>.</returns>
    public Task<JsonElement> AddProviderAsync(object body, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(HttpMethod.Post, ProvidersPath, body, null, cancellationToken);

    /// <summary>
    /// Re-check a stored key against the provider.
    /// <c>POST /v1/ai-providers/{provider}/test</c>.
    /// </summary>
    /// <remarks>
    /// A POST with no body, because it is not idempotent in the useful sense:
    /// it makes a real call to the provider and records the result as the
    /// row's latest validation. The response carries no <c>reindexQueued</c>
    /// — this does not re-embed anything.
    /// </remarks>
    /// <param name="provider">
    /// The provider to re-check. Validated against a closed enum, so an
    /// unrecognised name is a 400 rather than a 404.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ provider, validation }</c>.</returns>
    public Task<JsonElement> TestProviderAsync(string provider, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Post,
            $"{ProvidersPath}/{Route.Segment(provider, nameof(provider))}/test",
            null,
            null,
            cancellationToken);

    /// <summary>
    /// Month-to-date AI spend against the workspace's cap, with a projection.
    /// <c>GET /v1/ai/quota</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Worth polling if your integration drives AI features. The cap is
    /// enforced, not advisory: past it the AI service refuses work rather
    /// than running up a bill, so a copilot that suddenly answers nothing is
    /// usually this.
    /// </para>
    /// <para>
    /// This route is gated on <c>workspace:billing:read</c> rather than
    /// <c>ai:read</c> — it is a spend figure, and a key scoped only to the AI
    /// permissions gets a 403 from it.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Spend, cap and projection for the current month.</returns>
    public Task<JsonElement> QuotaAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/ai/quota", null, cancellationToken);

    /// <summary>
    /// The workspace's tone rules. <c>GET /v1/ai/tone</c>.
    /// </summary>
    /// <remarks>
    /// <b>A bare JSON array</b>, ordered by category then position — not a
    /// page and not an object with a <c>rules</c> key, even though the write
    /// below takes one. Pagination-exempt: the rule set is capped per
    /// category, so there is nothing to walk.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A JSON array of rules.</returns>
    public Task<JsonElement> ToneAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(TonePath, null, cancellationToken);

    /// <summary>
    /// Replace the whole tone rule set. <c>PUT /v1/ai/tone</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A PUT and a full replacement: rules you omit are <i>deleted</i>. There
    /// is no per-rule endpoint, so read
    /// <see cref="ToneAsync(CancellationToken)"/> first and send the whole set
    /// back with your edit applied.
    /// </para>
    /// <para>
    /// The request body is <c>{ rules: [...] }</c> but the response is the
    /// same bare array <see cref="ToneAsync(CancellationToken)"/> returns,
    /// re-read after the write. The asymmetry is the server's, not this
    /// method's.
    /// </para>
    /// </remarks>
    /// <param name="rules">The complete rule set. An empty sequence clears every rule.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The stored rule set, as a JSON array.</returns>
    public Task<JsonElement> SetToneAsync(
        IEnumerable<object> rules,
        CancellationToken cancellationToken = default)
        => SetToneAsync((object)new { rules }, cancellationToken);

    /// <summary>
    /// Replace the whole tone rule set from your own payload shape.
    /// <c>PUT /v1/ai/tone</c>.
    /// </summary>
    /// <param name="body">The request body, which must carry a <c>rules</c> array.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The stored rule set, as a JSON array.</returns>
    public Task<JsonElement> SetToneAsync(object body, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(HttpMethod.Put, TonePath, body, null, cancellationToken);

    /// <summary>
    /// Propose tone rules from a pasted style guide.
    /// <c>POST /v1/ai/tone/generate</c>.
    /// </summary>
    /// <remarks>
    /// <b>This does not save anything.</b> It is an LLM call that returns
    /// candidate rules for a person to approve; persisting them is a separate
    /// <see cref="SetToneAsync(object, CancellationToken)"/>. It is also
    /// metered AI spend, so it can be refused by the quota above.
    /// </remarks>
    /// <param name="styleGuide">
    /// The prose to derive rules from. The server requires at least 40
    /// characters and accepts at most 20,000 — shorter or longer is a 400,
    /// not a truncation.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Proposed rules, unsaved.</returns>
    public Task<JsonElement> GenerateToneAsync(string styleGuide, CancellationToken cancellationToken = default)
        => GenerateToneAsync((object)new { styleGuide }, cancellationToken);

    /// <summary>
    /// Propose tone rules from your own payload shape.
    /// <c>POST /v1/ai/tone/generate</c>.
    /// </summary>
    /// <param name="body">The request body, which must carry <c>styleGuide</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Proposed rules, unsaved.</returns>
    public Task<JsonElement> GenerateToneAsync(object body, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(HttpMethod.Post, $"{TonePath}/generate", body, null, cancellationToken);

    /// <summary>
    /// The exact text block the tone rules compile to, as it is injected into
    /// every customer-facing system prompt. <c>GET /v1/ai/tone/preview</c>.
    /// </summary>
    /// <remarks>
    /// Useful for diffing what you think you configured against what the
    /// model is actually told. A workspace that has configured nothing gets
    /// an empty string rather than a default house style — there is no
    /// implicit tone.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>An object wrapping the rendered block.</returns>
    public Task<JsonElement> TonePreviewAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync($"{TonePath}/preview", null, cancellationToken);

    /// <summary>
    /// The workspace's custom AI tools. <c>GET /v1/ai/tools</c>.
    /// </summary>
    /// <remarks>
    /// <b>A bare JSON array</b>, and secret header <i>values</i> are omitted
    /// from every row — only the names come back. Pagination-exempt.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A JSON array of tool definitions.</returns>
    public Task<JsonElement> ToolsAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(ToolsPath, null, cancellationToken);

    /// <summary>Define a custom AI tool. <c>POST /v1/ai/tools</c>.</summary>
    /// <remarks>
    /// A tool is a callable an AI agent may invoke against your own HTTP
    /// endpoint, so creating one widens what the autonomous agent can do in
    /// your systems. Scoped to <c>ai:write</c>, and the route <b>requires</b>
    /// an idempotency key — this SDK mints one per call, so you only need to
    /// think about it if you pin one.
    /// </remarks>
    /// <param name="tool">The tool definition, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The created tool, at 201. Secret header values are not echoed.</returns>
    public Task<JsonElement> CreateToolAsync(object tool, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(HttpMethod.Post, ToolsPath, tool, null, cancellationToken);

    /// <summary>Change part of a custom AI tool. <c>PATCH /v1/ai/tools/{id}</c>.</summary>
    /// <remarks>
    /// Omit <c>secretHeaders</c> entirely to keep the stored values. Sending
    /// it replaces the whole bag, so sending an empty object clears every
    /// secret header rather than leaving them alone.
    /// </remarks>
    /// <param name="toolId">The tool to change.</param>
    /// <param name="changes">The fields to change, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The updated tool.</returns>
    public Task<JsonElement> UpdateToolAsync(
        string toolId,
        object changes,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            new HttpMethod("PATCH"),
            $"{ToolsPath}/{Route.Segment(toolId, nameof(toolId))}",
            changes,
            null,
            cancellationToken);

    /// <summary>Delete a custom AI tool. <c>DELETE /v1/ai/tools/{id}</c>.</summary>
    /// <remarks>
    /// Answers <b>200 with <c>{ deleted: true }</c></b>, not a 204. Any AI
    /// agent configured to use the tool stops being offered it on its next
    /// run; existing conversations are not rewritten.
    /// </remarks>
    /// <param name="toolId">The tool to delete.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{ deleted: true }</c>.</returns>
    public Task<JsonElement> DeleteToolAsync(string toolId, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Delete,
            $"{ToolsPath}/{Route.Segment(toolId, nameof(toolId))}",
            null,
            null,
            cancellationToken);
}
