using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the three places in the publish route that decide which channel a tag publishes to:
/// the tag patterns the workflow triggers on, the gate job that reads a channel off the tag
/// suffix, and the release step that marks the release. They are three edits in one file and
/// nothing but a reading connected them.
///
/// The two failures this exists for are different shapes. A tag pattern with no gate arm spends
/// a tag on a run that stops in the gate, which is safe and irreversible. A release step naming
/// a channel as a literal publishes a pre-release tag as an ordinary release, which fails
/// nothing at all and reaches every operator subscribed to the stable address.
/// </summary>
public class ReleaseChannelTests
{
    /// <summary>
    /// A pattern the trigger accepts and the gate does not recognise is a tag somebody can push
    /// and cannot publish. The run reaches the gate's catch-all, stops, and leaves the tag spent,
    /// because a tag that has had a run is not one to reuse.
    /// </summary>
    [Fact]
    public void EveryTagThePublishRouteAcceptsIsAChannelTheGateRecognises()
    {
        var route = ReleaseRoute.OfThisRepository();

        Assert.Empty(route.TriggerSuffixes.Except(route.Channels.Keys, StringComparer.Ordinal));
    }

    /// <summary>
    /// The other direction, and it fails soft rather than loud, which is why it is worth a test.
    /// A gate arm for a suffix no pattern accepts is an arm nothing reaches: it reads as a
    /// channel that exists and there is no tag that gets to it, so the channel is documented,
    /// believed and dead.
    /// </summary>
    [Fact]
    public void EveryChannelTheGateRecognisesIsATagThePublishRouteAccepts()
    {
        var route = ReleaseRoute.OfThisRepository();

        Assert.Empty(route.Channels.Keys.Except(route.TriggerSuffixes, StringComparer.Ordinal));
    }

    /// <summary>
    /// The channel the release carries is read from the tag rather than written into the step.
    /// This is the assertion that a literal cannot pass, and a literal is what the route carried
    /// while it had one channel.
    /// </summary>
    [Fact]
    public void TheChannelOnTheReleaseIsReadFromWhatTheGateDerived()
    {
        var route = ReleaseRoute.OfThisRepository();

        Assert.Equal("${{ needs.gate.outputs.prerelease }}", route.ReleaseChannelExpression);
    }

    /// <summary>
    /// The two channels the decision on #1 names, present and mapped the way their names say.
    /// A gate that read every suffix as an ordinary release would pass both tests above and
    /// still publish a pre-release to the stable address.
    /// </summary>
    [Fact]
    public void TheStableAndPreReleaseChannelsAreBothThereAndMarkedApart()
    {
        var route = ReleaseRoute.OfThisRepository();

        Assert.Equal("false", route.Channels["stable"]);
        Assert.Equal("true", route.Channels["prerelease"]);
    }

    /// <summary>
    /// The release runbook is what somebody reads before pushing a tag, so a channel in the
    /// workflow that the document does not name is a channel nobody knows how to use, and a
    /// channel in the document that the workflow does not accept is an instruction that spends
    /// a tag on a run that cannot publish.
    /// </summary>
    [Fact]
    public void TheRunbookNamesTheChannelsTheRouteAccepts()
    {
        var route = ReleaseRoute.OfThisRepository();

        Assert.Equal(
            route.Channels.Keys.OrderBy(name => name, StringComparer.Ordinal),
            ReleaseRoute.SuffixesTheRunbookNames().OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// The first guard proven by deleting it. The near-miss opens a third channel the way
    /// somebody actually would, by adding its tag pattern to the trigger, and leaves the gate
    /// alone. The repair is the one gate arm the mistake left out.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAChannelTheGateNeverGotAndPassesItsRepair()
    {
        var missed = ReleaseRoute.Read(ReleaseRoute.Fixture("unmapped-channel-near-miss.txt"));

        Assert.Equal(new[] { "beta" }, missed.TriggerSuffixes.Except(missed.Channels.Keys, StringComparer.Ordinal));

        var repaired = ReleaseRoute.Read(ReleaseRoute.Fixture("unmapped-channel-near-miss-repaired.txt"));

        Assert.Empty(repaired.TriggerSuffixes.Except(repaired.Channels.Keys, StringComparer.Ordinal));
        Assert.Empty(repaired.Channels.Keys.Except(repaired.TriggerSuffixes, StringComparer.Ordinal));
    }

    /// <summary>
    /// The second guard proven the same way, on the mistake that fails nothing. The near-miss is
    /// the route as it stood with one channel, with the second channel's tag pattern and gate arm
    /// added and the release step's literal left behind. The repair is that one value.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAWrittenInChannelAndPassesItsRepair()
    {
        var literal = ReleaseRoute.Read(ReleaseRoute.Fixture("written-in-channel-near-miss.txt"));

        Assert.Equal("false", literal.ReleaseChannelExpression);
        Assert.Empty(literal.TriggerSuffixes.Except(literal.Channels.Keys, StringComparer.Ordinal));

        var repaired = ReleaseRoute.Read(ReleaseRoute.Fixture("written-in-channel-near-miss-repaired.txt"));

        Assert.Equal("${{ needs.gate.outputs.prerelease }}", repaired.ReleaseChannelExpression);
    }

    /// <summary>
    /// Reads the three channel decisions out of the publish workflow. The read is anchored on the
    /// shapes those three lines have rather than on a YAML parse, for the reason the manifest
    /// reads in this project are: one dependency for three lines, in a file every reader of this
    /// route already reads by eye.
    ///
    /// Every read fails loudly on finding nothing. A regular expression that stopped matching
    /// would otherwise turn each assertion above into a comparison of two empty sets.
    /// </summary>
    internal sealed class ReleaseRoute
    {
        private const string Workflow = ".github/workflows/publish.yaml";

        private const string Runbook = "docs/RELEASING.md";

        private ReleaseRoute(
            IReadOnlyList<string> triggerSuffixes,
            IReadOnlyDictionary<string, string> channels,
            string releaseChannelExpression)
        {
            TriggerSuffixes = triggerSuffixes;
            Channels = channels;
            ReleaseChannelExpression = releaseChannelExpression;
        }

        /// <summary>
        /// Gets the suffix of every tag pattern the workflow triggers on.
        /// </summary>
        internal IReadOnlyList<string> TriggerSuffixes { get; }

        /// <summary>
        /// Gets each suffix the gate recognises, against the value it hands the release step.
        /// </summary>
        internal IReadOnlyDictionary<string, string> Channels { get; }

        /// <summary>
        /// Gets what the release step was given for the channel, verbatim.
        /// </summary>
        internal string ReleaseChannelExpression { get; }

        /// <summary>
        /// Reads the route this repository ships rather than a copy of it.
        /// </summary>
        /// <returns>The route.</returns>
        internal static ReleaseRoute OfThisRepository() =>
            Read(File.ReadAllText(Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), Workflow)));

        /// <summary>
        /// Reads a route out of workflow text.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>The route.</returns>
        internal static ReleaseRoute Read(string text)
        {
            // A tag pattern is a quoted scalar in the trigger's list. The numeric part is
            // whatever the pattern says; the suffix is the word after the last hyphen, which is
            // what the gate below matches on.
            var suffixes = Regex
                .Matches(text, "(?m)^\\s+- \"\\[0-9\\][^\"]*-(?<suffix>[a-z]+)\"\\s*$")
                .Select(match => match.Groups["suffix"].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (suffixes.Count == 0)
            {
                Assert.Fail($"No tag pattern was found in {Workflow}. The read is anchored on a quoted pattern in the trigger list and that shape has changed, so nothing here is judging the route.");
            }

            // One arm per channel, and the value it sets is the whole of what the channel means
            // to the release step.
            var channels = Regex
                .Matches(text, "(?m)^\\s+\\*-(?<suffix>[a-z]+)\\)[^\\n]*?prerelease=(?<flag>true|false)")
                .ToDictionary(
                    match => match.Groups["suffix"].Value,
                    match => match.Groups["flag"].Value,
                    StringComparer.Ordinal);

            if (channels.Count == 0)
            {
                Assert.Fail($"No channel arm was found in {Workflow}. The read is anchored on a case arm that sets prerelease and that shape has changed, so nothing here is judging the route.");
            }

            // Anchored on the action that creates the release rather than on the first
            // prerelease key in the file, which is the gate job declaring its output. The value
            // that matters is the one handed to the step that publishes.
            var release = Regex.Match(
                text,
                "(?m)^\\s+uses: softprops/action-gh-release[^\\n]*\\n(?:[^\\n]*\\n)*?\\s+prerelease:\\s*(?<value>\\S.*?)\\s*$");

            if (!release.Success)
            {
                Assert.Fail($"No prerelease input was found on the release action in {Workflow}. That step is where the channel reaches the release, and nothing here is judging it.");
            }

            return new ReleaseRoute(suffixes, channels, release.Groups["value"].Value);
        }

        /// <summary>
        /// Reads the tag suffixes the release runbook names, out of the code spans it writes them
        /// in. Prose mentioning a word is not a channel; a suffix written as a tag form is.
        /// </summary>
        /// <returns>The suffixes.</returns>
        internal static IReadOnlyList<string> SuffixesTheRunbookNames()
        {
            var document = File.ReadAllText(Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), Runbook));

            var suffixes = Regex
                .Matches(document, "`X\\.Y\\.Z(?:\\.W)?-(?<suffix>[a-z]+)`")
                .Select(match => match.Groups["suffix"].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (suffixes.Count == 0)
            {
                Assert.Fail($"No tag form was found in {Runbook}. The read is anchored on the form the document writes a tag in and that shape has changed, so nothing here is comparing the document to the route.");
            }

            return suffixes;
        }

        /// <summary>
        /// Reads a fixture from the tracked file rather than from a copy in the output directory,
        /// because a copy proves the state of the file on the day it was copied.
        /// </summary>
        /// <param name="name">The file name.</param>
        /// <returns>The fixture text.</returns>
        internal static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                "Jellyfin.Plugin.WatchSync.Tests",
                "Release",
                name));
    }
}
