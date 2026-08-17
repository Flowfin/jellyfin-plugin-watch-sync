using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Nothing the suite creates under the temporary root outlives the case that created it.
///
/// This is #86's third condition, and that issue records three times that it had no subject:
/// until the store folder arrived with #68 no case in this suite created a directory at all,
/// and an assertion over a root nothing writes to is a green run over nothing.
///
/// The condition asks for no leftover under the temporary root after the suite completes, and
/// nothing inside the suite runs after the suite. A case reading the root while other cases are
/// still running reads a moving directory, and a case ordered last would depend on another case
/// having run, which is the first of this issue's own rules. So the property is held in two
/// places that answer different questions, and neither is the whole of it:
///
/// what is here reads the sources and refuses a case that takes a directory under the
/// temporary root outside the one type that removes it, which holds on every route the suite
/// runs on, including a clone running dotnet test by hand, and never reads an actual leftover;
///
/// and the step named "Refuse a leftover under the temporary root" in the suite workflow reads
/// the real root after every run a leg makes and refuses an entry carrying this suite's prefix,
/// which reads an actual leftover and only on the routes that workflow covers.
///
/// The bound each one carries is written where it lives: the vocabulary beside this file says
/// the scan reads calls and not paths handed in from elsewhere, and the workflow step says
/// which of its own runs it cannot see the root of.
/// </summary>
public class LeftoverTests
{
    /// <summary>
    /// The whole point, run against the tree as it is. Every finding says what the call leaves
    /// unheld and what to take instead, because a guard that only says no is a guard people
    /// work around.
    /// </summary>
    [Fact]
    public void EveryTemporaryDirectoryInTheTrackedTestSourcesIsTakenFromTheTypeThatRemovesIt()
    {
        var report = Leftovers.ScanTheTree();

        Assert.Empty(report.Findings.Select(finding =>
            $"{finding.Path}:{finding.Line} {finding.Needs} ({finding.Id}). Use {finding.Instead}."));
    }

    /// <summary>
    /// A departure is a debt rather than a dispensation, so it fails closed in both directions.
    /// One naming a file the scan does not reach, or a file that no longer carries the call it
    /// was written for, is refused rather than left to rot.
    /// </summary>
    [Fact]
    public void NoDeclaredDepartureHasOutlivedWhatItWasWrittenFor()
    {
        var report = Leftovers.ScanTheTree();

        Assert.Empty(report.Dangling.Select(entry =>
            $"{entry.Path} no longer carries a hit for {entry.Id}, so its departure is dangling."));
    }

    /// <summary>
    /// The guard proven by deleting it, on the mistake somebody actually makes. The near-miss is
    /// a queue test that takes its own directory and removes it on the last line of the case,
    /// which is right on every run that passes. Everything else about the fixture is right,
    /// which is what makes it the near-miss rather than an obviously broken file, and the repair
    /// is the directory taken from the type that removes it on disposal.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheNearMissAndPassesItsRepair()
    {
        var vocabulary = Leftovers.Vocabulary();

        var refused = HeadlessGuardTests.HeadlessGuard.Scan(
            new[] { ("Leftovers/near-miss.txt", Leftovers.Fixture("near-miss.txt")) },
            vocabulary,
            Array.Empty<HeadlessGuardTests.HeadlessGuard.Departure>());

        var finding = Assert.Single(refused.Findings);
        Assert.Equal("temp-subdirectory", finding.Id);

        var repaired = HeadlessGuardTests.HeadlessGuard.Scan(
            new[] { ("Leftovers/near-miss-repaired.txt", Leftovers.Fixture("near-miss-repaired.txt")) },
            vocabulary,
            Array.Empty<HeadlessGuardTests.HeadlessGuard.Departure>());

        Assert.Empty(repaired.Findings);
    }

    /// <summary>
    /// The departure leg proven the same way. One declared departure covers the call and removes
    /// the finding; a second names an identifier the file does not carry and is reported as
    /// dangling.
    /// </summary>
    [Fact]
    public void ADepartureCoversItsCallAndOneThatCoversNothingIsRefused()
    {
        var sources = new[] { ("Leftovers/near-miss.txt", Leftovers.Fixture("near-miss.txt")) };
        var vocabulary = Leftovers.Vocabulary();

        var covered = HeadlessGuardTests.HeadlessGuard.Scan(
            sources,
            vocabulary,
            new[] { new HeadlessGuardTests.HeadlessGuard.Departure("Leftovers/near-miss.txt", "temp-subdirectory", "a reason") });

        Assert.Empty(covered.Findings);
        Assert.Empty(covered.Dangling);

        var stale = HeadlessGuardTests.HeadlessGuard.Scan(
            sources,
            vocabulary,
            new[] { new HeadlessGuardTests.HeadlessGuard.Departure("Leftovers/near-miss.txt", "temp-file", "a reason") });

        var dangling = Assert.Single(stale.Dangling);
        Assert.Equal("temp-file", dangling.Id);
    }

    /// <summary>
    /// The scan is derived from the tree rather than from a list of file names, so a source
    /// added tomorrow is covered without anybody remembering to add it. It reads the same set
    /// the headless guard reads, which is what keeps the two from disagreeing about which files
    /// are the suite's.
    /// </summary>
    [Fact]
    public void TheScanReadsTheTreeRatherThanAListOfFileNames()
    {
        var root = HeadlessGuardTests.HeadlessGuard.RepositoryRoot();

        var scanned = HeadlessGuardTests.HeadlessGuard.TestSources(root)
            .Select(source => source.Path)
            .ToList();

        Assert.Contains("Jellyfin.Plugin.WatchSync.Tests/TemporaryDirectory.cs", scanned);
    }

    /// <summary>
    /// A vocabulary entry with a missing field would refuse a call and say nothing useful about
    /// it, and two entries sharing an identifier would make a departure cover a call nobody
    /// meant to except.
    /// </summary>
    [Fact]
    public void EveryVocabularyEntryIsCompleteAndNamedOnce()
    {
        var vocabulary = Leftovers.Vocabulary();

        Assert.NotEmpty(vocabulary);
        Assert.All(vocabulary, rule =>
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Id));
            Assert.False(string.IsNullOrWhiteSpace(rule.Needs));
            Assert.False(string.IsNullOrWhiteSpace(rule.Instead));
        });

        Assert.Equal(vocabulary.Count, vocabulary.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The type the rules point at, holding up its half. A directory with something in it is the
    /// case that matters: a removal that only works on an empty directory would pass a test that
    /// created one and nothing else, and every real case puts a file in it.
    /// </summary>
    [Fact]
    public void TheDirectoryAndEverythingUnderItGoWithTheCase()
    {
        string path;

        using (var directory = TemporaryDirectory.Create("removal"))
        {
            path = directory.FullPath;

            Assert.True(Directory.Exists(path));
            Assert.StartsWith(TemporaryDirectory.Prefix, Path.GetFileName(path), StringComparison.Ordinal);

            var below = Path.Combine(path, "a", "b");
            Directory.CreateDirectory(below);
            File.WriteAllText(Path.Combine(below, "document.txt"), "something a removal has to cope with");
        }

        Assert.False(Directory.Exists(path));
    }

    /// <summary>
    /// The property the near-miss fixture is about, asserted rather than described. The run that
    /// leaves a directory behind is the run where something else already failed, so the removal
    /// has to happen on the path where the case does not reach its own last line.
    /// </summary>
    [Fact]
    public void TheDirectoryGoesEvenWhereTheCaseFailsPartWayThrough()
    {
        string path = string.Empty;

        Action failingCase = () =>
        {
            using var directory = TemporaryDirectory.Create("failing-case");
            path = directory.FullPath;

            File.WriteAllText(Path.Combine(path, "written-before-the-failure.txt"), "written before the failure");

            throw new InvalidOperationException("what an assertion failing part way through a case looks like from here");
        };

        Assert.Throws<InvalidOperationException>(failingCase);

        Assert.NotEqual(string.Empty, path);
        Assert.False(Directory.Exists(path));
    }

    /// <summary>
    /// Reads the guard's own data files. The scan itself is the headless guard's, run against
    /// this vocabulary, because two implementations of one comparison drift and the one that
    /// drifts is the one nobody is looking at.
    /// </summary>
    internal static class Leftovers
    {
        private const string Separator = " :: ";

        private const string TestProject = "Jellyfin.Plugin.WatchSync.Tests";

        /// <summary>
        /// Runs the scan over the tracked test sources of this repository.
        /// </summary>
        /// <returns>The report.</returns>
        internal static HeadlessGuardTests.HeadlessGuard.Report ScanTheTree()
        {
            var root = HeadlessGuardTests.HeadlessGuard.RepositoryRoot();

            return HeadlessGuardTests.HeadlessGuard.Scan(
                HeadlessGuardTests.HeadlessGuard.TestSources(root),
                Vocabulary(),
                Departures());
        }

        /// <summary>
        /// Reads the vocabulary from the tracked file rather than from a copy in the output
        /// directory, because a copy proves the state of the file on the day it was copied.
        /// </summary>
        /// <returns>The rules.</returns>
        internal static IReadOnlyList<HeadlessGuardTests.HeadlessGuard.Rule> Vocabulary() =>
            Entries("vocabulary.txt", 4)
                .Select(fields => new HeadlessGuardTests.HeadlessGuard.Rule(
                    fields[0],
                    new Regex(fields[1], RegexOptions.CultureInvariant),
                    fields[2],
                    fields[3]))
                .ToList();

        /// <summary>
        /// Reads the declared departures.
        /// </summary>
        /// <returns>The departures.</returns>
        internal static IReadOnlyList<HeadlessGuardTests.HeadlessGuard.Departure> Departures() =>
            Entries("exceptions.txt", 3)
                .Select(fields => new HeadlessGuardTests.HeadlessGuard.Departure(fields[0], fields[1], fields[2]))
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

            var significant = File.ReadAllLines(DataFile(name))
                .Select(line => line.Trim())
                .Where(trimmed => trimmed.Length > 0 && !trimmed.StartsWith('#'));

            foreach (var trimmed in significant)
            {
                var parts = trimmed.Split(Separator);
                Assert.True(parts.Length == fields, $"{name} has an entry with {parts.Length} fields where {fields} are required: {trimmed}");

                entries.Add(parts.Select(part => part.Trim()).ToArray());
            }

            return entries;
        }

        private static string DataFile(string name) =>
            Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), TestProject, "Leftovers", name);
    }
}
