using System;
using System.Text.Json;

namespace Chatly;

/// <summary>
/// Turns an HTTP refusal into the right <see cref="ChatlyException"/> subtype.
/// </summary>
internal static class ChatlyErrorFactory
{
    /// <summary>
    /// Build the exception for a non-2xx response.
    /// </summary>
    /// <param name="status">The HTTP status.</param>
    /// <param name="body">The raw response body, kept verbatim.</param>
    /// <param name="requestId">The <c>x-request-id</c> header, if any.</param>
    /// <param name="retryAfter">The parsed <c>Retry-After</c> header, if any.</param>
    /// <returns>An exception whose runtime type matches the status.</returns>
    internal static ChatlyException Create(int status, string? body, string? requestId, TimeSpan? retryAfter)
    {
        var (code, message) = Decode(body);
        var text = message ?? DefaultMessage(status);

        return status switch
        {
            401 => new ChatlyAuthenticationException(text, status, code, requestId, body),
            403 => new ChatlyPermissionException(text, status, code, requestId, body),
            404 => new ChatlyNotFoundException(text, status, code, requestId, body),
            409 => new ChatlyConflictException(text, status, code, requestId, body),
            400 or 422 => new ChatlyValidationException(text, status, code, requestId, body),
            429 => new ChatlyRateLimitException(text, status, code, requestId, body, retryAfter),
            >= 500 => new ChatlyServerException(text, status, code, requestId, body),
            _ => new ChatlyException(text, status, code, requestId, body),
        };
    }

    /// <summary>
    /// Pull <c>code</c> and <c>message</c> out of an error body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The API nests them: its exception filter sends
    /// <c>{"error":{"code":"forbidden","message":"origin not allowed"}}</c>.
    /// Reading <c>code</c> at the top level — which an earlier client in this
    /// product did — gets <c>undefined</c> for every real refusal, and the bug
    /// survived a green test suite because the fixtures were hand-written
    /// top-level objects. So: nested first, top level only as a fallback for a
    /// refusal written by a proxy ahead of the API.
    /// </para>
    /// <para>Never throws. A body that is not JSON yields two nulls.</para>
    /// </remarks>
    /// <param name="body">The raw response body.</param>
    /// <returns>The code and message, either of which may be null.</returns>
    private static (string? Code, string? Message) Decode(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var nested) && nested.ValueKind == JsonValueKind.Object)
            {
                return (ReadString(nested, "code"), ReadString(nested, "message"));
            }

            return (ReadString(root, "code"), ReadString(root, "message"));
        }
        catch (JsonException)
        {
            // An HTML error page from a load balancer, or a truncated body.
            // The raw text is still handed to the caller as `Body`.
            return (null, null);
        }
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string DefaultMessage(int status) => status switch
    {
        401 => "Unauthorized: the API key was not accepted.",
        403 => "Forbidden: the API key lacks the scope this endpoint needs.",
        404 => "Not found.",
        409 => "Conflict.",
        429 => "Rate limited.",
        >= 500 => "The Chatly API returned a server error.",
        _ => $"The Chatly API returned HTTP {status}.",
    };
}
