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
    /// The rules this guard carries, read from the vocabulary rather than listed here. That is
    /// what makes the near-miss obligation arrive with the rule: a sixth entry added to the
    /// vocabulary has no fixture pair, and the theory below fails until one exists.
    /// </summary>
    /// <returns>One case per rule.</returns>
    public static TheoryData<string> RulesThisGuardCarries()
    {
        var data = new TheoryData<string>();

        foreach (var rule in StorageIdentityGuard.Vocabulary())
        {
            data.Add(rule.Id);
        }

        return data;
    }

    /// <summary>
    /// Each rule proven by deleting it, on the mistake somebody will actually make. The pairs
    /// are one change apart and the repair is that one change, so a guard that passed the repair
    /// and refused its neighbour refused the mistake rather than the shape of the fixture.
    ///
    /// One pair per rule rather than one for the guard. Until #365 there was a single pair, it
    /// tripped the location rule, and the other four rules could be narrowed until they matched
    /// nothing with the whole suite staying green.
    /// </summary>
    /// <param name="rule">The rule whose fixture pair is exercised.</param>
    [Theory]
    [MemberData(nameof(RulesThisGuardCarries))]
    public void EachRuleIsRefusedOnItsNearMissAndPassesItsRepair(string rule)
    {
        var vocabulary = StorageIdentityGuard.Vocabulary();

        var refused = StorageIdentityGuard.Scan(
            new[] { ($"Matching/{rule}-near-miss.txt", StorageIdentityGuard.Fixture($"{rule}-near-miss.txt")) },
            vocabulary,
            Array.Empty<StorageIdentityGuard.Departure>());

        var finding = Assert.Single(refused.Findings);
        Assert.Equal(rule, finding.Id);

        var repaired = StorageIdentityGuard.Scan(
            new[] { ($"Matching/{rule}-near-miss-repaired.txt", StorageIdentityGuard.Fixture($"{rule}-near-miss-repaired.txt")) },
            vocabulary,
            Array.Empty<StorageIdentityGuard.Departure>());

        Assert.Empty(repaired.Findings.Select(entry =>
            $"the repaired fixture for {rule} still trips {entry.Id} at line {entry.Line}."));
    }

    /// <summary>
    /// The accounting the theory above cannot make, measured a rule at a time.
    ///
    /// A theory asserting that a fixture produces a finding naming its own rule says nothing
    /// about a rule whose fixture nobody wrote, and nothing about one that has been narrowed
    /// until a sibling of the same invariant reports every line it used to. Both were live here:
    /// #358 built this accounting for the vocabulary beside this one and this guard's five rules
    /// were outside it in both arguments, so four of them were refused by nothing while the
    /// invariant went on reporting itself proven.
    ///
    /// The accounting is the one <see cref="InvariantGuardTests"/> already carries rather than a
    /// second implementation of it, because two measurements of one thing disagree and the
    /// disagreement is discovered by somebody trusting the wrong one.
    /// </summary>
    [Fact]
    public void EveryRuleIsReachedByANearMissOrIsDeclaredUnreached()
    {
        var reach = InvariantGuardTests.InvariantGuard.ReachOfEachRule(
            StorageIdentityGuard.RulesAsInvariantRules(),
            StorageIdentityGuard.NearMisses(),
            StorageIdentityGuard.Unreached());

        Assert.Empty(reach.Unproven.Select(entry =>
            $"{entry.Id} is {entry.State}, so no near-miss fixture proves it, and Matching/storage-identity-unreached.txt does not declare it."));

        Assert.Empty(reach.Stale.Select(id =>
            $"Matching/storage-identity-unreached.txt declares {id} unreached, and a near-miss fixture reaches it."));

        Assert.Empty(reach.Dangling.Select(id =>
            $"Matching/storage-identity-unreached.txt declares {id}, which the vocabulary does not carry."));
    }

    /// <summary>
    /// The departure leg proven the same way. One declared departure covers the call and removes
    /// the finding; a second one names an identifier the file does not carry and is reported as
    /// dangling. The tree declares none today, so this is where that leg is exercised.
    /// </summary>
    [Fact]
    public void ADepartureCoversItsCallAndOneThatCoversNothingIsRefused()
    {
        var sources = new[] { ("Matching/storage-path-near-miss.txt", StorageIdentityGuard.Fixture("storage-path-near-miss.txt")) };
        var vocabulary = StorageIdentityGuard.Vocabulary();

        var covered = StorageIdentityGuard.Scan(
            sources,
            vocabulary,
            new[] { new StorageIdentityGuard.Departure("Matching/storage-path-near-miss.txt", "storage-path", "a reason") });

        Assert.Empty(covered.Findings);
        Assert.Empty(covered.Dangling);

        var stale = StorageIdentityGuard.Scan(
            sources,
            vocabulary,
            new[] { new StorageIdentityGuard.Departure("Matching/storage-path-near-miss.txt", "storage-container", "a reason") });

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

        /// <summary>
        /// The name the register knows this guard's invariant by.
        /// </summary>
        internal const string Invariant = "storage-identity";

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
        /// Reads the rules declared as deliberately unreached by any near-miss in this tree.
        /// </summary>
        /// <returns>The declarations.</returns>
        internal static IReadOnlyList<InvariantGuardTests.InvariantGuard.Declaration> Unreached() =>
            Entries("storage-identity-unreached.txt", 2)
                .Select(fields => new InvariantGuardTests.InvariantGuard.Declaration(fields[0], fields[1]))
                .ToList();

        /// <summary>
        /// This vocabulary in the shape the shared reach accounting takes, so the measurement is
        /// the one the other guard is already held to rather than a second one written here.
        ///
        /// Every rule carries the same invariant, which is the whole point of the accounting: a
        /// rule whose lines a sibling of that invariant also reports moves nothing when it is
        /// taken out, and is reported as inert rather than counted as proven. The issue is read
        /// off the register rather than typed, because the register is what names it.
        /// </summary>
        /// <returns>The rules, as the shared accounting reads them.</returns>
        internal static IReadOnlyList<InvariantGuardTests.InvariantGuard.Rule> RulesAsInvariantRules()
        {
            var entry = InvariantGuardTests.InvariantGuard.Register()
                .SingleOrDefault(candidate => string.Equals(candidate.Id, Invariant, StringComparison.Ordinal));

            Assert.True(entry is not null, $"The register names no invariant {Invariant}, so there is nothing for this guard's rules to belong to.");

            return Vocabulary()
                .Select(rule => new InvariantGuardTests.InvariantGuard.Rule(
                    rule.Id,
                    Invariant,
                    entry!.Issue,
                    rule.Pattern,
                    rule.Identifies,
                    rule.Instead))
                .ToList();
        }

        /// <summary>
        /// The near-miss fixture of each rule, named from the vocabulary so that a rule added
        /// with no pair beside it fails on the read rather than being skipped.
        /// </summary>
        /// <returns>One near-miss per rule.</returns>
        internal static IReadOnlyList<InvariantGuardTests.InvariantGuard.NearMiss> NearMisses() =>
            Vocabulary()
                .Select(rule => new InvariantGuardTests.InvariantGuard.NearMiss(
                    Invariant,
                    $"Matching/{rule.Id}-near-miss.txt",
                    Fixture($"{rule.Id}-near-miss.txt")))
                .ToList();

        /// <summary>
        /// Reads one of the fixtures.
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
