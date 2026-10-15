using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Chatly.Tests;

/// <summary>
/// <see cref="ChatlyWebhooks.VerifyChatlySignature"/> against the cross-SDK
/// corpus, plus the never-throws half of the contract.
/// </summary>
/// <remarks>
/// <para>
/// <b>The corpus is the test, not a sample of it.</b>
/// <c>tests/Fixtures/signature_vectors.json</c> is byte-identical to the copy
/// in every other SDK (sdk-node's spec fails if one drifts), so passing it is
/// what "this port agrees with the reference" means. Every vector's every
/// expected field is asserted, a refusal must carry a non-empty message, and
/// ALL failing vectors are reported at once rather than the first — when a
/// port diverges, the set of vectors that broke is the diagnosis.
/// </para>
/// <para>
/// <b>Found from the package directory, never from a sibling package.</b> The
/// file is located under the directory holding <c>Chatly.sln</c>, which is
/// <c>packages/sdk-dotnet</c> in the monorepo and the repository root in the
/// public <c>chatly-dotnet</c> mirror. A path through <c>../sdk-node</c> or
/// <c>../sdk-spec</c> would pass here and fail in the mirror, whose CI is the
/// only CI that runs.
/// </para>
/// </remarks>
public static class SignatureChecks
{
    /// <summary>
    /// A floor, so a loader that reads nothing cannot pass on zero vectors.
    /// It is the corpus's CURRENT size, 61. It read 59 — the size when the Go
    /// port was written — while the corpus held 61, so a loader that dropped
    /// two vectors passed. Raise it when the corpus grows.
    /// </summary>
    private const int MinimumVectors = 61;

    /// <summary>Every input key the corpus may use. An unknown one fails the check.</summary>
    /// <remarks>
    /// So a field added to the corpus for a new behaviour cannot be silently
    /// ignored here and pass for the wrong reason.
    /// </remarks>
    private static readonly HashSet<string> KnownInputKeys = new(StringComparer.Ordinal)
    {
        "surface", "body", "headers", "secret", "method", "path", "v2",
        "allowUnprotectedReplay", "toleranceSeconds", "nowMs",
    };

    /// <summary>Run every vector in the corpus and assert every expected field.</summary>
    public static Task EveryCorpusVectorAgrees()
    {
        var file = Path.Combine(Manifest.PackageDirectory(), "tests", "Fixtures", "signature_vectors.json");
        if (!File.Exists(file))
        {
            throw new CheckFailedException(
                $"the signature corpus is missing at {file} — a missing corpus is a failure, not a skip");
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(file));
        var root = document.RootElement;
        Expect.Equal(1, root.GetProperty("version").GetInt32(), "corpus version");

        var vectors = root.GetProperty("vectors");
        var count = vectors.GetArrayLength();
        Expect.True(count >= MinimumVectors, $"at least {MinimumVectors} vectors in the corpus, found {count}");

        var failures = new List<string>();
        foreach (var vector in vectors.EnumerateArray())
        {
            var name = vector.GetProperty("name").GetString() ?? "<unnamed>";
            try
            {
                RunVector(vector);
            }
            catch (CheckFailedException ex)
            {
                failures.Add($"[{name}] {ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            throw new CheckFailedException(
                $"{failures.Count} of {count} vectors failed:\n        " + string.Join("\n        ", failures));
        }

        return Task.CompletedTask;
    }

    private static void RunVector(JsonElement vector)
    {
        var raw = vector.GetProperty("input");
        foreach (var property in raw.EnumerateObject())
        {
            if (!KnownInputKeys.Contains(property.Name))
            {
                throw new CheckFailedException($"unknown input key <{property.Name}> — teach this runner what it means");
            }
        }

        var input = new ChatlySignatureInput
        {
            Surface = raw.GetProperty("surface").GetString(),
            BodyText = raw.GetProperty("body").GetString(),
            Headers = ReadHeaders(raw.GetProperty("headers")),
            NowMs = raw.GetProperty("nowMs").GetInt64(),
        };

        var secret = raw.GetProperty("secret");
        if (secret.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string?>();
            foreach (var item in secret.EnumerateArray())
            {
                list.Add(item.GetString());
            }

            input = input with { Secrets = list };
        }
        else
        {
            input = input with { Secret = secret.GetString() };
        }

        if (raw.TryGetProperty("method", out var method))
        {
            input = input with { Method = method.GetString() };
        }

        if (raw.TryGetProperty("path", out var path))
        {
            input = input with { Path = path.GetString() };
        }

        if (raw.TryGetProperty("v2", out var v2))
        {
            input = input with
            {
                V2 = v2.GetString() switch
                {
                    "prefer" => ChatlyV2Policy.Prefer,
                    "require" => ChatlyV2Policy.Require,
                    "ignore" => ChatlyV2Policy.Ignore,
                    var other => throw new CheckFailedException($"unknown v2 policy <{other}> in the corpus"),
                },
            };
        }

        if (raw.TryGetProperty("allowUnprotectedReplay", out var allow))
        {
            input = input with { AllowUnprotectedReplay = allow.GetBoolean() };
        }

        if (raw.TryGetProperty("toleranceSeconds", out var tolerance))
        {
            input = input with { ToleranceSeconds = tolerance.GetDouble() };
        }

        var result = ChatlyWebhooks.VerifyChatlySignature(input);
        var expect = vector.GetProperty("expect");
        var ok = expect.GetProperty("ok").GetBoolean();
        var detail = $" (got ok={result.Ok}, reason={result.Reason ?? "null"}, message={result.Message ?? "null"})";

        Expect.Equal(ok, result.Ok, "ok" + detail);
        Expect.Equal(expect.GetProperty("surface").GetString(), result.Surface, "surface");

        if (ok)
        {
            Expect.Equal(expect.GetProperty("base").GetString(), result.Base, "base");
            Expect.Equal<int?>(expect.GetProperty("secretIndex").GetInt32(), result.SecretIndex, "secretIndex");
            var ts = expect.GetProperty("timestampMs");
            long? expectedTs = ts.ValueKind == JsonValueKind.Null ? null : ts.GetInt64();
            Expect.Equal(expectedTs, result.TimestampMs, "timestampMs");
            Expect.Equal<string?>(null, result.Reason, "reason on success");
        }
        else
        {
            Expect.Equal(expect.GetProperty("reason").GetString(), result.Reason, "reason" + detail);
            Expect.True(!string.IsNullOrWhiteSpace(result.Message), "a non-empty message on a refusal");
            Expect.Equal<int?>(null, result.SecretIndex, "secretIndex on a refusal");
        }
    }

    /// <summary>
    /// Corpus headers are <c>name → string | string[]</c>. Read into a dictionary
    /// with an ORDINAL comparer, so the "two casings count as twice" vector
    /// reaches the verifier as two keys — exactly as JSON delivered it.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string?>?> ReadHeaders(JsonElement headers)
    {
        var map = new Dictionary<string, IReadOnlyList<string?>?>(StringComparer.Ordinal);
        foreach (var property in headers.EnumerateObject())
        {
            var values = new List<string?>();
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in property.Value.EnumerateArray())
                {
                    values.Add(item.GetString());
                }
            }
            else
            {
                values.Add(property.Value.GetString());
            }

            map[property.Name] = values;
        }

        return map;
    }

    /// <summary>
    /// Garbage in, a refusal out — never an exception, never <c>Ok</c>.
    /// </summary>
    /// <remarks>
    /// The corpus is JSON and cannot express a null body, a null header map or
    /// a null secret; C# can, so this is where they are proven.
    /// </remarks>
    public static Task GarbageNeverThrows()
    {
        const string Sig = "t=1760000000,v1=68c8081fadfddd5bded0385d719bd284e690ec14847d59a15a30816bf65c4f1e";
        var good = new ChatlySignatureInput
        {
            Surface = ChatlySignatureSurfaces.EventWebhook,
            BodyText = "{}",
            Headers = ChatlySignatureInput.HeadersFrom(new[] { Pair("x-livechat-signature", Sig) }),
            Secret = "whsec_test",
            NowMs = 1_760_000_000_000,
        };

        var cases = new (string What, ChatlySignatureInput? Input, string? Reason)[]
        {
            ("a null input", null, ChatlySignatureFailure.UnknownSurface),
            ("a null surface", good with { Surface = null }, ChatlySignatureFailure.UnknownSurface),
            ("a wrong-case surface", good with { Surface = "Event_Webhook" }, ChatlySignatureFailure.UnknownSurface),
            ("a null secret", good with { Secret = null }, ChatlySignatureFailure.MissingSecret),
            ("a null secrets list entry only", good with { Secret = null, Secrets = new string?[] { null, "" } }, ChatlySignatureFailure.MissingSecret),
            ("both Secret and Secrets", good with { Secrets = new[] { "whsec_test" } }, ChatlySignatureFailure.MissingSecret),
            ("null headers", good with { Headers = null }, ChatlySignatureFailure.MissingSignatureHeader),
            ("a null value list", good with { Headers = Map(("x-livechat-signature", null)) }, ChatlySignatureFailure.MissingSignatureHeader),
            ("a null value inside the list", good with { Headers = Map(("x-livechat-signature", new string?[] { null })) }, ChatlySignatureFailure.MissingSignatureHeader),
            ("a null body", good with { BodyText = null }, ChatlySignatureFailure.SignatureMismatch),
            ("both Body and BodyText", good with { Body = Array.Empty<byte>() }, ChatlySignatureFailure.SignatureMismatch),
            ("a 25-digit timestamp", good with { Headers = Sign("t=1234567890123456789012345,v1=" + new string('a', 64)) }, ChatlySignatureFailure.MalformedTimestamp),
            ("a seconds value whose x1000 overflows", good with { Headers = Sign("t=9223372036854776,v1=" + new string('a', 64)) }, ChatlySignatureFailure.MalformedTimestamp),
            ("a trailing newline on t", good with { Headers = Sign("t=1760000000\n,v1=" + new string('a', 64)) }, null),
            ("Arabic-Indic digits in t", good with { Headers = Sign("t=١٧٦٠٠٠٠٠٠٠,v1=" + new string('a', 64)) }, ChatlySignatureFailure.MalformedTimestamp),
            ("a NaN tolerance", good with { ToleranceSeconds = double.NaN, NowMs = 0 }, ChatlySignatureFailure.TimestampOutsideTolerance),
            ("a negative tolerance", good with { ToleranceSeconds = -1 }, ChatlySignatureFailure.TimestampOutsideTolerance),
            // Both overflow a (decimal) cast; they were refused, but as
            // signature_mismatch via the catch-all, naming the wrong cause.
            ("a negative-infinite tolerance", good with { ToleranceSeconds = double.NegativeInfinity }, ChatlySignatureFailure.TimestampOutsideTolerance),
            ("a tolerance below decimal's range", good with { ToleranceSeconds = -1e30 }, ChatlySignatureFailure.TimestampOutsideTolerance),
            ("a hostile now", good with { NowMs = long.MinValue }, ChatlySignatureFailure.TimestampOutsideTolerance),
            ("an undefined V2 value", good with { V2 = (ChatlyV2Policy)42 }, ChatlySignatureFailure.SignatureMismatch),
            ("a sign on t", good with { Headers = Sign("t=+1760000000,v1=" + new string('a', 64)) }, ChatlySignatureFailure.MalformedTimestamp),
        };

        foreach (var (what, input, reason) in cases)
        {
            ChatlySignatureResult result;
            try
            {
                result = ChatlyWebhooks.VerifyChatlySignature(input!);
            }
            catch (Exception ex)
            {
                throw new CheckFailedException($"{what}: threw {ex.GetType().Name}: {ex.Message}");
            }

            Expect.NotNull(result, what);
            Expect.False(result.Ok, $"{what} is refused");
            Expect.True(!string.IsNullOrWhiteSpace(result.Message), $"{what} carries a message");
            if (reason is not null)
            {
                Expect.Equal(reason, result.Reason, what);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The exported table, the helper and an infinite tolerance — the parts the
    /// corpus does not reach.
    /// </summary>
    public static Task SurfaceTableAndHelpers()
    {
        Expect.Equal(8, ChatlySignatureSurfaces.All.Count, "eight surfaces");
        foreach (var surface in ChatlySignatureSurfaces.All)
        {
            var result = ChatlyWebhooks.VerifyChatlySignature(new ChatlySignatureInput { Surface = surface });
            Expect.NotEqual(ChatlySignatureFailure.UnknownSurface, result.Reason, $"{surface} is in the table");
        }

        Expect.Equal(300, ChatlyWebhooks.ChatlySignatureToleranceSeconds, "the default tolerance");
        Expect.Equal("x-chatly-signature-v2", ChatlyWebhooks.ChatlyV2SignatureHeader, "the v2 header name");

        // HeadersFrom keeps two casings as two keys, so the duplicate check sees both.
        var doubled = ChatlySignatureInput.HeadersFrom(new[]
        {
            Pair("x-livechat-signature", "a"),
            Pair("X-Livechat-Signature", "b"),
        });
        var dup = ChatlyWebhooks.VerifyChatlySignature(new ChatlySignatureInput
        {
            Surface = ChatlySignatureSurfaces.EventWebhook,
            BodyText = "{}",
            Headers = doubled,
            Secret = "s",
        });
        Expect.Equal(ChatlySignatureFailure.DuplicateSignatureHeader, dup.Reason, "two casings through HeadersFrom");

        // A genuine archived delivery verifies with an infinite tolerance and
        // the real clock — what an archive re-verifier actually does.
        var body = Encoding.UTF8.GetBytes("{\"id\":\"evt_1\"}");
        var header = ChatlyWebhooks.Sign(body, "whsec_test", DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        var archived = ChatlyWebhooks.VerifyChatlySignature(new ChatlySignatureInput
        {
            Surface = ChatlySignatureSurfaces.EventWebhook,
            Body = body,
            Headers = ChatlySignatureInput.HeadersFrom(new Dictionary<string, IEnumerable<string>>
            {
                ["X-LiveChat-Signature"] = new[] { header },
            }),
            Secret = "whsec_test",
            ToleranceSeconds = double.PositiveInfinity,
        });
        Expect.True(archived.Ok, $"an archived delivery verifies with infinite tolerance ({archived.Message})");
        Expect.Equal<long?>(1_700_000_000_000, archived.TimestampMs, "timestampMs is milliseconds");
        return Task.CompletedTask;
    }

    private static KeyValuePair<string, string?> Pair(string key, string value) => new(key, value);

    private static IReadOnlyDictionary<string, IReadOnlyList<string?>?> Sign(string value)
        => Map(("x-livechat-signature", new string?[] { value }));

    private static IReadOnlyDictionary<string, IReadOnlyList<string?>?> Map(
        params (string Key, IReadOnlyList<string?>? Values)[] entries)
    {
        var map = new Dictionary<string, IReadOnlyList<string?>?>(StringComparer.Ordinal);
        foreach (var (key, values) in entries)
        {
            map[key] = values;
        }

        return map;
    }
}
