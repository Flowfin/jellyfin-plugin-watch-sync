using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the watcher of #121 to the four properties that decide whether it is worth having.
///
/// It names no other workflow, so what it examines is derived from the runs rather than from a
/// list that goes stale the day a workflow is added. The board it is modelled on watched six
/// workflows by name and the one that stayed red for fifteen hours was the seventh.
///
/// It and the release runbook name one repair heading, so the issue it files sends a reader to
/// a section that exists. The heading is declared once in the workflow and read out of it here,
/// and the runbook is held to carrying it, so a rename on either side reddens the suite rather
/// than leaving an issue pointing at nothing.
///
/// It grants itself nothing but the two scopes filing needs. A job that reads a failed run's
/// log and edits an issue has no business writing the tree, and the grant that would let it is
/// one line somebody adds to make a step work.
///
/// It judges a workflow by that workflow's own latest run rather than by a clock, so a failure
/// stops being reported because a better run replaced it and never because it got old. The
/// window this replaces was 24 hours against six scheduled workflows that run weekly or
/// monthly, so on all six the alert was filed and closed again the next day with the failure
/// still standing.
///
/// What this cannot judge is whether the sweep is right about a run. It reads two files. Whether
/// the filter it holds selects what it says is proven by a dry run against the runs API, and the
/// runbook says so in the section this holds the heading of.
/// </summary>
public class PublishFailureAlertTests
{
    /// <summary>
    /// The derived set. Strip the comment lines and no name of another workflow is left, which is
    /// the property that makes a workflow added tomorrow watched without anybody registering it.
    /// </summary>
    [Fact]
    public void TheWatcherNamesNoOtherWorkflowOutsideItsComments()
    {
        var names = Watcher.OtherWorkflowNames();

        Assert.NotEmpty(names);
        Assert.Empty(Watcher.NamedOutsideComments(Watcher.OfThisRepository(), names));
    }

    /// <summary>
    /// The first guard proven by the mistake it exists for: the sweep narrowed to the one workflow
    /// the issue is titled for, which reads as precision and is the enumeration.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAnEnumeratedWorkflowAndPassesItsRepair()
    {
        var names = Watcher.OtherWorkflowNames();

        Assert.Equal(
            new[] { "Publish Release" },
            Watcher.NamedOutsideComments(Watcher.Fixture("watcher-enumerates-near-miss.txt"), names));

        Assert.Empty(Watcher.NamedOutsideComments(Watcher.Fixture("watcher-enumerates-near-miss-repaired.txt"), names));
    }

    /// <summary>
    /// The heading the issue body points at is on the runbook, and the runbook names the file
    /// that points at it, so the two can be read from either end.
    /// </summary>
    [Fact]
    public void TheWatcherAndTheRunbookNameOneRepairHeading()
    {
        var heading = Watcher.RepairHeading(Watcher.OfThisRepository());
        var runbook = Watcher.RunbookOfThisRepository();

        Assert.True(Watcher.HasHeading(runbook, heading), $"docs/RELEASING.md carries no heading '## {heading}', which is the heading the watcher's issue body sends a reader to.");
        Assert.Contains(Watcher.Path, runbook, StringComparison.Ordinal);
    }

    /// <summary>
    /// The second guard proven on the edit that tidies a heading. The section stays, every
    /// sentence in it holds, and the issue points at a heading that is gone.
    /// </summary>
    [Fact]
    public void TheGuardRefusesARunbookWhoseHeadingMovedAndPassesItsRepair()
    {
        const string heading = "When the watcher files an issue";

        Assert.False(Watcher.HasHeading(Watcher.Fixture("watcher-heading-near-miss.txt"), heading));
        Assert.True(Watcher.HasHeading(Watcher.Fixture("watcher-heading-near-miss-repaired.txt"), heading));
    }

    /// <summary>
    /// Everything denied at the top, and the job holding exactly the two grants filing needs.
    /// </summary>
    [Fact]
    public void TheWatcherGrantsOnlyWhatFilingNeeds()
    {
        var text = Watcher.OfThisRepository();

        Assert.True(Watcher.DeniesEverythingAtTheTop(text), "The watcher does not declare an empty permissions block at the workflow level.");
        Assert.Equal(new[] { "actions: read", "issues: write" }, Watcher.JobGrants(text));
    }

    /// <summary>
    /// The third guard proven on the one line somebody adds to make a step work.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAGrantBeyondFilingAndPassesItsRepair()
    {
        Assert.Equal(
            new[] { "actions: read", "contents: write", "issues: write" },
            Watcher.JobGrants(Watcher.Fixture("watcher-grant-near-miss.txt")));

        Assert.Equal(
            new[] { "actions: read", "issues: write" },
            Watcher.JobGrants(Watcher.Fixture("watcher-grant-near-miss-repaired.txt")));
    }

    /// <summary>
    /// The fourth property, in both halves. Nothing drops a run for being old, and the sweep
    /// groups the runs by workflow and judges each by the head of its own list, which is what
    /// makes the first half a rule rather than a gap somebody has not filled yet.
    /// </summary>
    [Fact]
    public void TheWatcherJudgesAWorkflowByItsLatestRunAndNotByAClock()
    {
        var text = Watcher.OfThisRepository();

        Assert.Empty(Watcher.RecencyCutoffs(text));
        Assert.True(
            Watcher.JudgesEachWorkflowByItsOwnLatestRun(text),
            "The sweep does not group the runs by workflow and take the head of each group, so nothing here holds it to judging a workflow by its latest run.");
    }

    /// <summary>
    /// The fourth guard proven on the window this repository actually shipped, which is the
    /// sharpest near-miss available: it was the code on the mainline, it reads as prudence, and
    /// it silenced the six workflows whose schedule is longer than it.
    /// </summary>
    [Fact]
    public void TheGuardRefusesARecencyWindowAndPassesItsRepair()
    {
        Assert.Equal(
            new[] { "a run's age compared against a cutoff", "a window counted in hours" },
            Watcher.RecencyCutoffs(Watcher.Fixture("watcher-window-near-miss.txt")));

        Assert.Empty(Watcher.RecencyCutoffs(Watcher.Fixture("watcher-window-near-miss-repaired.txt")));
    }

    /// <summary>
    /// Reads the watcher and the runbook out of the tracked tree rather than out of copies,
    /// because a copy proves the state of the file on the day it was copied. Every read that
    /// anchors on a shape fails loudly on finding nothing, so a file whose shape moved cannot
    /// turn an assertion above into a comparison against an empty value.
    /// </summary>
    internal static class Watcher
    {
        /// <summary>
        /// The watcher, as the runbook names it.
        /// </summary>
        internal const string Path = ".github/workflows/publish-failure-alert.yml";

        private const string Runbook = "docs/RELEASING.md";

        /// <summary>
        /// Reads the watcher this repository ships.
        /// </summary>
        /// <returns>The workflow text.</returns>
        internal static string OfThisRepository() => File.ReadAllText(Tracked(Path));

        /// <summary>
        /// Reads the release runbook this repository ships.
        /// </summary>
        /// <returns>The runbook text.</returns>
        internal static string RunbookOfThisRepository() => File.ReadAllText(Tracked(Runbook));

        /// <summary>
        /// Reads a fixture from the tracked file rather than from a copy in the output directory.
        /// </summary>
        /// <param name="name">The file name under the release fixtures.</param>
        /// <returns>The fixture text.</returns>
        internal static string Fixture(string name) =>
            File.ReadAllText(Tracked(System.IO.Path.Combine("Jellyfin.Plugin.WatchSync.Tests", "Release", name)));

        /// <summary>
        /// The declared name of every workflow in this repository other than the watcher.
        /// </summary>
        /// <returns>The names, in file order.</returns>
        internal static IReadOnlyList<string> OtherWorkflowNames()
        {
            var directory = Tracked(System.IO.Path.Combine(".github", "workflows"));
            var names = new List<string>();

            foreach (var file in Directory.EnumerateFiles(directory).OrderBy(path => path, StringComparer.Ordinal))
            {
                if (string.Equals(System.IO.Path.GetFileName(file), System.IO.Path.GetFileName(Path), StringComparison.Ordinal))
                {
                    continue;
                }

                var match = Regex.Match(File.ReadAllText(file), @"(?m)^name:\s*(?<name>[^\r\n]+?)\s*$");

                if (!match.Success)
                {
                    Assert.Fail($"{file} declares no name, so the watcher cannot be held to not naming it.");
                }

                names.Add(match.Groups["name"].Value.Trim('\'', '"'));
            }

            return names;
        }

        /// <summary>
        /// The workflow names that appear in the text once every comment line is removed. A
        /// comment may say what the watcher is modelled on and what it once missed; the code may
        /// not know a name.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <param name="names">The names to look for.</param>
        /// <returns>Those of the names the code names, in the order given.</returns>
        internal static IReadOnlyList<string> NamedOutsideComments(string text, IReadOnlyList<string> names)
        {
            var code = CodeOf(text);

            return names
                .Where(name => Regex.IsMatch(code, @"(?<![\p{L}\p{N}])" + Regex.Escape(name) + @"(?![\p{L}\p{N}])"))
                .ToList();
        }

        /// <summary>
        /// The ways the sweep could drop a red run for being old, as they appear outside the
        /// comments. A comment may say what the window was and why it went; the code may not
        /// carry one. A run's moment is still read - the runs are ordered by it and the streak
        /// is dated from it - so what is looked for is a COMPARISON of that moment, not a
        /// mention of it.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>The probes that hit, in a fixed order.</returns>
        internal static IReadOnlyList<string> RecencyCutoffs(string text)
        {
            var code = CodeOf(text);
            var found = new List<string>();

            if (Regex.IsMatch(code, @"\.updatedAt\s*(>=|<=|>|<)")
                || Regex.IsMatch(code, @"(>=|<=|>|<)\s*\$?\{?cutoff"))
            {
                found.Add("a run's age compared against a cutoff");
            }

            if (Regex.IsMatch(code, @"WINDOW_HOURS") || Regex.IsMatch(code, @"date\s+-u\s+-d"))
            {
                found.Add("a window counted in hours");
            }

            return found;
        }

        /// <summary>
        /// Whether the sweep groups the runs by workflow and orders each group newest first, so
        /// that the run it judges a workflow by is that workflow's own latest one.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>True where it does.</returns>
        internal static bool JudgesEachWorkflowByItsOwnLatestRun(string text)
        {
            var code = CodeOf(text);

            return Regex.IsMatch(code, @"group_by\(\.workflowName\)")
                && Regex.IsMatch(code, @"sort_by\(\.updatedAt\)\s*\|\s*reverse");
        }

        /// <summary>
        /// The workflow with every comment line removed, which is what every property here is
        /// read from: the comments carry what the file is for and what it once got wrong, and
        /// only the code is held to anything.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>The text without its comment lines.</returns>
        private static string CodeOf(string text) =>
            string.Join(
                "\n",
                text.Split('\n').Where(line => !Regex.IsMatch(line, @"^\s*#")));

        /// <summary>
        /// The heading the watcher's issue body sends a reader to, read out of the one place the
        /// workflow declares it.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>The heading, without its leading marks.</returns>
        internal static string RepairHeading(string text)
        {
            var match = Regex.Match(text, @"(?m)^\s*REPAIR_HEADING:\s*""(?<heading>[^""\r\n]+)""\s*$");

            if (!match.Success)
            {
                Assert.Fail("The watcher declares no REPAIR_HEADING, so nothing here is judging which heading its issue body points at.");
            }

            return match.Groups["heading"].Value;
        }

        /// <summary>
        /// Whether a document carries a second-level heading with exactly this text.
        /// </summary>
        /// <param name="document">The document text.</param>
        /// <param name="heading">The heading text, without its leading marks.</param>
        /// <returns>True where the heading is there.</returns>
        internal static bool HasHeading(string document, string heading) =>
            Regex.IsMatch(document, @"(?m)^## " + Regex.Escape(heading) + @"\s*$");

        /// <summary>
        /// Whether the workflow denies every permission at the workflow level.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>True where it does.</returns>
        internal static bool DeniesEverythingAtTheTop(string text) =>
            Regex.IsMatch(text, @"(?m)^permissions:\s*\{\}\s*$");

        /// <summary>
        /// The grants of the one job, as <c>scope: level</c>, sorted so two readings compare.
        /// The block is anchored on the indented <c>permissions:</c> line and ends at the first
        /// line that is not a grant beneath it, and a trailing comment on a grant is not part of
        /// the grant.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>The grants.</returns>
        internal static IReadOnlyList<string> JobGrants(string text)
        {
            var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
            var start = lines.FindIndex(line => Regex.IsMatch(line, @"^\s+permissions:\s*$"));

            if (start < 0)
            {
                Assert.Fail("No job declares a permissions block, so nothing here is judging what the watcher grants itself.");
            }

            var indent = Regex.Match(lines[start], @"^\s+").Value.Length;
            var grants = new List<string>();

            for (var i = start + 1; i < lines.Count; i++)
            {
                var match = Regex.Match(lines[i], @"^(?<indent>\s+)(?<scope>[a-z-]+):\s*(?<level>read|write|none)\b");

                if (!match.Success || match.Groups["indent"].Value.Length <= indent)
                {
                    break;
                }

                grants.Add(match.Groups["scope"].Value + ": " + match.Groups["level"].Value);
            }

            if (grants.Count == 0)
            {
                Assert.Fail("The job's permissions block holds no grant, so the read is anchored on a shape that has changed.");
            }

            return grants.OrderBy(grant => grant, StringComparer.Ordinal).ToList();
        }

        private static string Tracked(string relative) =>
            System.IO.Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), relative);
    }
}
