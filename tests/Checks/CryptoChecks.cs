using System;
using System.Text;
using System.Threading.Tasks;

namespace Chatly.Tests;

/// <summary>
/// The two local, offline halves of the contract: webhook signature
/// verification (§6, §10.7, §10.9) and the widget identity hash (§7, §10.8).
/// </summary>
/// <remarks>
/// Split out of <see cref="ContractChecks"/> to keep both files under the
/// contract's 650-line ceiling, and because these make no HTTP call at all —
/// they are pure crypto over bytes the caller supplies.
/// </remarks>
public static class CryptoChecks
{
    // ── §10.7 and §10.9: webhook verification ─────────────────────────────

    private const string PinnedSecret = "whsec_test";
    private const string PinnedBody = "{\"id\":\"evt_1\"}";
    private const long PinnedTimestamp = 1_700_000_000;
    private const string PinnedHeader =
        "t=1700000000,v1=c89214b5b5da833daed6f0b8c5bb6bd58cea9022bd80ccc78230f3942d632925";
    private const string BodyOnlyDigest =
        "030fa3b2413d1993c551364bd53bb9b3edb5c0c34d55dba6ada6041245632811";

    /// <summary>
    /// §10.9 — the pinned delivery verifies, verbatim, with a fixed clock.
    /// </summary>
    public static Task PinnedWebhookDeliveryVerifies()
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(PinnedTimestamp);
        var bytes = Encoding.UTF8.GetBytes(PinnedBody);

        Expect.Equal(14, bytes.Length, "the pinned body is those 14 bytes, with no trailing newline");

        Expect.True(
            ChatlyWebhooks.VerifySignature(bytes, PinnedHeader, PinnedSecret, now: at),
            "the pinned delivery verifies with a fixed now of 1700000000");
        Expect.True(
            ChatlyWebhooks.VerifySignature(
                bytes,
                PinnedHeader,
                PinnedSecret,
                ChatlyWebhooks.InfiniteTolerance),
            "the pinned delivery verifies with infinite tolerance");
        Expect.True(
            ChatlyWebhooks.VerifySignature(PinnedBody, PinnedHeader, PinnedSecret, now: at),
            "the string overload verifies the same delivery");

        // The digest this SDK computes, for cross-language comparison.
        var produced = ChatlyWebhooks.Sign(bytes, PinnedSecret, at);
        Expect.Equal(PinnedHeader, produced, "Sign reproduces the pinned header exactly");

        return Task.CompletedTask;
    }

    /// <summary>
    /// §10.9 — the same delivery with the digest taken over the body alone is
    /// rejected.
    /// </summary>
    /// <remarks>
    /// An earlier Node implementation HMAC'd the body alone and so rejected
    /// 100% of genuine deliveries. This asserts the inverse: that the body-only
    /// digest is not accepted, so the timestamp really is inside the digest.
    /// </remarks>
    public static Task BodyOnlyDigestIsRejected()
    {
        var header = $"t={PinnedTimestamp},v1={BodyOnlyDigest}";
        Expect.False(
            ChatlyWebhooks.VerifySignature(
                Encoding.UTF8.GetBytes(PinnedBody),
                header,
                PinnedSecret,
                now: DateTimeOffset.FromUnixTimeSeconds(PinnedTimestamp)),
            "a digest over the body alone is rejected");
        return Task.CompletedTask;
    }

    /// <summary>§10.7 — a freshly signed delivery verifies.</summary>
    public static Task GenuineSignatureIsAccepted()
    {
        var now = DateTimeOffset.UtcNow;
        var body = Encoding.UTF8.GetBytes("{\"event\":\"conversation.created\",\"id\":\"evt_9\"}");
        var header = ChatlyWebhooks.Sign(body, "whsec_live_1", now);

        Expect.True(
            ChatlyWebhooks.VerifySignature(body, header, "whsec_live_1"),
            "a delivery signed now verifies with the default tolerance");
        Expect.False(
            ChatlyWebhooks.VerifySignature(body, header, "whsec_other"),
            "the same delivery fails under a different secret");

        var tampered = Encoding.UTF8.GetBytes("{\"event\":\"conversation.created\",\"id\":\"evt_8\"}");
        Expect.False(
            ChatlyWebhooks.VerifySignature(tampered, header, "whsec_live_1"),
            "a modified body fails");

        // Byte-exactness: the same JSON re-serialised with different key order
        // and spacing is a different body, and must not verify.
        var reserialised = Encoding.UTF8.GetBytes("{\"id\":\"evt_9\", \"event\":\"conversation.created\"}");
        Expect.False(
            ChatlyWebhooks.VerifySignature(reserialised, header, "whsec_live_1"),
            "re-serialised JSON does not verify — pass the raw bytes");

        return Task.CompletedTask;
    }

    /// <summary>§10.7 — a stale timestamp is rejected, and so is a future one.</summary>
    public static Task StaleTimestampIsRejected()
    {
        var signedAt = DateTimeOffset.FromUnixTimeSeconds(PinnedTimestamp);
        var body = Encoding.UTF8.GetBytes(PinnedBody);

        Expect.False(
            ChatlyWebhooks.VerifySignature(body, PinnedHeader, PinnedSecret, now: signedAt.AddSeconds(301)),
            "301 seconds late is outside the default 300s tolerance");
        Expect.True(
            ChatlyWebhooks.VerifySignature(body, PinnedHeader, PinnedSecret, now: signedAt.AddSeconds(299)),
            "299 seconds late is inside it");
        Expect.False(
            ChatlyWebhooks.VerifySignature(body, PinnedHeader, PinnedSecret, now: signedAt.AddSeconds(-301)),
            "a timestamp 301 seconds in the future is rejected too");
        Expect.False(
            ChatlyWebhooks.VerifySignature(
                body,
                PinnedHeader,
                PinnedSecret,
                TimeSpan.FromSeconds(60),
                signedAt.AddSeconds(90)),
            "a caller-supplied tolerance is honoured");

        return Task.CompletedTask;
    }

    /// <summary>§10.7 — malformed input returns false and never throws.</summary>
    public static Task MalformedSignatureHeadersReturnFalse()
    {
        var body = Encoding.UTF8.GetBytes(PinnedBody);
        var at = DateTimeOffset.FromUnixTimeSeconds(PinnedTimestamp);

        var malformed = new (string? Header, string What)[]
        {
            (null, "a missing header"),
            (string.Empty, "an empty header"),
            ("garbage", "a header with no key=value pair"),
            ("v1=c89214b5", "a header with no t"),
            ("t=1700000000", "a header with no v1"),
            ("t=,v1=c89214b5", "an empty t"),
            ("t=not-a-number,v1=c89214b5", "a non-numeric t"),
            ("t=1700000000,v1=", "an empty v1"),
            ("t=1700000000,v1=zzzz", "a non-hex v1"),
            ("t=1700000000,v1=c89214b5b5da833daed6f0b8c5bb6bd58cea9022bd80ccc78230f3942d6329", "a short digest"),
            ("t=1700000000,v1=c89214b5b5da833daed6f0b8c5bb6bd58cea9022bd80ccc78230f3942d63292500", "a long digest"),
            ("t=9223372036854775807,v1=c89214b5", "a t at long.MaxValue"),
            ("t=-9223372036854775808,v1=c89214b5", "a t at long.MinValue"),
            ("t=1700000000,v1=c89214b5,v2=future", "an unknown extra field with a bad v1"),
        };

        foreach (var (header, what) in malformed)
        {
            // Any throw out of here fails the check: a verifier that throws is
            // an accepted webhook in a handler that forgot to catch.
            Expect.False(
                ChatlyWebhooks.VerifySignature(body, header, PinnedSecret, now: at),
                $"{what} is rejected without throwing");
        }

        Expect.False(
            ChatlyWebhooks.VerifySignature(body, PinnedHeader, string.Empty, now: at),
            "an empty secret is rejected without throwing");
        Expect.False(
            ChatlyWebhooks.VerifySignature(string.Empty, PinnedHeader, PinnedSecret, now: at),
            "an empty body is rejected without throwing");

        // An unknown extra field alongside a GOOD v1 still verifies, so a
        // future v2= cannot break existing endpoints.
        Expect.True(
            ChatlyWebhooks.VerifySignature(body, PinnedHeader + ",v2=whatever", PinnedSecret, now: at),
            "an unknown extra field is ignored");

        return Task.CompletedTask;
    }

    // ── §10.8 ─────────────────────────────────────────────────────────────

    /// <summary>§10.8 — the identity hash matches the pinned cross-language vector.</summary>
    public static Task IdentityHashMatchesThePinnedVector()
    {
        Expect.Equal(
            "5e26772dce97d5ba995796e621f83f2e97e6f8c3f9386dbf153a98ab24c10271",
            ChatlyIdentity.Hash("sek_test_secret", "user_42"),
            "identity_hash(\"sek_test_secret\", \"user_42\")");

        var digest = ChatlyIdentity.Hash("sek_test_secret", "user_42");
        Expect.Equal(64, digest.Length, "a SHA-256 digest is 64 hex characters");
        Expect.Equal(digest, digest.ToLowerInvariant(), "the digest is lowercase hex");

        Expect.NotEqual(
            digest,
            ChatlyIdentity.Hash("sek_other_secret", "user_42"),
            "a different secret gives a different digest");
        Expect.NotEqual(
            digest,
            ChatlyIdentity.Hash("sek_test_secret", "user_43"),
            "a different id gives a different digest");

        Expect.Throws<ArgumentException>(
            () => ChatlyIdentity.Hash(string.Empty, "user_42"),
            "an empty secret is refused");
        Expect.Throws<ArgumentException>(
            () => ChatlyIdentity.Hash("sek_test_secret", string.Empty),
            "an empty external id is refused");

        return Task.CompletedTask;
    }
}
