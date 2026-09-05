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
        /// The expression that names the archive the packager produced.
        /// </summary>
        private const string PackagerOutput = "${{ steps.jprm.outputs.artifact }}";

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
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

            var start = Array.FindIndex(lines, line => Regex.IsMatch(line, "^[ ]+- name: " + Regex.Escape(StepName) + "[ \t]*$"));

            if (start < 0)
            {
                return new ArchiveGate(false, false, false, false);
            }

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

            var packager = Array.FindIndex(lines, line => line.TrimStart().StartsWith(Packager, StringComparison.Ordinal));

            return new ArchiveGate(
                true,
                body.Any(line => line.Contains(Checker, StringComparison.Ordinal) && line.Contains("--archive", StringComparison.Ordinal)),
                body.Any(line => line.Contains(PackagerOutput, StringComparison.Ordinal)),
                packager >= 0 && packager < start);
        }
    }
}
