using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly;

/// <summary>
/// One HTTP exchange that has already succeeded: the status, the raw body and
/// the request id.
/// </summary>
internal readonly struct ChatlyResponse
{
    internal ChatlyResponse(int status, string body, string? requestId)
    {
        Status = status;
        Body = body;
        RequestId = requestId;
    }

    internal int Status { get; }

    internal string Body { get; }

    internal string? RequestId { get; }

    /// <summary>
    /// Decode the body as JSON, detached from its document so it stays valid
    /// after this method returns. An empty body (204, or a write that answers
    /// with nothing) decodes as JSON null.
    /// </summary>
    internal JsonElement Json(string path)
    {
        if (string.IsNullOrWhiteSpace(Body))
        {
            return Transport.NullElement;
        }

        try
        {
            using var doc = JsonDocument.Parse(Body);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new ChatlyException(
                $"{path} returned HTTP {Status} with a body that is not JSON: {Truncate(Body)}",
                Status,
                "invalid_json_response",
                RequestId,
                Body,
                ex);
        }
    }

    internal T? Deserialize<T>(string path)
    {
        if (string.IsNullOrWhiteSpace(Body))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(Body, Transport.SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new ChatlyException(
                $"{path} returned HTTP {Status}, which did not deserialize into {typeof(T).Name}: {Truncate(Body)}",
                Status,
                "invalid_json_response",
                RequestId,
                Body,
                ex);
        }
    }

    private static string Truncate(string value)
        => value.Length <= 200 ? value : value.Substring(0, 200) + "…";
}

/// <summary>
/// Auth, idempotency, retries, timeouts and error mapping — everything every
/// request does, in one place.
/// </summary>
/// <remarks>
/// Internal on purpose: a caller who needs an un-modelled route uses
/// <see cref="ChatlyClient.RequestAsync(HttpMethod, string, ChatlyQuery, object, CancellationToken)"/>,
/// which comes through here and therefore gets the same behaviour.
/// </remarks>
internal sealed class Transport : IDisposable
{
    /// <summary>The SDK version, as it appears in the <c>User-Agent</c>.</summary>
    internal const string Version = "0.1.0";

    internal static readonly JsonElement NullElement = JsonDocument.Parse("null").RootElement.Clone();

    internal static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        // The API speaks camelCase (`externalId`, `nextCursor`), so a C#
        // property or an anonymous-type member in PascalCase lands on the
        // right field without the caller spelling it out. Dictionary keys are
        // left exactly as given — those are data, not identifiers.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly ChatlyClientOptions _options;
    private readonly string _userAgent;
    private readonly Random _random = new Random();

    internal Transport(HttpClient http, bool ownsHttpClient, ChatlyClientOptions options)
    {
        _http = http;
        _ownsHttpClient = ownsHttpClient;
        _options = options;
        _userAgent = BuildUserAgent(options.UserAgentSuffix);
    }

    internal ChatlyClientOptions Options => _options;

    internal string UserAgent => _userAgent;

    /// <summary>Build a transport that owns a private <see cref="HttpClient"/>.</summary>
    internal static Transport Create(ChatlyClientOptions options)
        => new Transport(new HttpClient(), ownsHttpClient: true, options);

    /// <summary>
    /// A copy of this transport with a different idempotency-key setting, over
    /// the <i>same</i> <see cref="HttpClient"/> — which it does not own, so
    /// disposing the derived client cannot close the original's connections.
    /// </summary>
    internal Transport WithOptions(ChatlyClientOptions options)
        => new Transport(_http, ownsHttpClient: false, options);

    /// <summary>
    /// Send one request, retrying what is safe to retry, and throw a typed
    /// exception on any non-2xx that survives the retries.
    /// </summary>
    internal async Task<ChatlyResponse> SendAsync(
        HttpMethod method,
        string path,
        ChatlyQuery? query,
        object? body,
        CancellationToken cancellationToken)
    {
        var uri = BuildUri(path, query);
        var isWrite = IsWrite(method);
        var payload = SerializeBody(method, body);

        // Minted ONCE, outside the attempt loop. Every retry of this call
        // carries the same Idempotency-Key, which is the only reason a
        // retried write cannot double-apply. A key minted per attempt would
        // make the header actively misleading.
        var idempotencyKey = isWrite
            ? _options.PinnedIdempotencyKey ?? Guid.NewGuid().ToString()
            : null;

        var policy = _options.Retry;
        var attempt = 0;

        while (true)
        {
            attempt++;
            cancellationToken.ThrowIfCancellationRequested();

            TimeSpan? retryAfter = null;
            int status;
            string responseBody;
            string? requestId;

            try
            {
                using var request = BuildRequest(method, uri, payload, idempotencyKey);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_options.Timeout);

                using var response = await _http
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)
                    .ConfigureAwait(false);

                status = (int)response.StatusCode;
                requestId = ReadRequestId(response);
                retryAfter = ReadRetryAfter(response);
                responseBody = response.Content is null
                    ? string.Empty
                    : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller cancelled. Not our failure to dress up.
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
            {
                if (attempt < policy.MaxAttempts)
                {
                    await DelayAsync(Backoff(attempt, null), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var reason = ex is OperationCanceledException
                    ? $"timed out after {_options.Timeout.TotalSeconds:0.##}s"
                    : ex.Message;
                throw new ChatlyTransportException(
                    $"{method.Method} {uri} failed after {attempt} attempt(s): {reason}",
                    ex);
            }

            if (status is >= 200 and < 300)
            {
                return new ChatlyResponse(status, responseBody, requestId);
            }

            // Retry only what repeating could fix: 429 and 5xx. Never another
            // 4xx — it will fail identically, and retrying a 422 just spends
            // the caller's rate limit on the same rejection.
            if (ShouldRetry(status) && attempt < policy.MaxAttempts)
            {
                await DelayAsync(Backoff(attempt, retryAfter), cancellationToken).ConfigureAwait(false);
                continue;
            }

            throw ChatlyErrorFactory.Create(status, responseBody, requestId, retryAfter);
        }
    }

    internal Uri BuildUri(string path, ChatlyQuery? query)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/')
        {
            throw new ArgumentException($"Path must start with '/', got \"{path}\".", nameof(path));
        }

        var baseUrl = _options.BaseUrl.GetLeftPart(UriPartial.Path).TrimEnd('/');
        var queryString = query is null ? string.Empty : query.ToString();
        var suffix = queryString.Length == 0 ? string.Empty : "?" + queryString;
        return new Uri(baseUrl + path + suffix, UriKind.Absolute);
    }

    internal static bool IsWrite(HttpMethod method)
        => method == HttpMethod.Post || method == HttpMethod.Put || method == HttpMethod.Delete
            || string.Equals(method.Method, "PATCH", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldRetry(int status) => status == 429 || status >= 500;

    private string? SerializeBody(HttpMethod method, object? body)
    {
        if (body is not null)
        {
            // A string is sent verbatim: it is already JSON. Anything else is
            // serialized. This is documented on every method that takes a body.
            return body is string raw ? raw : JsonSerializer.Serialize(body, SerializerOptions);
        }

        // POST/PUT/PATCH with no body still needs one: the API's JSON body
        // parser rejects a zero-length body when the content type says JSON,
        // so a bodyless write would be a 400 instead of the no-argument call
        // the route actually accepts. DELETE sends no content at all.
        return method == HttpMethod.Post || method == HttpMethod.Put
            || string.Equals(method.Method, "PATCH", StringComparison.OrdinalIgnoreCase)
            ? "{}"
            : null;
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, Uri uri, string? payload, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);

        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        if (payload is not null)
        {
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static string BuildUserAgent(string? suffix)
    {
        var agent = "chatly-sdk-dotnet/" + Version;
        return string.IsNullOrWhiteSpace(suffix) ? agent : agent + " " + suffix.Trim();
    }

    private static string? ReadRequestId(HttpResponseMessage response)
    {
        foreach (var name in new[] { "x-request-id", "request-id", "x-correlation-id" })
        {
            if (response.Headers.TryGetValues(name, out var values))
            {
                foreach (var value in values)
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        return value;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Parse <c>Retry-After</c> in either permitted form: delay-seconds, or an
    /// HTTP-date which is converted to a delay from now.
    /// </summary>
    private TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Retry-After", out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var text = value.Trim();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                && seconds >= 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }

            if (DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var when))
            {
                var now = _options.Clock?.Invoke() ?? DateTimeOffset.UtcNow;
                var delay = when - now;
                return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
            }
        }

        return null;
    }

    /// <summary>
    /// How long to wait before attempt <c>n+1</c>. A server-supplied
    /// <c>Retry-After</c> wins — it is an instruction, not a guess, and jitter
    /// is not applied to it.
    /// </summary>
    private TimeSpan Backoff(int attempt, TimeSpan? retryAfter)
    {
        var policy = _options.Retry;
        if (retryAfter.HasValue && policy.RespectRetryAfter)
        {
            return retryAfter.Value;
        }

        var scale = Math.Pow(policy.BackoffMultiplier, attempt - 1);
        var millis = Math.Min(policy.InitialBackoff.TotalMilliseconds * scale, policy.MaxBackoff.TotalMilliseconds);

        if (policy.Jitter)
        {
            double sample;
            lock (_random)
            {
                sample = _random.NextDouble();
            }

            // Full jitter: uniform in [0, millis]. A fleet that all hit the
            // same 429 does not then retry in lockstep.
            millis *= sample;
        }

        return TimeSpan.FromMilliseconds(millis);
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var hook = _options.DelayAsync;
        return hook is not null
            ? hook(delay, cancellationToken)
            : Task.Delay(delay, cancellationToken);
    }

    /// <summary>GET a route and return its decoded JSON body.</summary>
    internal async Task<JsonElement> GetJsonAsync(string path, ChatlyQuery? query, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, path, query, null, cancellationToken).ConfigureAwait(false);
        return response.Json("GET " + path);
    }

    /// <summary>
    /// Write to a route and return its decoded JSON body. An empty response
    /// body — a 204, or a write that answers with nothing — decodes as JSON
    /// null rather than throwing.
    /// </summary>
    internal async Task<JsonElement> WriteJsonAsync(
        HttpMethod method,
        string path,
        object? body,
        ChatlyQuery? query,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(method, path, query, body, cancellationToken).ConfigureAwait(false);
        return response.Json(method.Method + " " + path);
    }

    /// <summary>GET one page of a cursor-paginated route.</summary>
    internal async Task<Page<JsonElement>> GetPageAsync(
        string path,
        ChatlyQuery? query,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, path, query, null, cancellationToken).ConfigureAwait(false);
        return Pager.ParsePage(response.Json("GET " + path), "GET " + path);
    }

    /// <summary>
    /// Walk every page of a cursor-paginated route, re-rendering the caller's
    /// options with each new cursor.
    /// </summary>
    internal IAsyncEnumerable<JsonElement> WalkPagesAsync(
        string path,
        ListOptions? options,
        CancellationToken cancellationToken)
        => Pager.WalkAsync(
            (cursor, token) => GetPageAsync(
                path,
                ListOptions.QueryFor(cursor is null ? options : (options ?? new ListOptions()) with { Cursor = cursor }),
                token),
            cancellationToken);

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}
