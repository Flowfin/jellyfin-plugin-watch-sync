using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds every call to the packaging tool in this repository to one commit and to the one
/// framework the manifest declares at its top level.
///
/// Two routes call it now, the merge gate and the release, and the failure this refuses is
/// them drifting apart: a gate packaging bytes the release never builds proves nothing about
/// the release, and it is the same defect whichever of the two pins moves.
///
/// The framework rule is the sharper of the two and it is not tidiness. The packager writes
/// the metadata that travels with the archive out of the manifest's top level `targetAbi`
/// rather than out of the framework it was told to build, so a second call passing the other
/// server line produces an archive compiled for that line and stamped with this one. That is a
/// plugin claiming a server it cannot run on, arriving from the packaging rather than from
/// anybody writing a wrong number. What retires this rule is a manifest per line, which is the
/// reading on #101 and the remaining half of that issue.
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
    /// Every call builds the framework the manifest names beside the ABI it stamps. A call
    /// passing the other line's framework is the archive that claims a server it cannot run on.
    /// </summary>
    [Fact]
    public void EveryPackagerCallBuildsTheFrameworkTheManifestDeclares()
    {
        var declared = BuildTargetsTests.BuildFacts.ScalarAtColumnZero(BuildTargetsTests.BuildFacts.ManifestText(), "framework");
        var calls = PackagerCall.InThisRepository();

        Assert.All(
            calls,
            call => Assert.True(
                string.Equals(call.Framework, declared, StringComparison.Ordinal),
                $"{call.Workflow} packages {call.Framework} while build.yaml declares {declared} beside the ABI the packager stamps, so that archive would claim a server line it was not built for."));
    }

    /// <summary>
    /// Every call the workflows make to the packaging tool, read out of the workflows this
    /// repository ships rather than out of a copy of them.
    /// </summary>
    internal sealed class PackagerCall
    {
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
            "(?m)^[ ]+uses: " + Regex.Escape(Action) + "@(?<commit>[0-9a-fA-F]{40})[^\n]*\n(?:[^\n]*\n){0,4}?[ ]+dotnet-target: \"(?<framework>[^\"]+)\"",
            RegexOptions.None);

        private PackagerCall(string workflow, string commit, string framework)
        {
            Workflow = workflow;
            Commit = commit;
            Framework = framework;
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
                var text = File.ReadAllText(file);
                var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

                // A file naming the action with no framework beside it is a call this reader
                // cannot judge rather than a file with no call, and passing it would leave the
                // rule silent about exactly the shape it exists for.
                var found = Call.Matches(text).Count;
                var named = Regex.Matches(text, "(?m)^[ ]+uses: " + Regex.Escape(Action) + "@").Count;

                Assert.True(
                    found == named,
                    $"{relative} calls the packager {named} times and {found} of those pass a target framework this reader could read. A call with no `dotnet-target` builds whatever the manifest defaults to and is not held by these rules.");

                foreach (Match match in Call.Matches(text))
                {
                    calls.Add(new PackagerCall(relative, match.Groups["commit"].Value, match.Groups["framework"].Value));
                }
            }

            Assert.NotEmpty(calls);

            return calls;
        }
    }
}
