using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Temporary. A required status check that nobody has watched fail is not known to work, so this
/// asserts something false once to show the check turn red, and is removed in the next commit.
/// </summary>
public class DeliberateFailureTests
{
    /// <summary>
    /// Fails on purpose.
    /// </summary>
    [Fact]
    public void TheRequiredTestCheckGoesRedWhenATestFails()
    {
        Assert.Equal("green", "red");
    }
}
