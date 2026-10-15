using System;
using System.Collections.Generic;

namespace Chatly;

// ═══ WHAT THIS FILE IS ═══════════════════════════════════════════════════
//
// The data half of `ChatlyWebhooks.VerifyChatlySignature`: the eight surfaces,
// the eleven refusal reasons, the input and the result. The verifier itself is
// `ChatlySignatures.cs`. They are split for the same reason the Node reference
// splits `signature-table.ts` from `signatures.ts` — the 650-line ceiling, and
// so the table can be read on its own when a verification fails.
//
// This is a PORT. The reference is `packages/sdk-node/src/signatures.ts` and
// `signature-table.ts`; the shared corpus at `tests/Fixtures/signature_vectors.json`
// is byte-identical across every SDK and is what proves the two agree.
//
// Surfaces, reasons and bases are STRINGS rather than enums, deliberately: they
// are the same spellings every other SDK and the corpus use, an unknown surface
// has to be reported back exactly as the caller wrote it, and a reason is
// something a caller logs and branches on across languages. The v2 policy is an
// enum because it is only ever an input.

/// <summary>
/// The outbound surfaces Chatly signs. Pass one as
/// <see cref="ChatlySignatureInput.Surface"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The header name does not determine the construction.</b> Three surfaces
/// send <c>x-chatly-signature</c> and sign three different strings, and two of
/// the eight send MILLISECONDS. The caller knows which route it is
/// terminating; the verifier cannot, which is why the surface is required and
/// never inferred.
/// </para>
/// <code>
/// surface                  header                     base string                  t unit
/// unified_v2               x-chatly-signature-v2      t.METHOD.path.body           seconds
/// agent_delivery           x-chatly-signature         t.body                       seconds
/// event_webhook            x-livechat-signature       t.body                       seconds
/// ai_tool_call             x-chatly-signature         t.METHOD.path.body           seconds
/// canvas_step              x-chatly-canvas-signature  ts.body (+ x-chatly-canvas-ts) MILLISECONDS
/// customer_card            x-chatly-signature         ts.body (+ x-chatly-timestamp) MILLISECONDS
/// workflow_webhook         x-chatly-signature-v2      t.body, or t.METHOD.path.body seconds
/// workflow_webhook_legacy  x-chatly-signature         body alone — NO timestamp    n/a
/// </code>
/// </remarks>
public static class ChatlySignatureSurfaces
{
    /// <summary>The unified construction every surface is moving to.</summary>
    public const string UnifiedV2 = "unified_v2";

    /// <summary>External-agent push delivery, signed with the bot's signing secret.</summary>
    public const string AgentDelivery = "agent_delivery";

    /// <summary>Event webhook, signed with that subscription's <c>whsec_</c> secret.</summary>
    public const string EventWebhook = "event_webhook";

    /// <summary>Custom AI tool call, over <c>t.METHOD.path.body</c>.</summary>
    public const string AiToolCall = "ai_tool_call";

    /// <summary>Canvas / interactive-step callback; timestamp in MILLISECONDS.</summary>
    public const string CanvasStep = "canvas_step";

    /// <summary>Customer-card fetch; timestamp in MILLISECONDS.</summary>
    public const string CustomerCard = "customer_card";

    /// <summary>Workflow "call a webhook" action, replay-bounded v2 header.</summary>
    public const string WorkflowWebhook = "workflow_webhook";

    /// <summary>
    /// Workflow action, LEGACY header over the body alone — no replay
    /// protection, so it is refused unless
    /// <see cref="ChatlySignatureInput.AllowUnprotectedReplay"/> is set.
    /// </summary>
    public const string WorkflowWebhookLegacy = "workflow_webhook_legacy";

    /// <summary>Every surface, in table order.</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        UnifiedV2, AgentDelivery, EventWebhook, AiToolCall,
        CanvasStep, CustomerCard, WorkflowWebhook, WorkflowWebhookLegacy,
    };
}

/// <summary>
/// Why a verification failed. Stable strings, identical in every Chatly SDK,
/// safe to branch on and to log.
/// </summary>
public static class ChatlySignatureFailure
{
    /// <summary>The surface is not one of <see cref="ChatlySignatureSurfaces.All"/>.</summary>
    public const string UnknownSurface = "unknown_surface";

    /// <summary>No non-empty secret was given.</summary>
    public const string MissingSecret = "missing_secret";

    /// <summary>The surface's signature header is absent.</summary>
    public const string MissingSignatureHeader = "missing_signature_header";

    /// <summary>The signature header arrived more than once.</summary>
    public const string DuplicateSignatureHeader = "duplicate_signature_header";

    /// <summary>The signature header could not be parsed, or a digest is not 64 hex characters.</summary>
    public const string MalformedSignatureHeader = "malformed_signature_header";

    /// <summary>No timestamp where the surface carries one.</summary>
    public const string MissingTimestamp = "missing_timestamp";

    /// <summary>The timestamp is not a run of ASCII digits, or does not fit in a <see cref="long"/>.</summary>
    public const string MalformedTimestamp = "malformed_timestamp";

    /// <summary>The timestamp is too far in the past OR the future.</summary>
    public const string TimestampOutsideTolerance = "timestamp_outside_tolerance";

    /// <summary>The construction signs the method and path and they were not supplied.</summary>
    public const string MissingRequestTarget = "missing_request_target";

    /// <summary><c>workflow_webhook_legacy</c> without the explicit opt-in.</summary>
    public const string ReplayUnprotected = "replay_unprotected";

    /// <summary>The digest does not match.</summary>
    public const string SignatureMismatch = "signature_mismatch";
}

/// <summary>Which string the matching MAC was taken over.</summary>
public static class ChatlySignatureBase
{
    /// <summary><c>&lt;t&gt;.&lt;rawBody&gt;</c>.</summary>
    public const string Body = "body";

    /// <summary><c>&lt;t&gt;.&lt;METHOD&gt;.&lt;path&gt;.&lt;rawBody&gt;</c> — the unified base.</summary>
    public const string Target = "target";

    /// <summary><c>&lt;rawBody&gt;</c> with no timestamp anywhere — unbounded replay.</summary>
    public const string BodyOnly = "body_only";
}

/// <summary>How hard to insist on the unified <c>x-chatly-signature-v2</c> header.</summary>
public enum ChatlyV2Policy
{
    /// <summary>
    /// The default. If the v2 header is present AND <c>Method</c> and
    /// <c>Path</c> were supplied, verify THAT and nothing else; otherwise
    /// verify the surface's own construction. A receiver that passes its
    /// method and path gets the stronger check the day the sender starts
    /// emitting v2, with no code change. Never cross-falls-back: once v2 is
    /// chosen, a bad v2 digest is a refusal even beside a good legacy one.
    /// </summary>
    Prefer = 0,

    /// <summary>Refuse unless the v2 header is there.</summary>
    Require = 1,

    /// <summary>The surface's own construction only.</summary>
    Ignore = 2,
}

/// <summary>
/// One request to verify with <see cref="ChatlyWebhooks.VerifyChatlySignature"/>.
/// </summary>
/// <remarks>
/// <para>
/// A record so a caller can build one per request with an object initializer,
/// and so the verifier can re-run itself on <c>input with { Surface = "unified_v2" }</c>
/// without copying fields by hand.
/// </para>
/// <para>
/// <b>Body and secret each have two properties</b> — <see cref="Body"/> /
/// <see cref="BodyText"/> and <see cref="Secret"/> / <see cref="Secrets"/> —
/// because C# has no <c>string | byte[]</c>. Set ONE of each pair. Setting both
/// is refused rather than resolved by a precedence rule, because a caller who
/// set <c>Secret = old</c> and <c>Secrets = [new]</c> meant something, and
/// silently ignoring half of it is the kind of bug that surfaces as "rotation
/// broke production".
/// </para>
/// </remarks>
public sealed record ChatlySignatureInput
{
    /// <summary>Which surface the request came from. See <see cref="ChatlySignatureSurfaces"/>.</summary>
    public string? Surface { get; init; }

    /// <summary>
    /// The EXACT bytes received, before any JSON parsing. Re-serialising parsed
    /// JSON reorders keys and changes whitespace, and the digest will not match.
    /// </summary>
    public byte[]? Body { get; init; }

    /// <summary>
    /// The body as text, for a caller that only has a string. Encoded as UTF-8
    /// before hashing — the MAC is always over bytes. Prefer <see cref="Body"/>.
    /// </summary>
    public string? BodyText { get; init; }

    /// <summary>
    /// The request's headers: name → every value that arrived under that name.
    /// </summary>
    /// <remarks>
    /// Looked up case-insensitively by walking EVERY key, never through the
    /// dictionary's own comparer — a case-sensitive dictionary holding both
    /// <c>x-livechat-signature</c> and <c>X-Livechat-Signature</c> is two values
    /// of one header, and two values are refused as a duplicate. Build one from
    /// ASP.NET Core's <c>request.Headers</c> or <c>HttpRequestMessage.Headers</c>
    /// with <see cref="HeadersFrom{TValues}"/>.
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<string?>?>? Headers { get; init; }

    /// <summary>The secret for THIS surface. No secret verifies across two surfaces.</summary>
    public string? Secret { get; init; }

    /// <summary>
    /// Every secret currently live on the surface, for a receiver mid-rotation.
    /// Empty entries are skipped, and <see cref="ChatlySignatureResult.SecretIndex"/>
    /// still indexes THIS list as given — <c>["", s]</c> verifying with <c>s</c> reports 1.
    /// </summary>
    public IReadOnlyList<string?>? Secrets { get; init; }

    /// <summary>
    /// Freshness window in SECONDS whatever unit the surface's timestamp is in.
    /// Defaults to <see cref="ChatlyWebhooks.ChatlySignatureToleranceSeconds"/>.
    /// Applies in both directions; exactly the tolerance is allowed. Pass
    /// <see cref="double.PositiveInfinity"/> only when re-verifying an archive.
    /// </summary>
    public double? ToleranceSeconds { get; init; }

    /// <summary>
    /// The HTTP method, which is inside the signed string on <c>unified_v2</c>
    /// and <c>ai_tool_call</c>. Upper-cased before signing. Supplying it on any
    /// surface is what lets <see cref="ChatlyV2Policy.Prefer"/> pick up the v2
    /// header when it appears.
    /// </summary>
    public string? Method { get; init; }

    /// <summary>
    /// <c>pathname + search</c> of the request as it arrived. Never the scheme
    /// or the host.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>How hard to insist on the v2 header. Default <see cref="ChatlyV2Policy.Prefer"/>.</summary>
    public ChatlyV2Policy V2 { get; init; }

    /// <summary>
    /// <c>workflow_webhook_legacy</c> only, and required there: that surface
    /// signs the body with NO timestamp, so a captured request verifies forever.
    /// </summary>
    public bool AllowUnprotectedReplay { get; init; }

    /// <summary>Escape hatch for tests: "now", in Unix MILLISECONDS.</summary>
    public long? NowMs { get; init; }

    /// <summary>
    /// Copy a framework's header collection into the shape <see cref="Headers"/> takes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Generic over the value type because the two collections a .NET receiver
    /// actually holds do not share one: ASP.NET Core's <c>IHeaderDictionary</c>
    /// enumerates <c>KeyValuePair&lt;string, StringValues&gt;</c> (a struct), and
    /// <c>HttpRequestMessage.Headers</c> enumerates
    /// <c>KeyValuePair&lt;string, IEnumerable&lt;string&gt;&gt;</c>. Neither
    /// converts to an <c>IReadOnlyDictionary</c> of lists without a copy.
    /// </para>
    /// <para>
    /// Keys are kept EXACTLY as given, with an ordinal comparer, so two casings
    /// stay two entries and the verifier sees both. Never throws.
    /// </para>
    /// </remarks>
    /// <typeparam name="TValues">The collection type of one header's values.</typeparam>
    /// <param name="headers">The framework's headers.</param>
    /// <returns>A new dictionary; empty for null.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<string?>?> HeadersFrom<TValues>(
        IEnumerable<KeyValuePair<string, TValues>>? headers)
        where TValues : IEnumerable<string?>
    {
        var copy = new Dictionary<string, IReadOnlyList<string?>?>(StringComparer.Ordinal);
        if (headers is null)
        {
            return copy;
        }

        foreach (var pair in headers)
        {
            if (pair.Key is null || pair.Value is null)
            {
                continue;
            }

            Append(copy, pair.Key, pair.Value);
        }

        return copy;
    }

    /// <summary>
    /// Copy single-valued headers into the shape <see cref="Headers"/> takes.
    /// </summary>
    /// <param name="headers">Name → value pairs. A repeated exact name becomes two values.</param>
    /// <returns>A new dictionary; empty for null.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<string?>?> HeadersFrom(
        IEnumerable<KeyValuePair<string, string?>>? headers)
    {
        var copy = new Dictionary<string, IReadOnlyList<string?>?>(StringComparer.Ordinal);
        if (headers is null)
        {
            return copy;
        }

        foreach (var pair in headers)
        {
            if (pair.Key is null || pair.Value is null)
            {
                continue;
            }

            Append(copy, pair.Key, new[] { pair.Value });
        }

        return copy;
    }

    private static void Append(
        Dictionary<string, IReadOnlyList<string?>?> into,
        string key,
        IEnumerable<string?> values)
    {
        var list = into.TryGetValue(key, out var existing) && existing is List<string?> mutable
            ? mutable
            : new List<string?>();
        list.AddRange(values);
        into[key] = list;
    }
}

/// <summary>
/// The answer from <see cref="ChatlyWebhooks.VerifyChatlySignature"/>.
/// </summary>
/// <remarks>
/// A reason instead of a boolean because <c>VerifySignature</c>'s <c>false</c>
/// covers nine distinct failures, and an integrator debugging a rejected
/// delivery cannot tell "your clock is wrong" from "your secret is wrong" from
/// "you passed the parsed body". <see cref="Message"/> is written to be printed.
/// </remarks>
public sealed record ChatlySignatureResult
{
    private ChatlySignatureResult(
        bool ok,
        string surface,
        string? reason,
        string? message,
        long? timestampMs,
        string? @base,
        int? secretIndex)
    {
        Ok = ok;
        Surface = surface;
        Reason = reason;
        Message = message;
        TimestampMs = timestampMs;
        Base = @base;
        SecretIndex = secretIndex;
    }

    /// <summary>True only when the signature is genuine and fresh.</summary>
    public bool Ok { get; }

    /// <summary>
    /// On success, what actually verified — <c>unified_v2</c> when the v2
    /// header was preferred over the surface asked for. On failure, the surface
    /// the refusal is about (the input's, or <c>unified_v2</c> after v2 was chosen).
    /// </summary>
    public string Surface { get; }

    /// <summary>On failure, one of <see cref="ChatlySignatureFailure"/>; null on success.</summary>
    public string? Reason { get; }

    /// <summary>On failure, a sentence for whoever has to fix it; null on success.</summary>
    public string? Message { get; }

    /// <summary>
    /// On success, when the sender says it signed, in Unix MILLISECONDS —
    /// normalised, so a seconds surface and a milliseconds one compare. Null on
    /// <c>workflow_webhook_legacy</c>, which sends no timestamp, and on failure.
    /// </summary>
    public long? TimestampMs { get; }

    /// <summary>On success, one of <see cref="ChatlySignatureBase"/>; null on failure.</summary>
    public string? Base { get; }

    /// <summary>
    /// On success, the index of the secret that verified — into
    /// <see cref="ChatlySignatureInput.Secrets"/> as given, or 0 for a single
    /// <see cref="ChatlySignatureInput.Secret"/>. During a rotation this is how
    /// you learn the old secret is no longer in use. Null on failure.
    /// </summary>
    public int? SecretIndex { get; }

    internal static ChatlySignatureResult Success(string surface, long? timestampMs, string @base, int secretIndex)
        => new(true, surface, null, null, timestampMs, @base, secretIndex);

    internal static ChatlySignatureResult Failure(string surface, string reason, string message)
        => new(false, surface, reason, message, null, null, null);
}

/// <summary>One row of the surface table. Internal: the table is data, read by the verifier.</summary>
internal sealed class ChatlySurfaceSpec
{
    public ChatlySurfaceSpec(
        string signatureHeader,
        string? timestampHeader,
        bool timestampInSignature,
        bool tv1,
        bool? seconds,
        string @base,
        bool alsoAcceptsTargetBase,
        string describe)
    {
        SignatureHeader = signatureHeader;
        TimestampHeader = timestampHeader;
        TimestampInSignature = timestampInSignature;
        Tv1 = tv1;
        Seconds = seconds;
        Base = @base;
        AlsoAcceptsTargetBase = alsoAcceptsTargetBase;
        Describe = describe;
    }

    /// <summary>The header carrying the digest.</summary>
    public string SignatureHeader { get; }

    /// <summary>The header carrying the timestamp on its own, or null.</summary>
    public string? TimestampHeader { get; }

    /// <summary>True when the timestamp is the digest header's own <c>t=</c> field.</summary>
    public bool TimestampInSignature { get; }

    /// <summary><c>t=…,v1=…</c> when true, <c>sha256=…</c> when false.</summary>
    public bool Tv1 { get; }

    /// <summary>True = seconds, false = milliseconds, null = the surface sends no timestamp.</summary>
    public bool? Seconds { get; }

    /// <summary>The string the MAC is taken over.</summary>
    public string Base { get; }

    /// <summary>
    /// True when the UNIFIED base is also accepted on this surface's own header.
    /// Only <c>workflow_webhook</c>, which sent <c>x-chatly-signature-v2</c>
    /// before the base gained the method and path.
    /// </summary>
    public bool AlsoAcceptsTargetBase { get; }

    /// <summary>A one-line description, for error messages.</summary>
    public string Describe { get; }

    /// <summary>
    /// The table. Ordinal keys: a surface is matched exactly, as the reference
    /// does with <c>hasOwnProperty</c> — <c>"Event_Webhook"</c> is unknown.
    /// </summary>
    public static IReadOnlyDictionary<string, ChatlySurfaceSpec> Table { get; } =
        new Dictionary<string, ChatlySurfaceSpec>(StringComparer.Ordinal)
        {
            [ChatlySignatureSurfaces.UnifiedV2] = new(
                ChatlyWebhooks.ChatlyV2SignatureHeader, null, true, true, true, ChatlySignatureBase.Target, false,
                "the unified v2 signature, sent alongside every surface's legacy header, over t.METHOD.path.body"),
            [ChatlySignatureSurfaces.AgentDelivery] = new(
                "x-chatly-signature", null, true, true, true, ChatlySignatureBase.Body, false,
                "external-agent push delivery, signed with the bot's signingSecret"),
            [ChatlySignatureSurfaces.EventWebhook] = new(
                "x-livechat-signature", null, true, true, true, ChatlySignatureBase.Body, false,
                "event webhook, signed with that subscription's whsec_ secret"),
            [ChatlySignatureSurfaces.AiToolCall] = new(
                "x-chatly-signature", null, true, true, true, ChatlySignatureBase.Target, false,
                "custom AI tool call, signed with that tool's secret over t.METHOD.path.body"),
            [ChatlySignatureSurfaces.CanvasStep] = new(
                "x-chatly-canvas-signature", "x-chatly-canvas-ts", false, false, false, ChatlySignatureBase.Body, false,
                "canvas / interactive-step callback, timestamp in MILLISECONDS"),
            [ChatlySignatureSurfaces.CustomerCard] = new(
                "x-chatly-signature", "x-chatly-timestamp", false, false, false, ChatlySignatureBase.Body, false,
                "customer-card fetch, timestamp in MILLISECONDS"),
            [ChatlySignatureSurfaces.WorkflowWebhook] = new(
                ChatlyWebhooks.ChatlyV2SignatureHeader, null, true, true, true, ChatlySignatureBase.Body, true,
                "workflow \"call a webhook\" action, replay-bounded v2 header"),
            [ChatlySignatureSurfaces.WorkflowWebhookLegacy] = new(
                "x-chatly-signature", null, false, false, null, ChatlySignatureBase.BodyOnly, false,
                "workflow action, LEGACY header over the body alone — no replay protection"),
        };
}
