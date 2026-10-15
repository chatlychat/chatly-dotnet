using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Tests;

/// <summary>
/// <c>bots.rotate_external_secret</c> with and without the overlap window the
/// API added in commit d509799a (<c>activateAt</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every assertion but the last is on <c>handler.Last</c> — what the SDK put
/// on the wire — because a fake that echoes the request agrees with any bug.
/// </para>
/// <para>
/// The server's body schema is <c>.strict()</c>: an unknown key such as
/// <c>activate_at</c> is a 400, and before that route had a body at all, a
/// field sent to it was discarded with a 201. So the key's exact spelling,
/// and the absence of every other key, is the thing under test.
/// </para>
/// </remarks>
public static class RotationChecks
{
    private const string Path = "/v1/bots/b_1/external/rotate-secret";

    /// <summary>
    /// The body carries exactly <c>{"activateAt": …}</c>, the string verbatim.
    /// </summary>
    public static async Task RotateWithActivateAtStringSendsExactlyThatKey()
    {
        var handler = new FakeHandler(FakeResponse.Ok());
        using var client = Fake.Client(handler);

        await client.Bots.RotateExternalSecretAsync("b_1", "2026-10-16T09:00:00.000Z", CancellationToken.None);

        Expect.Equal(1, handler.CallCount, "exactly one request");
        Expect.Equal("POST", handler.Last.Verb, "it is a POST");
        Expect.Equal(Path, handler.Last.Path, "to the rotate-secret route");
        Expect.Equal(string.Empty, handler.Last.Uri.Query, "with no query string");
        Expect.NotNull(handler.Last.Header("idempotency-key"), "carrying an Idempotency-Key");
        AssertOnlyActivateAt(handler.Last.Body, "2026-10-16T09:00:00.000Z");
    }

    /// <summary>
    /// A <see cref="DateTimeOffset"/> is sent as the UTC <c>Z</c> form of the
    /// same instant, converted from whatever offset it carried.
    /// </summary>
    public static async Task RotateWithDateTimeOffsetSendsTheUtcInstant()
    {
        var handler = new FakeHandler(FakeResponse.Ok());
        using var client = Fake.Client(handler);

        // 11:00:00.250 at +02:00 is 09:00:00.250 UTC.
        var local = new DateTimeOffset(2026, 10, 16, 11, 0, 0, 250, TimeSpan.FromHours(2));
        await client.Bots.RotateExternalSecretAsync("b_1", local, CancellationToken.None);
        Expect.Equal(Path, handler.Last.Path, "to the rotate-secret route");
        AssertOnlyActivateAt(handler.Last.Body, "2026-10-16T09:00:00.250Z");

        // Already UTC: unchanged, milliseconds padded to three digits.
        await client.Bots.RotateExternalSecretAsync(
            "b_1",
            new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero),
            CancellationToken.None);
        AssertOnlyActivateAt(handler.Last.Body, "2026-10-16T09:00:00.000Z");
    }

    /// <summary>
    /// Without an activateAt — every way of spelling "none" — the request is
    /// what the hard cut always sent: the transport's bodyless <c>{}</c>.
    /// </summary>
    /// <remarks>
    /// <c>PathOnlyWritesSendNoBodyAndAnIdempotencyKey</c> pins the
    /// <c>(id, ct)</c> form unchanged; this adds the <c>(id)</c> and
    /// <c>(id, default)</c> forms — the second is the source-compatibility
    /// trap, since <c>default</c> converts to all three overloads' second
    /// parameter and must still bind to the CancellationToken one — and the
    /// two explicit nulls.
    /// </remarks>
    public static async Task RotateWithoutActivateAtSendsTheEmptyObject()
    {
        var calls = new (string Name, Func<ChatlyClient, Task> Run)[]
        {
            ("(id)", c => c.Bots.RotateExternalSecretAsync("b_1")),
            ("(id, default)", c => c.Bots.RotateExternalSecretAsync("b_1", default)),
            ("(id, ct)", c => c.Bots.RotateExternalSecretAsync("b_1", CancellationToken.None)),
            ("(id, (DateTimeOffset?)null)", c => c.Bots.RotateExternalSecretAsync("b_1", (DateTimeOffset?)null)),
            ("(id, (string?)null)", c => c.Bots.RotateExternalSecretAsync("b_1", (string?)null)),
        };

        foreach (var (name, run) in calls)
        {
            var handler = new FakeHandler(FakeResponse.Ok());
            using var client = Fake.Client(handler);

            await run(client);

            Expect.Equal(1, handler.CallCount, $"{name} issued exactly one request");
            Expect.Equal("POST", handler.Last.Verb, $"{name} is a POST");
            Expect.Equal(Path, handler.Last.Path, $"{name} hit the rotate-secret route");
            Expect.Equal("{}", handler.Last.Body, $"{name} sends the bodyless {{}}, as before activateAt existed");
            Expect.NotNull(handler.Last.Header("idempotency-key"), $"{name} carries an Idempotency-Key");
        }
    }

    /// <summary>
    /// The response's <c>activatesAt</c> reaches the caller, null or not, in
    /// the shape <c>rotatedSigningSecretSchema</c> declares.
    /// </summary>
    public static async Task RotateResponseExposesActivatesAt()
    {
        var deferred = new FakeHandler(FakeResponse.Object(
            "{\"signingSecret\":\"whsec_x\",\"activatesAt\":\"2026-10-16T09:00:00.000Z\"}"));
        using (var client = Fake.Client(deferred))
        {
            var result = await client.Bots.RotateExternalSecretAsync("b_1", "2026-10-16T09:00:00.000Z", CancellationToken.None);
            Expect.Equal("whsec_x", result.GetProperty("signingSecret").GetString(), "signingSecret is readable");
            Expect.Equal(
                "2026-10-16T09:00:00.000Z",
                result.GetProperty("activatesAt").GetString(),
                "a deferred rotation reports when the switch happens");
        }

        var immediate = new FakeHandler(FakeResponse.Object("{\"signingSecret\":\"whsec_x\",\"activatesAt\":null}"));
        using (var client = Fake.Client(immediate))
        {
            var result = await client.Bots.RotateExternalSecretAsync("b_1", CancellationToken.None);
            Expect.Equal(
                JsonValueKind.Null,
                result.GetProperty("activatesAt").ValueKind,
                "a hard cut reports activatesAt as JSON null — the new secret is already signing");
        }
    }

    private static void AssertOnlyActivateAt(string? body, string expected)
    {
        Expect.NotNull(body, "a body was sent");
        using var doc = JsonDocument.Parse(body!);
        var props = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Expect.False(props.Contains("activate_at"), "the key is not snake_case");
        Expect.Equal("activateAt", string.Join(",", props), "the body has exactly one key, activateAt");
        Expect.Equal(expected, doc.RootElement.GetProperty("activateAt").GetString(), "carrying the expected instant");
    }
}
