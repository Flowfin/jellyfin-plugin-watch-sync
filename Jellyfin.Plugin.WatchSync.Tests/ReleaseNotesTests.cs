using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the route that carries a changelog fragment to the person deciding whether to
/// upgrade.
///
/// The fragments under `changelog.d/` are written for an operator whose users' watch state
/// this plugin writes into, and until the assembler existed nothing read them: the publish
/// route asked GitHub to generate notes from the merged pull requests, so every entry
/// reached the directory and stopped there. `ChangelogFragmentTests` holds the format of a
/// fragment. This holds the other end, which is that a release actually reads them.
///
/// Three edits in three files connect the two ends and nothing but a reading connected them:
/// the assembler, the step in the publish route that runs it, and the step in the pull
/// request check that proves it bites. Each is one deletion away from a route that publishes
/// exactly as it did before, in silence, and the silence is the failure. A release with no
/// operator-facing notes fails nothing and is discovered by the operator.
/// </summary>
public class ReleaseNotesTests
{
    /// <summary>
    /// The publish route runs the assembler. This is the one that catches the step being
    /// removed or renamed away, which would leave the fragments unread again with every
    /// other check on this repository green.
    /// </summary>
    [Fact]
    public void ThePublishRouteAssemblesTheNotesFromTheFragments()
    {
        var publish = ReleaseNotesRoute.Publish();

        Assert.Contains(ReleaseNotesRoute.Assembler, publish, StringComparison.Ordinal);
    }

    /// <summary>
    /// The assembled notes are the body of the release, taken from the file the gate job
    /// wrote. Without this the run assembles the notes, prints them on the run page and
    /// publishes a release that carries none of them, which is the shape a green run hides
    /// best.
    /// </summary>
    [Fact]
    public void TheReleaseCarriesTheAssembledNotesAsItsBody()
    {
        var publish = ReleaseNotesRoute.Publish();

        Assert.Contains("body_path: ${{ runner.temp }}/notes/release-notes.md", publish, StringComparison.Ordinal);
    }

    /// <summary>
    /// The notes are downloaded into the runner's temporary directory and never into the
    /// working directory. The release step attaches every file it finds there, so a notes
    /// file landing beside the archive would be published as a release asset as well as
    /// read as the body, and the checksum step counts the files it expects to find.
    /// </summary>
    [Fact]
    public void TheNotesAreDownloadedOutsideTheDirectoryTheAssetsAreAttachedFrom()
    {
        var publish = ReleaseNotesRoute.Publish();

        Assert.Contains("path: ${{ runner.temp }}/notes", publish, StringComparison.Ordinal);
    }

    /// <summary>
    /// The assembler is proven on every pull request. It runs once per release, in a route
    /// no pull request takes, so a version of it that had been gutted into a pass would be
    /// discovered by the first release that needed it and not before.
    ///
    /// Each fixture is named rather than the step being asserted to exist, because a step
    /// that still runs one of the four proves the arm the other three were about.
    /// </summary>
    /// <param name="fixture">A path the proof step has to name.</param>
    [Theory]
    [InlineData(".github/release-notes-near-miss/0001-a-conflict-rule-that-moves.md")]
    [InlineData(".github/release-notes-near-miss-repaired/*.md")]
    [InlineData(".github/release-notes-ordering/*.md")]
    [InlineData(".github/release-notes-ordering/0001-an-ordinary-change.md")]
    public void TheAssemblerIsProvenOnEveryPullRequest(string fixture)
    {
        var check = ReleaseNotesRoute.PullRequestCheck();

        Assert.Contains(ReleaseNotesRoute.Assembler, check, StringComparison.Ordinal);
        Assert.Contains(fixture, check, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every fixture the proof step names is on disk. A step naming a path that is not there
    /// fails on the runner rather than passing, so this is the cheaper half of that failure:
    /// it says which file is missing here instead of in a workflow log.
    /// </summary>
    [Fact]
    public void EveryFixtureTheProofNamesIsInTheTree()
    {
        var root = HeadlessGuardTests.HeadlessGuard.RepositoryRoot();
        var missing = new List<string>();

        foreach (var directory in new[]
                 {
                     "release-notes-near-miss",
                     "release-notes-near-miss-repaired",
                     "release-notes-ordering",
                 })
        {
            var path = Path.Combine(root, ".github", directory);

            if (!Directory.Exists(path) || Directory.GetFiles(path, "*.md").Length == 0)
            {
                missing.Add(".github/" + directory);
            }
        }

        Assert.Empty(missing);
    }

    /// <summary>
    /// The assembler and the guard in the suite read one document and name the same two
    /// markings. They are two readers of one format, and the marking is what decides which
    /// entries the notes lead with, so an assembler spelling it differently would file every
    /// entry as ordinary and refuse nothing while doing it.
    /// </summary>
    [Fact]
    public void TheAssemblerNamesTheSameTwoMarkingsTheGuardDoes()
    {
        var assembler = ReleaseNotesRoute.Assembly();

        Assert.Contains("CHANGED = \"changed\"", assembler, StringComparison.Ordinal);
        Assert.Contains("UNCHANGED = \"unchanged\"", assembler, StringComparison.Ordinal);
    }

    /// <summary>
    /// The assembler reads the field names out of `docs/changelog.md` rather than carrying a
    /// second copy of them, which is what the guard in the suite already does. Two lists of
    /// one format drift, and the one that drifts is the one nothing runs.
    /// </summary>
    [Fact]
    public void TheAssemblerReadsTheFieldSetOutOfTheDocument()
    {
        var assembler = ReleaseNotesRoute.Assembly();

        Assert.Contains("docs\" / \"changelog.md", assembler, StringComparison.Ordinal);
        Assert.Contains("## The fields", assembler, StringComparison.Ordinal);
    }

    /// <summary>
    /// The files this class reads.
    /// </summary>
    internal static class ReleaseNotesRoute
    {
        /// <summary>
        /// The assembler, as the publish route and the proof both name it.
        /// </summary>
        internal const string Assembler = ".github/assemble-release-notes.py";

        /// <summary>
        /// The publish workflow.
        /// </summary>
        /// <returns>Its text.</returns>
        internal static string Publish() => Read(".github/workflows/publish.yaml");

        /// <summary>
        /// The workflow that proves the assembler on a pull request.
        /// </summary>
        /// <returns>Its text.</returns>
        internal static string PullRequestCheck() => Read(".github/workflows/pull-request-check.yml");

        /// <summary>
        /// The assembler itself.
        /// </summary>
        /// <returns>Its text.</returns>
        internal static string Assembly() => Read(Assembler);

        /// <summary>
        /// Reads a repository-relative file.
        /// </summary>
        /// <param name="relative">The path below the repository root.</param>
        /// <returns>Its text.</returns>
        private static string Read(string relative)
        {
            var path = Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                relative.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(path), $"{relative} is not in the tree, and the release notes route is three files that only work together.");

            return File.ReadAllText(path);
        }
    }
}
