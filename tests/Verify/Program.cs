using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Chatly.Tests;

namespace Chatly.Verify;

/// <summary>
/// Runs every check in <see cref="CheckRegistry"/> and exits non-zero if any
/// fails.
/// </summary>
internal static class Program
{
    private static async Task<int> Main()
    {
        Console.WriteLine($"chatly .NET SDK — {CheckRegistry.All.Count} contract checks");
        Console.WriteLine(new string('-', 72));

        var failures = 0;
        var stopwatch = Stopwatch.StartNew();

        foreach (var check in CheckRegistry.All)
        {
            try
            {
                await check.Run().ConfigureAwait(false);
                Console.WriteLine($"PASS  {check.Name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"FAIL  {check.Name}");
                Console.WriteLine($"      {ex.GetType().Name}: {ex.Message}");
            }
        }

        stopwatch.Stop();
        PrintVectors();

        Console.WriteLine(new string('-', 72));
        Console.WriteLine(
            $"{CheckRegistry.All.Count - failures} passed, {failures} failed "
            + $"in {stopwatch.ElapsedMilliseconds} ms");

        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Print the digests this implementation produces, so they can be compared
    /// against the other language SDKs by eye as well as by assertion.
    /// </summary>
    private static void PrintVectors()
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var body = Encoding.UTF8.GetBytes("{\"id\":\"evt_1\"}");

        Console.WriteLine(new string('-', 72));
        Console.WriteLine("digests produced by this implementation:");
        Console.WriteLine(
            "  ChatlyIdentity.Hash(\"sek_test_secret\", \"user_42\")");
        Console.WriteLine($"    {ChatlyIdentity.Hash("sek_test_secret", "user_42")}");
        Console.WriteLine(
            "  ChatlyWebhooks.Sign({\"id\":\"evt_1\"}, \"whsec_test\", t=1700000000)");
        Console.WriteLine($"    {ChatlyWebhooks.Sign(body, "whsec_test", at)}");
    }
}
