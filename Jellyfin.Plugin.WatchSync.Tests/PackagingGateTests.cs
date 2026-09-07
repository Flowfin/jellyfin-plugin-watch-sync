using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds every call to the packaging tool in this repository to one commit and to the
/// framework declared by the manifest standing in front of it.
///
/// Two routes call it and both are files here: the merge gate packages on every pull request,
/// and the release packages what it ships. The failure this refuses is them drifting apart: a
/// route packaging bytes another never builds proves nothing about that other one, and it is
/// the same defect whichever pin moves.
///
/// There were three until #90. `.github/workflows/build.yaml` called a shared workflow in
/// another organisation's tree, that workflow ran the same packager, and a grep for the tool
/// over these files found two of the three. That call is gone, so every call this rule is
/// about is now a call it can read, and the bound this comment carried - that the third
/// route's own steps were not in this tree - has gone with it.
///
/// The framework rule is the sharper of the two and it is not tidiness. The packager writes
/// the metadata that travels with the archive out of the manifest's top level `targetAbi`
/// rather than out of the framework it was told to build, so a call passing a framework the
/// manifest in front of it does not declare produces an archive compiled for one line and
/// stamped with the other. That is a plugin claiming a server it cannot run on, arriving from
/// the packaging rather than from anybody writing a wrong number.
///
/// <para>
/// UNTIL #101 THAT RULE WAS A RULE ABOUT ONE MANIFEST, and it read every gate call against
/// `build.yaml` because there was one call and one manifest. #375 landed a manifest per line
/// and the gate now packages both, so what a call is held to is the manifest that stands at
/// `build.yaml` when it runs, which is `build.yaml` itself until a step swaps another there.
/// A second call added with no swap in front of it is exactly the archive the rule is about,
/// and it is the near-miss below.
/// </para>
/// </summary>
public class PackagingGateTests
{
    /// <summary>
    /// The merge gate and the release both package. Deleting either call leaves one route
    /// unmeasured and the other one unmatched, and neither is visible in the file that lost it.
    /// </summary>
    [Fact]
    public void TheGateAndTheReleaseBothCallThePackager()
    {
        var calls = PackagerCall.InThisRepository();

        Assert.Contains(calls, call => call.Workflow.EndsWith("package.yaml", StringComparison.Ordinal));
        Assert.Contains(calls, call => call.Workflow.EndsWith("publish.yaml", StringComparison.Ordinal));
    }

    /// <summary>
    /// One commit across every call. A pin moved in one file and not in the other is two
    /// packagers, and the gate then reports on a tool the release does not use.
    /// </summary>
    [Fact]
    public void EveryPackagerCallIsPinnedToOneCommit()
    {
        var calls = PackagerCall.InThisRepository();

        var commits = calls.Select(call => call.Commit).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        Assert.True(
            commits.Count == 1,
            $"The packager is called at {commits.Count} different commits: {string.Join(", ", calls.Select(call => $"{call.Workflow} at {call.Commit}"))}");
    }

    /// <summary>
    /// Every call builds the framework the manifest it reads names beside the ABI it stamps. A
    /// call passing a framework that manifest does not declare is the archive that claims a
    /// server it cannot run on.
    ///
    /// Two shapes satisfy it. A call on a route that swaps by name is held to the manifest
    /// standing at `build.yaml` when it runs, which the reader follows step by step. The
    /// release route names no manifest a reader here can resolve: its gate derives the
    /// framework from the tag and swaps the matching manifest to that name first, and
    /// ReleaseLineTests holds each row of that table to the manifest it names. So the release
    /// call is held here to taking the gate's output and there to what the output can be, and a
    /// literal reappearing on it is refused by both.
    /// </summary>
    [Fact]
    public void EveryPackagerCallBuildsTheFrameworkTheManifestDeclares()
    {
        Assert.All(PackagerCall.InThisRepository(), call => AssertTheCallMatchesItsManifest(call));
    }

    /// <summary>
    /// The merge gate packages every line the manifest set declares, one call each. This is
    /// #101's first condition: a pull request that breaks the packaging of either line fails on
    /// the change that made it rather than on a tag three weeks later, and a gate covering one
    /// of two lines leaves the other one packaged for the first time by a release.
    /// </summary>
    [Fact]
    public void TheMergeGatePackagesEveryLineTheManifestsDeclare()
    {
        var declared = BuildTargetsTests.BuildFacts.DeclaredTargets()
            .Select(target => target.Framework)
            .OrderBy(framework => framework, StringComparer.Ordinal)
            .ToList();

        var packaged = PackagerCall.InThisRepository()
            .Where(call => call.Workflow.EndsWith(PackagerCall.MergeGate, StringComparison.Ordinal))
            .Select(call => call.Framework)
            .OrderBy(framework => framework, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            declared.SequenceEqual(packaged, StringComparer.Ordinal),
            $"The manifests declare the lines {string.Join(", ", declared)} and the merge gate packages {string.Join(", ", packaged)}. A line the gate does not package is a line a pull request cannot break visibly.");
    }

    /// <summary>
    /// The guard proven by the mistake the packager invites: a second call added for the other
    /// line with no manifest swap in front of it. Everything about that run is green - two
    /// archives are produced, both check out against the manifest, both carry an inventory -
    /// and the second one is compiled for the 12.0 line and stamped with the 10.11 ABI, which
    /// installs on a server it cannot run on. Its repair is the swap.
    /// </summary>
    [Fact]
    public void TheGuardRefusesASecondCallWithNoSwapInFrontOfItAndPassesItsRepair()
    {
        var mistake = PackagerCall.In(
            PackagerCall.MergeGate,
            PackagerCall.Fixture("gate-second-call-without-the-swap-near-miss.txt"));

        Assert.Equal(2, mistake.Count);
        Assert.All(mistake, call => Assert.Equal(PackagerCall.DefaultManifest, call.Manifest));

        Assert.NotEqual(
            BuildTargetsTests.BuildFacts.ScalarAtColumnZero(
                PackagerCall.ManifestText(mistake[1].Manifest),
                "framework"),
            mistake[1].Framework);

        var repaired = PackagerCall.In(
            PackagerCall.MergeGate,
            PackagerCall.Fixture("gate-second-call-without-the-swap-near-miss-repaired.txt"));

        Assert.Equal(2, repaired.Count);
        Assert.Equal("build.yaml", repaired[0].Manifest);
        Assert.Equal("build-jf12.yaml", repaired[1].Manifest);
        Assert.All(repaired, call => AssertTheCallMatchesItsManifest(call));
    }

    /// <summary>
    /// The guard proven by the state this repository was in before #101's first condition: one
    /// call, one line, and a second line nothing on a pull request builds. It is the shape a
    /// green gate hides best, because nothing about a run that packages one line looks like a
    /// run that was meant to package two.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAGateThatPackagesOneOfTwoLines()
    {
        var packaged = PackagerCall.In(
            PackagerCall.MergeGate,
            PackagerCall.Fixture("gate-one-line-near-miss.txt"));

        Assert.Single(packaged);
        Assert.NotEqual(
            BuildTargetsTests.BuildFacts.DeclaredTargets().Count,
            packaged.Count);
    }

    /// <summary>
    /// Holds one call to the framework its manifest declares, in the shape the facts above
    /// share, so the near-miss is judged by the rule rather than by a copy of it.
    /// </summary>
    /// <param name="call">The call.</param>
    private static void AssertTheCallMatchesItsManifest(PackagerCall call)
    {
        if (call.Workflow.EndsWith("publish.yaml", StringComparison.Ordinal))
        {
            Assert.True(
                string.Equals(call.Framework, ReleaseLineTests.ReleaseLines.FrameworkFromTheGate, StringComparison.Ordinal),
                $"{call.Workflow} packages {call.Framework} rather than the framework its gate derived from the tag, so a tag naming the other line would build this one.");
            return;
        }

        var declared = BuildTargetsTests.BuildFacts.ScalarAtColumnZero(
            PackagerCall.ManifestText(call.Manifest),
            "framework");

        Assert.True(
            string.Equals(call.Framework, declared, StringComparison.Ordinal),
            $"{call.Workflow} packages {call.Framework} while {call.Manifest}, the manifest standing at build.yaml when that call runs, declares {declared} beside the ABI the packager stamps, so that archive would claim a server line it was not built for.");
    }

    /// <summary>
    /// Every call the workflows make to the packaging tool, read out of the workflows this
    /// repository ships rather than out of a copy of them.
    /// </summary>
    internal sealed class PackagerCall
    {
        /// <summary>
        /// The route that packages on every pull request.
        /// </summary>
        internal const string MergeGate = "package.yaml";

        /// <summary>
        /// The manifest the packager finds by name. Every other manifest reaches it by standing
        /// here for the length of a call.
        /// </summary>
        internal const string DefaultManifest = "build.yaml";

        /// <summary>
        /// The action, without its version, so a call is found whichever commit it is pinned to.
        /// </summary>
        private const string Action = "oddstr13/jellyfin-plugin-repository-manager";

        /// <summary>
        /// The call, anchored on the `uses:` line and reading the framework out of the `with:`
        /// block under it. Anchored rather than parsed, which is what every other read of these
        /// workflows in this suite does and for the same reason: one dependency for one block,
        /// in a file its readers read by eye.
        /// </summary>
        private static readonly Regex Call = new Regex(
            "^[ ]+uses: " + Regex.Escape(Action) + "@(?<commit>[0-9a-fA-F]{40})[^\n]*\n(?:[^\n]*\n){0,4}?[ ]+dotnet-target: \"(?<framework>[^\"]+)\"",
            RegexOptions.Multiline);

        /// <summary>
        /// The swap, as the step that makes it declares the manifest it puts at the name the
        /// packager reads. A value that is not a file name is a manifest this reader cannot
        /// resolve, which is the release route's shape and is judged there instead.
        /// </summary>
        private static readonly Regex Swap = new Regex(
            "^[ ]+MANIFEST: (?<manifest>[A-Za-z0-9][A-Za-z0-9._-]*\\.ya?ml)[ \t]*$",
            RegexOptions.Multiline);

        private PackagerCall(string workflow, string commit, string framework, string manifest)
        {
            Workflow = workflow;
            Commit = commit;
            Framework = framework;
            Manifest = manifest;
        }

        /// <summary>
        /// Gets the path of the workflow the call is in, relative to the repository root.
        /// </summary>
        internal string Workflow { get; }

        /// <summary>
        /// Gets the commit the action is pinned to.
        /// </summary>
        internal string Commit { get; }

        /// <summary>
        /// Gets the target framework the call passes.
        /// </summary>
        internal string Framework { get; }

        /// <summary>
        /// Gets the manifest standing at the name the packager reads when the call runs, which is
        /// the default until a step above the call swaps another one there.
        /// </summary>
        internal string Manifest { get; }

        /// <summary>
        /// Reads a manifest this repository ships rather than a copy of it.
        /// </summary>
        /// <param name="manifest">The file name at the repository root.</param>
        /// <returns>Its text.</returns>
        internal static string ManifestText(string manifest)
        {
            var path = Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), manifest);

            Assert.True(File.Exists(path), $"{manifest} is named as the manifest a packager call reads and is not at the repository root, so that call would package whatever stands at {DefaultManifest} instead.");

            return File.ReadAllText(path);
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

        /// <summary>
        /// Reads every call out of one workflow's text, in file order, each carrying the manifest
        /// standing in front of it.
        /// </summary>
        /// <param name="workflow">The path the calls are reported under.</param>
        /// <param name="text">The workflow text.</param>
        /// <returns>The calls.</returns>
        internal static IReadOnlyList<PackagerCall> In(string workflow, string text)
        {
            var normalised = text.Replace("\r\n", "\n", StringComparison.Ordinal);

            // A file naming the action with no framework beside it is a call this reader
            // cannot judge rather than a file with no call, and passing it would leave the
            // rule silent about exactly the shape it exists for.
            var found = Call.Matches(normalised).Count;
            var named = Regex.Matches(normalised, "^[ ]+uses: " + Regex.Escape(Action) + "@", RegexOptions.Multiline).Count;

            Assert.True(
                found == named,
                $"{workflow} calls the packager {named} times and {found} of those pass a target framework this reader could read. A call with no `dotnet-target` builds whatever the manifest defaults to and is not held by these rules.");

            var events = new List<(int Index, string Manifest, Match Call)>();

            foreach (Match swap in Swap.Matches(normalised))
            {
                events.Add((swap.Index, swap.Groups["manifest"].Value, null!));
            }

            foreach (Match call in Call.Matches(normalised))
            {
                events.Add((call.Index, string.Empty, call));
            }

            var calls = new List<PackagerCall>();
            var standing = DefaultManifest;

            foreach (var entry in events.OrderBy(entry => entry.Index))
            {
                if (entry.Call is null)
                {
                    standing = entry.Manifest;
                    continue;
                }

                calls.Add(new PackagerCall(
                    workflow,
                    entry.Call.Groups["commit"].Value,
                    entry.Call.Groups["framework"].Value,
                    standing));
            }

            return calls;
        }

        /// <summary>
        /// Reads every call in the workflows this repository ships.
        /// </summary>
        /// <returns>The calls, in the order the files are read.</returns>
        internal static IReadOnlyList<PackagerCall> InThisRepository()
        {
            var root = HeadlessGuardTests.HeadlessGuard.RepositoryRoot();
            var directory = Path.Combine(root, ".github", "workflows");

            Assert.True(Directory.Exists(directory), $"{directory} does not exist, so no workflow could be read.");

            var calls = new List<PackagerCall>();

            foreach (var file in Directory.EnumerateFiles(directory).OrderBy(path => path, StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

                calls.AddRange(In(relative, File.ReadAllText(file)));
            }

            Assert.NotEmpty(calls);

            return calls;
        }
    }
}
