using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the files the publish route hands from the build job to the release: the archive,
/// the packaging metadata written beside it, and the manifest the package was built from.
/// Everything the release attaches is one of those files, so a file missing here is a file
/// missing from the release.
///
/// Both failures this exists for are silent ones that cost a tag. A path that names nothing
/// is a warning rather than an error, because the upload's own error switch fires only when
/// no path matches at all, so a release short of one asset publishes and reports success. A
/// path taken from the checkout instead of from the packager's output directory is worse: the
/// upload roots an artifact at the deepest directory its paths have in common, so adding one
/// workspace-relative path moves the archive a directory down in the download, where the
/// release job's glob does not find it and the run stops on a tag that cannot be reused.
/// </summary>
public class ReleaseAssetsTests
{
    /// <summary>
    /// The manifest carries the version, the ABI and the framework the package was built with.
    /// A catalog entry for a release, and every repair of one, is written from those three
    /// values, and reading them back out of the tree afterwards reads a later commit.
    /// </summary>
    [Fact]
    public void TheManifestThePackageWasBuiltFromIsHandedToTheRelease()
    {
        var upload = ReleaseUpload.OfThisRepository();

        Assert.True(upload.CarriesTheManifest, $"None of the paths the build job hands over is the manifest: {string.Join(", ", upload.Paths)}");
    }

    /// <summary>
    /// Every path is the output of a step in the same job, which is what keeps all of them in
    /// the packager's output directory and the download flat. A literal path is one relative to
    /// the workspace, and the workspace is a directory above the one the archive is in.
    /// </summary>
    [Fact]
    public void EveryFileHandedToTheReleaseIsNamedByTheStepThatProducedIt()
    {
        var upload = ReleaseUpload.OfThisRepository();

        Assert.All(upload.Paths, path => Assert.StartsWith("${{ steps.", path, StringComparison.Ordinal));
    }

    /// <summary>
    /// The guard proven by the mistake somebody writes when they add the manifest: naming it
    /// where it sits in the checkout, which is one word shorter than the step output and reads
    /// as obviously correct. Nothing about that fixture is red until a tag has been pushed. The
    /// repair is the copy beside the package and the step output that names the copy.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAManifestTakenFromTheCheckoutAndPassesItsRepair()
    {
        var mistake = ReleaseUpload.Read(ReleaseUpload.Fixture("manifest-from-the-checkout-near-miss.txt"));

        Assert.True(mistake.CarriesTheManifest);
        Assert.Contains(mistake.Paths, path => !path.StartsWith("${{ steps.", StringComparison.Ordinal));

        var repaired = ReleaseUpload.Read(ReleaseUpload.Fixture("manifest-from-the-checkout-near-miss-repaired.txt"));

        Assert.True(repaired.CarriesTheManifest);
        Assert.All(repaired.Paths, path => Assert.StartsWith("${{ steps.", path, StringComparison.Ordinal));
    }

    /// <summary>
    /// Reads the paths the build job hands to the later jobs out of the publish workflow. The
    /// read is anchored on the upload step rather than on a YAML parse, which is what every
    /// other read of this route in the suite does, for the same reason: one dependency for one
    /// block, in a file its readers read by eye.
    /// </summary>
    internal sealed class ReleaseUpload
    {
        private const string Workflow = ".github/workflows/publish.yaml";

        private ReleaseUpload(IReadOnlyList<string> paths, IReadOnlyList<string> manifestOutputs)
        {
            Paths = paths;
            ManifestOutputs = manifestOutputs;
        }

        /// <summary>
        /// Gets each path the build job hands over, verbatim.
        /// </summary>
        internal IReadOnlyList<string> Paths { get; }

        /// <summary>
        /// Gets every step output in the workflow that was written a path ending in the manifest,
        /// as the expression a later step names it by.
        /// </summary>
        internal IReadOnlyList<string> ManifestOutputs { get; }

        /// <summary>
        /// Gets a value indicating whether one of the paths is the manifest, whether it is written
        /// as a path or as the output of the step that put a copy beside the package.
        /// </summary>
        internal bool CarriesTheManifest =>
            Paths.Any(path =>
                path.EndsWith("build.yaml", StringComparison.Ordinal)
                || ManifestOutputs.Any(output => string.Equals(path, output, StringComparison.Ordinal)));

        /// <summary>
        /// Reads the route this repository ships rather than a copy of it.
        /// </summary>
        /// <returns>The upload.</returns>
        internal static ReleaseUpload OfThisRepository() =>
            Read(File.ReadAllText(Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), Workflow)));

        /// <summary>
        /// Reads an upload out of workflow text.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>The upload.</returns>
        internal static ReleaseUpload Read(string text)
        {
            // The block scalar under the upload step's path key. Its entries are indented
            // further than the key, which is what ends the block and what the count below
            // refuses having read as empty.
            var block = Regex.Match(
                text,
                "(?m)^[ ]+uses: actions/upload-artifact[^\\n]*\\n(?:[^\\n]*\\n)*?[ ]+path: \\|[^\\n]*\\n(?<paths>(?:[ ]{12,}\\S[^\\n]*\\n)+)");

            if (!block.Success)
            {
                Assert.Fail($"No path block was found on the upload step in {Workflow}. That step is the whole of what the release attaches, and nothing here is judging it.");
            }

            var paths = block.Groups["paths"].Value
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();

            if (paths.Count == 0)
            {
                Assert.Fail($"The upload step in {Workflow} was found with no paths under it, so nothing here is judging what the release carries.");
            }

            return new ReleaseUpload(paths, ManifestOutputsOf(text));
        }

        /// <summary>
        /// Reads every step output written a path that ends in the manifest, and returns the
        /// expression a later step names it by. A step that copies the manifest and hands its
        /// location on is how the file reaches the upload without a path relative to the
        /// workspace, so the value is followed rather than the word.
        /// </summary>
        /// <param name="text">The workflow text.</param>
        /// <returns>The expressions.</returns>
        private static IReadOnlyList<string> ManifestOutputsOf(string text)
        {
            var outputs = new List<string>();
            var step = string.Empty;

            foreach (var line in text.Split('\n'))
            {
                var identifier = Regex.Match(line, "^[ ]+id:[ ]*(?<id>[A-Za-z0-9_-]+)[ \t\r]*$");

                if (identifier.Success)
                {
                    step = identifier.Groups["id"].Value;
                    continue;
                }

                var written = Regex.Match(line, "(?<name>[a-z][a-z0-9_-]*)=(?<value>[^\"\\r\\n]*build\\.yaml)");

                if (written.Success && step.Length > 0)
                {
                    outputs.Add($"${{{{ steps.{step}.outputs.{written.Groups["name"].Value} }}}}");
                }
            }

            return outputs;
        }

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
    }
}
