using System;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly;

/// <summary>
/// Everything a <see cref="ChatlyClient"/> needs: the API key, and the
/// handful of knobs worth turning.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="ApiKey"/> is required. The default <see cref="BaseUrl"/> is
/// the production host, <c>https://api.chatlychat.com</c> — override it only
/// to reach another Chatly environment.
/// </para>
/// <para>
/// This is a record, so a derived configuration is a <c>with</c> expression:
/// <c>options with { Timeout = TimeSpan.FromSeconds(30) }</c>.
/// </para>
/// </remarks>
public sealed record ChatlyClientOptions
{
    /// <summary>
    /// The production API host. Never a development or example hostname.
    /// </summary>
    /// <remarks>
    /// Four of this product's SDKs shipped with <c>api.livechat.local</c> or
    /// <c>api.chatly.example</c> as the default. Both resolve nowhere, so
    /// every call failed for anyone who followed the docs and did not pass an
    /// explicit URL. No test caught it, because every test passes one.
    /// </remarks>
    public static readonly Uri ProductionBaseUrl = new Uri("https://api.chatlychat.com");

    /// <summary>
    /// The workspace API key — a <c>ck_…</c> token minted under
    /// <b>Workspace → Developer → API keys</b>.
    /// </summary>
    /// <remarks>
    /// This is a bearer credential for the whole workspace. It belongs in
    /// server-side code and must never be shipped to a browser or a mobile
    /// app; use <c>@chatlychat/sdk-js</c> there.
    /// </remarks>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Where the API lives. Defaults to <see cref="ProductionBaseUrl"/>.
    /// A path prefix is honoured, so <c>https://chat.example.com/api</c> works
    /// when another environment is served under a path prefix.
    /// </summary>
    public Uri BaseUrl { get; init; } = ProductionBaseUrl;

    /// <summary>
    /// How long one HTTP attempt may take before it is abandoned and (if the
    /// retry policy allows) retried. Default 15 seconds.
    /// </summary>
    /// <remarks>
    /// Applied by the SDK with a linked <see cref="CancellationTokenSource"/>,
    /// not by mutating <c>HttpClient.Timeout</c> — an <c>HttpClient</c> you
    /// injected is never modified, because it may be shared.
    /// </remarks>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>What to retry, how often, and how long to wait between tries.</summary>
    public ChatlyRetryPolicy Retry { get; init; } = ChatlyRetryPolicy.Default;

    /// <summary>
    /// Pin one <c>Idempotency-Key</c> across every write this client makes.
    /// </summary>
    /// <remarks>
    /// Normally leave this null: the client mints a fresh UUID per write, so
    /// an SDK-level retry cannot double-write. Set it — or better, call
    /// <see cref="ChatlyClient.WithIdempotencyKey(string)"/> — when you are
    /// retrying the <i>same</i> logical operation from your own code. Same key
    /// plus same body replays the first response; same key plus a different
    /// body is a 409.
    /// </remarks>
    public string? PinnedIdempotencyKey { get; init; }

    /// <summary>
    /// Appended to the SDK's own <c>User-Agent</c>, e.g. <c>"acme-billing/2.1"</c>.
    /// Optional; useful when our support team is reading your request logs.
    /// </summary>
    public string? UserAgentSuffix { get; init; }

    /// <summary>
    /// Replaces <see cref="Task.Delay(TimeSpan, CancellationToken)"/> for
    /// retry backoff. A test seam: the SDK's own suite passes a no-op so the
    /// retry tests do not sleep. Leave null in production.
    /// </summary>
    public Func<TimeSpan, CancellationToken, Task>? DelayAsync { get; init; }

    /// <summary>
    /// Supplies "now" for the <c>Retry-After</c> HTTP-date form. A test seam;
    /// leave null in production and <see cref="DateTimeOffset.UtcNow"/> is used.
    /// </summary>
    public Func<DateTimeOffset>? Clock { get; init; }

    /// <summary>
    /// Throw if this configuration could not possibly work, so the failure
    /// lands at construction instead of at the first call.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The key is empty, the base URL is relative, or the timeout is not positive.
    /// </exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new ArgumentException(
                "ChatlyClientOptions.ApiKey is empty. Pass the ck_… workspace API key.",
                nameof(ApiKey));
        }

        if (BaseUrl is null || !BaseUrl.IsAbsoluteUri)
        {
            throw new ArgumentException(
                "ChatlyClientOptions.BaseUrl must be an absolute URI, e.g. https://api.chatlychat.com.",
                nameof(BaseUrl));
        }

        if (Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "ChatlyClientOptions.Timeout must be greater than zero.",
                nameof(Timeout));
        }

        if (PinnedIdempotencyKey is not null && PinnedIdempotencyKey.Length == 0)
        {
            throw new ArgumentException(
                "ChatlyClientOptions.PinnedIdempotencyKey must be null or a non-empty string.",
                nameof(PinnedIdempotencyKey));
        }

        Retry.Validate();
    }
}

/// <summary>
/// When the SDK tries again, and how long it waits first.
/// </summary>
/// <remarks>
/// <para>
/// Only requests that are safe to repeat are retried: a transport failure
/// (DNS, connect, timeout), a 429, or a 5xx. A 4xx that is not 429 is never
/// retried — it will fail identically every time.
/// </para>
/// <para>
/// A retried write re-sends the <i>same</i> <c>Idempotency-Key</c> as the
/// attempt that failed. That is the whole reason the header exists.
/// </para>
/// </remarks>
public sealed record ChatlyRetryPolicy
{
    /// <summary>Three attempts, 250 ms base backoff, full jitter, honouring <c>Retry-After</c>.</summary>
    public static ChatlyRetryPolicy Default { get; } = new ChatlyRetryPolicy();

    /// <summary>One attempt and no backoff — retries off.</summary>
    public static ChatlyRetryPolicy None { get; } = new ChatlyRetryPolicy { MaxAttempts = 1 };

    /// <summary>
    /// Total attempts, including the first. 3 by default; 1 disables retrying.
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>Backoff before the second attempt. Default 250 ms.</summary>
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Ceiling on computed backoff. Default 8 seconds.</summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>Growth factor per attempt. Default 2.0 (exponential).</summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    /// Add jitter to the computed backoff, so a fleet of workers that all hit
    /// a 429 at once does not retry in lockstep. Default true.
    /// </summary>
    /// <remarks>
    /// Full jitter: the wait is a uniform random value in
    /// <c>[0, computed backoff]</c>. It is not applied to a server-supplied
    /// <c>Retry-After</c>, which is an instruction rather than a guess.
    /// </remarks>
    public bool Jitter { get; init; } = true;

    /// <summary>
    /// Wait exactly as long as a <c>Retry-After</c> response header asks,
    /// rather than using the computed backoff. Default true.
    /// </summary>
    public bool RespectRetryAfter { get; init; } = true;

    /// <summary>Throw if the policy is self-contradictory.</summary>
    /// <exception cref="ArgumentException">Any field is out of range.</exception>
    public void Validate()
    {
        if (MaxAttempts < 1)
        {
            throw new ArgumentException("ChatlyRetryPolicy.MaxAttempts must be at least 1.", nameof(MaxAttempts));
        }

        if (InitialBackoff < TimeSpan.Zero || MaxBackoff < TimeSpan.Zero)
        {
            throw new ArgumentException("ChatlyRetryPolicy backoff values cannot be negative.", nameof(InitialBackoff));
        }

        if (BackoffMultiplier < 1.0)
        {
            throw new ArgumentException("ChatlyRetryPolicy.BackoffMultiplier must be at least 1.0.", nameof(BackoffMultiplier));
        }
    }
}
