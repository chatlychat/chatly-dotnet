using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Tests;

/// <summary>One request as the SDK actually sent it.</summary>
public sealed class RecordedRequest
{
    public RecordedRequest(HttpMethod method, Uri uri, IReadOnlyDictionary<string, string> headers, string? body)
    {
        Method = method;
        Uri = uri;
        Headers = headers;
        Body = body;
    }

    public HttpMethod Method { get; }

    public Uri Uri { get; }

    /// <summary>Headers, lower-cased, joined by comma where repeated.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    public string? Body { get; }

    public string? Header(string name)
        => Headers.TryGetValue(name.ToLowerInvariant(), out var value) ? value : null;

    public string Verb => Method.Method;

    public string PathAndQuery => Uri.PathAndQuery;

    public string Path => Uri.AbsolutePath;
}

/// <summary>What the fake transport should answer with.</summary>
public sealed class FakeResponse
{
    public FakeResponse(int status, string body = "{}", IDictionary<string, string>? headers = null)
    {
        Status = status;
        Body = body;
        Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public int Status { get; }

    public string Body { get; }

    public IDictionary<string, string> Headers { get; }

    public static FakeResponse Ok(string body = "{}") => new FakeResponse(200, body);

    public static FakeResponse Page(string items = "[]", string? nextCursor = null)
        => Ok($"{{\"items\":{items},\"nextCursor\":{(nextCursor is null ? "null" : $"\"{nextCursor}\"")}}}");

    public static FakeResponse Error(int status, string code, string message, string? requestId = "req_test")
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (requestId is not null)
        {
            headers["x-request-id"] = requestId;
        }

        // The envelope the API's exception filter actually sends: the code and
        // message are NESTED under `error`. A fixture that invents a
        // top-level shape makes the suite agree with the SDK instead of with
        // the server.
        return new FakeResponse(
            status,
            $"{{\"error\":{{\"code\":\"{code}\",\"message\":\"{message}\"}}}}",
            headers);
    }
}

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers from a script and records
/// what it was asked.
/// </summary>
/// <remarks>
/// No test in this suite reaches the network, needs an API key that works, or
/// needs a running API. Everything the SDK does over HTTP is observed here.
/// </remarks>
public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<RecordedRequest, int, FakeResponse> _responder;
    private readonly List<RecordedRequest> _requests = new List<RecordedRequest>();

    public FakeHandler(Func<RecordedRequest, int, FakeResponse> responder) => _responder = responder;

    /// <summary>Answer every request the same way.</summary>
    public FakeHandler(FakeResponse response) : this((_, _) => response)
    {
    }

    /// <summary>Answer each request from the queue, in order.</summary>
    public static FakeHandler Sequence(params FakeResponse[] responses)
        => new FakeHandler((_, index) => responses[Math.Min(index, responses.Length - 1)]);

    public IReadOnlyList<RecordedRequest> Requests => _requests;

    public RecordedRequest Last => _requests[_requests.Count - 1];

    public int CallCount => _requests.Count;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string? body = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key.ToLowerInvariant()] = string.Join(",", header.Value);
        }

        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                headers[header.Key.ToLowerInvariant()] = string.Join(",", header.Value);
            }
        }

        var recorded = new RecordedRequest(request.Method, request.RequestUri!, headers, body);
        var index = _requests.Count;
        _requests.Add(recorded);

        var planned = _responder(recorded, index);
        var response = new HttpResponseMessage((HttpStatusCode)planned.Status)
        {
            Content = new StringContent(planned.Body, System.Text.Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };

        foreach (var header in planned.Headers)
        {
            if (!response.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return response;
    }
}

/// <summary>A handler that fails the way a dead host does.</summary>
public sealed class ThrowingHandler : HttpMessageHandler
{
    private readonly Func<Exception> _failure;

    public ThrowingHandler(Func<Exception> failure) => _failure = failure;

    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        throw _failure();
    }
}

/// <summary>Shared construction so every check builds a client the same way.</summary>
public static class Fake
{
    /// <summary>A client over a fake transport, with retry backoff that does not sleep.</summary>
    public static ChatlyClient Client(
        HttpMessageHandler handler,
        ChatlyClientOptions? options = null,
        bool instantRetries = true)
    {
        var resolved = options ?? new ChatlyClientOptions { ApiKey = "ck_test_key" };
        if (instantRetries)
        {
            // A test seam, not production behaviour: without it the retry
            // checks would really wait out the backoff.
            resolved = resolved with { DelayAsync = (_, _) => Task.CompletedTask };
        }

        return new ChatlyClient(new HttpClient(handler), resolved);
    }

    public static IReadOnlyList<string> QueryValues(RecordedRequest request, string name)
    {
        var query = request.Uri.Query.TrimStart('?');
        return query.Length == 0
            ? Array.Empty<string>()
            : query.Split('&')
                .Select(part => part.Split('=', 2))
                .Where(pair => Uri.UnescapeDataString(pair[0]) == name)
                .Select(pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty)
                .ToList();
    }
}
