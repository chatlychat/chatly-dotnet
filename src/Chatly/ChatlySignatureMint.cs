using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Chatly;

// ═══ MINTING A v2 SIGNATURE — THE OTHER DIRECTION FROM VerifyChatlySignature ═
//
// A port of `signChatlyV2` / `chatlyV2BaseString` in
// `packages/sdk-node/src/signature-mint.ts`, which is the reference: where this
// file and that one disagree about the bytes, that one is right and this one has
// a bug. `tests/Checks/SignatureMintChecks.cs` pins both digests against values
// the REFERENCE produced rather than against this code, because a mint asserted
// against its own base-string construction agrees with a wrong construction and
// the pair would pass every check while production refused every delivery. That
// is not hypothetical: `packages/shared` once emitted a header in milliseconds
// over a seconds value, and a verifier following our own published docs refused
// 100% of real traffic.
//
// Verifying is what a customer's handler does in production and it lives in
// `ChatlySignatures.cs`. Minting is what a TEST does — standing up a fake Chatly
// and firing a signed request at your own endpoint — and until this existed a
// .NET integrator writing that test had to re-derive
// `<t>.<METHOD>.<path>.<rawBody>` by hand from the documentation. A hand-derived
// base string is the one thing a signature test must not have.
//
// ═══ IT THROWS, WHERE THE VERIFIER NEVER DOES ════════════════════════════
//
// `VerifyChatlySignature` never throws because a verifier that throws is an
// accepted webhook in a handler that forgot to catch. Nothing of that applies in
// this direction: a mint that quietly signed the wrong string would be a test
// that passes while production refuses every delivery, so every input it cannot
// sign faithfully is an `ArgumentException` instead.
//
// ═══ NOT FOR SIGNING REAL OUTBOUND TRAFFIC ════════════════════════════════
//
// Nothing in this package sends a signed request to anyone; Chatly's own
// producers are `packages/shared/src/webhook-signature/v2.ts`. This is here so a
// test can mint what Chatly would send.
public static partial class ChatlyWebhooks
{
    /// <summary>
    /// Mint an <c>x-chatly-signature-v2</c> value, for a test that stands up a
    /// fake Chatly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Emits <c>b=t</c> for the unified base and <c>b=b</c> for the body base,
    /// and never an absent marker. The one wire value it cannot produce on its
    /// own is the workflow action's current DUAL-digest form,
    /// <c>t=&lt;T&gt;,v1=&lt;target&gt;,v1=&lt;body&gt;</c> with no marker: that
    /// is built from two mints by taking the <c>v1=</c> field out of each, which
    /// is exactly what the producer does (<c>digestOf</c> in
    /// <c>apps/worker/src/handlers/action-handlers/fire-webhook.ts</c>).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var header = ChatlyWebhooks.SignChatlyV2(new ChatlyV2MintInput
    /// {
    ///     Base = ChatlyV2MintBase.Target,
    ///     Secret = secret,
    ///     TimestampSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
    ///     Method = "POST",
    ///     Path = "/hooks/chatly?run=7",   // pathname + query, never the host
    ///     Body = rawBody,
    /// });
    /// // t=1760000000,v1=&lt;64 hex&gt;,b=t
    /// </code>
    /// </example>
    /// <param name="input">What to sign.</param>
    /// <returns>The header value, <c>t=…,v1=…,b=…</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The input cannot be signed faithfully — an unknown base, an empty or
    /// missing secret, a negative timestamp, no body, both <c>Body</c> and
    /// <c>BodyText</c>, or <see cref="ChatlyV2MintBase.Target"/> with no method
    /// or path. Each would otherwise mint a digest the receiver cannot
    /// reproduce, and "signature mismatch" is the least useful place to discover
    /// it.
    /// </exception>
    public static string SignChatlyV2(ChatlyV2MintInput input)
    {
        var message = ChatlyV2BaseString(input);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(input.Secret!));
        var digest = Convert.ToHexString(hmac.ComputeHash(message)).ToLowerInvariant();
        var marker = input.Base == ChatlyV2MintBase.Target ? "t" : "b";
        return "t=" + input.TimestampSeconds.ToString(CultureInfo.InvariantCulture)
            + ",v1=" + digest + ",b=" + marker;
    }

    /// <summary>
    /// The exact bytes <see cref="SignChatlyV2"/> takes the MAC over, so a test
    /// can pin the construction itself and not only the digest.
    /// </summary>
    /// <remarks>
    /// <c>&lt;t&gt;.&lt;METHOD&gt;.&lt;path&gt;.&lt;rawBody&gt;</c> for
    /// <see cref="ChatlyV2MintBase.Target"/> and
    /// <c>&lt;t&gt;.&lt;rawBody&gt;</c> for <see cref="ChatlyV2MintBase.Body"/>,
    /// with the method upper-cased exactly as the verifier upper-cases it.
    /// Bytes rather than a string, because a body is not always valid UTF-8
    /// text.
    /// </remarks>
    /// <param name="input">What to sign.</param>
    /// <returns>The signed bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    /// <exception cref="ArgumentException">See <see cref="SignChatlyV2"/>.</exception>
    public static byte[] ChatlyV2BaseString(ChatlyV2MintInput input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (input.Base is not (ChatlyV2MintBase.Target or ChatlyV2MintBase.Body))
        {
            throw new ArgumentException(
                $"SignChatlyV2: Base must be Target (<t>.<METHOD>.<path>.<body>) or Body (<t>.<body>), not {(int)input.Base}. "
                + "There is no default: the two are indistinguishable without the b= marker this emits.",
                nameof(input));
        }

        if (string.IsNullOrEmpty(input.Secret))
        {
            throw new ArgumentException(
                "SignChatlyV2: Secret is required and must not be empty. An empty string is a perfectly valid "
                + "HMAC key, which is the problem — the likeliest cause is a configuration value that never got set.",
                nameof(input));
        }

        if (input.TimestampSeconds < 0)
        {
            throw new ArgumentException(
                "SignChatlyV2: TimestampSeconds must not be negative — unix SECONDS, not milliseconds. Two "
                + "surfaces send millisecond timestamps in their LEGACY headers; the v2 header is seconds on "
                + "every one of them.",
                nameof(input));
        }

        // Last, so a caller who got several things wrong hears about the base, the
        // secret and the timestamp before the body shape.
        var body = MintBody(input);

        var timestamp = input.TimestampSeconds.ToString(CultureInfo.InvariantCulture);
        string prefix;
        if (input.Base == ChatlyV2MintBase.Body)
        {
            // Method and Path are IGNORED here rather than refused, as the
            // reference documents: a harness that sets every field once and
            // switches only the base has to keep minting the same value.
            prefix = timestamp + ".";
        }
        else
        {
            if (string.IsNullOrEmpty(input.Method))
            {
                throw new ArgumentException(
                    "SignChatlyV2: ChatlyV2MintBase.Target signs the METHOD, so Method is required.",
                    nameof(input));
            }

            if (string.IsNullOrEmpty(input.Path))
            {
                throw new ArgumentException(
                    "SignChatlyV2: ChatlyV2MintBase.Target signs the path, so Path is required — pathname + "
                    + "query of the request line, not the scheme or host.",
                    nameof(input));
            }

            // Upper-cased because the VERIFIER upper-cases it (`SigBaseStringPrefix`);
            // a lower-case method in a mint would otherwise produce a value the
            // verifier refuses.
            prefix = timestamp + "." + input.Method.ToUpperInvariant() + "." + input.Path + ".";
        }

        var head = Encoding.UTF8.GetBytes(prefix);
        var buffer = new byte[head.Length + body.Length];
        head.CopyTo(buffer.AsSpan());
        body.CopyTo(buffer.AsSpan(head.Length));
        return buffer;
    }

    /// <summary>The body as bytes, with the same one-of-each-pair rule as the verifier's input.</summary>
    private static ReadOnlySpan<byte> MintBody(ChatlyV2MintInput input)
    {
        if (input.Body is not null && input.BodyText is not null)
        {
            throw new ArgumentException(
                "SignChatlyV2: set Body or BodyText, not both — a caller who set both meant something, and "
                + "silently signing one of them is the kind of bug that surfaces as \"the digest never matches\".",
                nameof(input));
        }

        if (input.Body is not null)
        {
            return input.Body;
        }

        if (input.BodyText is not null)
        {
            return Encoding.UTF8.GetBytes(input.BodyText);
        }

        throw new ArgumentException(
            "SignChatlyV2: Body or BodyText is required. An empty body is an empty array, not null.",
            nameof(input));
    }
}

/// <summary>Which string a v2 signature is taken over.</summary>
/// <remarks>
/// There is deliberately no "unspecified" member and no sensible default: the
/// two constructions are indistinguishable from the value alone, which is
/// exactly why the <c>b=</c> marker exists, so a caller has to say which one
/// they mean. An ABSENT marker means <see cref="Body"/> on the wire, because
/// every v2 signature minted before the marker existed was that construction;
/// <see cref="ChatlyWebhooks.SignChatlyV2"/> never emits an absent one.
/// </remarks>
public enum ChatlyV2MintBase
{
    /// <summary><c>&lt;t&gt;.&lt;METHOD&gt;.&lt;path&gt;.&lt;rawBody&gt;</c>, the unified base. Emitted as <c>b=t</c>.</summary>
    Target = 0,

    /// <summary><c>&lt;t&gt;.&lt;rawBody&gt;</c>, the workflow action's pre-unified base. Emitted as <c>b=b</c>.</summary>
    Body = 1,
}

/// <summary>
/// What to mint an <c>x-chatly-signature-v2</c> value over. See
/// <see cref="ChatlyWebhooks.SignChatlyV2"/>.
/// </summary>
/// <remarks>
/// A record with named properties rather than six positional arguments: the
/// construction is <c>&lt;t&gt;.&lt;METHOD&gt;.&lt;path&gt;.&lt;rawBody&gt;</c>,
/// three strings in a row, and a positional
/// <c>SignChatlyV2(secret, t, method, path, body)</c> compiles with
/// <c>path</c> and <c>body</c> swapped. It then mints a digest nobody can
/// reproduce, which reaches the receiver as "signature mismatch" and sends
/// whoever wrote it hunting their own secret. This is the same reason
/// <see cref="ChatlySignatureInput"/> is a record.
/// </remarks>
public sealed record ChatlyV2MintInput
{
    /// <summary>Which string to sign. Required — see <see cref="ChatlyV2MintBase"/>.</summary>
    public ChatlyV2MintBase Base { get; init; }

    /// <summary>The surface's signing secret. Required, and an empty one is refused.</summary>
    public string? Secret { get; init; }

    /// <summary>
    /// Unix <b>seconds</b>, never milliseconds. A millisecond value signs a
    /// <c>t</c> the receiver reads as the year 55,000 and refuses as
    /// <c>timestamp_outside_tolerance</c>.
    /// </summary>
    public long TimestampSeconds { get; init; }

    /// <summary>The exact bytes that will be sent as the body. Set this or <see cref="BodyText"/>, not both.</summary>
    public byte[]? Body { get; init; }

    /// <summary>The body as text, encoded UTF-8 — the MAC is always over bytes. Prefer <see cref="Body"/>.</summary>
    public string? BodyText { get; init; }

    /// <summary>
    /// The HTTP method, upper-cased before signing. Required for
    /// <see cref="ChatlyV2MintBase.Target"/>, ignored for
    /// <see cref="ChatlyV2MintBase.Body"/>.
    /// </summary>
    public string? Method { get; init; }

    /// <summary>
    /// <c>pathname + query</c> of the request line — never the scheme, never the
    /// host. Required for <see cref="ChatlyV2MintBase.Target"/>, ignored for
    /// <see cref="ChatlyV2MintBase.Body"/>.
    /// </summary>
    public string? Path { get; init; }
}
