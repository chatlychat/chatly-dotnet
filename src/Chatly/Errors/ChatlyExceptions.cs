using System;
using System.Threading.Tasks;

namespace Chatly;

/// <summary>
/// HTTP 401 — the key was missing, malformed, revoked or expired.
/// </summary>
/// <remarks>
/// Distinct from <see cref="ChatlyPermissionException"/> on purpose: this one
/// means the credential is not accepted at all, not that it lacks a scope.
/// </remarks>
public sealed class ChatlyAuthenticationException : ChatlyException
{
    /// <summary>Build a 401.</summary>
    /// <param name="message">The API's message.</param>
    /// <param name="status">HTTP status (401).</param>
    /// <param name="code">The API's error code.</param>
    /// <param name="requestId">The <c>x-request-id</c> header.</param>
    /// <param name="body">The raw response body.</param>
    public ChatlyAuthenticationException(string message, int status, string? code, string? requestId, string? body)
        : base(message, status, code, requestId, body)
    {
    }
}

/// <summary>
/// HTTP 403 — the key is valid but may not do this.
/// </summary>
/// <remarks>
/// Usually a missing scope: tick it when you mint the key, or call
/// <see cref="ChatlyClient.WhoAmIAsync(System.Threading.CancellationToken)"/>
/// to see what the key actually holds. It also appears when the route needs a
/// signed-in person rather than a key — 746 of the API's 886 routes do, and
/// this SDK deliberately ships no method for those.
/// </remarks>
public sealed class ChatlyPermissionException : ChatlyException
{
    /// <summary>Build a 403.</summary>
    /// <param name="message">The API's message.</param>
    /// <param name="status">HTTP status (403).</param>
    /// <param name="code">The API's error code.</param>
    /// <param name="requestId">The <c>x-request-id</c> header.</param>
    /// <param name="body">The raw response body.</param>
    public ChatlyPermissionException(string message, int status, string? code, string? requestId, string? body)
        : base(message, status, code, requestId, body)
    {
    }
}

/// <summary>
/// HTTP 404 — no such record, or none this key's workspace can see.
/// </summary>
/// <remarks>
/// Per-workspace isolation is enforced in the database, so another
/// workspace's record is indistinguishable from one that does not exist.
/// That is deliberate: a 403 here would confirm the id is real.
/// </remarks>
public sealed class ChatlyNotFoundException : ChatlyException
{
    /// <summary>Build a 404.</summary>
    /// <param name="message">The API's message.</param>
    /// <param name="status">HTTP status (404).</param>
    /// <param name="code">The API's error code.</param>
    /// <param name="requestId">The <c>x-request-id</c> header.</param>
    /// <param name="body">The raw response body.</param>
    public ChatlyNotFoundException(string message, int status, string? code, string? requestId, string? body)
        : base(message, status, code, requestId, body)
    {
    }
}

/// <summary>
/// HTTP 409 — the write conflicts with the current state, or an
/// <c>Idempotency-Key</c> was reused with a different body.
/// </summary>
/// <remarks>
/// The idempotency case is worth recognising: the same key plus the same body
/// replays the first response, while the same key plus a <i>different</i> body
/// is this error. If you see it after a
/// <see cref="ChatlyClient.WithIdempotencyKey(string)"/> retry, the retry
/// changed the payload.
/// </remarks>
public sealed class ChatlyConflictException : ChatlyException
{
    /// <summary>Build a 409.</summary>
    /// <param name="message">The API's message.</param>
    /// <param name="status">HTTP status (409).</param>
    /// <param name="code">The API's error code.</param>
    /// <param name="requestId">The <c>x-request-id</c> header.</param>
    /// <param name="body">The raw response body.</param>
    public ChatlyConflictException(string message, int status, string? code, string? requestId, string? body)
        : base(message, status, code, requestId, body)
    {
    }
}

/// <summary>
/// HTTP 422 (and 400) — the request body or query failed validation.
/// </summary>
/// <remarks>
/// The rejected fields are in <see cref="ChatlyException.Body"/> under
/// <c>error.details.issues</c>, in the shape the API's zod schemas produce.
/// The SDK surfaces the raw body rather than modelling that shape, because a
/// hand-written model of someone else's response is a model that can disagree
/// with it.
/// </remarks>
public sealed class ChatlyValidationException : ChatlyException
{
    /// <summary>Build a 422 or 400.</summary>
    /// <param name="message">The API's message.</param>
    /// <param name="status">HTTP status (422 or 400).</param>
    /// <param name="code">The API's error code.</param>
    /// <param name="requestId">The <c>x-request-id</c> header.</param>
    /// <param name="body">The raw response body.</param>
    public ChatlyValidationException(string message, int status, string? code, string? requestId, string? body)
        : base(message, status, code, requestId, body)
    {
    }
}

/// <summary>
/// HTTP 429 — rate limited. <see cref="RetryAfter"/> is how long the API
/// asked you to wait.
/// </summary>
/// <remarks>
/// The SDK retries a 429 up to <see cref="ChatlyRetryPolicy.MaxAttempts"/>
/// times, honouring <c>Retry-After</c>. Seeing this exception means the
/// retries also came back 429.
/// </remarks>
public sealed class ChatlyRateLimitException : ChatlyException
{
    /// <summary>Build a 429.</summary>
    /// <param name="message">The API's message.</param>
    /// <param name="status">HTTP status (429).</param>
    /// <param name="code">The API's error code.</param>
    /// <param name="requestId">The <c>x-request-id</c> header.</param>
    /// <param name="body">The raw response body.</param>
    /// <param name="retryAfter">The parsed <c>Retry-After</c> header, when present.</param>
    public ChatlyRateLimitException(
        string message,
        int status,
        string? code,
        string? requestId,
        string? body,
        TimeSpan? retryAfter)
        : base(message, status, code, requestId, body)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// The <c>Retry-After</c> response header, parsed. Null when the response
    /// carried none. Both forms are understood: delay-seconds, and an
    /// HTTP-date (converted to a delay from now).
    /// </summary>
    public TimeSpan? RetryAfter { get; }
}

/// <summary>
/// HTTP 5xx — the API failed. Safe to retry; the SDK already did.
/// </summary>
public sealed class ChatlyServerException : ChatlyException
{
    /// <summary>Build a 5xx.</summary>
    /// <param name="message">The API's message.</param>
    /// <param name="status">HTTP status.</param>
    /// <param name="code">The API's error code.</param>
    /// <param name="requestId">The <c>x-request-id</c> header.</param>
    /// <param name="body">The raw response body.</param>
    public ChatlyServerException(string message, int status, string? code, string? requestId, string? body)
        : base(message, status, code, requestId, body)
    {
    }
}

/// <summary>
/// The request never produced an HTTP response: DNS failure, refused
/// connection, TLS failure, or the SDK's own timeout elapsed.
/// </summary>
/// <remarks>
/// <see cref="ChatlyException.Status"/> is 0, because there was no status.
/// <see cref="Exception.InnerException"/> carries the
/// <see cref="System.Net.Http.HttpRequestException"/> or
/// <see cref="TaskCanceledException"/> underneath.
/// A write that fails this way may or may not have been applied — retry it
/// with the same <c>Idempotency-Key</c> and the API will not double-apply it.
/// </remarks>
public sealed class ChatlyTransportException : ChatlyException
{
    /// <summary>Build a transport failure.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying exception.</param>
    public ChatlyTransportException(string message, Exception? innerException = null)
        : base(message, 0, "transport_error", null, null, innerException)
    {
    }

    /// <summary>
    /// True when the failure was the SDK's own per-attempt timeout
    /// (<see cref="ChatlyClientOptions.Timeout"/>) rather than a network error.
    /// </summary>
    public bool IsTimeout => InnerException is TaskCanceledException or OperationCanceledException;
}
