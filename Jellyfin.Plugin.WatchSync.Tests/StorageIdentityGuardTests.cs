using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Refuses a read of storage identity in this plugin's own sources: where a file is kept, what
/// it is called, what it is packaged in, how large it is, and what it hashes to.
///
/// `docs/matching.md` refuses all five, and the reason is the premise of the plugin rather than
/// a preference. The two servers are not required to hold the same files, so a key over any of
/// them works only where they happen to, and fails silently for the case this plugin exists to
/// serve. Every prior attempt reached for the path when the identifiers were missing, and each
/// one pays for it in a documented limitation.
///
/// Written before the matcher rather than after it, because the failure this refuses is a
/// fallback somebody adds at the moment the identifiers turn out to be absent, which is exactly
/// when it looks reasonable. A guard that arrives after that line does not prevent it.
///
/// The vocabulary, the declared departures and the two fixtures are data files rather than
/// source, for the reason the headless guard's are: a scanner carrying these literals inside the
/// project it scans would match itself, and the exclusion repairing that would be a hole in the
/// middle of the guard.
/// </summary>
public class StorageIdentityGuardTests
{
    /// <summary>
    /// The whole point, run against the tree as it is. Every finding says what the call
    /// identifies and what to reach for instead, because a guard that only says no is a guard
    /// people work around.
    /// </summary>
    [Fact]
    public void NoPluginSourceDerivesAKeyFromWhereOrHowTheFileIsStored()
    {
        var report = StorageIdentityGuard.ScanTheTree();

        Assert.Empty(report.Findings.Select(finding =>
            $"{finding.Path}:{finding.Line} reads {finding.Identifies} ({finding.Id}). Use {finding.Instead}."));
    }

    /// <summary>
    /// A departure is a debt rather than a dispensation, so it fails closed in both directions.
    /// One naming a file the scan does not reach, or a file that no longer carries the call it
    /// was written for, is refused rather than left to rot.
    /// </summary>
    [Fact]
    public void NoDeclaredDepartureHasOutlivedWhatItWasWrittenFor()
    {
        var report = StorageIdentityGuard.ScanTheTree();

        Assert.Empty(report.Dangling.Select(entry =>
            $"{entry.Path} no longer carries a hit for {entry.Id}, so its departure is dangling."));
    }

    /// <summary>
    /// The guard proven by deleting it, on the mistake the prior art actually makes. The
    /// near-miss is a movie key that prefers the three provider identifiers in the order the
    /// document fixes and falls back to the storage location for the item that carries none of
    /// them. Everything else about it is right, and the repair is that one branch returning no
    /// key instead.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheNearMissAndPassesItsRepair()
    {
        var vocabulary = StorageIdentityGuard.Vocabulary();

        var refused = StorageIdentityGuard.Scan(
            new[] { ("Matching/storage-identity-near-miss.txt", StorageIdentityGuard.Fixture("storage-identity-near-miss.txt")) },
            vocabulary,
            Array.Empty<StorageIdentityGuard.Departure>());

        var finding = Assert.Single(refused.Findings);
        Assert.Equal("storage-path", finding.Id);

        var repaired = StorageIdentityGuard.Scan(
            new[] { ("Matching/storage-identity-near-miss-repaired.txt", StorageIdentityGuard.Fixture("storage-identity-near-miss-repaired.txt")) },
            vocabulary,
            Array.Empty<StorageIdentityGuard.Departure>());

        Assert.Empty(repaired.Findings);
    }

    /// <summary>
    /// The departure leg proven the same way. One declared departure covers the call and removes
    /// the finding; a second one names an identifier the file does not carry and is reported as
    /// dangling. The tree declares none today, so this is where that leg is exercised.
    /// </summary>
    [Fact]
    public void ADepartureCoversItsCallAndOneThatCoversNothingIsRefused()
    {
        var sources = new[] { ("Matching/storage-identity-near-miss.txt", StorageIdentityGuard.Fixture("storage-identity-near-miss.txt")) };
        var vocabulary = StorageIdentityGuard.Vocabulary();

        var covered = StorageIdentityGuard.Scan(
            sources,
            vocabulary,
            new[] { new StorageIdentityGuard.Departure("Matching/storage-identity-near-miss.txt", "storage-path", "a reason") });

        Assert.Empty(covered.Findings);
        Assert.Empty(covered.Dangling);

        var stale = StorageIdentityGuard.Scan(
            sources,
            vocabulary,
            new[] { new StorageIdentityGuard.Departure("Matching/storage-identity-near-miss.txt", "storage-container", "a reason") });

        var dangling = Assert.Single(stale.Dangling);
        Assert.Equal("storage-container", dangling.Id);
    }

    /// <summary>
    /// The scan is derived from the tree rather than from a list of file names, so the matcher is
    /// covered the moment its first file is written and nobody has to remember to add it. It also
    /// refuses an empty source set: a scan that reaches nothing reports no findings, which is
    /// indistinguishable from a clean tree and is the way a guard like this dies quietly.
    /// </summary>
    [Fact]
    public void TheScanReadsTheTreeAndRefusesToJudgeNothing()
    {
        var root = StorageIdentityGuard.RepositoryRoot();

        Assert.NotEmpty(StorageIdentityGuard.TrackedPluginSourcePaths(root));

        var scanned = StorageIdentityGuard.PluginSources(root).Select(source => source.Path).ToList();

        Assert.NotEmpty(scanned);
        Assert.Contains("Jellyfin.Plugin.WatchSync/Plugin.cs", scanned);
    }

    /// <summary>
    /// The refusals are written in `docs/matching.md` and the guard is what refuses a violation
    /// of them. Two lists of the same thing drift, and a document describing a guard it has
    /// fallen behind is worse than no document because somebody reads it and believes it. So the
    /// document names every rule the guard carries and nothing else.
    /// </summary>
    [Fact]
    public void TheMatchingDocumentAndTheVocabularyNameTheSameRules()
    {
        var documented = StorageIdentityGuard.DocumentedRuleIds();
        var carried = StorageIdentityGuard.Vocabulary().Select(rule => rule.Id).ToList();

        Assert.NotEmpty(documented);
        Assert.Empty(documented.Except(carried, StringComparer.Ordinal));
        Assert.Empty(carried.Except(documented, StringComparer.Ordinal));
    }

    /// <summary>
    /// A vocabulary entry with a missing field would refuse a call and say nothing useful about
    /// it, and two entries sharing an identifier would make a departure cover a call nobody meant
    /// to except.
    /// </summary>
    [Fact]
    public void EveryVocabularyEntryIsCompleteAndNamedOnce()
    {
        var vocabulary = StorageIdentityGuard.Vocabulary();

        Assert.NotEmpty(vocabulary);
        Assert.All(vocabulary, rule =>
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Id));
            Assert.False(string.IsNullOrWhiteSpace(rule.Identifies));
            Assert.False(string.IsNullOrWhiteSpace(rule.Instead));
        });

        Assert.Equal(vocabulary.Count, vocabulary.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count());
    }

    internal static class StorageIdentityGuard
    {
        private const string Separator = " :: ";

        private const string PluginProject = "Jellyfin.Plugin.WatchSync";

        private const string TestProject = "Jellyfin.Plugin.WatchSync.Tests";

        private const string DocumentSection = "## How a key derivation is held to these refusals";

        internal sealed record Rule(string Id, Regex Pattern, string Identifies, string Instead);

        internal sealed record Departure(string Path, string Id, string Reason);

        internal sealed record Finding(string Path, int Line, string Id, string Identifies, string Instead);

        internal sealed record Report(IReadOnlyList<Finding> Findings, IReadOnlyList<Departure> Dangling);

        /// <summary>
        /// Scans a set of sources against a vocabulary, honouring the declared departures and
        /// reporting the ones that matched nothing. Pure, so the fixtures run through the same
        /// code the tree does rather than through a second implementation of it.
        /// </summary>
        /// <param name="sources">The path and text of each source to scan.</param>
        /// <param name="vocabulary">The rules to refuse.</param>
        /// <param name="departures">The declared departures.</param>
        /// <returns>What was found and which departures covered nothing.</returns>
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

                        findings.Add(new Finding(path, index + 1, rule.Id, rule.Identifies, rule.Instead));
                    }
                }
            }

            var dangling = departures
                .Where(entry => !covered.Contains(entry.Path + Separator + entry.Id))
                .ToList();

            return new Report(findings, dangling);
        }

        /// <summary>
        /// Runs the scan over this plugin's own sources.
        /// </summary>
        /// <returns>The report.</returns>
        internal static Report ScanTheTree()
        {
            var root = RepositoryRoot();
            var sources = PluginSources(root);

            Assert.True(sources.Count > 0, $"No plugin sources were found under {PluginProject}. A scan that reaches nothing reports nothing, which reads exactly like a clean tree.");

            return Scan(sources, Vocabulary(), Departures());
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
        /// Asks git which sources the plugin project tracks, so nothing here is a list of file
        /// names anybody has to maintain.
        /// </summary>
        /// <param name="root">The repository root.</param>
        /// <returns>The repository-relative path of each tracked source.</returns>
        internal static IReadOnlyList<string> TrackedPluginSourcePaths(string root) =>
            Git(root, "ls-files -- " + PluginProject)
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.EndsWith(".cs", StringComparison.Ordinal))
                .ToList();

        /// <summary>
        /// The set the scan reads: what git tracks, plus what is on disk and not yet committed.
        /// Tracking alone would leave a file uncovered between being written and being committed,
        /// which is exactly when somebody is deciding whether the line they just typed is
        /// acceptable. Build output is left out because it is generated rather than written.
        /// </summary>
        /// <param name="root">The repository root.</param>
        /// <returns>The path and text of each source to scan.</returns>
        internal static IReadOnlyList<(string Path, string Text)> PluginSources(string root)
        {
            var paths = new SortedSet<string>(TrackedPluginSourcePaths(root), StringComparer.Ordinal);

            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, PluginProject), "*.cs", SearchOption.AllDirectories))
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
            Entries("storage-identity-vocabulary.txt", 4)
                .Select(fields => new Rule(fields[0], new Regex(fields[1], RegexOptions.CultureInvariant), fields[2], fields[3]))
                .ToList();

        /// <summary>
        /// Reads the declared departures.
        /// </summary>
        /// <returns>The departures.</returns>
        internal static IReadOnlyList<Departure> Departures() =>
            Entries("storage-identity-exceptions.txt", 3)
                .Select(fields => new Departure(fields[0], fields[1], fields[2]))
                .ToList();

        /// <summary>
        /// Reads one of the two fixtures.
        /// </summary>
        /// <param name="name">The fixture file name.</param>
        /// <returns>Its text.</returns>
        internal static string Fixture(string name) => File.ReadAllText(DataFile(name));

        /// <summary>
        /// Reads the rule identifiers out of the one section of the matching document that names
        /// them. The read is scoped to that section rather than to the whole file, because the
        /// document carries other tables and prose mentioning a rule elsewhere is not the same as
        /// declaring it.
        /// </summary>
        /// <returns>The identifiers the document names.</returns>
        internal static IReadOnlyList<string> DocumentedRuleIds()
        {
            var document = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "matching.md"));
            var start = document.IndexOf(DocumentSection, StringComparison.Ordinal);

            Assert.True(start >= 0, $"docs/matching.md carries no section headed \"{DocumentSection}\", so there is nothing to hold the vocabulary against.");

            var rest = document[(start + DocumentSection.Length)..];
            var end = rest.IndexOf("\n## ", StringComparison.Ordinal);
            var section = end < 0 ? rest : rest[..end];

            return Regex
                .Matches(section, "(?m)^\\|\\s*`(?<id>[a-z0-9-]+)`\\s*\\|")
                .Select(match => match.Groups["id"].Value)
                .ToList();
        }

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
            Path.Combine(RepositoryRoot(), TestProject, "Matching", name);

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
