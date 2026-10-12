using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Chatly;

/// <summary>
/// Verification for the signature header on an outbound webhook delivery.
/// </summary>
/// <remarks>
/// <para>
/// The header is <c>x-livechat-signature</c> and looks like
/// <c>t=1700000000,v1=&lt;hex&gt;</c>. <c>v1</c> is HMAC-SHA256 over the bytes
/// <c>&lt;t&gt;.&lt;raw body&gt;</c> — the timestamp is bound <i>into</i> the
/// digest, which is what makes the replay window enforceable at all.
/// </para>
/// <para>
/// <b>The header name is a trap worth knowing.</b>
/// <c>x-chatly-signature</c> is also real, but it belongs to four other
/// surfaces — external-agent deliveries, custom AI tool calls, customer cards
/// and the workflow <c>fire_webhook</c> action — and each constructs its base
/// string differently. Outbound event webhooks, the ones this class verifies,
/// use <c>x-livechat-signature</c>. Do not share one verifier across them
/// without checking the construction.
/// </para>
/// </remarks>
public static class ChatlyWebhooks
{
    /// <summary>
    /// The header carrying the signature on an outbound event webhook.
    /// </summary>
    public const string SignatureHeaderName = "x-livechat-signature";

    /// <summary>
    /// How far the delivery's <c>t</c> may be from now before it is treated as
    /// a replay: five minutes.
    /// </summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromSeconds(300);

    /// <summary>
    /// Pass as <c>tolerance</c> to skip the freshness check entirely — only
    /// when re-verifying an archived delivery, never on a live endpoint.
    /// </summary>
    public static readonly TimeSpan InfiniteTolerance = TimeSpan.MaxValue;

    /// <summary>
    /// Verify a delivery against the raw bytes that arrived.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pass the raw request body.</b> Re-serialising parsed JSON reorders
    /// keys and changes whitespace, and the digest will not match. In ASP.NET
    /// Core that means reading <c>HttpRequest.Body</c> yourself rather than
    /// binding a model.
    /// </para>
    /// <para>
    /// This never throws: a malformed header, a missing field, a non-numeric
    /// <c>t</c>, a stale timestamp, a bad hex digest, an empty secret and a
    /// null header all return false. A verifier that throws is an accepted
    /// webhook in a handler that forgot to catch.
    /// </para>
    /// </remarks>
    /// <param name="body">The exact request-body bytes received.</param>
    /// <param name="signatureHeader">
    /// The <c>x-livechat-signature</c> header value, or null if it was absent.
    /// </param>
    /// <param name="secret">The endpoint's <c>whsec_…</c> signing secret.</param>
    /// <param name="tolerance">
    /// Freshness window; defaults to <see cref="DefaultTolerance"/>. Pass
    /// <see cref="InfiniteTolerance"/> to skip the check.
    /// </param>
    /// <param name="now">
    /// Treated as the current time. Defaults to <see cref="DateTimeOffset.UtcNow"/>;
    /// pass a fixed value to verify a recorded delivery.
    /// </param>
    /// <returns>True only when the signature is genuine and fresh.</returns>
    public static bool VerifySignature(
        ReadOnlySpan<byte> body,
        string? signatureHeader,
        string secret,
        TimeSpan? tolerance = null,
        DateTimeOffset? now = null)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signatureHeader))
        {
            return false;
        }

        if (!TryParseHeader(signatureHeader!, out var timestampText, out var signatureHex))
        {
            return false;
        }

        if (!long.TryParse(timestampText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var issuedAt))
        {
            return false;
        }

        var window = tolerance ?? DefaultTolerance;
        if (window != InfiniteTolerance)
        {
            if (window < TimeSpan.Zero)
            {
                return false;
            }

            var current = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
            // Longs, not TimeSpan arithmetic: a `t` of long.MinValue would
            // overflow a TimeSpan and throw out of a verifier that must not.
            var skew = current > issuedAt ? current - issuedAt : issuedAt - current;
            if (skew < 0 || skew > (long)window.TotalSeconds)
            {
                return false;
            }
        }

        if (!TryDecodeHex(signatureHex, out var provided))
        {
            return false;
        }

        var expected = ComputeDigest(timestampText, body, secret);

        // Lengths first. FixedTimeEquals returns false on a length mismatch,
        // but comparing lengths up front keeps that contract explicit — and in
        // languages where the constant-time compare throws instead, this is
        // the line that stops a thrown verifier from becoming an accepted
        // webhook.
        return provided.Length == expected.Length && CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    /// <summary>
    /// Verify a delivery whose body you hold as a string.
    /// </summary>
    /// <remarks>
    /// The string is encoded as UTF-8 and verified as those bytes. Prefer the
    /// <see cref="ReadOnlySpan{T}"/> overload when you have the bytes: a
    /// decode-then-encode round trip is only lossless if the body really was
    /// UTF-8.
    /// </remarks>
    /// <param name="body">The exact body text received.</param>
    /// <param name="signatureHeader">The <c>x-livechat-signature</c> header value.</param>
    /// <param name="secret">The endpoint's <c>whsec_…</c> signing secret.</param>
    /// <param name="tolerance">Freshness window; defaults to <see cref="DefaultTolerance"/>.</param>
    /// <param name="now">Treated as the current time.</param>
    /// <returns>True only when the signature is genuine and fresh.</returns>
    public static bool VerifySignature(
        string body,
        string? signatureHeader,
        string secret,
        TimeSpan? tolerance = null,
        DateTimeOffset? now = null)
        => VerifySignature(
            Encoding.UTF8.GetBytes(body ?? string.Empty),
            signatureHeader,
            secret,
            tolerance,
            now);

    /// <summary>
    /// Produce the signature header for a body — the exact construction the
    /// platform signs with.
    /// </summary>
    /// <remarks>
    /// Here so that a test of your own handler can build a genuine delivery
    /// without reimplementing the scheme. The platform signs; you verify. An
    /// earlier Node implementation HMAC'd the body alone and so rejected 100%
    /// of genuine deliveries, which is the failure this method makes visible.
    /// </remarks>
    /// <param name="body">The body bytes to sign.</param>
    /// <param name="secret">The endpoint's signing secret.</param>
    /// <param name="timestamp">The timestamp to bind into the digest.</param>
    /// <returns>A header value of the form <c>t=…,v1=…</c>.</returns>
    public static string Sign(ReadOnlySpan<byte> body, string secret, DateTimeOffset timestamp)
    {
        if (string.IsNullOrEmpty(secret))
        {
            throw new ArgumentException("ChatlyWebhooks.Sign: secret is empty.", nameof(secret));
        }

        var seconds = timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var digest = ComputeDigest(seconds, body, secret);
        return "t=" + seconds + ",v1=" + Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static byte[] ComputeDigest(string timestampText, ReadOnlySpan<byte> body, string secret)
    {
        var prefix = Encoding.UTF8.GetBytes(timestampText + ".");
        var buffer = new byte[prefix.Length + body.Length];
        prefix.CopyTo(buffer.AsSpan());
        body.CopyTo(buffer.AsSpan(prefix.Length));

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return hmac.ComputeHash(buffer);
    }

    /// <summary>
    /// Split <c>t=…,v1=…</c>. Unknown parts are ignored so a future
    /// <c>v2=</c> does not break this; a missing <c>t</c> or <c>v1</c> fails.
    /// </summary>
    private static bool TryParseHeader(string header, out string timestamp, out string signature)
    {
        timestamp = string.Empty;
        signature = string.Empty;

        foreach (var part in header.Split(','))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var name = part.Substring(0, eq).Trim();
            var value = part.Substring(eq + 1).Trim();

            if (name == "t" && timestamp.Length == 0)
            {
                timestamp = value;
            }
            else if (name == "v1" && signature.Length == 0)
            {
                signature = value;
            }
        }

        return timestamp.Length > 0 && signature.Length > 0;
    }

    /// <summary>
    /// Decode lowercase or uppercase hex without throwing on malformed input —
    /// <see cref="Convert.FromHexString(string)"/> throws, and this path must
    /// return false instead.
    /// </summary>
    private static bool TryDecodeHex(string hex, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (hex.Length == 0 || hex.Length % 2 != 0)
        {
            return false;
        }

        var output = new byte[hex.Length / 2];
        for (var i = 0; i < output.Length; i++)
        {
            if (!TryNibble(hex[i * 2], out var high) || !TryNibble(hex[(i * 2) + 1], out var low))
            {
                return false;
            }

            output[i] = (byte)((high << 4) | low);
        }

        bytes = output;
        return true;
    }

    private static bool TryNibble(char c, out int value)
    {
        if (c >= '0' && c <= '9')
        {
            value = c - '0';
            return true;
        }

        if (c >= 'a' && c <= 'f')
        {
            value = c - 'a' + 10;
            return true;
        }

        if (c >= 'A' && c <= 'F')
        {
            value = c - 'A' + 10;
            return true;
        }

        value = 0;
        return false;
    }
}
