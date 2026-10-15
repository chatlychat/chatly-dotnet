using System;
using System.Collections.Generic;

namespace Chatly;

// The parsing half of `VerifyChatlySignature` — header collection, secret
// filtering, the `t=…,v1=…` parser, and the ASCII / ECMAScript-whitespace
// helpers that keep .NET from quietly disagreeing with the Node reference.
// Split out of `ChatlySignatures.cs` for the 650-line ceiling; it is the same
// partial class, so the verifier reads these as its own private members.
public static partial class ChatlyWebhooks
{
    /// <summary>
    /// Every value under <paramref name="name"/>, collected across EVERY key
    /// that matches case-insensitively.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>headers.TryGetValue</c>: whether that is
    /// case-insensitive depends on the comparer the caller built the dictionary
    /// with, and even when it is, it returns one key's values — so a header that
    /// arrived under two casings would be counted once and the duplicate check
    /// would never fire. A null value list or a null entry is treated as absent.
    /// </remarks>
    private static List<string> SigHeaderValues(
        IReadOnlyDictionary<string, IReadOnlyList<string?>?>? headers,
        string name)
    {
        var found = new List<string>();
        if (headers is null)
        {
            return found;
        }

        foreach (var pair in headers)
        {
            if (pair.Key is null
                || pair.Value is null
                || !string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var value in pair.Value)
            {
                if (value is not null)
                {
                    found.Add(value);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Every non-empty secret, with the index the CALLER gave it — <c>["", s]</c>
    /// reports 1 for <c>s</c>, so the index means something in the caller's list.
    /// </summary>
    private static List<(string Value, int Index)> SigSecretsFrom(ChatlySignatureInput input, out string? problem)
    {
        problem = null;
        var found = new List<(string, int)>();
        if (input.Secret is not null && input.Secrets is not null)
        {
            problem = "Both Secret and Secrets were set. Set one: Secrets for every secret live on the "
                      + "surface during a rotation, Secret otherwise.";
            return found;
        }

        if (input.Secrets is not null)
        {
            for (var i = 0; i < input.Secrets.Count; i++)
            {
                var value = input.Secrets[i];
                if (!string.IsNullOrEmpty(value))
                {
                    found.Add((value, i));
                }
            }
        }
        else if (!string.IsNullOrEmpty(input.Secret))
        {
            found.Add((input.Secret, 0));
        }

        return found;
    }

    /// <summary>
    /// Parse <c>t=…,v1=…[,b=…]</c>, collecting EVERY <c>v1</c> and keeping the
    /// FIRST <c>t</c> and FIRST <c>b</c>, so a crafted second one cannot move the
    /// window or the base. Unknown fields are skipped: a future <c>v3</c> must
    /// not break a <c>v1</c> verifier.
    /// </summary>
    private static bool SigParseTv1(string raw, out string t, out List<string> v1, out string? b)
    {
        string? first = null;
        b = null;
        v1 = new List<string>();
        foreach (var part in raw.Split(','))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var key = JsTrim(part.Substring(0, eq));
            var value = JsTrim(part.Substring(eq + 1));
            if (key == "t")
            {
                first ??= value;
            }
            else if (key == "b")
            {
                b ??= value;
            }
            else if (key == "v1")
            {
                v1.Add(value);
            }
        }

        t = first ?? string.Empty;
        return first is not null && v1.Count > 0;
    }

    private static bool SigHasTarget(ChatlySignatureInput input)
        => !string.IsNullOrEmpty(input.Method) && !string.IsNullOrEmpty(input.Path);

    private static string SigBaseStringPrefix(string @base, string? rawTimestamp, ChatlySignatureInput input)
    {
        if (@base == ChatlySignatureBase.BodyOnly)
        {
            return string.Empty;
        }

        // The RAW timestamp text, never re-formatted: "0017…" signs as "0017…".
        var t = rawTimestamp ?? string.Empty;
        return @base == ChatlySignatureBase.Body
            ? t + "."
            : t + "." + (input.Method ?? string.Empty).ToUpperInvariant() + "." + (input.Path ?? string.Empty) + ".";
    }

    private static bool SigIsHexSha256(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SigIsAsciiDigits(string value)
    {
        foreach (var c in value)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return value.Length > 0;
    }

    /// <summary>ECMAScript's <c>String.prototype.trim</c>: WhiteSpace plus LineTerminator.</summary>
    private static string JsTrim(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && IsJsWhitespace(value[start]))
        {
            start++;
        }

        while (end > start && IsJsWhitespace(value[end - 1]))
        {
            end--;
        }

        return value.Substring(start, end - start);
    }

    private static bool IsJsWhitespace(char c)
        => c is '\t' or '\n' or '\v' or '\f' or '\r' or ' '
            or (char)0x00A0 or (char)0x1680 or (>= (char)0x2000 and <= (char)0x200A)
            or (char)0x2028 or (char)0x2029 or (char)0x202F or (char)0x205F
            or (char)0x3000 or (char)0xFEFF;

    private static string Capitalise(string value)
        => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value.Substring(1);

    /// <summary>Clip an untrusted header value so an error message stays one line.</summary>
    private static string Preview(string value)
        => value.Length > 48 ? value.Substring(0, 47) + "…" : value;
}
