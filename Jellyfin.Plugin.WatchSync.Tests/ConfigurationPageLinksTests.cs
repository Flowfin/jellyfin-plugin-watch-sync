using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchSync;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the addresses the configuration page hands an operator, which is #109's fourth condition
/// and half of #107's.
///
/// The page cannot carry a relative link the way the README does. It is served by the server out
/// of the plugin assembly, at an address that has nothing to do with this repository, so a
/// document is named by its address on the forge or not at all. That is the one place in this tree
/// where an address nothing could otherwise follow is legitimate, and it is why this file exists
/// rather than the rule being left to the reading that <c>ReadmeLinkTests</c> does for the front
/// door.
///
/// What it does instead is read the path out of the address and resolve that against the tree. So
/// a document renamed or moved reddens the suite here, which is the failure this is for: a link an
/// operator follows and lands on nothing is one they do not report, and the page is the last file
/// anybody reads again after it is written.
///
/// The bound is that the branch in the address is not judged. Nothing here can decide whether the
/// forge serves that path on that branch, because reaching the network is refused by the headless
/// rule, so what is held is the path and not the address.
/// </summary>
public class ConfigurationPageLinksTests
{
    /// <summary>
    /// An address on this project's own repository, with the path inside it captured.
    /// </summary>
    private static readonly Regex Blob = new(
        @"https://github\.com/Flowfin/jellyfin-plugin-watch-sync/blob/[^/""]+/(?<path>[^""#]+)",
        RegexOptions.Compiled);

    /// <summary>
    /// Any address the page carries at all.
    /// </summary>
    private static readonly Regex Address = new(
        "href\\s*=\\s*[\"'](?<target>[^\"']+)[\"']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Every address on the page resolves to a path in this repository.
    ///
    /// The set is asserted non-empty first, so a pattern that stopped matching leaves this red
    /// rather than green over nothing, which is the failure a link check has rather than the one
    /// it prevents.
    /// </summary>
    [Fact]
    public void EveryAddressOnThePageResolvesToAPathInThisRepository()
    {
        var addresses = Address.Matches(ThePage()).Select(match => match.Groups["target"].Value).ToList();

        Assert.NotEmpty(addresses);

        Assert.Empty(addresses
            .Where(target => !Resolves(target))
            .Select(target => $"The configuration page links {target}, which is not a path in this repository on any branch."));
    }

    /// <summary>
    /// The page links the two documents the conditions name, from above the controls rather than
    /// from somewhere else on the page.
    ///
    /// This refuses the deletion of a link and refuses nothing about where the paragraph carrying
    /// it sits, which is a weaker fact than the one above and is stated rather than assumed. What
    /// it is for is the day a setting is added and the paragraph is rewritten around it.
    /// </summary>
    /// <param name="path">The document the page has to name.</param>
    [Theory]
    [InlineData("docs/configuration.md")]
    [InlineData("docs/privacy.md")]
    public void ThePageNamesTheDocumentAnOperatorNeedsBesideTheSettings(string path)
    {
        Assert.Contains(
            path,
            Blob.Matches(ThePage()).Select(match => match.Groups["path"].Value),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// An address on somebody else's site is refused, because the rule above is the one that would
    /// quietly stop biting: a pattern anchored on this repository would report nothing for an
    /// address it does not recognise, and nothing is what a green check looks like.
    /// </summary>
    [Fact]
    public void AnAddressThisCheckCannotFollowIsRefused()
    {
        Assert.False(Resolves("https://example.invalid/docs/configuration.md"));
    }

    /// <summary>
    /// Whether an address names a path this repository holds.
    /// </summary>
    /// <param name="target">The address.</param>
    /// <returns><c>true</c> where the path exists in the tree.</returns>
    private static bool Resolves(string target)
    {
        var match = Blob.Match(target);
        if (!match.Success)
        {
            return false;
        }

        var path = Path.Combine(
            HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
            match.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar));

        return File.Exists(path) || Directory.Exists(path);
    }

    /// <summary>
    /// The page as the server is handed it, out of the assembly rather than off disk, for the
    /// reason <c>ConfigurationPageControlsTests</c> reads it that way: a file that stopped being
    /// embedded would leave a check reading the disk perfectly happy.
    /// </summary>
    /// <returns>The page's markup.</returns>
    private static string ThePage()
    {
        var resource = typeof(Plugin).Namespace + ".Configuration.configPage.html";

        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(resource);

        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!);

        return reader.ReadToEnd();
    }
}
