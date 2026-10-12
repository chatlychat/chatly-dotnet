using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Chatly.Tests;

/// <summary>One entry of <c>packages/sdk-spec/surface.json</c>.</summary>
public sealed class ManifestEntry
{
    public string Ns { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Http { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// True when the manifest marks the route cursor-paginated.
    /// </summary>
    /// <remarks>
    /// This flag is the single source for which routes the SDK may treat as
    /// pages. It is read here rather than restated in the test table, because
    /// restating it is how <c>macros.list</c> came to be walked by an
    /// auto-paginator against a route that has no cursor.
    /// </remarks>
    public bool Paged { get; set; }

    public string Key => Ns + "." + Name;

    public string Line => Http + " " + Path;
}

/// <summary>
/// Reads the shared manifest and locates this package, for the checks that
/// must agree with the contract rather than with themselves.
/// </summary>
public static class Manifest
{
    /// <summary>The <c>packages/sdk-dotnet</c> directory.</summary>
    /// <exception cref="CheckFailedException">It could not be found.</exception>
    public static string PackageDirectory()
        => FindUpwards(directory => File.Exists(System.IO.Path.Combine(directory, "Chatly.sln")))
            ?? throw new CheckFailedException(
                "could not find the sdk-dotnet package directory (looked for Chatly.sln above "
                + AppContext.BaseDirectory + ")");

    /// <summary>
    /// Load every manifest entry, in manifest order.
    /// </summary>
    /// <remarks>
    /// A missing manifest is a failure, not a skip: every check built on this
    /// exists to compare the SDK against the contract, and a check that
    /// quietly passes when it cannot find the contract is worse than no check.
    /// </remarks>
    /// <exception cref="CheckFailedException">The manifest is missing or empty.</exception>
    public static IReadOnlyList<ManifestEntry> Load()
    {
        // Monorepo first, then the copy a public mirror vendors at its root: a
        // mirror has no "packages/" above the package, and without this
        // candidate the surface proof cannot run there at all.
        string[] candidates =
        {
            System.IO.Path.Combine("packages", "sdk-spec", "surface.json"),
            System.IO.Path.Combine("sdk-spec", "surface.json"),
        };

        string? manifestFile = null;
        foreach (var candidate in candidates)
        {
            var root = FindUpwards(directory => File.Exists(System.IO.Path.Combine(directory, candidate)));
            if (root is not null)
            {
                manifestFile = System.IO.Path.Combine(root, candidate);
                break;
            }
        }

        if (manifestFile is null)
        {
            throw new CheckFailedException(
                "could not find sdk-spec/surface.json above " + AppContext.BaseDirectory
                + " — the checks are asserted against the manifest, so a missing manifest is a "
                + "failure, not a skip.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestFile));

        var entries = new List<ManifestEntry>();
        foreach (var element in document.RootElement.GetProperty("entries").EnumerateArray())
        {
            entries.Add(new ManifestEntry
            {
                Ns = element.GetProperty("ns").GetString() ?? string.Empty,
                Name = element.GetProperty("name").GetString() ?? string.Empty,
                Http = element.GetProperty("http").GetString() ?? string.Empty,
                Path = element.GetProperty("path").GetString() ?? string.Empty,
                Paged = element.TryGetProperty("paged", out var paged)
                    && paged.ValueKind == JsonValueKind.True,
            });
        }

        Expect.True(entries.Count > 0, "the manifest lists at least one entry");
        return entries;
    }

    private static string? FindUpwards(Func<string, bool> predicate)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (predicate(directory.FullName))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
