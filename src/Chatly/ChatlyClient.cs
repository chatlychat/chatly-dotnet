using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Chatly.Resources;

namespace Chatly;

/// <summary>
/// The Chatly API, from your backend.
/// </summary>
/// <remarks>
/// <para>
/// Authenticates with a workspace API key (<c>ck_…</c>), which is a bearer
/// credential for the whole workspace — so this class belongs in server-side
/// code and never in a browser or a mobile app.
/// </para>
/// <para>
/// One instance is thread-safe and meant to be long-lived: it holds an
/// <see cref="HttpClient"/>, and creating one per request exhausts sockets.
/// Register it as a singleton, or as a typed client with
/// <c>AddHttpClient&lt;ChatlyClient&gt;()</c>.
/// </para>
/// <example>
/// <code>
/// var chatly = new ChatlyClient(Environment.GetEnvironmentVariable("CHATLY_API_KEY")!);
///
/// var me = await chatly.WhoAmIAsync(ct);
/// var page = await chatly.Contacts.ListAsync(ct);
/// await foreach (var contact in chatly.Contacts.ListAllAsync(ct))
/// {
///     Console.WriteLine(contact.GetStringOrNull("email"));
/// }
/// </code>
/// </example>
/// </remarks>
public sealed class ChatlyClient : IDisposable
{
    private readonly Transport _transport;

    /// <summary>
    /// Build a client for the production API with default timeout and retries.
    /// </summary>
    /// <param name="apiKey">The workspace API key (<c>ck_…</c>).</param>
    /// <exception cref="ArgumentException">The key is empty.</exception>
    public ChatlyClient(string apiKey)
        : this(new ChatlyClientOptions { ApiKey = apiKey })
    {
    }

    /// <summary>
    /// Build a client with explicit options, over an <see cref="HttpClient"/>
    /// the client creates and owns.
    /// </summary>
    /// <param name="options">The configuration. <see cref="ChatlyClientOptions.ApiKey"/> is required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">The options are unusable — see <see cref="ChatlyClientOptions.Validate"/>.</exception>
    public ChatlyClient(ChatlyClientOptions options)
        : this(OwnedTransport(options))
    {
    }

    /// <summary>
    /// Build a client over an <see cref="HttpClient"/> you supply — the
    /// constructor <c>AddHttpClient&lt;ChatlyClient&gt;()</c> uses.
    /// </summary>
    /// <remarks>
    /// The supplied client is never modified: its <c>Timeout</c>,
    /// <c>BaseAddress</c> and default headers are left alone, because it may
    /// be shared, and the per-attempt timeout is applied with a linked
    /// cancellation token instead. It is also not disposed by
    /// <see cref="Dispose"/> — whoever created it owns its lifetime, which for
    /// a typed client is <c>IHttpClientFactory</c>.
    /// </remarks>
    /// <param name="httpClient">The HTTP client to send through.</param>
    /// <param name="options">The configuration.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public ChatlyClient(HttpClient httpClient, ChatlyClientOptions options)
        : this(BorrowedTransport(httpClient, options))
    {
    }

    /// <summary>
    /// The one place the resource bag is built. Every public constructor and
    /// <see cref="WithIdempotencyKey(string)"/> comes through here, so a
    /// resource cannot be left bound to a different client's transport — the
    /// Node SDK had exactly that bug, where a derived client's `search`
    /// methods still went through the parent's request path.
    /// </summary>
    private ChatlyClient(Transport transport)
    {
        _transport = transport;
        Contacts = new ContactsResource(transport);
        Conversations = new ConversationsResource(transport);
        Tickets = new TicketsResource(transport);
        Macros = new MacrosResource(transport);
        Kb = new KnowledgeBaseResource(transport);
        Search = new SearchResource(transport);
        Events = new EventsResource(transport);
        Webhooks = new WebhooksResource(transport);
        Reports = new ReportsResource(transport);
        Channels = new ChannelsResource(transport);
        Billing = new BillingResource(transport);
        Workspace = new WorkspaceResource(transport);
        Ai = new AiResource(transport);
        Bots = new BotsResource(transport);
        BotRoutes = new BotRoutesResource(transport);
        Brands = new BrandsResource(transport);
        CanvasSteps = new CanvasStepsResource(transport);
        CustomerCards = new CustomerCardsResource(transport);
        CustomerCardEndpoints = new CustomerCardEndpointsResource(transport);
        DocsSources = new DocsSourcesResource(transport);
        EmailTemplates = new EmailTemplatesResource(transport);
        KnowledgeGaps = new KnowledgeGapsResource(transport);
        Triggers = new TriggersResource(transport);
    }

    private static Transport OwnedTransport(ChatlyClientOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        options.Validate();
        return Transport.Create(options);
    }

    private static Transport BorrowedTransport(HttpClient httpClient, ChatlyClientOptions options)
    {
        if (httpClient is null)
        {
            throw new ArgumentNullException(nameof(httpClient));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        options.Validate();
        return new Transport(httpClient, ownsHttpClient: false, options);
    }

    /// <summary>Where this client is sending — the production host unless overridden.</summary>
    public Uri BaseUrl => _transport.Options.BaseUrl;

    /// <summary>The <c>User-Agent</c> this client sends, e.g. <c>chatly-sdk-dotnet/0.1.0</c>.</summary>
    public string UserAgent => _transport.UserAgent;

    /// <summary>The <c>Idempotency-Key</c> pinned across writes, or null when each write mints its own.</summary>
    public string? PinnedIdempotencyKey => _transport.Options.PinnedIdempotencyKey;

    /// <summary>People: upsert, merge, tag, unsubscribe, read their journey.</summary>
    public ContactsResource Contacts { get; }

    /// <summary>Threads: read them, reply, schedule a reply, hand one to or take one from an AI agent.</summary>
    public ConversationsResource Conversations { get; }

    /// <summary>Tickets: list, comment, merge, split, bulk-update.</summary>
    public TicketsResource Tickets { get; }

    /// <summary>Saved replies.</summary>
    public MacrosResource Macros { get; }

    /// <summary>Help-centre articles and collections.</summary>
    public KnowledgeBaseResource Kb { get; }

    /// <summary>Full-text search across contacts, conversations, messages, tickets and the KB.</summary>
    public SearchResource Search { get; }

    /// <summary>Customer-data-platform events: track them, read them back.</summary>
    public EventsResource Events { get; }

    /// <summary>
    /// Webhook <i>deliveries</i>: what we sent you, and a redelivery button.
    /// For verifying a delivery's signature, see <see cref="ChatlyWebhooks"/>.
    /// </summary>
    public WebhooksResource Webhooks { get; }

    /// <summary>Reporting aggregates.</summary>
    public ReportsResource Reports { get; }

    /// <summary>The workspace's channels.</summary>
    public ChannelsResource Channels { get; }

    /// <summary>Plans and current usage.</summary>
    public BillingResource Billing { get; }

    /// <summary>Workspace settings, labels, inboxes, team members, agent skills, key scopes and the credential census.</summary>
    public WorkspaceResource Workspace { get; }

    /// <summary>AI provider keys, the spend meter, tone rules and custom tools.</summary>
    public AiResource Ai { get; }

    /// <summary>AI agents and externally-hosted agents, with their delivery log.</summary>
    public BotsResource Bots { get; }

    /// <summary>The first-match-wins rules that pick which AI agent answers a visitor.</summary>
    public BotRoutesResource BotRoutes { get; }

    /// <summary>Brands — optional sub-tenants inside one workspace.</summary>
    public BrandsResource Brands { get; }

    /// <summary>Pending interactive widget steps, waiting on your backend for the next canvas.</summary>
    public CanvasStepsResource CanvasSteps { get; }

    /// <summary>Sidebar panels your own backend serves, live, when an agent opens a thread.</summary>
    public CustomerCardsResource CustomerCards { get; }

    /// <summary>Per-URL signing secrets for those panels, with a staged rotation.</summary>
    public CustomerCardEndpointsResource CustomerCardEndpoints { get; }

    /// <summary>Documentation providers pulled into the knowledge base on a schedule.</summary>
    public DocsSourcesResource DocsSources { get; }

    /// <summary>The platform's own transactional emails, and this workspace's overrides.</summary>
    public EmailTemplatesResource EmailTemplates { get; }

    /// <summary>Questions real traffic asked that the knowledge base could not answer.</summary>
    public KnowledgeGapsResource KnowledgeGaps { get; }

    /// <summary>Event-driven rules: conditions over a payload, and actions.</summary>
    public TriggersResource Triggers { get; }

    /// <summary>
    /// Describe the credential this client is using: which workspace it
    /// belongs to and which scopes it was granted.
    /// </summary>
    /// <remarks>
    /// <para><b>Make this your first call.</b></para>
    /// <para>
    /// It turns a mis-scoped key into an obvious error at integration time
    /// rather than a puzzling 403 from an unrelated endpoint in production.
    /// </para>
    /// <para>
    /// It is one of only two routes on the api-keys controller that accept an
    /// API key — the other is
    /// <see cref="WorkspaceResource.KeyScopesAsync(CancellationToken)"/>,
    /// which lists the scopes a key can be granted. Everything else there
    /// stays closed: minting or listing keys with a key would let a leaked
    /// read-only key escalate itself. (This comment claimed <c>whoami</c> was
    /// the <i>only</i> one until 2026-10-14, which was never true of
    /// <c>/scopes</c> once it was opened.)
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The decoded response — <c>workspaceId</c> and <c>scopes</c>. Bind it
    /// with <see cref="ChatlyJson.As{T}(JsonElement)"/> if you want a type.
    /// </returns>
    public Task<JsonElement> WhoAmIAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/api-keys/whoami", null, cancellationToken);

    /// <summary>
    /// Whoever is calling — a person and their active membership, or, for an
    /// API key, the key. <c>GET /v1/me</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The response is <b>discriminated on <c>principal</c></b>. From this
    /// SDK it is always <c>"api_key"</c>, and the object beside it is the same
    /// one <see cref="WhoAmIAsync(CancellationToken)"/> returns — there is no
    /// user behind a key, and this handler never looks for one. (It also
    /// never touches the database on the key path, which is why it is cheap
    /// enough to poll.)
    /// </para>
    /// <para>
    /// So why have both? Because this route carries <b>no
    /// <c>@RequirePermission</c> at all</b>, where <c>whoami</c> wants
    /// <c>workspace:read</c>. That makes this <b>the liveness check</b>: a
    /// key with no useful scope still gets a 200 here, and the answer is
    /// true. Reach for it when you want to know whether a credential is live
    /// and what it was granted, and for <see cref="WhoAmIAsync(CancellationToken)"/>
    /// when you are already sure the key can read the workspace.
    /// </para>
    /// <para>
    /// Withholding a credential's own description from the credential is how
    /// an integrator ends up learning their scopes by provoking 403s, which
    /// is the reasoning on the handler for leaving it ungated. It is also
    /// <c>@AllowWhenSuspended</c>, so it answers for a suspended workspace.
    /// </para>
    /// <para>
    /// Only this handler on the <c>/v1/me</c> controller accepts a key. The
    /// profile patch, the session list and the notification preferences each
    /// write or enumerate a <c>users</c> row, and a key has none — so they
    /// answer 403 and this SDK ships no method for them.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// <c>{ principal: "api_key", … }</c> for a key: its workspace and its
    /// grant list.
    /// </returns>
    public Task<JsonElement> MeAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync("/v1/me", null, cancellationToken);

    /// <summary>
    /// A view of this client that pins one <c>Idempotency-Key</c> across every
    /// write, for retrying the <i>same</i> logical operation from your code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The returned client shares this one's <see cref="HttpClient"/> and
    /// leaves this one unchanged. Same key plus same body replays the first
    /// response; same key plus a different body is a 409.
    /// </para>
    /// <para>
    /// You rarely need this for network retries — the SDK already re-sends the
    /// same key when it retries internally. Reach for it when <i>your</i> code
    /// is the thing retrying, e.g. a job that may run twice.
    /// </para>
    /// <example>
    /// <code>
    /// await chatly.WithIdempotencyKey($"refund-{orderId}")
    ///             .Contacts.UpsertAsync(new ContactInput { Email = email }, ct);
    /// </code>
    /// </example>
    /// </remarks>
    /// <param name="idempotencyKey">The key to send on every write. Must be non-empty.</param>
    /// <returns>A derived client. Disposing it does not dispose this one's HTTP client.</returns>
    /// <exception cref="ArgumentException">The key is null or empty.</exception>
    public ChatlyClient WithIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrEmpty(idempotencyKey))
        {
            throw new ArgumentException(
                "WithIdempotencyKey: the key must be a non-empty string.",
                nameof(idempotencyKey));
        }

        return new ChatlyClient(
            _transport.WithOptions(_transport.Options with { PinnedIdempotencyKey = idempotencyKey }));
    }

    /// <summary>
    /// Call any key-accessible route, including the ones this SDK has no typed
    /// method for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 159 key-accessible routes have a typed method here, and the API has
    /// more than that — <c>scripts/report-api-key-surface.mjs</c> counted 174
    /// open out of 909 on 2026-10-14, and the shared manifest names three it
    /// has not caught up with. This is how you reach the rest without
    /// abandoning the SDK: it goes through the same authentication,
    /// idempotency-key minting, retries and typed errors as every typed
    /// method. Do not read either number as fixed; the open count grows as
    /// routes are opened to key auth.
    /// </para>
    /// <para>
    /// A route that does <i>not</i> accept an API key answers 403 no matter
    /// what you pass — those need a signed-in person, and no header will
    /// substitute.
    /// </para>
    /// <example>
    /// <code>
    /// var json = await chatly.RequestAsync(
    ///     HttpMethod.Post,
    ///     "/v1/tickets/42/time-entries",
    ///     query: null,
    ///     body: new { minutes = 15, note = "call" },
    ///     cancellationToken: ct);
    /// </code>
    /// </example>
    /// </remarks>
    /// <param name="method">The HTTP method. POST/PUT/PATCH/DELETE get an <c>Idempotency-Key</c>.</param>
    /// <param name="path">An absolute API path starting with <c>/</c>, e.g. <c>/v1/labels</c>.</param>
    /// <param name="query">Query parameters, or null. Repeated names stay repeated.</param>
    /// <param name="body">
    /// The request body. An object is serialized as JSON; a <see cref="string"/>
    /// is sent verbatim (it is assumed to be JSON already); null sends
    /// <c>{}</c> for POST/PUT/PATCH and nothing for GET/DELETE.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The decoded JSON response; JSON null for an empty body.</returns>
    /// <exception cref="ChatlyException">The API refused, or never answered.</exception>
    public Task<JsonElement> RequestAsync(
        HttpMethod method,
        string path,
        ChatlyQuery? query = null,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        if (method is null)
        {
            throw new ArgumentNullException(nameof(method));
        }

        return _transport.WriteJsonAsync(method, path, body, query, cancellationToken);
    }

    /// <summary>
    /// <see cref="RequestAsync(HttpMethod, string, ChatlyQuery, object, CancellationToken)"/>,
    /// deserialized into your own type.
    /// </summary>
    /// <typeparam name="T">The type to bind the response to.</typeparam>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">An absolute API path starting with <c>/</c>.</param>
    /// <param name="query">Query parameters, or null.</param>
    /// <param name="body">The request body, or null.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The bound response, or default for an empty body.</returns>
    public async Task<T?> RequestAsync<T>(
        HttpMethod method,
        string path,
        ChatlyQuery? query = null,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        if (method is null)
        {
            throw new ArgumentNullException(nameof(method));
        }

        var response = await _transport
            .SendAsync(method, path, query, body, cancellationToken)
            .ConfigureAwait(false);
        return response.Deserialize<T>(method.Method + " " + path);
    }

    /// <summary>
    /// One page of any cursor-paginated route, including routes with no typed
    /// method here.
    /// </summary>
    /// <param name="path">An absolute API path starting with <c>/</c>.</param>
    /// <param name="options">Cursor, limit, filters and sort.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page as returned.</returns>
    /// <exception cref="ChatlyException">The response was not a <c>{ items, nextCursor }</c> page.</exception>
    public Task<Page<JsonElement>> RequestPageAsync(
        string path,
        ListOptions? options = null,
        CancellationToken cancellationToken = default)
        => _transport.GetPageAsync(path, ListOptions.QueryFor(options), cancellationToken);

    /// <summary>
    /// Every item of any cursor-paginated route, fetching pages as you consume
    /// them.
    /// </summary>
    /// <param name="path">An absolute API path starting with <c>/</c>.</param>
    /// <param name="options">Limit, filters and sort. <see cref="ListOptions.Cursor"/> is the starting point.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>An async stream over every item.</returns>
    public IAsyncEnumerable<JsonElement> RequestAllAsync(
        string path,
        ListOptions? options = null,
        CancellationToken cancellationToken = default)
        => _transport.WalkPagesAsync(path, options, cancellationToken);

    /// <summary>
    /// Dispose the <see cref="HttpClient"/> this client created.
    /// </summary>
    /// <remarks>
    /// A no-op when the <see cref="HttpClient"/> was supplied by you or by
    /// <c>IHttpClientFactory</c>, and on a client returned by
    /// <see cref="WithIdempotencyKey(string)"/> — in both cases something else
    /// owns it.
    /// </remarks>
    public void Dispose() => _transport.Dispose();
}
