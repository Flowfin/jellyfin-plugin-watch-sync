using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Refuses a test that needs a display, elevation, a machine-wide trust store, a machine-wide
/// path, the network, a real wait or the machine clock.
///
/// The rule this enforces is a sentence until something refuses a violation of it. A test that
/// quietly needs one of those passes on the machine it was written on and is found by whoever
/// next runs the suite somewhere else, which for a suite that runs in one place is the same as
/// never being found at all.
///
/// The vocabulary, the declared exceptions and the two fixtures are data files rather than
/// source. A scanner carrying these literals in a .cs file would match itself, and the exclusion
/// that repaired that would be a hole in the middle of the guard.
/// </summary>
public class HeadlessGuardTests
{
    /// <summary>
    /// The whole point, run against the tree as it is. Every finding carries what the call would
    /// need and what to use instead, because a guard that only says no is a guard people work
    /// around.
    /// </summary>
    [Fact]
    public void TheTrackedTestSourcesNeedNoDisplayElevationTrustStoreNetworkOrMachineClock()
    {
        var report = HeadlessGuard.ScanTheTree();

        Assert.Empty(report.Findings.Select(finding =>
            $"{finding.Path}:{finding.Line} needs {finding.Needs} ({finding.Id}). Use {finding.Instead}."));
    }

    /// <summary>
    /// An exception is a debt rather than a dispensation, so it fails closed in both directions.
    /// One that names a file the scan does not reach, or a file that no longer carries the call
    /// it was written for, is refused rather than left to rot.
    /// </summary>
    [Fact]
    public void NoDeclaredExceptionHasOutlivedWhatItWasWrittenFor()
    {
        var report = HeadlessGuard.ScanTheTree();

        Assert.Empty(report.Dangling.Select(entry =>
            $"{entry.Path} no longer carries a hit for {entry.Id}, so its exception is dangling."));
    }

    /// <summary>
    /// The guard proven by deleting it, on the mistake somebody actually makes. The near-miss is
    /// a backoff test that waits on real elapsed time instead of on the injected clock. Everything
    /// else about the fixture is right, which is what makes it the near-miss rather than an
    /// obviously broken file, and the repair is that one line and nothing else.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheNearMissAndPassesItsRepair()
    {
        var vocabulary = HeadlessGuard.Vocabulary();

        var refused = HeadlessGuard.Scan(
            new[] { ("Headless/near-miss.txt", HeadlessGuard.Fixture("near-miss.txt")) },
            vocabulary,
            Array.Empty<HeadlessGuard.Departure>());

        var finding = Assert.Single(refused.Findings);
        Assert.Equal("sleep-thread", finding.Id);

        var repaired = HeadlessGuard.Scan(
            new[] { ("Headless/near-miss-repaired.txt", HeadlessGuard.Fixture("near-miss-repaired.txt")) },
            vocabulary,
            Array.Empty<HeadlessGuard.Departure>());

        Assert.Empty(repaired.Findings);
    }

    /// <summary>
    /// The exception leg proven the same way. One declared exception covers the call and removes
    /// the finding; a second one names an identifier the file does not carry and is reported as
    /// dangling. The tree declares no exceptions today, so this is where that leg is exercised.
    /// </summary>
    [Fact]
    public void AnExceptionCoversItsCallAndOneThatCoversNothingIsRefused()
    {
        var sources = new[] { ("Headless/near-miss.txt", HeadlessGuard.Fixture("near-miss.txt")) };
        var vocabulary = HeadlessGuard.Vocabulary();

        var covered = HeadlessGuard.Scan(
            sources,
            vocabulary,
            new[] { new HeadlessGuard.Departure("Headless/near-miss.txt", "sleep-thread", "a reason") });

        Assert.Empty(covered.Findings);
        Assert.Empty(covered.Dangling);

        var stale = HeadlessGuard.Scan(
            sources,
            vocabulary,
            new[] { new HeadlessGuard.Departure("Headless/near-miss.txt", "network-socket", "a reason") });

        var dangling = Assert.Single(stale.Dangling);
        Assert.Equal("network-socket", dangling.Id);
    }

    /// <summary>
    /// The scan is derived from the tree rather than from a list of file names, so a source added
    /// tomorrow is covered without anybody remembering to add it. This file asserts that about
    /// itself, and it names itself through the compiler rather than as a literal, so renaming it
    /// cannot leave the assertion pointing at a file that no longer exists.
    /// </summary>
    [Fact]
    public void TheScanReadsTheTreeRatherThanAListOfFileNames()
    {
        var root = HeadlessGuard.RepositoryRoot();

        Assert.NotEmpty(HeadlessGuard.TrackedTestSourcePaths(root));

        var scanned = HeadlessGuard.TestSources(root).Select(source => source.Path).ToList();
        var relative = Path.GetRelativePath(root, ThisFile()).Replace('\\', '/');

        Assert.Contains(relative, scanned);
    }

    /// <summary>
    /// The path of the file this call is written in, filled in by the compiler.
    /// </summary>
    /// <param name="path">Supplied by the compiler.</param>
    /// <returns>The path.</returns>
    private static string ThisFile([CallerFilePath] string path = "") => path;

    /// <summary>
    /// A vocabulary entry with a missing field would refuse a call and say nothing useful about
    /// it, and two entries sharing an identifier would make an exception cover a call nobody
    /// meant to except.
    /// </summary>
    [Fact]
    public void EveryVocabularyEntryIsCompleteAndNamedOnce()
    {
        var vocabulary = HeadlessGuard.Vocabulary();

        Assert.NotEmpty(vocabulary);
        Assert.All(vocabulary, rule =>
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Id));
            Assert.False(string.IsNullOrWhiteSpace(rule.Needs));
            Assert.False(string.IsNullOrWhiteSpace(rule.Instead));
        });

        Assert.Equal(vocabulary.Count, vocabulary.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count());
    }

    internal static class HeadlessGuard
    {
        private const string Separator = " :: ";

        private const string TestProject = "Jellyfin.Plugin.WatchSync.Tests";

        internal sealed record Rule(string Id, Regex Pattern, string Needs, string Instead);

        internal sealed record Departure(string Path, string Id, string Reason);

        internal sealed record Finding(string Path, int Line, string Id, string Needs, string Instead);

        internal sealed record Report(IReadOnlyList<Finding> Findings, IReadOnlyList<Departure> Dangling);

        /// <summary>
        /// Scans a set of sources against a vocabulary, honouring the declared departures and
        /// reporting the ones that matched nothing. Pure, so the fixtures above run through the
        /// same code the tree does rather than through a second implementation of it.
        /// </summary>
        /// <param name="sources">The path and text of each source to scan.</param>
        /// <param name="vocabulary">The rules to refuse.</param>
        /// <param name="departures">The declared exceptions.</param>
        /// <returns>What was found and which exceptions covered nothing.</returns>
        internal static Report Scan(
            IReadOnlyList<(string Path, string Text)> sources,
            IReadOnlyList<Rule> vocabulary,
            IReadOnlyList<Departure> departures)
        {
            var findings = new List<Finding>();
            var covered = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (path, text) in sources)
            {
                var lines = text.Split('\n');

                for (var index = 0; index < lines.Length; index++)
                {
                    foreach (var rule in vocabulary.Where(rule => rule.Pattern.IsMatch(lines[index])))
                    {
                        var declared = departures.Any(entry =>
                            string.Equals(entry.Path, path, StringComparison.Ordinal)
                            && string.Equals(entry.Id, rule.Id, StringComparison.Ordinal));

                        if (declared)
                        {
                            covered.Add(path + Separator + rule.Id);
                            continue;
                        }

                        findings.Add(new Finding(path, index + 1, rule.Id, rule.Needs, rule.Instead));
                    }
                }
            }

            var dangling = departures
                .Where(entry => !covered.Contains(entry.Path + Separator + entry.Id))
                .ToList();

            return new Report(findings, dangling);
        }

        /// <summary>
        /// Runs the scan over the tracked test sources of this repository.
        /// </summary>
        /// <returns>The report.</returns>
        internal static Report ScanTheTree()
        {
            var root = RepositoryRoot();

            return Scan(TestSources(root), Vocabulary(), Departures());
        }

        /// <summary>
        /// Walks up from the test binaries until the repository is found. The guard reads the
        /// tracked tree, so a run outside a checkout has nothing to judge and says so rather than
        /// passing.
        /// </summary>
        /// <returns>The repository root.</returns>
        internal static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                var marker = Path.Combine(directory.FullName, ".git");

                if (Directory.Exists(marker) || File.Exists(marker))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            Assert.Fail($"No repository root above {AppContext.BaseDirectory}. The guard reads the tracked tree and cannot judge a run that has none.");

            return string.Empty;
        }

        /// <summary>
        /// Asks git which sources the test project tracks, so nothing here is a list of file
        /// names anybody has to maintain.
        /// </summary>
        /// <param name="root">The repository root.</param>
        /// <returns>The repository-relative path of each tracked source.</returns>
        internal static IReadOnlyList<string> TrackedTestSourcePaths(string root) =>
            Git(root, "ls-files -- " + TestProject)
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.EndsWith(".cs", StringComparison.Ordinal))
                .ToList();

        /// <summary>
        /// The set the scan reads: what git tracks, plus what is on disk and not yet committed.
        /// Tracking alone would leave a file uncovered between being written and being committed,
        /// which is exactly when somebody is deciding whether the call they just typed is
        /// acceptable. Build output is left out because it is generated rather than written.
        /// </summary>
        /// <param name="root">The repository root.</param>
        /// <returns>The path and text of each source to scan.</returns>
        internal static IReadOnlyList<(string Path, string Text)> TestSources(string root)
        {
            var paths = new SortedSet<string>(TrackedTestSourcePaths(root), StringComparer.Ordinal);

            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, TestProject), "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');

                if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal))
                {
                    continue;
                }

                paths.Add(relative);
            }

            return paths
                .Select(path => (Path: path, Text: File.ReadAllText(Path.Combine(root, path))))
                .ToList();
        }

        /// <summary>
        /// Reads the vocabulary from the tracked file rather than from a copy in the output
        /// directory, because a copy proves the state of the file on the day it was copied.
        /// </summary>
        /// <returns>The rules.</returns>
        internal static IReadOnlyList<Rule> Vocabulary() =>
            Entries("vocabulary.txt", 4)
                .Select(fields => new Rule(fields[0], new Regex(fields[1], RegexOptions.CultureInvariant), fields[2], fields[3]))
                .ToList();

        /// <summary>
        /// Reads the declared exceptions.
        /// </summary>
        /// <returns>The departures.</returns>
        internal static IReadOnlyList<Departure> Departures() =>
            Entries("exceptions.txt", 3)
                .Select(fields => new Departure(fields[0], fields[1], fields[2]))
                .ToList();

        /// <summary>
        /// Reads one of the two fixtures.
        /// </summary>
        /// <param name="name">The fixture file name.</param>
        /// <returns>Its text.</returns>
        internal static string Fixture(string name) => File.ReadAllText(DataFile(name));

        private static IReadOnlyList<string[]> Entries(string name, int fields)
        {
            var entries = new List<string[]>();

            foreach (var line in File.ReadAllLines(DataFile(name)))
            {
                var trimmed = line.Trim();

                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                var parts = trimmed.Split(Separator);
                Assert.True(parts.Length == fields, $"{name} has an entry with {parts.Length} fields where {fields} are required: {trimmed}");

                entries.Add(parts.Select(part => part.Trim()).ToArray());
            }

            return entries;
        }

        private static string DataFile(string name) =>
            Path.Combine(RepositoryRoot(), TestProject, "Headless", name);

        private static string Git(string root, string arguments)
        {
            var start = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(start);
            Assert.NotNull(process);

            var output = process!.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(process.ExitCode == 0, $"git {arguments} exited {process.ExitCode}: {error}");

            return output;
        }
    }
}
