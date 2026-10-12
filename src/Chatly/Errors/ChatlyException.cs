using System;

namespace Chatly;

/// <summary>
/// Base class for every failure this SDK raises. Catch this to catch them all.
/// </summary>
/// <remarks>
/// <para>
/// The SDK never throws a bare <see cref="Exception"/> with a formatted
/// string. Branch on the subtype — <see cref="ChatlyAuthenticationException"/>
/// (401), <see cref="ChatlyPermissionException"/> (403),
/// <see cref="ChatlyNotFoundException"/> (404),
/// <see cref="ChatlyConflictException"/> (409),
/// <see cref="ChatlyValidationException"/> (422),
/// <see cref="ChatlyRateLimitException"/> (429),
/// <see cref="ChatlyServerException"/> (5xx) or
/// <see cref="ChatlyTransportException"/> (never reached the API) — rather
/// than on <see cref="Exception.Message"/>. "My key is wrong" and "my key
/// lacks a scope" are different types, not different strings.
/// </para>
/// <para>
/// These types live in the <c>Chatly</c> namespace rather than
/// <c>Chatly.Errors</c>, so that catching them needs no second <c>using</c>.
/// </para>
/// </remarks>
public class ChatlyException : Exception
{
    /// <summary>Build an error from a decoded API refusal.</summary>
    /// <param name="message">Human-readable message, usually the API's own.</param>
    /// <param name="status">HTTP status, or 0 when no response arrived.</param>
    /// <param name="code">The API's machine-readable <c>code</c>, when it sent one.</param>
    /// <param name="requestId">The <c>x-request-id</c> response header, when present.</param>
    /// <param name="body">The raw response body, for anything the fields above lose.</param>
    /// <param name="innerException">The underlying failure, for transport errors.</param>
    public ChatlyException(
        string message,
        int status = 0,
        string? code = null,
        string? requestId = null,
        string? body = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Status = status;
        Code = code;
        RequestId = requestId;
        Body = body;
    }

    /// <summary>
    /// The HTTP status that was returned, or <c>0</c> when the request never
    /// got a response (see <see cref="ChatlyTransportException"/>).
    /// </summary>
    public int Status { get; }

    /// <summary>
    /// The API's machine-readable error code — e.g. <c>validation_failed</c>,
    /// <c>forbidden</c>, <c>not_found</c> — or null when the body carried none.
    /// </summary>
    /// <remarks>
    /// Read from <c>error.code</c>: the API nests its refusals as
    /// <c>{"error":{"code":…,"message":…}}</c>. A top-level <c>code</c> is
    /// accepted as a fallback, for a refusal written by a proxy in front of
    /// the API rather than by the API itself.
    /// </remarks>
    public string? Code { get; }

    /// <summary>
    /// The <c>x-request-id</c> response header, when the response carried one.
    /// Quote it in a support request: it is how we find your call in our logs.
    /// </summary>
    public string? RequestId { get; }

    /// <summary>
    /// The raw response body, exactly as received and never re-serialised.
    /// Null when there was no response.
    /// </summary>
    /// <remarks>
    /// Keep this when you log a failure: a 422's <c>error.details.issues</c>
    /// is the list of fields that were rejected, and this SDK does not model
    /// that shape rather than guess at it.
    /// </remarks>
    public string? Body { get; }

    /// <summary>
    /// True when trying the identical request again could plausibly succeed:
    /// a transport failure, a 429 or a 5xx. The SDK already retries these
    /// according to <see cref="ChatlyRetryPolicy"/>; this tells you whether
    /// it gave up on something retryable or refused to retry at all.
    /// </summary>
    public bool IsRetryable => Status == 0 || Status == 429 || Status >= 500;

    /// <summary>A one-line summary with the status, code and request id.</summary>
    /// <returns>The summary, for logs.</returns>
    public override string ToString()
    {
        var code = Code is null ? string.Empty : $" code={Code}";
        var requestId = RequestId is null ? string.Empty : $" request_id={RequestId}";
        return $"{GetType().Name}: status={Status}{code}{requestId} {Message}";
    }
}
