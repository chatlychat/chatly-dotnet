using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Tests;

/// <summary>
/// One row of <c>surface.lock.json</c>: a method of this SDK, and the request
/// it was <i>observed</i> to issue.
/// </summary>
/// <remarks>
/// Property order here is the property order in the written file — declaration
/// order is what <c>System.Text.Json</c> serializes in.
/// </remarks>
public sealed class SurfaceLockEntry
{
    public string Ns { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Http { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Key => Ns + "." + Name;

    public string Line => $"{Http} {Path}";
}

/// <summary>
/// Produces <c>surface.lock.json</c> by executing the client, and fails if it
/// disagrees with <c>packages/sdk-spec/surface.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the parity evidence. The earlier approach — grepping SDK source for
/// the manifest's path strings — could not tell a doc comment from an
/// implementation, and reported methods as present that did not exist. It also
/// could not see a path assembled from pieces, which is how most of them are
/// built here: <c>TeamMembersAsync</c> interpolates the id into
/// <c>$"/v1/teams/{id}/members"</c>, so no literal of the manifest path
/// appears anywhere in this package.
/// </para>
/// <para>
/// So every row below comes from a real <c>HttpRequestMessage</c>: each method
/// is called with throwaway arguments against a fake transport, the URL it
/// actually produced is captured, and the interpolated segments are mapped
/// back to the manifest's parameter names <i>by position</i>. A method that
/// targets the wrong path cannot produce a passing row, and a comment cannot
/// produce a row at all.
/// </para>
/// <para>
/// The file is written <i>before</i> the comparison, on purpose. The lock is
/// evidence of what the code does, not a copy of what the manifest wants, so a
/// method pointing at the wrong route leaves a lock row saying exactly that —
/// and a failing test beside it. Asserting first would have let the lock keep
/// a stale row that agrees with the manifest while the code does not, which is
/// the failure mode this whole artifact exists to rule out.
/// </para>
/// </remarks>
public static class SurfaceLock
{
    private const string LockFileName = "surface.lock.json";

    /// <summary>
    /// Regenerate the lock from observed requests and assert it equals the
    /// manifest.
    /// </summary>
    public static async Task GenerateAndAssertAsync()
    {
        var packageDirectory = Manifest.PackageDirectory();
        var manifest = Manifest.Load();

        // Drift guard: the table that drives the invocations must cover exactly
        // the manifest, no more and no less. When the manifest changed
        // `workspace.teams` to `workspace.team_members`, this is the assertion
        // that would have failed rather than quietly locking a stale surface.
        var manifestKeys = manifest.Select(entry => entry.Key).OrderBy(key => key, StringComparer.Ordinal).ToList();
        var tableKeys = SurfaceChecks.Entries.Select(entry => entry.Name).OrderBy(key => key, StringComparer.Ordinal).ToList();

        var onlyInManifest = manifestKeys.Except(tableKeys, StringComparer.Ordinal).ToList();
        var onlyInTable = tableKeys.Except(manifestKeys, StringComparer.Ordinal).ToList();
        Expect.True(
            onlyInManifest.Count == 0,
            $"every manifest entry has a method here (missing: {Join(onlyInManifest)})");
        Expect.True(
            onlyInTable.Count == 0,
            $"no method here is absent from the manifest (extra: {Join(onlyInTable)})");

        var observed = new List<SurfaceLockEntry>();

        foreach (var entry in manifest)
        {
            var invocation = SurfaceChecks.Entries.First(row => row.Name == entry.Key);

            // The shape this route really answers with — not a page envelope
            // for everything, which is what hid the macros decode failure.
            var handler = new FakeHandler(FakeResponse.For(invocation.Shape));
            using var client = Fake.Client(handler);
            await invocation.Invoke(client, CancellationToken.None);

            Expect.Equal(1, handler.CallCount, $"{entry.Key} issued exactly one request");
            Expect.Equal(
                "https://api.chatlychat.com",
                handler.Last.Uri.GetLeftPart(UriPartial.Authority),
                $"{entry.Key} went to the production host");

            observed.Add(new SurfaceLockEntry
            {
                Ns = entry.Ns,
                Name = entry.Name,
                Http = handler.Last.Verb,
                Path = Templatise(handler.Last.Uri.AbsolutePath, entry.Path, entry.Key),
            });
        }

        var lockEntries = observed
            .OrderBy(entry => entry.Ns, StringComparer.Ordinal)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();

        Write(Path.Combine(packageDirectory, LockFileName), lockEntries);

        // The assertion the gate cares about: observed == manifest, row for row.
        var expected = manifest
            .OrderBy(entry => entry.Ns, StringComparer.Ordinal)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();

        Expect.Equal(expected.Count, lockEntries.Count, "the lock has one row per manifest entry");

        for (var i = 0; i < expected.Count; i++)
        {
            Expect.Equal(expected[i].Key, lockEntries[i].Key, $"row {i}: namespace and name");
            Expect.Equal(
                expected[i].Line,
                lockEntries[i].Line,
                $"{expected[i].Key}: the request the method actually issued");
        }
    }

    /// <summary>
    /// Map an issued path back onto the manifest's template, replacing each
    /// interpolated segment with the manifest's own parameter name, by
    /// position. Literal segments are left alone, so a typo in one still fails.
    /// </summary>
    private static string Templatise(string issuedPath, string template, string key)
    {
        var issued = issuedPath.Split('/');
        var pattern = template.Split('/');

        if (issued.Length != pattern.Length)
        {
            throw new CheckFailedException(
                $"{key}: issued <{issuedPath}> has {issued.Length} segments, "
                + $"the manifest's <{template}> has {pattern.Length}");
        }

        for (var i = 0; i < pattern.Length; i++)
        {
            issued[i] = pattern[i].StartsWith("{", StringComparison.Ordinal)
                && pattern[i].EndsWith("}", StringComparison.Ordinal)
                    ? pattern[i]
                    : Uri.UnescapeDataString(issued[i]);
        }

        return string.Join("/", issued);
    }

    private static void Write(string path, IReadOnlyList<SurfaceLockEntry> entries)
    {
        var document = new Dictionary<string, object>
        {
            ["sdk"] = "dotnet",
            ["generatedBy"] = "tests/Checks/SurfaceLock.cs",
            ["entries"] = entries.Select(entry => new Dictionary<string, string>
            {
                ["ns"] = entry.Ns,
                ["name"] = entry.Name,
                ["http"] = entry.Http,
                ["path"] = entry.Path,
            }).ToList(),
        };

        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json + Environment.NewLine);
    }

    private static string Join(IEnumerable<string> values)
    {
        var text = string.Join(", ", values);
        return text.Length == 0 ? "none" : text;
    }
}
