using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Chatly.Tests;

/// <summary>
/// <see cref="ChatlyWebhooks.SignChatlyV2"/> against digests the REFERENCE
/// produced, and then through this SDK's own verifier.
/// </summary>
/// <remarks>
/// <para>
/// <b>The pinned digests are the point.</b> They come from
/// <c>packages/sdk-node/src/signature-mint.ts</c>, not from this code: a mint
/// asserted against its own base-string construction agrees with a wrong
/// construction, and the pair of them would then pass every check while
/// production refused every delivery. Regenerate with (no double quotes, so it
/// survives a copy-paste):
/// </para>
/// <code>
/// node --input-type=module -e "import{createHmac}from'node:crypto';
///   const s='whsec_dual_phase',b=JSON.stringify({workflow:'wf_1',run:7});
///   const h=x=>createHmac('sha256',s).update(x).digest('hex');
///   console.log(h('1760000000.POST./hooks/chatly?run=7.'+b),h('1760000000.'+b));"
/// </code>
/// <para>
/// Round-tripping a minted value through
/// <see cref="ChatlyWebhooks.VerifyChatlySignature"/> is the second half and not
/// the first: the two could agree on a wrong base string, which is exactly what
/// the pinned digests rule out.
/// </para>
/// </remarks>
public static class SignatureMintChecks
{
    private const string Secret = "whsec_dual_phase";
    private const string Body = "{\"workflow\":\"wf_1\",\"run\":7}";
    private const long T = 1_760_000_000;
    private const string Method = "POST";
    private const string PathAndQuery = "/hooks/chatly?run=7";

    /// <summary>HMAC-SHA256(Secret, "1760000000.POST./hooks/chatly?run=7." + Body), from the reference.</summary>
    private const string TargetDigest = "f1b09a141a4ac0a8b9e2902225312f679caa016cb26df48aa6a0e0cf1e4f4b36";

    /// <summary>HMAC-SHA256(Secret, "1760000000." + Body), from the reference.</summary>
    private const string BodyDigest = "8e5164b6f89caf31b699bc6e3d8b413d5f8edc870a21fc8788745bfaf565b874";

    private static ChatlyV2MintInput Mint(ChatlyV2MintBase @base) => @base == ChatlyV2MintBase.Target
        ? new ChatlyV2MintInput
        {
            Base = ChatlyV2MintBase.Target,
            Secret = Secret,
            TimestampSeconds = T,
            BodyText = Body,
            Method = Method,
            Path = PathAndQuery,
        }
        : new ChatlyV2MintInput
        {
            Base = ChatlyV2MintBase.Body,
            Secret = Secret,
            TimestampSeconds = T,
            BodyText = Body,
        };

    /// <summary>Both bases mint what the reference minted, marker included.</summary>
    public static Task MintedValuesMatchTheReference()
    {
        Expect.Equal(
            "t=1760000000,v1=" + TargetDigest + ",b=t",
            ChatlyWebhooks.SignChatlyV2(Mint(ChatlyV2MintBase.Target)),
            "the target base, with b=t");
        Expect.Equal(
            "t=1760000000,v1=" + BodyDigest + ",b=b",
            ChatlyWebhooks.SignChatlyV2(Mint(ChatlyV2MintBase.Body)),
            "the body base, with b=b");

        // The construction itself, not just the digest over it.
        Expect.Equal(
            "1760000000.POST./hooks/chatly?run=7." + Body,
            Encoding.UTF8.GetString(ChatlyWebhooks.ChatlyV2BaseString(Mint(ChatlyV2MintBase.Target))),
            "the target base string");
        Expect.Equal(
            "1760000000." + Body,
            Encoding.UTF8.GetString(ChatlyWebhooks.ChatlyV2BaseString(Mint(ChatlyV2MintBase.Body))),
            "the body base string");

        // The method is upper-cased, as the verifier upper-cases it.
        Expect.Equal(
            ChatlyWebhooks.SignChatlyV2(Mint(ChatlyV2MintBase.Target)),
            ChatlyWebhooks.SignChatlyV2(Mint(ChatlyV2MintBase.Target) with { Method = "post" }),
            "a lower-case method mints the same value");

        // Body and BodyText are the same bytes, non-ASCII included.
        const string Unicode = "{\"name\":\"Ünïcödé — ✓\"}";
        Expect.Equal(
            ChatlyWebhooks.SignChatlyV2(Mint(ChatlyV2MintBase.Target) with { BodyText = Unicode }),
            ChatlyWebhooks.SignChatlyV2(
                Mint(ChatlyV2MintBase.Target) with { BodyText = null, Body = Encoding.UTF8.GetBytes(Unicode) }),
            "BodyText and its UTF-8 bytes agree");

        // Body ignores Method and Path rather than refusing them: a harness that
        // sets every field once and switches only the base keeps minting the same
        // value, which is what the reference documents.
        Expect.Equal(
            ChatlyWebhooks.SignChatlyV2(Mint(ChatlyV2MintBase.Body)),
            ChatlyWebhooks.SignChatlyV2(Mint(ChatlyV2MintBase.Body) with { Method = "GET", Path = "/elsewhere" }),
            "the body base ignores a method and a path");

        return Task.CompletedTask;
    }

    /// <summary>A minted value verifies — as the base its marker claims, and not as the other one.</summary>
    public static Task AMintedValueVerifies()
    {
        var target = ChatlyWebhooks.VerifyChatlySignature(new ChatlySignatureInput
        {
            Surface = ChatlySignatureSurfaces.UnifiedV2,
            BodyText = Body,
            Headers = Headers(ChatlyWebhooks.SignChatlyV2(Mint(ChatlyV2MintBase.Target))),
            Secret = Secret,
            Method = Method,
            Path = PathAndQuery,
            NowMs = T * 1000,
        });
        Expect.True(target.Ok, $"a minted target value verifies ({target.Message})");
        Expect.Equal(ChatlySignatureBase.Target, target.Base, "as the target base");

        // b=b on unified_v2 is the refusal the marker exists to make legible.
        var bodyOnUnified = ChatlyWebhooks.VerifyChatlySignature(new ChatlySignatureInput
        {
            Surface = ChatlySignatureSurfaces.UnifiedV2,
            BodyText = Body,
            Headers = Headers(ChatlyWebhooks.SignChatlyV2(Mint(ChatlyV2MintBase.Body))),
            Secret = Secret,
            Method = Method,
            Path = PathAndQuery,
            NowMs = T * 1000,
        });
        Expect.Equal(
            ChatlySignatureFailure.SignatureMismatch,
            bodyOnUnified.Reason,
            "a b=b value is refused on unified_v2");

        // …and the same value is what workflow_webhook, which accepts both, is for.
        var bodyOnWorkflow = ChatlyWebhooks.VerifyChatlySignature(new ChatlySignatureInput
        {
            Surface = ChatlySignatureSurfaces.WorkflowWebhook,
            BodyText = Body,
            Headers = Headers(ChatlyWebhooks.SignChatlyV2(Mint(ChatlyV2MintBase.Body))),
            Secret = Secret,
            Method = Method,
            Path = PathAndQuery,
            NowMs = T * 1000,
        });
        Expect.True(bodyOnWorkflow.Ok, $"and verifies on workflow_webhook ({bodyOnWorkflow.Message})");
        Expect.Equal(ChatlySignatureBase.Body, bodyOnWorkflow.Base, "as the body base");

        return Task.CompletedTask;
    }

    /// <summary>Two mints compose the workflow action's live dual-digest value.</summary>
    /// <remarks>
    /// What <c>fire-webhook.ts</c> sends while <c>WORKFLOW_WEBHOOK_V2_PHASE</c>
    /// is <c>'dual'</c>: the <c>v1</c> field out of each mint, one <c>t</c>, and
    /// NO marker — the value carries two constructions at once and a marker can
    /// only name one of them.
    /// </remarks>
    public static Task TwoMintsComposeTheDualDigestValue()
    {
        var dual = "t=" + T
            + ",v1=" + V1Of(ChatlyWebhooks.SignChatlyV2(Mint(ChatlyV2MintBase.Target)))
            + ",v1=" + V1Of(ChatlyWebhooks.SignChatlyV2(Mint(ChatlyV2MintBase.Body)));
        Expect.Equal("t=1760000000,v1=" + TargetDigest + ",v1=" + BodyDigest, dual, "the composed value");

        var withTarget = ChatlyWebhooks.VerifyChatlySignature(new ChatlySignatureInput
        {
            Surface = ChatlySignatureSurfaces.WorkflowWebhook,
            BodyText = Body,
            Headers = Headers(dual),
            Secret = Secret,
            Method = Method,
            Path = PathAndQuery,
            NowMs = T * 1000,
        });
        Expect.True(withTarget.Ok, $"the dual value verifies with a target ({withTarget.Message})");
        Expect.Equal(ChatlySignatureBase.Target, withTarget.Base, "on the target base");

        var withoutTarget = ChatlyWebhooks.VerifyChatlySignature(new ChatlySignatureInput
        {
            Surface = ChatlySignatureSurfaces.WorkflowWebhook,
            BodyText = Body,
            Headers = Headers(dual),
            Secret = Secret,
            NowMs = T * 1000,
        });
        Expect.True(withoutTarget.Ok, $"and with none ({withoutTarget.Message})");
        Expect.Equal(ChatlySignatureBase.Body, withoutTarget.Base, "on the body base");

        return Task.CompletedTask;
    }

    /// <summary>An input that cannot be signed faithfully throws rather than minting.</summary>
    public static Task AnUnsignableInputIsRefused()
    {
        var target = Mint(ChatlyV2MintBase.Target);
        var cases = new (string What, ChatlyV2MintInput Input)[]
        {
            // An undefined enum value: C# cannot stop a cast, so it is checked.
            ("an undefined base", target with { Base = (ChatlyV2MintBase)42 }),
            // An empty secret is a perfectly valid HMAC key, which is the problem.
            ("an empty secret", target with { Secret = string.Empty }),
            ("a null secret", target with { Secret = null }),
            // Milliseconds are the trap TimestampSeconds is named against; a
            // negative is the only shape a long can be refused on.
            ("a negative timestamp", target with { TimestampSeconds = -1 }),
            // An empty body is an empty array; null is a caller who lost it.
            ("no body", target with { BodyText = null }),
            ("both Body and BodyText", target with { Body = Array.Empty<byte>() }),
            // Target signs the method and the path, so neither can be guessed.
            ("no method", target with { Method = null }),
            ("an empty method", target with { Method = string.Empty }),
            ("no path", target with { Path = null }),
            ("an empty path", target with { Path = string.Empty }),
        };

        foreach (var (what, input) in cases)
        {
            Expect.Throws<ArgumentException>(() => ChatlyWebhooks.SignChatlyV2(input), what);
            Expect.Throws<ArgumentException>(() => ChatlyWebhooks.ChatlyV2BaseString(input), what + " (base string)");
        }

        // A null input is an ArgumentNullException, which is an ArgumentException.
        Expect.Throws<ArgumentNullException>(() => ChatlyWebhooks.SignChatlyV2(null!), "a null input");
        Expect.Throws<ArgumentNullException>(() => ChatlyWebhooks.ChatlyV2BaseString(null!), "a null input (base string)");

        return Task.CompletedTask;
    }

    /// <summary>The <c>v1=</c> field of a minted value, as the producer's own <c>digestOf</c> does.</summary>
    private static string V1Of(string value)
    {
        foreach (var part in value.Split(','))
        {
            if (part.StartsWith("v1=", StringComparison.Ordinal))
            {
                return part["v1=".Length..];
            }
        }

        throw new CheckFailedException($"no v1= in {value}");
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string?>?> Headers(string value)
        => new Dictionary<string, IReadOnlyList<string?>?>(StringComparer.Ordinal)
        {
            [ChatlyWebhooks.ChatlyV2SignatureHeader] = new string?[] { value },
        };
}
