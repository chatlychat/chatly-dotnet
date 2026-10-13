using System.Threading.Tasks;
using Xunit;

namespace Chatly.Tests;

/// <summary>
/// The xunit face of the shared check suite.
/// </summary>
/// <remarks>
/// <para>
/// Each row is one entry of <see cref="CheckRegistry"/>, so the test names
/// xunit prints are the contract sections themselves. The assertions live in
/// <c>tests/Checks</c> rather than here because the same code is also run by
/// <c>tests/Verify</c>, which needs no NuGet restore.
/// </para>
/// <para>
/// Nothing in here touches the network, needs an API key or needs a running
/// API: every request goes through a fake <c>HttpMessageHandler</c>.
/// </para>
/// </remarks>
public class ContractTests
{
    [Theory]
    [MemberData(nameof(CheckRegistry.Names), MemberType = typeof(CheckRegistry))]
    public Task Contract(string name) => CheckRegistry.Get(name).Run();

    /// <summary>
    /// The registry is not empty and has not silently shrunk.
    /// </summary>
    /// <remarks>
    /// A theory over an empty data source is a green suite that ran nothing.
    /// This is the floor that makes that failure loud.
    /// </remarks>
    [Fact]
    public void RegistryIsNotEmpty()
    {
        Assert.True(
            CheckRegistry.All.Count >= 48,
            $"the check registry should hold at least 48 checks, found {CheckRegistry.All.Count}");
    }
}
