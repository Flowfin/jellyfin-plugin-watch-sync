using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the component inventory to the release route, which is #118's first condition.
///
/// The merge gate has written a CycloneDX inventory since #101 and attaches it to the run,
/// where it expires with the run. Nothing carried one to a release, so an operator holding a
/// downloaded binary had no file to read and could only trust a claim about what is in it.
///
/// <para>
/// THE SUITE DOES NOT WRITE AN INVENTORY AND CANNOT. Producing one needs a NuGet tool fetched
/// over the network and a restored graph, and the headless rule refuses both. So the inventory
/// is a step in the run, CycloneDX is what writes it, and what this suite holds is that the
/// step is on the release route, that it runs after the packager, that it writes beside the
/// package rather than at the workspace root, that its path reaches the upload the release is
/// assembled from, that the release job asks for the file by name after the download, and that
/// the two packaging routes pin one version of one tool. A green run here has read a workflow
/// and never an inventory.
/// </para>
/// </summary>
public class ReleaseInventoryTests
{
    /// <summary>
    /// The release route writes the inventory, and every part of how it writes it is a way the
    /// release can end up without one.
    /// </summary>
    [Fact]
    public void TheReleaseRouteWritesTheComponentInventoryBesideThePackage()
    {
        var step = InventoryStep.Read(InventoryStep.WorkflowText(InventoryStep.PublishRoute));

        Assert.True(step.HasStep, $"{InventoryStep.PublishRoute} carries no step named `{InventoryStep.StepName}`, so a release publishes an archive with no list of what went into it.");
        Assert.True(step.RunsTheTool, $"{InventoryStep.PublishRoute} carries the step and it does not run {InventoryStep.Tool}, so the step is a name and not an inventory.");
        Assert.True(step.AfterThePackager, $"{InventoryStep.PublishRoute} carries the step before the packager call, so it describes a package that does not exist yet.");
        Assert.True(step.WritesBesideThePackage, $"{InventoryStep.PublishRoute} writes the inventory somewhere other than the packager's output directory. The upload roots its artifact at the deepest directory its paths share, so a file at the workspace root moves the archive a directory down in the download, where the release job's ./*.zip finds nothing.");
        Assert.True(step.HandsItOn, $"{InventoryStep.PublishRoute} writes the inventory and no step output names it, so a later step can only name it by a path relative to the workspace.");
    }

    /// <summary>
    /// The path reaches the upload the release job downloads and attaches. A step that writes a
    /// file nobody hands over produces an inventory that expires with the run, which is the state
    /// this change is against.
    /// </summary>
    [Fact]
    public void TheComponentInventoryTravelsWithTheRelease()
    {
        var text = InventoryStep.WorkflowText(InventoryStep.PublishRoute);
        var upload = ReleaseAssetsTests.ReleaseUpload.OfThisRepository();

        var inventory = InventoryStep.Carried(upload.Paths, text);

        Assert.True(
            inventory.Count == 1,
            $"The build job hands over {inventory.Count} paths that are the inventory: {string.Join(", ", upload.Paths)}");
        Assert.StartsWith("${{ steps.", inventory[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// The release job asks for the file by name after the download rather than trusting the
    /// upload. The upload's own error switch fires only when no path matches at all, so a path
    /// that stopped matching is a warning in a job that goes on to publish a release one asset
    /// short.
    /// </summary>
    [Fact]
    public void TheReleaseJobAsksForTheInventoryAfterTheDownload()
    {
        var text = InventoryStep.WorkflowText(InventoryStep.PublishRoute);

        Assert.True(
            InventoryStep.AsksForTheDownloadedFile(text),
            $"{InventoryStep.PublishRoute} does not test for ./{InventoryStep.Document} after the download and fail the run naming it, so a release short of the inventory is a state this route can reach.");
    }

    /// <summary>
    /// One tool at one version on both packaging routes. The release is not a second answer to
    /// what went into the package, and two versions of one tool over one graph is two answers
    /// whether or not they differ on the day they are read.
    /// </summary>
    [Fact]
    public void BothPackagingRoutesWriteTheInventoryWithOneToolAtOneVersion()
    {
        var gate = InventoryStep.PinnedVersion(InventoryStep.WorkflowText(InventoryStep.MergeGate));
        var release = InventoryStep.PinnedVersion(InventoryStep.WorkflowText(InventoryStep.PublishRoute));

        Assert.False(string.IsNullOrEmpty(gate), $"{InventoryStep.MergeGate} pins no version of {InventoryStep.Tool}, so the merge gate's inventory is written by whichever version the feed served.");
        Assert.False(string.IsNullOrEmpty(release), $"{InventoryStep.PublishRoute} pins no version of {InventoryStep.Tool}, so the release's inventory is written by whichever version the feed served.");
        Assert.True(
            string.Equals(gate, release, StringComparison.Ordinal),
            $"The merge gate pins {InventoryStep.Tool} {gate} and the release route pins {release}. One graph described by two versions of one tool is two answers to what went into the package.");
    }

    /// <summary>
    /// The guard proven by the mistake somebody makes: the merge gate's step copied across
    /// verbatim, which writes the inventory into a directory of its own at the workspace root and
    /// names that literal in the upload. Every file is produced, the run is green, and the archive
    /// arrives in the release job one directory down where its glob finds nothing, after the tag
    /// has been pushed.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheMergeGatesSpellingOnTheReleaseRouteAndPassesItsRepair()
    {
        var mistake = InventoryStep.Read(InventoryStep.Fixture("inventory-at-the-workspace-root-near-miss.txt"));

        Assert.True(mistake.HasStep);
        Assert.True(mistake.RunsTheTool);
        Assert.False(mistake.WritesBesideThePackage);
        Assert.False(mistake.HandsItOn);

        var mistakenText = InventoryStep.Fixture("inventory-at-the-workspace-root-near-miss.txt");
        var mistakenUpload = ReleaseAssetsTests.ReleaseUpload.Read(mistakenText);
        var mistakenInventory = InventoryStep.Carried(mistakenUpload.Paths, mistakenText);

        Assert.Single(mistakenInventory);
        Assert.DoesNotContain("${{ steps.", mistakenInventory[0], StringComparison.Ordinal);

        var repaired = InventoryStep.Read(InventoryStep.Fixture("inventory-at-the-workspace-root-near-miss-repaired.txt"));

        Assert.True(repaired.HasStep);
        Assert.True(repaired.RunsTheTool);
        Assert.True(repaired.WritesBesideThePackage);
        Assert.True(repaired.HandsItOn);

        var repairedText = InventoryStep.Fixture("inventory-at-the-workspace-root-near-miss-repaired.txt");
        var repairedUpload = ReleaseAssetsTests.ReleaseUpload.Read(repairedText);
        var repairedInventory = InventoryStep.Carried(repairedUpload.Paths, repairedText);

        Assert.Single(repairedInventory);
        Assert.All(repairedUpload.Paths, path => Assert.StartsWith("${{ steps.", path, StringComparison.Ordinal));
    }

    /// <summary>
    /// The step deleted while tidying the release route, which leaves the file valid, the archive
    /// where it belongs and every other check green.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAReleaseRouteWithNoInventoryStep()
    {
        var mistake = InventoryStep.Read(InventoryStep.Fixture("inventory-absent-near-miss.txt"));

        Assert.False(mistake.HasStep);
        Assert.False(mistake.RunsTheTool);
    }

    /// <summary>
    /// One route's pin raised and the other left behind, which is what a dependency bump looks
    /// like when it is made in the file somebody had open.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAPinThatDriftedFromTheMergeGate()
    {
        var gate = InventoryStep.PinnedVersion(InventoryStep.WorkflowText(InventoryStep.MergeGate));
        var drifted = InventoryStep.PinnedVersion(InventoryStep.Fixture("inventory-version-drift-near-miss.txt"));

        Assert.False(string.IsNullOrEmpty(drifted));
        Assert.NotEqual(gate, drifted);

        var repaired = InventoryStep.PinnedVersion(InventoryStep.Fixture("inventory-at-the-workspace-root-near-miss-repaired.txt"));

        Assert.Equal(gate, repaired);
    }

    /// <summary>
    /// Reads the step out of workflow text. Anchored on the step's name and on the lines under it
    /// rather than on a YAML parse, which is what every other read of these workflows in this
    /// suite does and for the same reason: one dependency for one step, in a file its readers
    /// read by eye.
    /// </summary>
    internal sealed class InventoryStep
    {
        /// <summary>
        /// The step's name on the release route. A rename removes it from this guard, so change it
        /// deliberately or not at all.
        /// </summary>
        internal const string StepName = "Write the component inventory beside the package";

        /// <summary>
        /// The tool that writes the inventory, as the command line names it.
        /// </summary>
        internal const string Tool = "dotnet-CycloneDX";

        /// <summary>
        /// The file the inventory is written to on both routes.
        /// </summary>
        internal const string Document = "components.cdx.json";

        /// <summary>
        /// The route that publishes a release.
        /// </summary>
        internal const string PublishRoute = ".github/workflows/publish.yaml";

        /// <summary>
        /// The route that packages on every pull request.
        /// </summary>
        internal const string MergeGate = ".github/workflows/package.yaml";

        /// <summary>
        /// The packager action, without its version.
        /// </summary>
        private const string Packager = "uses: oddstr13/jellyfin-plugin-repository-manager@";

        /// <summary>
        /// The expression that names the archive the packager produced.
        /// </summary>
        private const string PackagerOutput = "${{ steps.jprm.outputs.artifact }}";

        private InventoryStep(bool hasStep, bool runsTheTool, bool writesBesideThePackage, bool handsItOn, bool afterThePackager)
        {
            HasStep = hasStep;
            RunsTheTool = runsTheTool;
            WritesBesideThePackage = writesBesideThePackage;
            HandsItOn = handsItOn;
            AfterThePackager = afterThePackager;
        }

        /// <summary>
        /// Gets a value indicating whether a step with the name is in the text.
        /// </summary>
        internal bool HasStep { get; }

        /// <summary>
        /// Gets a value indicating whether the step's lines run the tool.
        /// </summary>
        internal bool RunsTheTool { get; }

        /// <summary>
        /// Gets a value indicating whether the step writes into the directory the packager wrote
        /// its archive to, derived from the packager's own output rather than typed.
        /// </summary>
        internal bool WritesBesideThePackage { get; }

        /// <summary>
        /// Gets a value indicating whether the step writes the location it used to a step output.
        /// </summary>
        internal bool HandsItOn { get; }

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
        /// The version the text pins the tool's installation to, or an empty string where it pins
        /// none.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>The version.</returns>
        internal static string PinnedVersion(string text)
        {
            var pin = Regex.Match(text, @"dotnet tool install --global CycloneDX --version (?<version>[0-9][A-Za-z0-9.\-]*)");

            return pin.Success ? pin.Groups["version"].Value : string.Empty;
        }

        /// <summary>
        /// The paths of an upload that are the inventory, whether one is written as a path or as
        /// the output of the step that wrote the file. The value is followed rather than the word,
        /// because a step that writes the location it used is how the file reaches the upload
        /// without a path relative to the workspace.
        /// </summary>
        /// <param name="paths">The paths the upload names.</param>
        /// <param name="text">The workflow text the upload was read from.</param>
        /// <returns>The paths that are the inventory.</returns>
        internal static IReadOnlyList<string> Carried(IReadOnlyList<string> paths, string text)
        {
            var outputs = OutputsWritingTheInventory(text);

            return paths
                .Where(path =>
                    path.EndsWith(Document, StringComparison.Ordinal)
                    || outputs.Any(output => string.Equals(path, output, StringComparison.Ordinal)))
                .ToList();
        }

        /// <summary>
        /// Whether the text tests for the downloaded inventory by name and fails the run naming
        /// it. Both halves, because a test whose failure says nothing sends nobody anywhere.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>Whether it does.</returns>
        internal static bool AsksForTheDownloadedFile(string text)
        {
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

            var test = Array.FindIndex(lines, line => line.Contains("[ ! -f ./" + Document + " ]", StringComparison.Ordinal));

            if (test < 0)
            {
                return false;
            }

            for (var index = test + 1; index < lines.Length && index <= test + 4; index++)
            {
                if (lines[index].Contains("::error::", StringComparison.Ordinal) && lines[index].Contains("inventory", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Every step output in the text that was written a path ending in the inventory, as the
        /// expression a later step names it by.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>The expressions.</returns>
        private static IReadOnlyList<string> OutputsWritingTheInventory(string text)
        {
            var outputs = new List<string>();
            var step = string.Empty;

            foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                var identifier = Regex.Match(line, "^[ ]+id:[ ]*(?<id>[A-Za-z0-9_-]+)[ \t]*$");

                if (identifier.Success)
                {
                    step = identifier.Groups["id"].Value;
                    continue;
                }

                var written = Regex.Match(line, "(?<name>[a-z][a-z0-9_-]*)=(?<value>[^\"\\r\\n]*" + Regex.Escape(Document) + ")");

                if (written.Success && step.Length > 0)
                {
                    outputs.Add($"${{{{ steps.{step}.outputs.{written.Groups["name"].Value} }}}}");
                }
            }

            return outputs;
        }

        /// <summary>
        /// Reads the step out of workflow text.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>The step.</returns>
        internal static InventoryStep Read(string text)
        {
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

            var start = Array.FindIndex(lines, line => Regex.IsMatch(line, "^[ ]+- name: " + Regex.Escape(StepName) + "[ \t]*$"));

            if (start < 0)
            {
                return new InventoryStep(false, false, false, false, false);
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

            // The directory is derived from the archive the packager produced rather than typed,
            // so the two cannot be moved apart by editing one of them.
            var readsThePackagerOutput = body.Any(line => line.Contains(PackagerOutput, StringComparison.Ordinal));
            var derivesTheDirectory = body.Any(line => line.Contains("dirname", StringComparison.Ordinal) && line.Contains("ARTIFACT", StringComparison.Ordinal));

            return new InventoryStep(
                true,
                body.Any(line => line.TrimStart().StartsWith(Tool, StringComparison.Ordinal)),
                readsThePackagerOutput && derivesTheDirectory && body.Any(line => line.Contains("--output \"${directory}\"", StringComparison.Ordinal)),
                body.Any(line => line.Contains("inventory=${directory}/" + Document, StringComparison.Ordinal) && line.Contains("GITHUB_OUTPUT", StringComparison.Ordinal)),
                packager >= 0 && packager < start);
        }
    }
}
