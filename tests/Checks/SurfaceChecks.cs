using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Tests;

/// <summary>
/// Every method in <c>packages/sdk-spec/surface.json</c>, driven once, with
/// the HTTP verb, the path and the response shape it produced asserted against
/// the spec.
/// </summary>
/// <remarks>
/// <para>
/// The table below is written out by hand rather than read from
/// <c>surface.json</c>, so that this package is self-contained — and because
/// a check that reads the same file the implementation was written from can
/// only catch a typo, not a misreading. Keep it in step with the spec: 162
/// entries, and the count is asserted. The table itself lives in
/// <c>SurfaceTable.cs</c> — same partial class, split out when 159 rows and
/// these assertions together went past CONTRACT.md §9's 650-line ceiling.
/// </para>
/// <para>
/// This is what catches a wrong path. A path typo is invisible to every other
/// test here — the fake transport answers anything — and arrives as a 404 in
/// someone else's integration.
/// </para>
/// </remarks>
public static partial class SurfaceChecks
{

    /// <summary>
    /// The <see cref="ResponseShape.Empty"/> fixture really sends nothing: 204,
    /// no content object, no <c>Content-Type</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A check on the fake rather than on the SDK, and it is here because the
    /// fake is the thing that has lied before. If
    /// <see cref="FakeResponse.NoContent"/> quietly attached an empty JSON
    /// document, the <c>macros.delete</c> row would prove nothing — a decoder
    /// that parses unconditionally would succeed against <c>""</c> as readily
    /// as against <c>{}</c>, and the only route on this surface that answers
    /// no body would be tested with a body.
    /// </para>
    /// <para>
    /// Driven through <see cref="System.Net.Http.HttpClient"/> directly, not
    /// through the SDK, because what is being asserted is the HTTP message.
    /// </para>
    /// </remarks>
    public static async Task TheEmptyFixtureReallySendsNoBody()
    {
        var handler = new FakeHandler(FakeResponse.For(ResponseShape.Empty));
        using var http = new System.Net.Http.HttpClient(handler);
        using var response = await http.GetAsync(new Uri("https://api.test.invalid/v1/macros/m_1"));

        Expect.Equal(204, (int)response.StatusCode, "the empty fixture answers 204");

        /*
         * No entity: no Content-Type, and nothing to read.
         *
         * `HttpResponseMessage.Content` is NOT null here even though the
         * handler set it to null — HttpClient substitutes a zero-length
         * placeholder, exactly as it does for a real 204 off the wire. So the
         * assertion is about the entity, not the object: a response with a
         * Content-Type of application/json and a zero-length body is a
         * different thing from a 204, and this fixture must be the latter.
         */
        Expect.True(
            response.Content.Headers.ContentType is null,
            "the empty fixture carries no Content-Type, as a 204 does not");
        Expect.Equal(
            string.Empty,
            await response.Content.ReadAsStringAsync(),
            "the empty fixture carries no bytes to parse");

        // And the one route declared `empty` is the only one, so no other
        // method is ever handed this.
        var declaredEmpty = Entries.Where(entry => entry.Shape == ResponseShape.Empty).ToList();
        Expect.Equal(1, declaredEmpty.Count, "exactly one route is declared empty");
        Expect.Equal("macros.delete", declaredEmpty[0].Name, "and it is macros.delete");
    }

    /// <summary>The spec lists 162 methods, and this table must cover all of them.</summary>
    /// <remarks>
    /// The number is asserted rather than derived from the manifest so that a
    /// manifest that SHRINKS — a parser dropping rows, which has happened to
    /// two gates in this repo — fails here instead of quietly narrowing what
    /// is audited. 55 until 2026-10-14, when the remaining 104 key-accessible
    /// routes got typed methods, and 162 once <c>webhooks.delete</c>,
    /// <c>root.me</c> and <c>workspace.mine</c> were added to the manifest —
    /// all three had been found key-accessible and unlisted by more than one
    /// SDK author reading handlers.
    /// </remarks>
    public static Task TableCoversTheWholeSpec()
    {
        Expect.Equal(162, Entries.Count, "surface.json has 162 entries and this table has one row each");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in Entries)
        {
            Expect.True(seen.Add(entry.Name), $"{entry.Name} appears once");
            Expect.True(entry.Path.StartsWith("/v1/", StringComparison.Ordinal), $"{entry.Name} path is a /v1 route");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The declared shape column really distinguishes routes: 33 arrays, 13
    /// pages, 115 objects, 1 empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Anti-vacuity, and not a hypothetical one. A column that said
    /// <c>Object</c> 162 times would satisfy every other assertion in this file
    /// — the fake would answer <c>{}</c> everywhere, every method would decode
    /// it, and the lock would be internally consistent — and it would be
    /// worthless. That is very close to the state this column was in while
    /// <see cref="ResponseShape.Object"/> was the constructor default: thirteen
    /// bare-array routes were declared objects and nothing noticed.
    /// </para>
    /// <para>
    /// The numbers come from reading all 162 <c>@ZodResponse</c> schemas —
    /// 55 on 2026-10-12, the next 104 on 2026-10-14, and the last three with
    /// them. If the surface grows they change — but all four values must stay
    /// represented, and <c>empty</c> stays at exactly one until a second real
    /// 204 ships.
    /// </para>
    /// <para>
    /// The 104 added on 2026-10-14 moved the column from 14/10/30/1 to
    /// 32/13/113/1, and the last three took it to 33/13/115/1. The
    /// <c>object</c> share going from 55% to 71% is why the anti-vacuity
    /// check matters more now than it did: the commonest value is now
    /// commoner still, so a wrongly-defaulted row is likelier to be right by
    /// accident. Note that <c>webhooks.delete</c> is one of those objects and
    /// looks like it should be the second <c>empty</c> — it answers 200 with
    /// <c>{ ok: true }</c>, which is exactly the kind of row a default would
    /// have got wrong.
    /// </para>
    /// </remarks>
    public static Task DeclaredShapeDistributionIsNotFlat()
    {
        var counts = Entries
            .GroupBy(entry => entry.Shape)
            .ToDictionary(group => group.Key, group => group.Count());

        int count(ResponseShape shape) => counts.TryGetValue(shape, out var n) ? n : 0;

        Expect.Equal(33, count(ResponseShape.Array), "bare-array routes declared");
        Expect.Equal(13, count(ResponseShape.Page), "page routes declared");
        Expect.Equal(115, count(ResponseShape.Object), "object routes declared");
        Expect.Equal(1, count(ResponseShape.Empty), "204 routes declared (macros.delete, and only it)");

        return Task.CompletedTask;
    }

    /// <summary>
    /// The shape declared for every route is the shape the manifest records,
    /// in both directions — and agrees with the manifest's <c>paged</c> flag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the check that would have caught the macros bug. The fake used
    /// to answer <i>every</i> route with <c>{ items, nextCursor }</c>, so
    /// <c>Macros.ListAsync</c> decoded a page here and threw
    /// <c>unexpected_response_shape</c> against the real API, which answers
    /// that route with a bare array. The fixture had invented a shape the
    /// server never sends, and the suite then agreed with the fixture.
    /// </para>
    /// <para>
    /// It used to compare only against <c>paged</c>, which is two-valued: it
    /// could tell a page from a not-page and nothing else, so every bare-array
    /// route declared <c>Object</c> passed it. The manifest now carries a
    /// four-valued <c>shape</c> per entry and this diffs the whole column
    /// against it.
    /// </para>
    /// <para>
    /// The column itself stays hand-written. It is NOT read from
    /// <c>surface.json</c> — a fixture derived from the manifest agrees with
    /// whatever the manifest says, including when it is wrong, which is not
    /// hypothetical: <c>macros.list</c> was flagged <c>paged</c> for a day
    /// while its handler is <c>@PaginationExempt</c>. Eight SDKs declare this
    /// column independently, so a wrong manifest row needs eight authors to
    /// misread the same handler.
    /// </para>
    /// </remarks>
    public static Task DeclaredShapesMatchTheManifestAsync()
    {
        var manifest = Manifest.Load();
        var declared = Entries.ToDictionary(entry => entry.Name, entry => entry.Shape, StringComparer.Ordinal);

        var pagedInManifest = 0;
        foreach (var entry in manifest)
        {
            Expect.True(
                declared.ContainsKey(entry.Key),
                $"{entry.Key} has a row in the surface table");

            var shape = declared[entry.Key];

            // The whole four-valued field, not just page-or-not.
            Expect.Equal(
                entry.ResponseShape,
                shape,
                $"{entry.Key}: the manifest says the response shape is <{entry.Shape}>, and this "
                + "package's fixture and decoder must say the same");

            // And `paged` must not drift from `shape`: the two are spellings
            // of one fact, added eighteen months apart.
            if (entry.Paged)
            {
                pagedInManifest++;
                Expect.Equal(
                    ResponseShape.Page,
                    shape,
                    $"{entry.Key} is paged in the manifest, so the fake must answer it with a page");
            }
            else
            {
                Expect.True(
                    shape != ResponseShape.Page,
                    $"{entry.Key} is NOT paged in the manifest, so the fake must not answer it with a "
                    + "page envelope — that is the fixture inventing a shape the API does not send");
            }
        }

        // The other direction: no row here is absent from the manifest, so the
        // column cannot carry a route the contract has never heard of.
        var manifestKeys = new HashSet<string>(manifest.Select(entry => entry.Key), StringComparer.Ordinal);
        foreach (var entry in Entries)
        {
            Expect.True(
                manifestKeys.Contains(entry.Name),
                $"{entry.Name} is declared here and must exist in the manifest");
        }

        /*
         * Anti-vacuity floor. THIRTEEN, and it has been 8, 7 and 10 — the
         * manifest's `paged` flag was wrong in both directions, which is why
         * this asserts the number at all.
         *
         * 8 -> 7: `macros.list` left, because its handler is
         * @PaginationExempt and answers a bare array capped at 200 rows by
         * usage rank. There was never a cursor to follow.
         *
         * 7 -> 10: `contacts.journey`, `conversations.unattended` and
         * `search.query` went in. All three answer real
         * `{ items, nextCursor }` envelopes — `unattended` carries a `total`
         * beside them and `search` carries `tier` and `facets` — and none had
         * been flagged.
         *
         * 10 -> 13: `bots.list`, `bots.deliveries` and `triggers.list`, all
         * three ending in `buildPaginatedResponse` and none of them flagged.
         * Found the same way as the three before them.
         *
         * Every move came from reading the handlers. None was a parser
         * dropping rows, which is the other reason this count changes and the
         * only one that is a bug. Check that surface.json parses before
         * believing the next move.
         */
        Expect.Equal(
            13,
            pagedInManifest,
            "the manifest marks 13 entries paged (macros.list is not one: @PaginationExempt, bare array; "
            + "nor is billing.invoices, whose cursor envelope is keyed `data`)");

        // And the one route known to answer a bare array is declared as one.
        Expect.Equal(
            ResponseShape.Array,
            declared["macros.list"],
            "macros.list answers a bare JSON array");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Drive every method and assert the verb, the path, and the shape of the
    /// value it handed back.
    /// </summary>
    public static async Task EveryMethodHitsTheSpecifiedRoute()
    {
        foreach (var entry in Entries)
        {
            var handler = new FakeHandler(FakeResponse.For(entry.Shape));
            using var client = Fake.Client(handler);

            // Decoding happens inside Invoke, so a method that expects a page
            // from a route that answers an array throws here rather than
            // passing on a fixture nobody serves.
            var value = await entry.Invoke(client, CancellationToken.None);

            Expect.Equal(1, handler.CallCount, $"{entry.Name} makes exactly one request");
            Expect.Equal(entry.Verb, handler.Last.Verb, $"{entry.Name} verb");
            Expect.Equal(entry.Path, handler.Last.Uri.AbsolutePath, $"{entry.Name} path");
            Expect.Equal(
                "https://api.chatlychat.com",
                handler.Last.Uri.GetLeftPart(UriPartial.Authority),
                $"{entry.Name} host");

            /*
             * And the value handed back really has the declared shape.
             *
             * This is what catches a method that wraps or unwraps: a `list`
             * that returned `page.Items` from a page route, or one that
             * reached for `items` on a bare-array route, disagrees here even
             * though the request it issued was perfect. A decoder that ignores
             * the body entirely fails on `macros.delete`, whose fixture has no
             * body for it to ignore.
             */
            Expect.Equal(
                Shapes.Name(entry.Shape),
                Shapes.Of(value),
                $"{entry.Name} decoded to the declared {Shapes.Name(entry.Shape)}");

            var isWrite = entry.Verb is "POST" or "PUT" or "PATCH" or "DELETE";
            if (isWrite)
            {
                Expect.NotNull(handler.Last.Header("idempotency-key"), $"{entry.Name} carries an idempotency key");
            }
            else
            {
                Expect.True(handler.Last.Header("idempotency-key") is null, $"{entry.Name} is a read and carries none");
            }
        }
    }


    /// <summary>
    /// The paged entries of the spec expose both a single page and an
    /// auto-paginator.
    /// </summary>
    public static async Task EveryPagedEntryHasAnAutoPaginator()
    {
        var walkers = Walkers();

        /*
         * The walker set is exactly the `page` rows — not merely as numerous.
         * THIRTEEN, and it was SEVEN for months while the manifest said ten,
         * which a count-only check reported as a pass because the number it
         * asserted was the number it found.
         */
        var pageRoutes = Entries
            .Where(entry => entry.Shape == ResponseShape.Page)
            .Select(entry => entry.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Expect.Equal(13, pageRoutes.Count, "thirteen routes are declared `page` above");
        Expect.Equal(
            string.Join(", ", pageRoutes),
            string.Join(", ", walkers.Keys.OrderBy(name => name, StringComparer.Ordinal)),
            "every `page` route has an auto-paginator here, and nothing else does");

        foreach (var (name, walk) in walkers)
        {
            var handler = new FakeHandler((_, index) => index == 0
                ? FakeResponse.Page("[{\"id\":\"x_1\"}]", "cursor_2")
                : FakeResponse.Page("[{\"id\":\"x_2\"}]"));
            using var client = Fake.Client(handler);

            var count = 0;
            await foreach (var _ in walk(client, CancellationToken.None))
            {
                count++;
            }

            Expect.Equal(2, count, $"{name} walked both pages");
            Expect.Equal(2, handler.CallCount, $"{name} stopped on the null cursor");
        }
    }

    /// <summary>
    /// The negative direction: a paginator fed a bare array must fail loudly.
    /// </summary>
    /// <remarks>
    /// <see cref="EveryPagedEntryHasAnAutoPaginator"/> proves each walker
    /// copes with the shape the manifest says the route sends. It cannot prove
    /// the declaration matters — a decoder that read <c>items</c> with a
    /// shrug, or one that treated a bare array as an empty page, passes it.
    /// This feeds every walker a bare array where it expects
    /// <c>{ items, nextCursor }</c> and requires it to reject, which is
    /// exactly what the real API handed this client on <c>macros.list</c>.
    /// Reading a bare array as an EMPTY page, which is what the Rust client
    /// did, is worse than throwing: a wrong answer with a 200 rather than a
    /// missing one.
    /// </remarks>
    public static async Task EveryAutoPaginatorRejectsABareArray()
    {
        foreach (var (name, walk) in Walkers())
        {
            var handler = new FakeHandler(FakeResponse.Array("[{\"id\":\"x_1\"}]"));
            using var client = Fake.Client(handler);

            var threw = false;
            try
            {
                await foreach (var _ in walk(client, CancellationToken.None))
                {
                    break;
                }
            }
            catch (ChatlyException)
            {
                threw = true;
            }

            Expect.True(threw, $"{name}'s paginator rejected a bare array where a page is expected");
        }
    }

    /// <summary>
    /// The escape hatch can reach a route with no typed method, and
    /// auto-paginate it.
    /// </summary>
    public static async Task EscapeHatchReachesUnmodelledRoutes()
    {
        var handler = new FakeHandler((_, index) => index == 0
            ? FakeResponse.Page("[{\"id\":\"a_1\"}]", "cursor_2")
            : FakeResponse.Page("[{\"id\":\"a_2\"}]"));
        using var client = Fake.Client(handler);

        var ids = new List<string?>();
        await foreach (var item in client.RequestAllAsync("/v1/audit-logs", new ListOptions { Limit = 1 }, CancellationToken.None))
        {
            ids.Add(item.GetStringOrNull("id"));
        }

        Expect.Equal("a_1,a_2", string.Join(",", ids), "an unmodelled paged route auto-paginates");
        Expect.Equal("/v1/audit-logs", handler.Last.Uri.AbsolutePath, "the path is used verbatim");

        var page = await client.RequestPageAsync("/v1/audit-logs", null, CancellationToken.None);
        Expect.Equal(1, page.Items.Count, "RequestPageAsync returns one page");
    }
}
