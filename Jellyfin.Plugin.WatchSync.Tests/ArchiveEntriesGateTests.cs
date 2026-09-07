using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds both packaging routes to the step that looks inside the archive, which is #374's
/// fourth condition and the fourth condition of #117.
///
/// The archive an operator installs is produced by the packager, and until that step nothing
/// on either route read a byte of what is inside it. A test assembly or a stray file from the
/// output directory shipped inside a plugin archive is loaded onto somebody's server, and the
/// first anybody hears of it is a server that behaves differently from the one it was tested
/// on.
///
/// <para>
/// THE SUITE IS NOT THAT STEP AND CANNOT BE. Producing the archive needs the packager, a
/// network fetch and a Python runtime, and the headless rule refuses all three. So the
/// assertion is a step in the run that produces the archive, `.github/check-archive-entries.py`
/// is what decides it, and what this suite holds is that the step is there on both routes, that
/// it runs after the packager on the archive the packager produced, that it runs the checker,
/// and that the checker names what it refuses. Deleting the step is then a red suite rather than
/// a quiet one. Nobody should read a green run here as having looked inside an archive.
/// </para>
/// </summary>
public class ArchiveEntriesGateTests
{
    /// <summary>
    /// Both routes that package carry the step. The merge gate is where a stray file is caught
    /// before a tag is spent, and the release is where the archive that ships is the one read.
    /// </summary>
    [Theory]
    [InlineData(".github/workflows/package.yaml")]
    [InlineData(".github/workflows/publish.yaml")]
    public void TheRouteRefusesAnArchiveCarryingAnythingButThePluginsOwnFiles(string workflow)
    {
        var gate = ArchiveGate.Read(ArchiveGate.WorkflowText(workflow));

        Assert.True(gate.HasStep, $"{workflow} carries no step named `{ArchiveGate.StepName}`, so that route packages an archive nobody looks into.");
        Assert.True(gate.RunsTheChecker, $"{workflow} carries the step and it does not run {ArchiveGate.Checker} on the archive, so the step is a name and not a refusal.");
        Assert.True(gate.ReadsThePackagerOutput, $"{workflow} carries the step and it does not read the packager's own output, so it could be looking into a different file than the one that ships.");
        Assert.True(gate.AfterThePackager, $"{workflow} carries the step before the packager call, so it runs on an archive that does not exist yet.");
    }

    /// <summary>
    /// Every archive a route produces is read, and not only the first. The merge gate calls the
    /// packager once per server line since #101, so a route with one of these steps and two
    /// calls looks into one archive and publishes two, with the unread one being exactly the
    /// line nobody was watching.
    /// </summary>
    /// <param name="workflow">A route that packages.</param>
    [Theory]
    [InlineData(".github/workflows/package.yaml")]
    [InlineData(".github/workflows/publish.yaml")]
    public void EveryArchiveTheRouteProducesIsReadIntoAndNotOnlyTheFirst(string workflow)
    {
        var text = ArchiveGate.WorkflowText(workflow);
        var gates = ArchiveGate.ReadAll(text);
        var calls = ArchiveGate.PackagerCalls(text);

        Assert.True(
            gates.Count == calls,
            $"{workflow} calls the packager {calls} times and carries {gates.Count} `{ArchiveGate.StepName}` steps. One archive is produced per call, so an archive without its own step is one that ships unread.");

        Assert.All(
            gates,
            gate =>
            {
                Assert.True(gate.RunsTheChecker);
                Assert.True(gate.ReadsThePackagerOutput);
                Assert.True(gate.AfterThePackager);
            });
    }

    /// <summary>
    /// The checker names what it refuses, in both directions: an entry that is not the plugin's
    /// own, and a declared artifact the archive does not carry. A checker refusing in silence
    /// would be a red run nobody can act on.
    /// </summary>
    [Fact]
    public void TheCheckerNamesWhatItRefuses()
    {
        var path = Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), ArchiveGate.Checker);

        Assert.True(File.Exists(path), $"{ArchiveGate.Checker} does not exist, so the step on both routes runs nothing.");

        var text = File.ReadAllText(path);

        Assert.Contains("is not the plugin's own", text, StringComparison.Ordinal);
        Assert.Contains("the archive does not carry", text, StringComparison.Ordinal);
        Assert.Contains("the archive is empty", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The merge gate proves the checker bites before it trusts it, on fixtures built in the run,
    /// so a checker that passes everything is a red gate rather than a green one.
    /// </summary>
    [Fact]
    public void TheMergeGateProvesTheCheckerRefusesWhatItIsWrittenFor()
    {
        var text = ArchiveGate.WorkflowText(".github/workflows/package.yaml");

        Assert.Contains(ArchiveGate.ProofStepName, text, StringComparison.Ordinal);
        Assert.Contains("archive-fixtures/manifest.yaml", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The guard proven by the mistake somebody makes: the step deleted while tidying a workflow,
    /// which leaves both files valid and every other check green.
    /// </summary>
    [Fact]
    public void TheGuardRefusesARouteWithoutTheStepAndPassesItsRepair()
    {
        var mistake = ArchiveGate.Read(ArchiveGate.Fixture("archive-gate-absent-near-miss.txt"));

        Assert.False(mistake.HasStep);

        var repaired = ArchiveGate.Read(ArchiveGate.Fixture("archive-gate-near-miss-repaired.txt"));

        Assert.True(repaired.HasStep);
        Assert.True(repaired.RunsTheChecker);
        Assert.True(repaired.ReadsThePackagerOutput);
        Assert.True(repaired.AfterThePackager);
    }

    /// <summary>
    /// The step kept and its command replaced by a listing: the run prints the entries, refuses
    /// nothing, and reads as a route that looks inside the archive.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAStepThatDoesNotRunTheChecker()
    {
        var mistake = ArchiveGate.Read(ArchiveGate.Fixture("archive-gate-not-running-the-checker-near-miss.txt"));

        Assert.True(mistake.HasStep);
        Assert.False(mistake.RunsTheChecker);
    }

    /// <summary>
    /// The step moved above the packager while reordering, where it runs on an archive that does
    /// not exist yet and fails every run, or, with a stale archive in the workspace, passes on the
    /// wrong file.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAStepBeforeThePackager()
    {
        var mistake = ArchiveGate.Read(ArchiveGate.Fixture("archive-gate-before-the-packager-near-miss.txt"));

        Assert.True(mistake.HasStep);
        Assert.True(mistake.RunsTheChecker);
        Assert.False(mistake.AfterThePackager);
    }

    /// <summary>
    /// Reads the step out of workflow text. Anchored on the step's name and on the lines under
    /// it rather than on a YAML parse, which is what every other read of these workflows in this
    /// suite does and for the same reason: one dependency for one step, in a file its readers
    /// read by eye.
    /// </summary>
    internal sealed class ArchiveGate
    {
        /// <summary>
        /// The step's name. A rename removes it from this guard, so change it deliberately or not
        /// at all.
        /// </summary>
        internal const string StepName = "Refuse an archive carrying anything but the plugin's own files";

        /// <summary>
        /// The step on the merge gate that proves the checker refuses what it is written for.
        /// </summary>
        internal const string ProofStepName = "Prove the archive check refuses what it is written for";

        /// <summary>
        /// The checker, relative to the repository root.
        /// </summary>
        internal const string Checker = ".github/check-archive-entries.py";

        /// <summary>
        /// The packager action, without its version.
        /// </summary>
        private const string Packager = "uses: oddstr13/jellyfin-plugin-repository-manager@";

        /// <summary>
        /// The expression that names the archive a packager call produced. Read as a shape rather
        /// than as one step's name, because the merge gate calls the packager once per server line
        /// and only the first of those calls is `jprm`.
        /// </summary>
        private static readonly Regex PackagerOutput = new Regex(
            "\\$\\{\\{ steps\\.[A-Za-z0-9_-]+\\.outputs\\.artifact \\}\\}",
            RegexOptions.None);

        private ArchiveGate(bool hasStep, bool runsTheChecker, bool readsThePackagerOutput, bool afterThePackager)
        {
            HasStep = hasStep;
            RunsTheChecker = runsTheChecker;
            ReadsThePackagerOutput = readsThePackagerOutput;
            AfterThePackager = afterThePackager;
        }

        /// <summary>
        /// Gets a value indicating whether a step with the name is in the text.
        /// </summary>
        internal bool HasStep { get; }

        /// <summary>
        /// Gets a value indicating whether the step's lines name the checker.
        /// </summary>
        internal bool RunsTheChecker { get; }

        /// <summary>
        /// Gets a value indicating whether the step's lines name the packager's output.
        /// </summary>
        internal bool ReadsThePackagerOutput { get; }

        /// <summary>
        /// Gets a value indicating whether the step comes after a packager call in the text.
        /// </summary>
        internal bool AfterThePackager { get; }

        /// <summary>
        /// Reads a workflow this repository ships rather than a copy of it.
        /// </summary>
        /// <param name="workflow">The path, relative to the repository root.</param>
        /// <returns>The text.</returns>
        internal static string WorkflowText(string workflow) =>
            File.ReadAllText(Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), workflow));

        /// <summary>
        /// Reads a fixture from the tracked file rather than from a copy in the output directory,
        /// because a copy proves the state of the file on the day it was written.
        /// </summary>
        /// <param name="name">The file name.</param>
        /// <returns>The fixture text.</returns>
        internal static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                "Jellyfin.Plugin.WatchSync.Tests",
                "Release",
                name));

        /// <summary>
        /// Reads the gate out of workflow text.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>The gate.</returns>
        internal static ArchiveGate Read(string text)
        {
            var all = ReadAll(text);

            return all.Count > 0 ? all[0] : new ArchiveGate(false, false, false, false);
        }

        /// <summary>
        /// Counts the packager calls in the text, which is how many archives the route produces
        /// and therefore how many of these steps it owes.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>The number of calls.</returns>
        internal static int PackagerCalls(string text) =>
            Regex.Matches(
                text.Replace("\r\n", "\n", StringComparison.Ordinal),
                "(?m)^[ ]+" + Regex.Escape(Packager)).Count;

        /// <summary>
        /// Reads every occurrence of the step out of workflow text, in file order. A route that
        /// packages more than one line carries one of these per archive, and a reader that stopped
        /// at the first would report on one archive while the run publishes two.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>The occurrences.</returns>
        internal static IReadOnlyList<ArchiveGate> ReadAll(string text)
        {
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var gates = new List<ArchiveGate>();

            for (var start = 0; start < lines.Length; start++)
            {
                if (!Regex.IsMatch(lines[start], "^[ ]+- name: " + Regex.Escape(StepName) + "[ \t]*$"))
                {
                    continue;
                }

                gates.Add(ReadAt(lines, start));
            }

            return gates;
        }

        /// <summary>
        /// Reads the occurrence whose name is on the line given.
        /// </summary>
        /// <param name="lines">The workflow's lines.</param>
        /// <param name="start">The index of the step's name line.</param>
        /// <returns>The occurrence.</returns>
        private static ArchiveGate ReadAt(string[] lines, int start)
        {

            // The step's lines run until the next step at the same indentation.
            var indent = lines[start].Length - lines[start].TrimStart().Length;
            var body = new List<string>();

            for (var index = start + 1; index < lines.Length; index++)
            {
                var line = lines[index];
                var lineIndent = line.Length - line.TrimStart().Length;

                if (line.Trim().Length > 0 && lineIndent <= indent && line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
                {
                    break;
                }

                if (line.Trim().Length > 0 && lineIndent < indent)
                {
                    break;
                }

                body.Add(line);
            }

            var packager = Array.FindLastIndex(lines, Math.Max(start - 1, 0), line => line.TrimStart().StartsWith(Packager, StringComparison.Ordinal));

            return new ArchiveGate(
                true,
                body.Any(line => line.Contains(Checker, StringComparison.Ordinal) && line.Contains("--archive", StringComparison.Ordinal)),
                body.Any(line => PackagerOutput.IsMatch(line)),
                packager >= 0 && packager < start);
        }
    }
}
