using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Chatly;

// ═══ ONE VERIFIER FOR EVERY SIGNATURE CHATLY PUTS ON AN OUTBOUND REQUEST ═══
//
// A port of `verifyChatlySignature` in `packages/sdk-node/src/signatures.ts`.
// The surfaces, reasons, input and result are `ChatlySignatureTypes.cs`.
//
// It lives on `ChatlyWebhooks` (a partial class — `Webhooks.cs` holds the
// original event-webhook verifier, whose behaviour this file does not touch)
// because that is where a .NET integrator already looks. `VerifySignature`
// stays: it is the narrow, boolean, event-webhook-only check and it is not
// wrong. This is the one that covers all eight surfaces and says WHY.
//
// ═══ PLACES WHERE .NET WOULD HAVE QUIETLY DISAGREED WITH THE REFERENCE ════
//
// Each of these passes a casual reading and fails the shared corpus or a
// hostile input, which is why they are written out rather than left to the BCL:
//
//   * `Regex` `\d` matches every Unicode digit (Arabic-Indic, fullwidth...) and
//     `$` matches BEFORE a trailing "\n". JavaScript's do neither. Both checks
//     here are hand-written loops over ASCII instead, so `"١٧٦٠"` is malformed
//     in .NET exactly as in Node, and the digest check does not lean on the
//     trim having already removed a trailing newline.
//   * `string.Trim()` uses `char.IsWhiteSpace`, which includes U+0085 and
//     excludes U+FEFF; JavaScript's `trim()` is the other way round on both.
//     `JsTrim` reproduces the ECMAScript set.
//   * A timestamp is parsed into a `long`. Node reads digits into a double and
//     a 25-digit value comes out "finite" and far in the future; here it does
//     not fit, and the spec says that is `malformed_timestamp`, as is a seconds
//     value whose ×1000 overflows. The decision table is unchanged for every
//     value a real sender can produce.
//   * The comparison is `CryptographicOperations.FixedTimeEquals`, which —
//     unlike Node's `timingSafeEqual` — does not throw on a length mismatch,
//     but lengths are still compared first so the reasoning ports unchanged.
//
// ═══ WHY IT NEVER THROWS ══════════════════════════════════════════════════
//
// CONTRACT.md §6: "a verifier that throws is an accepted webhook in a handler
// that forgot to catch". Every path returns a result, and a catch-all around
// the whole thing maps anything unforeseen to `signature_mismatch` — the
// refusal that cannot be read as acceptance.
public static partial class ChatlyWebhooks
{
    /// <summary>
    /// The default freshness window for <see cref="VerifyChatlySignature"/>:
    /// five minutes, matching the producing side.
    /// </summary>
    public const int ChatlySignatureToleranceSeconds = 300;

    /// <summary>The header the unified v2 construction travels in, on every surface.</summary>
    public const string ChatlyV2SignatureHeader = "x-chatly-signature-v2";

    /// <summary>
    /// Verify one signed request from Chatly, on any of the eight surfaces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never throws. Every failure — an unknown surface, a missing or doubled
    /// header, a non-hex digest, a stale or far-future timestamp, a wrong
    /// secret, a null anything — comes back with <see cref="ChatlySignatureResult.Ok"/>
    /// false, a stable <see cref="ChatlySignatureResult.Reason"/> and a
    /// printable <see cref="ChatlySignatureResult.Message"/>.
    /// </para>
    /// <para>
    /// Key rotation works from both ends: <see cref="ChatlySignatureInput.Secrets"/>
    /// takes every live secret and the result names which one verified, and a
    /// <c>t=…,v1=…,v1=…</c> header with two digests verifies when either matches.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var result = ChatlyWebhooks.VerifyChatlySignature(new ChatlySignatureInput
    /// {
    ///     Surface = ChatlySignatureSurfaces.EventWebhook,
    ///     Body = rawBytes,
    ///     Headers = ChatlySignatureInput.HeadersFrom(request.Headers),
    ///     Secret = secret,
    ///     Method = request.Method,
    ///     Path = request.Path + request.QueryString,
    /// });
    /// if (!result.Ok) { logger.LogWarning("{Reason}: {Message}", result.Reason, result.Message); }
    /// </code>
    /// </example>
    /// <param name="input">The request to verify.</param>
    /// <returns>The verdict; never null.</returns>
    public static ChatlySignatureResult VerifyChatlySignature(ChatlySignatureInput input)
    {
        try
        {
            return VerifyCore(input);
        }
#pragma warning disable CA1031 // The whole point: no exception may escape a verifier.
        catch (Exception)
#pragma warning restore CA1031
        {
            return ChatlySignatureResult.Failure(
                input?.Surface ?? string.Empty,
                ChatlySignatureFailure.SignatureMismatch,
                "The request could not be verified (an unexpected input shape was refused rather "
                + "than accepted). Pass the raw body bytes, the request headers and this surface's secret.");
        }
    }

    private static ChatlySignatureResult VerifyCore(ChatlySignatureInput? input)
    {
        var surface = input?.Surface ?? string.Empty;
        if (input is null || !ChatlySurfaceSpec.Table.TryGetValue(surface, out var spec))
        {
            return Fail(
                surface,
                ChatlySignatureFailure.UnknownSurface,
                $"\"{(input?.Surface is null ? "null" : surface)}\" is not a Chatly signature surface. "
                + $"One of: {string.Join(", ", ChatlySignatureSurfaces.All)}.");
        }

        var secrets = SigSecretsFrom(input, out var secretProblem);
        if (secrets.Count == 0)
        {
            return Fail(
                surface,
                ChatlySignatureFailure.MissingSecret,
                secretProblem
                ?? $"No secret given for {surface}. Each surface has its own secret — {spec.Describe}. "
                   + "A secret from another surface will never verify.");
        }

        if (surface == ChatlySignatureSurfaces.WorkflowWebhookLegacy && !input.AllowUnprotectedReplay)
        {
            return Fail(
                surface,
                ChatlySignatureFailure.ReplayUnprotected,
                "The legacy workflow header signs the body with no timestamp, so a captured request "
                + "verifies forever and nothing here can bound a replay. The same call also sends "
                + "x-chatly-signature-v2 — verify that instead, with surface \"workflow_webhook\". "
                + "If you must accept the legacy header, set AllowUnprotectedReplay = true and make "
                + "the endpoint idempotent.");
        }

        var viaV2 = SigApplyV2Policy(input, surface, spec);
        if (viaV2 is not null)
        {
            return viaV2;
        }

        // ── The digest header ───────────────────────────────────────────────
        var sigHeaders = SigHeaderValues(input.Headers, spec.SignatureHeader);
        if (sigHeaders.Count == 0)
        {
            return Fail(
                surface,
                ChatlySignatureFailure.MissingSignatureHeader,
                $"No {spec.SignatureHeader} header. {Capitalise(spec.Describe)} always sends one; "
                + "if the request carries a differently named signature header, you have the wrong surface.");
        }

        if (sigHeaders.Count > 1)
        {
            // Two values means something between us and you merged or injected
            // one, and picking either is a guess. Refuse rather than trial-verify.
            return Fail(
                surface,
                ChatlySignatureFailure.DuplicateSignatureHeader,
                $"{sigHeaders.Count} {spec.SignatureHeader} headers arrived. Chatly sends exactly one; "
                + "a proxy merged or added one, and choosing between them would be a guess.");
        }

        var rawSignature = JsTrim(sigHeaders[0]);

        List<string> digestsHex;
        string? rawTimestamp = null;
        string? marker = null;
        if (spec.Tv1)
        {
            if (!SigParseTv1(rawSignature, out var t, out var v1, out var b))
            {
                return Fail(
                    surface,
                    ChatlySignatureFailure.MalformedSignatureHeader,
                    $"{spec.SignatureHeader} should be \"t=<unix seconds>,v1=<hex>\" on {surface}; "
                    + $"got \"{Preview(rawSignature)}\".");
            }

            digestsHex = v1;
            rawTimestamp = t;
            marker = b;
        }
        else
        {
            if (!rawSignature.StartsWith("sha256=", StringComparison.Ordinal))
            {
                return Fail(
                    surface,
                    ChatlySignatureFailure.MalformedSignatureHeader,
                    $"{spec.SignatureHeader} should be \"sha256=<hex>\" on {surface}; "
                    + $"got \"{Preview(rawSignature)}\". A \"t=…,v1=…\" value here means you have the "
                    + "wrong surface — that is the other signature family.");
            }

            digestsHex = new List<string> { JsTrim(rawSignature.Substring("sha256=".Length)) };
        }

        foreach (var digestHex in digestsHex)
        {
            // Shape-checked BEFORE decoding, so a garbage digest is reported as
            // malformed rather than reaching the compare and reading as a
            // mismatched signature — which sends the integrator hunting their secret.
            if (!SigIsHexSha256(digestHex))
            {
                return Fail(
                    surface,
                    ChatlySignatureFailure.MalformedSignatureHeader,
                    $"A digest in {spec.SignatureHeader} is not 64 hex characters (got {digestHex.Length})."
                    + (digestsHex.Count > 1
                        ? $" {digestsHex.Count} v1 values arrived; every one has to be well formed."
                        : string.Empty));
            }
        }

        // ── The timestamp, and its unit ─────────────────────────────────────
        long? timestampMs = null;
        if (spec.Seconds is bool seconds)
        {
            if (spec.TimestampHeader is not null)
            {
                var tsHeaders = SigHeaderValues(input.Headers, spec.TimestampHeader);
                if (tsHeaders.Count == 0)
                {
                    return Fail(
                        surface,
                        ChatlySignatureFailure.MissingTimestamp,
                        $"No {spec.TimestampHeader} header. On {surface} the timestamp travels in its own "
                        + "header and is part of the signed string, so it cannot be reconstructed.");
                }

                rawTimestamp = JsTrim(tsHeaders[0]);
            }

            if (string.IsNullOrEmpty(rawTimestamp))
            {
                return Fail(surface, ChatlySignatureFailure.MissingTimestamp, $"No timestamp found for {surface}.");
            }

            // ASCII digits only, by hand: `long.TryParse` would accept a sign,
            // and `Regex` `\d` would accept any Unicode digit.
            if (!SigIsAsciiDigits(rawTimestamp))
            {
                return Fail(
                    surface,
                    ChatlySignatureFailure.MalformedTimestamp,
                    $"The timestamp on {surface} is not a run of digits: \"{Preview(rawTimestamp)}\".");
            }

            if (!long.TryParse(rawTimestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                || (seconds && n > long.MaxValue / 1000))
            {
                return Fail(
                    surface,
                    ChatlySignatureFailure.MalformedTimestamp,
                    $"The timestamp on {surface} is too large to be a time: \"{Preview(rawTimestamp)}\".");
            }

            timestampMs = seconds ? n * 1000 : n;

            var refusal = SigCheckTolerance(input, surface, seconds, timestampMs.Value);
            if (refusal is not null)
            {
                return refusal;
            }
        }

        // ── The base string, and the compare ────────────────────────────────
        var bases = SigCandidateBases(input, surface, spec, marker, out var markerRefusal);
        if (markerRefusal is not null)
        {
            return markerRefusal;
        }

        if (bases.Contains(ChatlySignatureBase.Target) && !SigHasTarget(input))
        {
            return Fail(
                surface,
                ChatlySignatureFailure.MissingRequestTarget,
                $"{surface} signs <t>.<METHOD>.<path>.<rawBody>, so both Method and Path are required. "
                + "Path is pathname + search of the request as Chatly rendered it — not the host, not the scheme.");
        }

        byte[] bodyBytes;
        if (input.Body is not null && input.BodyText is not null)
        {
            return Fail(
                surface,
                ChatlySignatureFailure.SignatureMismatch,
                "Both Body and BodyText were set. Set exactly one — the raw bytes received, preferably.");
        }
        else if (input.Body is not null)
        {
            bodyBytes = input.Body;
        }
        else if (input.BodyText is not null)
        {
            bodyBytes = Encoding.UTF8.GetBytes(input.BodyText);
        }
        else
        {
            // Null is not empty: a bodiless GET is `Body = Array.Empty<byte>()`.
            // A null here is almost always a body that was never read.
            return Fail(
                surface,
                ChatlySignatureFailure.SignatureMismatch,
                "No body was given. Pass the raw request bytes as Body (an empty array for a bodiless "
                + "request); a body that was never read cannot verify.");
        }

        var given = new List<byte[]>(digestsHex.Count);
        foreach (var hex in digestsHex)
        {
            given.Add(Convert.FromHexString(hex));
        }

        foreach (var @base in bases)
        {
            var prefix = Encoding.UTF8.GetBytes(SigBaseStringPrefix(@base, rawTimestamp, input));
            var message = new byte[prefix.Length + bodyBytes.Length];
            prefix.CopyTo(message, 0);
            bodyBytes.CopyTo(message, prefix.Length);

            foreach (var (secret, index) in secrets)
            {
                var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), message);
                foreach (var candidate in given)
                {
                    // Lengths first, then constant time. FixedTimeEquals would
                    // return false on a length mismatch rather than throw, but
                    // the reference's ordering is kept so a reader of either
                    // sees the same reasoning.
                    if (candidate.Length == expected.Length
                        && CryptographicOperations.FixedTimeEquals(candidate, expected))
                    {
                        return ChatlySignatureResult.Success(surface, timestampMs, @base, index);
                    }
                }
            }
        }

        return Fail(
            surface,
            ChatlySignatureFailure.SignatureMismatch,
            $"The digest does not match for {surface}. The three usual causes, in order: a "
            + "re-serialised body (pass the RAW bytes, not a re-serialised model), the "
            + $"secret from a different surface, or the wrong surface — {spec.Describe}."
            + (secrets.Count > 1 ? $" None of the {secrets.Count} secrets matched." : string.Empty));
    }

    /// <summary>
    /// The v2 decision, taken before anything is parsed. Returns a result when
    /// it has answered the whole question — by delegating to <c>unified_v2</c>
    /// or by refusing — and null when verification continues on the surface asked for.
    /// </summary>
    private static ChatlySignatureResult? SigApplyV2Policy(
        ChatlySignatureInput input,
        string surface,
        ChatlySurfaceSpec spec)
    {
        // A surface whose OWN header is the v2 header handles both bases in its
        // candidate list instead; there is nothing to delegate to.
        if (input.V2 == ChatlyV2Policy.Ignore
            || surface == ChatlySignatureSurfaces.UnifiedV2
            || spec.SignatureHeader == ChatlyV2SignatureHeader)
        {
            return null;
        }

        var present = SigHeaderValues(input.Headers, ChatlyV2SignatureHeader).Count > 0;
        if (input.V2 == ChatlyV2Policy.Require && !present)
        {
            return Fail(
                surface,
                ChatlySignatureFailure.MissingSignatureHeader,
                $"V2 = Require was asked for and this request carries no {ChatlyV2SignatureHeader} "
                + $"header. {spec.SignatureHeader} alone means the sender has not moved to the unified "
                + "construction yet — use the default V2 = Prefer until it has.");
        }

        if (!present)
        {
            return null;
        }

        if (!SigHasTarget(input))
        {
            if (input.V2 == ChatlyV2Policy.Require)
            {
                return Fail(
                    surface,
                    ChatlySignatureFailure.MissingRequestTarget,
                    $"The unified {ChatlyV2SignatureHeader} signs <t>.<METHOD>.<path>.<rawBody>, so "
                    + "both Method and Path are required to verify it. Path is pathname + search of the "
                    + "request as it arrived — not the host, not the scheme.");
            }

            // Prefer, with no target to verify against: the legacy header is
            // still sent beside it, so fall through rather than refuse.
            return null;
        }

        // Once v2 is chosen it is the ONLY answer. A good legacy digest beside a
        // bad v2 one is tampering or a sender bug, and accepting it hides both.
        var result = VerifyCore(input with { Surface = ChatlySignatureSurfaces.UnifiedV2, V2 = ChatlyV2Policy.Ignore });
        if (result.Ok)
        {
            return result;
        }

        return Fail(
            ChatlySignatureSurfaces.UnifiedV2,
            result.Reason ?? ChatlySignatureFailure.SignatureMismatch,
            $"{result.Message} (This request carried {ChatlyV2SignatureHeader}, so it was verified "
            + $"as the unified v2 construction rather than as {surface}. Set V2 = Ignore to verify "
            + $"{spec.SignatureHeader} instead.)");
    }

    /// <summary>
    /// Which strings the MAC may have been taken over, most specific first — or
    /// a refusal (in <paramref name="refusal"/>) when the <c>b=</c> marker rules
    /// every candidate out.
    /// </summary>
    /// <remarks>
    /// The marker is read ONLY off <c>x-chatly-signature-v2</c>, and it NARROWS
    /// the candidates; it never widens them past what the surface and the policy
    /// allow, because a marker is something anyone on the wire can write. An
    /// absent or unrecognised marker (<c>b=z</c>) changes nothing: every v2 value
    /// minted before the marker existed is absent, and keeps verifying as it did.
    /// </remarks>
    private static List<string> SigCandidateBases(
        ChatlySignatureInput input,
        string surface,
        ChatlySurfaceSpec spec,
        string? marker,
        out ChatlySignatureResult? refusal)
    {
        refusal = null;
        var policy = input.V2;
        var fromV2 = spec.SignatureHeader == ChatlyV2SignatureHeader;
        if (fromV2 && marker == "b" && (spec.Base == ChatlySignatureBase.Target || policy == ChatlyV2Policy.Require))
        {
            refusal = Fail(
                surface,
                ChatlySignatureFailure.SignatureMismatch,
                $"This {ChatlyV2SignatureHeader} is marked b=b: it was signed over <t>.<rawBody>, the "
                + "workflow action's pre-unified base. "
                + (surface == ChatlySignatureSurfaces.UnifiedV2
                    ? "unified_v2 accepts only b=t. Verify it with surface \"workflow_webhook\", which accepts both."
                    : "V2 = Require accepts only the unified base (b=t)."));
            return new List<string>();
        }

        if (!spec.AlsoAcceptsTargetBase || policy == ChatlyV2Policy.Ignore)
        {
            return new List<string> { spec.Base };
        }

        if (marker == "t")
        {
            return new List<string> { ChatlySignatureBase.Target };
        }

        if (marker == "b" || !SigHasTarget(input))
        {
            return new List<string> { spec.Base };
        }

        // Require on this surface means the unified base and nothing else.
        return policy == ChatlyV2Policy.Require
            ? new List<string> { ChatlySignatureBase.Target }
            : new List<string> { ChatlySignatureBase.Target, spec.Base };
    }

    private static ChatlySignatureResult? SigCheckTolerance(
        ChatlySignatureInput input,
        string surface,
        bool seconds,
        long signedMs)
    {
        var toleranceSeconds = input.ToleranceSeconds ?? ChatlySignatureToleranceSeconds;
        if (double.IsPositiveInfinity(toleranceSeconds))
        {
            return null;
        }

        var nowMs = input.NowMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Decimal, because `nowMs - signedMs` in longs overflows for a hostile
        // `t` near long.MaxValue, and a double loses the millisecond that
        // decides "exactly 300s is allowed".
        var driftMs = Math.Abs((decimal)nowMs - signedMs);
        var limitMs = toleranceSeconds * 1000;

        // NaN would compare false against everything and so ACCEPT everything;
        // the reference has that hole, this refuses instead. A limit beyond
        // decimal's range is wider than any representable drift. A NEGATIVE
        // limit refuses before the cast: `(decimal)` of -Infinity or -1e30
        // throws OverflowException, which the catch-all above turned into
        // signature_mismatch — "check your secret" for what is a bad
        // tolerance. Any negative window is narrower than a drift of zero.
        bool outside = double.IsNaN(limitMs)
            || limitMs < 0
            || (limitMs < 7.9e27 && driftMs > (decimal)limitMs);
        if (!outside)
        {
            return null;
        }

        // Both directions are refused, and the message says which. A FAR-FUTURE
        // timestamp is a sender clock or — far more often — the wrong unit, which
        // lands ~54,000 years out one way or ~54 years the other and reads as
        // "expired" unless something says so.
        var driftYears = (double)driftMs / (1000d * 60 * 60 * 24 * 365);
        var unitHint = driftYears > 1
            ? $" That is {driftYears.ToString("F0", CultureInfo.InvariantCulture)} year(s) out, which is what "
              + $"a wrong UNIT looks like: {surface} sends {(seconds ? "seconds" : "milliseconds")}."
            : string.Empty;
        var driftSeconds = Math.Round(driftMs / 1000, MidpointRounding.AwayFromZero);
        return Fail(
            surface,
            ChatlySignatureFailure.TimestampOutsideTolerance,
            $"The timestamp on {surface} is {driftSeconds.ToString(CultureInfo.InvariantCulture)}s "
            + $"{(signedMs > nowMs ? "in the FUTURE" : "in the past")}, outside the "
            + $"{toleranceSeconds.ToString(CultureInfo.InvariantCulture)}s tolerance.{unitHint}");
    }

    private static ChatlySignatureResult Fail(string surface, string reason, string message)
        => ChatlySignatureResult.Failure(surface, reason, message);
}
