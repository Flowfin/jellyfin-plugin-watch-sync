using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.WatchSync;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What the configuration page tells somebody who installed this plugin on its own, which is the
/// half of #46's second condition a page with nothing on it can carry.
///
/// That issue is written for the person who found this plugin in a catalogue and did not read
/// what it needs. The page they met said Watch Sync has no settings because nothing on it changes
/// what the plugin does, and a page saying only that reads as a plugin that is working and has
/// nothing to configure. It is the version of this plugin the issue's first paragraph is against,
/// and it was live rather than waiting on somebody building a control.
///
/// What the page still cannot do is name which of the three states it is in, because nothing here
/// reads whether the pairing plugin is present, enabled or of a contract version this plugin was
/// built against. So the page says that it does not read it, and this set refuses the page losing
/// that sentence.
///
/// The bound is the one <c>ConfigurationDocumentTests</c> states for the fact of its own family:
/// this refuses the deletion, which is how a page loses the half that says no, and it refuses
/// nothing about a rewrite that keeps the words and changes what sits around them. That is
/// written here rather than left for a reader to find out by trusting it.
/// </summary>
public class ConfigurationPageNoticeTests
{
    /// <summary>
    /// The page as the plugin serves it says all three things.
    /// </summary>
    [Fact]
    public void ThePageSaysWhatThisPluginNeedsAndWhatItDoesNotYetRead()
    {
        Assert.Empty(Missing(ThePage()));
    }

    /// <summary>
    /// The page this plugin served before is refused, and it is refused for all three reasons.
    ///
    /// This is the near miss as it actually stood in the tree rather than one invented for the
    /// fact: one paragraph, true in every word, and read by the person it was written for as a
    /// plugin with nothing wrong with it.
    /// </summary>
    [Fact]
    public void TheGuardRefusesThePageThatReadAsWorking()
    {
        const string Before =
            "<div id=\"WatchSyncConfigPage\"><p id=\"WatchSyncNoSettings\">Watch Sync has no "
            + "settings yet. Nothing on this page changes what the plugin does, so nothing on "
            + "this page is offered.</p></div>";

        Assert.Equal(3, Missing(Before).Count);
    }

    /// <summary>
    /// A page that names the prerequisite and then offers its own emptiness as an assurance is
    /// refused for that alone.
    ///
    /// This is the repair somebody writes next, and it is the one that costs most, because the
    /// page would then be helpful and still wrong in the direction nobody checks: it would have
    /// told the operator what the plugin needs and left them to read an empty page as a report
    /// that they have it.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAPageThatNamesTheNeedAndNotWhatItCannotRead()
    {
        const string Half =
            "<div id=\"WatchSyncConfigPage\"><p>Pairing belongs to the Server Pairing plugin, "
            + "and without it this plugin syncs nothing.</p></div>";

        var missing = Assert.Single(Missing(Half));

        Assert.Equal("the-page-offers-its-emptiness-as-an-assurance", missing);
    }

    /// <summary>
    /// What a page has to say before an operator can read it as anything, one identifier per
    /// sentence that is missing.
    ///
    /// It is a function over markup rather than a search over the file, so both directions are
    /// proven on markup written here and the fact does not rest on the page as it happens to
    /// stand.
    /// </summary>
    /// <param name="markup">The page.</param>
    /// <returns>The identifier of each sentence the page does not carry.</returns>
    private static IReadOnlyList<string> Missing(string markup)
    {
        var findings = new List<string>();

        if (!markup.Contains("Server Pairing", StringComparison.Ordinal))
        {
            findings.Add("the-page-does-not-name-what-this-plugin-needs");
        }

        if (!markup.Contains("syncs nothing", StringComparison.Ordinal))
        {
            findings.Add("the-page-does-not-say-that-nothing-syncs-without-it");
        }

        if (!markup.Contains("does not yet read", StringComparison.Ordinal))
        {
            findings.Add("the-page-offers-its-emptiness-as-an-assurance");
        }

        return findings;
    }

    /// <summary>
    /// The page as the plugin serves it, read out of the assembly rather than off disk, because
    /// what an operator meets is the embedded resource.
    /// </summary>
    /// <returns>The markup.</returns>
    private static string ThePage()
    {
        var resource = typeof(Plugin).Namespace + ".Configuration.configPage.html";

        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(resource);

        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!);

        return reader.ReadToEnd();
    }
}
