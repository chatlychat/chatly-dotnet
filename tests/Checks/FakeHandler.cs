using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
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

/// <summary>
/// The top-level shape of a successful response from one route.
/// </summary>
/// <remarks>
/// <para>
/// Four values, and every route on the surface is exactly one of them. See
/// <c>$shapeMeans</c> in <c>packages/sdk-spec/surface.json</c> for how each was
/// derived from the handler's <c>@ZodResponse</c> schema.
/// </para>
/// <para>
/// Declared per route rather than assumed, because the API sends all four and
/// a client that guesses wrong fails at decode time — in production, against a
/// suite that passed. There is deliberately NO default: see
/// <see cref="SurfaceChecks.SurfaceEntry"/>, where <c>Object</c> being the
/// constructor default left thirteen bare-array routes silently declared as
/// objects, which is how a default that happens to be the commonest value
/// hides wrong rows.
/// </para>
/// </remarks>
public enum ResponseShape
{
    /// <summary>A JSON object: one record, or a summary. 30 routes.</summary>
    Object = 0,

    /// <summary>A bare JSON array: a pagination-exempt list. 14 routes.</summary>
    Array = 1,

    /// <summary>
    /// A <c>{ items, nextCursor }</c> page, with whatever sits beside them.
    /// 10 routes, and exactly the ones carrying <c>paged: true</c>.
    /// </summary>
    Page = 2,

    /// <summary>
    /// No body at all — a real 204. Exactly one route,
    /// <c>macros.delete</c>.
    /// </summary>
    /// <remarks>
    /// Its own value rather than "an object that happens to be empty": the
    /// fake answers it with no content whatsoever, so a decoder that insists
    /// on parsing a body fails here. That is the whole point of the value
    /// existing — <c>{}</c> is a fixture the server never sends for this
    /// route.
    /// </remarks>
    Empty = 3,
}

/// <summary>
/// The one vocabulary for response shapes: the manifest's spelling of a
/// declared shape, and the shape a decoded value <i>actually</i> has.
/// </summary>
/// <remarks>
/// <see cref="Of"/> is the anti-circularity half of the surface proof. The
/// route checks feed each method the shape this package declares and then ask
/// this what came back, so a method that wraps or unwraps — a list that
/// returned <c>page.Items</c> from a page route, or one that reached for
/// <c>items</c> on a bare-array route — disagrees here even though the request
/// it issued was perfect.
/// </remarks>
public static class Shapes
{
    /// <summary>The manifest's lower-case name for a declared shape.</summary>
    public static string Name(ResponseShape shape) => shape switch
    {
        ResponseShape.Array => "array",
        ResponseShape.Page => "page",
        ResponseShape.Object => "object",
        ResponseShape.Empty => "empty",
        _ => throw new CheckFailedException($"no manifest name for response shape {shape}"),
    };

    /// <summary>The shape a value handed back by an SDK method really has.</summary>
    public static string Of(object? value) => value switch
    {
        null => "empty",

        // Judge the BODY, not the C# type. A `Page<T>` exists only because
        // `Pager.ParsePage` accepted the response, but reading `Raw` keeps
        // this assertion about the data that arrived rather than about what
        // the method's signature promised.
        Page<JsonElement> page => OfJson(page.Raw),

        JsonElement element => OfJson(element),
        string => "scalar(string)",
        System.Collections.IEnumerable => "array",
        _ => "scalar(" + value.GetType().Name + ")",
    };

    private static string OfJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => "empty",
        JsonValueKind.Array => "array",
        JsonValueKind.Object => IsPage(element) ? "page" : "object",
        _ => "scalar(" + element.ValueKind + ")",
    };

    /// <summary>
    /// A page is an object carrying an <c>items</c> array and a
    /// <c>nextCursor</c> property. Anything else beside them — <c>hasMore</c>,
    /// <c>total</c>, <c>tier</c>, <c>facets</c> — is still a page.
    /// </summary>
    private static bool IsPage(JsonElement element)
        => element.TryGetProperty("items", out var items)
            && items.ValueKind == JsonValueKind.Array
            && element.TryGetProperty("nextCursor", out _);
}

/// <summary>What the fake transport should answer with.</summary>
public sealed class FakeResponse
{
    public FakeResponse(int status, string? body = "{}", IDictionary<string, string>? headers = null)
    {
        Status = status;
        Body = body;
        Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public int Status { get; }

    /// <summary>
    /// The body, or null for <i>no entity at all</i> — which is not the same
    /// as an empty string. A 204 carries no <c>Content-Type</c> and no
    /// content object, and a fake that attaches an empty JSON document
    /// instead cannot tell a decoder that parses unconditionally from one
    /// that checks first.
    /// </summary>
    public string? Body { get; }

    public IDictionary<string, string> Headers { get; }

    public static FakeResponse Ok(string body = "{}") => new FakeResponse(200, body);

    /// <summary>A <c>{ items, nextCursor }</c> page — ONLY for a route the manifest marks paged.</summary>
    /// <remarks>
    /// Do not reach for this as a general-purpose "success". A fake that
    /// answers every route with a page envelope is why this SDK's suite stayed
    /// green while <c>Macros.ListAsync</c> could not decode a real response:
    /// that route is pagination-exempt and returns a bare array, and the
    /// fixture invented a shape the server never sends. Shapes are declared
    /// per route in <see cref="SurfaceChecks.Entries"/> and diffed against the
    /// manifest's four-valued <c>shape</c> field — and its <c>paged</c> flag —
    /// by <see cref="SurfaceChecks.DeclaredShapesMatchTheManifestAsync"/>.
    /// </remarks>
    public static FakeResponse Page(string items = "[]", string? nextCursor = null)
        => Ok($"{{\"items\":{items},\"nextCursor\":{(nextCursor is null ? "null" : $"\"{nextCursor}\"")}}}");

    /// <summary>A bare JSON array, as a pagination-exempt list route answers.</summary>
    public static FakeResponse Array(string items = "[]") => Ok(items);

    /// <summary>A plain JSON object, the shape of a single-record or summary route.</summary>
    public static FakeResponse Object(string body = "{}") => Ok(body);

    /// <summary>
    /// A real 204: status 204 and <b>no body at all</b>, not an empty JSON
    /// object.
    /// </summary>
    /// <remarks>
    /// One route on the surface answers this — <c>DELETE /v1/macros/{id}</c>
    /// — and it is the only one. A decoder that parses a body unconditionally
    /// throws against this fixture, which is what the
    /// <see cref="ResponseShape.Empty"/> value is for.
    /// </remarks>
    public static FakeResponse NoContent() => new FakeResponse(204, body: null);

    /// <summary>The success body for one declared response shape.</summary>
    /// <remarks>
    /// Exhaustive, with no <c>_ =&gt;</c> arm. A catch-all here would quietly
    /// answer a newly added shape with <c>{}</c> — the same class of mistake
    /// as <see cref="ResponseShape.Object"/> having been the constructor
    /// default.
    /// </remarks>
    /// <exception cref="CheckFailedException">The shape is not one of the four.</exception>
    public static FakeResponse For(ResponseShape shape) => shape switch
    {
        // Non-empty items on purpose: a method that unwraps a page to its
        // items hands back a one-element array rather than an empty one, so
        // the shape assertion sees `array` and not an ambiguous nothing.
        ResponseShape.Page => Page("[{\"id\":\"x_1\"}]"),
        ResponseShape.Array => Array("[{\"id\":\"x_1\"}]"),
        ResponseShape.Object => Object("{\"id\":\"x_1\"}"),
        ResponseShape.Empty => NoContent(),
        _ => throw new CheckFailedException($"no fixture for response shape {shape}"),
    };

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
            // A null body means no content object at all, the way a 204
            // arrives. Attaching an empty StringContent would give it a
            // Content-Type and a zero-length entity, which is a different
            // response.
            Content = planned.Body is null
                ? null
                : new StringContent(planned.Body, System.Text.Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };

        foreach (var header in planned.Headers)
        {
            if (!response.Headers.TryAddWithoutValidation(header.Key, header.Value)
                && response.Content is not null)
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
