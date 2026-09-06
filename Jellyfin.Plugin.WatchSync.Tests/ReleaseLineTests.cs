using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the second server line's release route together. Five places decide which line a tag
/// publishes, and nothing but a reading connects them: the tag patterns the publish workflow
/// triggers on, the gate arm that reads a line segment off the tag, the table that turns a line
/// into the framework the packager builds and the manifest it reads, the manifest files those
/// rows name, and the packager call that has to take what the gate derived rather than a literal.
///
/// The failure this exists for is the one #117 was opened against: an archive compiled for one
/// server line and stamped with the other's ABI, which installs on a server it cannot run on.
/// The packager stamps the archive with the top level of whatever file is called build.yaml
/// when it runs, so the route swaps the tagged line's manifest under that name, and every fact
/// here is one way that swap, or the table that decides it, could go quietly wrong.
/// </summary>
public class ReleaseLineTests
{
    /// <summary>
    /// A line segment the trigger accepts and the gate does not read is a tag somebody can push
    /// and cannot publish. The run reaches the gate, the channel arm sees a suffix it does not
    /// know, and the tag is spent on a run that stops.
    /// </summary>
    [Fact]
    public void EveryLineTheTriggerAcceptsIsOneTheGateReads()
    {
        var lines = ReleaseLines.OfThisRepository();

        Assert.Empty(lines.TriggerLines.Except(lines.ArmLines, StringComparer.Ordinal));
    }

    /// <summary>
    /// The other direction. An arm for a segment no pattern accepts is a line that reads as
    /// published and is reached by no tag, so it is documented, believed and dead.
    /// </summary>
    [Fact]
    public void EveryLineTheGateReadsIsOneTheTriggerAccepts()
    {
        var lines = ReleaseLines.OfThisRepository();

        Assert.Empty(lines.ArmLines.Except(lines.TriggerLines, StringComparer.Ordinal));
    }

    /// <summary>
    /// Every line the gate can derive, the empty one included, has a row that says what to build
    /// it from. The table's catch-all stops a run on a line with no row, which is safe and arrives
    /// after the tag is spent; this makes it a red suite instead.
    /// </summary>
    [Fact]
    public void EveryLineTheGateReadsHasARowInTheTable()
    {
        var lines = ReleaseLines.OfThisRepository();
        var derivable = lines.ArmLines.Append(string.Empty).OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(derivable, lines.Rows.Keys.OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// A tag with no segment publishes from the file the packager finds by name, with no swap.
    /// The default row naming any other file would make the swap step copy over build.yaml on
    /// every release, including the ones the runbook describes today.
    /// </summary>
    [Fact]
    public void TheDefaultRowNamesTheManifestThePackagerFindsByName()
    {
        var lines = ReleaseLines.OfThisRepository();

        Assert.Equal("build.yaml", lines.Rows[string.Empty].Manifest);
    }

    /// <summary>
    /// Each row's manifest is in the tree, declares the row's framework at its top level, and
    /// names a pair the targets list carries. A row pointing at the other line's manifest is the
    /// archive claiming the wrong server, arriving from the table rather than from the swap.
    /// </summary>
    [Fact]
    public void EveryRowNamesAManifestThatDeclaresItsFramework()
    {
        var lines = ReleaseLines.OfThisRepository();

        Assert.Empty(ReleaseLines.RowsDisagreeingWithTheirManifest(lines, ReleaseLines.ManifestAtRoot));
    }

    /// <summary>
    /// Every target the manifests declare has a row, and every row builds a declared target. A
    /// third line added to the targets list with no route is a line the plugin claims to support
    /// and nothing can publish.
    /// </summary>
    [Fact]
    public void TheTableCoversEveryTargetTheManifestsDeclare()
    {
        var lines = ReleaseLines.OfThisRepository();
        var declared = BuildTargetsTests.BuildFacts.DeclaredTargets().Select(target => target.Framework);

        Assert.Equal(
            declared.OrderBy(name => name, StringComparer.Ordinal),
            lines.Rows.Values.Select(row => row.Framework).OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// Two rows naming manifests with one top level pair are one line published twice under two
    /// tags, which the manifest generator refuses on the day it reads the history.
    /// </summary>
    [Fact]
    public void EveryManifestNamesADifferentLine()
    {
        var lines = ReleaseLines.OfThisRepository();

        var pairs = lines.Rows.Values
            .Select(row => ReleaseLines.ManifestAtRoot(row.Manifest))
            .Select(text => (
                BuildTargetsTests.BuildFacts.ScalarAtColumnZero(text, "framework"),
                BuildTargetsTests.BuildFacts.ScalarAtColumnZero(text, "targetAbi")))
            .ToList();

        Assert.Equal(pairs.Count, pairs.Distinct().Count());
    }

    /// <summary>
    /// The framework reaches the packager and every framework check from the gate's output, not
    /// from a literal. A literal is what this route carried while it had one line, and a literal
    /// left in one step builds one line for a tag naming the other and fails nothing.
    /// </summary>
    [Fact]
    public void ThePackagerAndEveryFrameworkCheckTakeWhatTheGateDerived()
    {
        var lines = ReleaseLines.OfThisRepository();

        Assert.Equal(ReleaseLines.FrameworkFromTheGate, lines.PackagerFramework);
        Assert.Equal(0, lines.WrittenInFrameworks);
    }

    /// <summary>
    /// The release job asks the history for this line's tags. One version is published once per
    /// line, so a candidate composed without the segment refuses the second line's release of
    /// every version the first line has already published.
    /// </summary>
    [Fact]
    public void TheReleaseAsksTheHistoryForTheTaggedLine()
    {
        var lines = ReleaseLines.OfThisRepository();

        Assert.Contains("candidate=\"${VERSION}${INFIX}-${suffix}\"", lines.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The manifests differ in the line and in nothing else: the identity, the version, the
    /// targets list, the artifacts and the description are one text. A version bump that reaches
    /// one file and not the other is two lines at two versions, which the release job cannot
    /// see because each tag is checked against its own manifest alone.
    /// </summary>
    [Fact]
    public void TheManifestsAgreeOnEverythingThatIsNotTheLine()
    {
        var lines = ReleaseLines.OfThisRepository();
        var reference = ReleaseLines.WithoutTheLine(ReleaseLines.ManifestAtRoot("build.yaml"));

        Assert.All(
            lines.Rows.Values,
            row => Assert.Equal(reference, ReleaseLines.WithoutTheLine(ReleaseLines.ManifestAtRoot(row.Manifest))));
    }

    /// <summary>
    /// The agreement guard proven by deleting it, on the mistake somebody actually makes: the
    /// version is raised in the manifest the runbook names and not in the second one.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAVersionBumpedInOneManifest()
    {
        var reference = ReleaseLines.WithoutTheLine(ReleaseLines.ManifestAtRoot("build.yaml"));
        var second = ReleaseLines.ManifestAtRoot("build-jf12.yaml");

        Assert.Equal(reference, ReleaseLines.WithoutTheLine(second));

        var bumped = Regex.Replace(second, "(?m)^version: \"[^\"]+\"", "version: \"9.9.9.9\"");

        Assert.NotEqual(reference, ReleaseLines.WithoutTheLine(bumped));
    }

    /// <summary>
    /// The trigger guard proven the same way. The near miss opens a third line the way somebody
    /// would, by adding its tag patterns to the trigger, and leaves the gate alone; the repair is
    /// the arm the mistake left out.
    /// </summary>
    [Fact]
    public void TheGuardRefusesALineTheTriggerAcceptsAndTheGateNeverReads()
    {
        var route = ReleaseLines.OfThisRepository().Text;
        var pattern = new Regex("(?m)^(?<pattern>[ ]+- \"\\[0-9\\][^\"]*-jf12-stable\")[ ]*\r?$");

        var missed = pattern.Replace(route, "${pattern}\n      - \"[0-9]+.[0-9]+.[0-9]+-jf13-stable\"", 1);
        var read = ReleaseLines.Read(missed);

        Assert.Equal(new[] { "jf13" }, read.TriggerLines.Except(read.ArmLines, StringComparer.Ordinal));

        var repaired = missed.Replace(
            "*-jf12-*) line=\"jf12\" ;;",
            "*-jf12-*) line=\"jf12\" ;;\n            *-jf13-*) line=\"jf13\" ;;",
            StringComparison.Ordinal);
        var reread = ReleaseLines.Read(repaired);

        Assert.Empty(reread.TriggerLines.Except(reread.ArmLines, StringComparer.Ordinal));
        Assert.Empty(reread.ArmLines.Except(reread.TriggerLines, StringComparer.Ordinal));
    }

    /// <summary>
    /// The table guard proven by the one character mistake: the second row built for the first
    /// line's framework. Its manifest declares the other one, and the disagreement names the row.
    /// </summary>
    [Fact]
    public void TheGuardRefusesARowBuildingTheOtherLinesFramework()
    {
        var route = ReleaseLines.OfThisRepository().Text;
        var wrong = route.Replace("jf12) expected_framework=\"net10.0\"", "jf12) expected_framework=\"net9.0\"", StringComparison.Ordinal);

        Assert.NotEqual(route, wrong);

        var disagreement = Assert.Single(ReleaseLines.RowsDisagreeingWithTheirManifest(ReleaseLines.Read(wrong), ReleaseLines.ManifestAtRoot));

        Assert.Contains("jf12", disagreement, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the line decisions out of the publish workflow. Anchored on the shapes those lines
    /// have rather than on a YAML parse, for the reason every other read of these workflows in
    /// this suite is: one dependency for a handful of lines, in a file its readers read by eye.
    /// Every read fails loudly on finding nothing, because a regular expression that stopped
    /// matching would otherwise turn the set comparisons above into comparisons of nothing.
    /// </summary>
    internal sealed class ReleaseLines
    {
        /// <summary>
        /// What the packager and every framework check are handed: the gate's output, verbatim.
        /// </summary>
        internal const string FrameworkFromTheGate = "${{ needs.gate.outputs.framework }}";

        private const string Workflow = ".github/workflows/publish.yaml";

        private ReleaseLines(
            string text,
            IReadOnlyList<string> triggerLines,
            IReadOnlyList<string> armLines,
            IReadOnlyDictionary<string, Row> rows,
            string packagerFramework,
            int writtenInFrameworks)
        {
            Text = text;
            TriggerLines = triggerLines;
            ArmLines = armLines;
            Rows = rows;
            PackagerFramework = packagerFramework;
            WrittenInFrameworks = writtenInFrameworks;
        }

        /// <summary>
        /// Gets the workflow text the route was read from.
        /// </summary>
        internal string Text { get; }

        /// <summary>
        /// Gets the line segment of every tag pattern the trigger accepts that carries one.
        /// </summary>
        internal IReadOnlyList<string> TriggerLines { get; }

        /// <summary>
        /// Gets every line segment the gate has an arm for.
        /// </summary>
        internal IReadOnlyList<string> ArmLines { get; }

        /// <summary>
        /// Gets the table, one row per line the gate can derive, keyed by the line with the empty
        /// string for a tag carrying no segment.
        /// </summary>
        internal IReadOnlyDictionary<string, Row> Rows { get; }

        /// <summary>
        /// Gets what the packager call was given as its target framework, verbatim.
        /// </summary>
        internal string PackagerFramework { get; }

        /// <summary>
        /// Gets how many steps still carry the framework as a written-in literal.
        /// </summary>
        internal int WrittenInFrameworks { get; }

        /// <summary>
        /// Reads the route this repository ships rather than a copy of it.
        /// </summary>
        /// <returns>The route.</returns>
        internal static ReleaseLines OfThisRepository() =>
            Read(File.ReadAllText(Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), Workflow)));

        /// <summary>
        /// Reads a route out of workflow text.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>The route.</returns>
        internal static ReleaseLines Read(string text)
        {
            // A tag pattern is a quoted scalar in the trigger's list. A line segment is a word
            // between the version and the channel suffix; a pattern with one hyphen carries none.
            var triggerLines = Regex
                .Matches(text, "(?m)^\\s+- \"\\[0-9\\][^\"]*?-(?<line>[a-z][a-z0-9]*)-[a-z]+\"\\s*$")
                .Select(match => match.Groups["line"].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (triggerLines.Count == 0)
            {
                Assert.Fail($"No tag pattern carrying a line segment was found in {Workflow}. The read is anchored on a quoted pattern with a segment before its suffix and that shape has changed, so nothing here is judging the route.");
            }

            // One arm per line, each naming the line it read so the segment and the name
            // cannot drift.
            var arms = Regex
                .Matches(text, "(?m)^\\s+\\*-(?<line>[a-z][a-z0-9]*)-\\*\\)\\s+line=\"(?<named>[^\"]+)\"")
                .ToList();

            if (arms.Count == 0)
            {
                Assert.Fail($"No line arm was found in {Workflow}. The read is anchored on a case arm that sets the line and that shape has changed, so nothing here is judging the route.");
            }

            foreach (var arm in arms)
            {
                Assert.True(
                    arm.Groups["line"].Value == arm.Groups["named"].Value,
                    $"The gate arm for the segment {arm.Groups["line"].Value} names the line {arm.Groups["named"].Value}, so the table would be asked for a line the tag did not carry.");
            }

            var armLines = arms.Select(arm => arm.Groups["line"].Value).Distinct(StringComparer.Ordinal).ToList();

            // One row per line: the framework and the manifest, on one line each.
            var rows = Regex
                .Matches(text, "(?m)^\\s+(?<key>\"\"|[a-z][a-z0-9]*)\\)\\s+expected_framework=\"(?<framework>[^\"]+)\"\\s*;\\s*manifest=\"(?<manifest>[^\"]+)\"")
                .ToDictionary(
                    match => match.Groups["key"].Value == "\"\"" ? string.Empty : match.Groups["key"].Value,
                    match => new Row(match.Groups["framework"].Value, match.Groups["manifest"].Value),
                    StringComparer.Ordinal);

            if (rows.Count == 0)
            {
                Assert.Fail($"No line table row was found in {Workflow}. The read is anchored on a case arm that sets the framework and the manifest and that shape has changed, so nothing here is judging the route.");
            }

            var packager = Regex.Match(text, "(?m)^\\s+dotnet-target:\\s*\"(?<value>[^\"]+)\"");

            if (!packager.Success)
            {
                Assert.Fail($"No target framework was found on the packager call in {Workflow}, so nothing here is judging what it builds.");
            }

            var writtenIn = Regex.Matches(text, "(?m)^\\s+EXPECTED_FRAMEWORK:\\s*\"").Count;

            return new ReleaseLines(text, triggerLines, armLines, rows, packager.Groups["value"].Value, writtenIn);
        }

        /// <summary>
        /// The rows whose manifest is missing, declares another framework at its top level, or
        /// names a pair the targets list does not carry, each as a sentence naming the row.
        /// </summary>
        /// <param name="lines">The route.</param>
        /// <param name="manifestText">How a manifest named by a row is read.</param>
        /// <returns>The disagreements, empty where every row agrees with its manifest.</returns>
        internal static IReadOnlyList<string> RowsDisagreeingWithTheirManifest(ReleaseLines lines, Func<string, string> manifestText)
        {
            var declared = BuildTargetsTests.BuildFacts.DeclaredTargets();
            var disagreements = new List<string>();

            foreach (var (line, row) in lines.Rows)
            {
                var text = manifestText(row.Manifest);
                var framework = BuildTargetsTests.BuildFacts.ScalarAtColumnZero(text, "framework");
                var abi = BuildTargetsTests.BuildFacts.ScalarAtColumnZero(text, "targetAbi");

                if (framework != row.Framework)
                {
                    disagreements.Add($"The row for the line '{line}' builds {row.Framework} and its manifest {row.Manifest} declares {framework} at its top level, so the archive would be compiled for one line and stamped for the other.");
                }

                if (!declared.Contains(new BuildTargetsTests.BuildFacts.Target(framework, abi)))
                {
                    disagreements.Add($"The manifest {row.Manifest} names the pair {framework} and {abi} at its top level, which the targets list does not carry.");
                }
            }

            return disagreements;
        }

        /// <summary>
        /// Reads a manifest by the name a row gives it, from the repository root, which is where
        /// the packager is handed it.
        /// </summary>
        /// <param name="name">The file name.</param>
        /// <returns>The manifest text.</returns>
        internal static string ManifestAtRoot(string name)
        {
            var path = Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), name);
            Assert.True(File.Exists(path), $"{name} is named by the line table and is not at the repository root, so the swap step would fail on the runner after the tag is spent.");

            return File.ReadAllText(path);
        }

        /// <summary>
        /// A manifest's lines with the two top level keys that name the line, and the comments,
        /// taken out. What is left is everything two manifests of one plugin have to agree on.
        /// </summary>
        /// <param name="text">The manifest text.</param>
        /// <returns>The lines that are not the line.</returns>
        internal static IReadOnlyList<string> WithoutTheLine(string text) =>
            text.Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => !line.StartsWith('#'))
                .Where(line => !Regex.IsMatch(line, "^(framework|targetAbi):"))
                .ToList();

        /// <summary>
        /// One row of the line table.
        /// </summary>
        /// <param name="Framework">The framework the packager builds for the line.</param>
        /// <param name="Manifest">The manifest file the line is packaged from.</param>
        internal sealed record Row(string Framework, string Manifest);
    }
}
