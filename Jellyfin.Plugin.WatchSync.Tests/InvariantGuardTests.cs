using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Turns this repository's own invariants into checks, one register entry per invariant and one
/// or more rules behind each.
///
/// The register is the authority for which invariants exist. A guard carries the patterns, and a
/// test here refuses the two disagreeing in either direction, so an invariant named with nothing
/// scanning it fails and a rule scanned with nothing naming it fails as well. Without that
/// closure a register is a list somebody maintains by remembering to, which is the failure mode
/// of every list in this repository that is not read by something.
///
/// Five of the six invariants are carried here. The sixth, storage identity, is carried by the
/// guard that landed with the document arguing it, and this register names it rather than
/// re-implementing it, because two scanners over one invariant would disagree about what counts
/// as a violation.
///
/// Four of the five scan a subject that is nearly empty today. That is stated rather than hidden:
/// a green run over sources that could not have violated the rule is not coverage, and the
/// near-miss fixtures are what make each rule a guard rather than a green tick. What each pattern
/// cannot see is written in `docs/invariants.md`, where somebody deciding whether to trust a run
/// will find it.
///
/// The register, the vocabulary, the declared departures and the fixtures are data files rather
/// than source, for the reason the two vocabularies already in the tree are: a scanner carrying
/// these literals inside a project it scans would match itself, and the exclusion repairing that
/// would be a hole in the middle of the guard.
/// </summary>
public class InvariantGuardTests
{
    /// <summary>
    /// The invariants the register says this guard carries. Reading them from the register rather
    /// than listing them here is what makes the near-miss obligation arrive with the entry: an
    /// invariant added to the register with this guard named against it has no fixture pair, and
    /// the theory below fails until one exists.
    /// </summary>
    /// <returns>One case per invariant this guard carries.</returns>
    public static TheoryData<string> InvariantsThisGuardCarries()
    {
        var data = new TheoryData<string>();

        foreach (var entry in InvariantGuard.Register().Where(entry => entry.Carrier == InvariantGuard.ThisGuard))
        {
            data.Add(entry.Id);
        }

        return data;
    }

    /// <summary>
    /// The whole point, run against the tree as it is. Every finding says what the call does and
    /// what to reach for instead, because a guard that only says no is a guard people work
    /// around.
    /// </summary>
    [Fact]
    public void NoPluginSourceViolatesAnInvariantThisGuardCarries()
    {
        var report = InvariantGuard.ScanTheTree();

        Assert.Empty(report.Findings.Select(finding =>
            $"{finding.Path}:{finding.Line} {finding.Does} ({finding.Id}, {finding.Invariant}, issue #{finding.Issue}). Use {finding.Instead}."));
    }

    /// <summary>
    /// A departure is a debt rather than a dispensation, so it fails closed in both directions.
    /// One naming a file the scan does not reach, or a file that no longer carries the call it was
    /// written for, is refused rather than left to rot.
    /// </summary>
    [Fact]
    public void NoDeclaredDepartureHasOutlivedWhatItWasWrittenFor()
    {
        var report = InvariantGuard.ScanTheTree();

        Assert.Empty(report.Dangling.Select(entry =>
            $"{entry.Path} no longer carries a hit for {entry.Id}, so its departure is dangling."));
    }

    /// <summary>
    /// Each rule proven by deleting it, on the mistake somebody will actually make. Each near-miss
    /// is one change away from correct and its repair is that one change, so a guard that passed
    /// the repair and refused its neighbour refused the mistake rather than the shape of the
    /// fixture.
    ///
    /// The findings are asserted to belong to the invariant under test, which is what separates a
    /// fixture that trips its own rule from one that trips a neighbouring rule and looks the same
    /// from the outside.
    /// </summary>
    /// <param name="invariant">The invariant whose fixture pair is exercised.</param>
    [Theory]
    [MemberData(nameof(InvariantsThisGuardCarries))]
    public void EachInvariantIsRefusedOnItsNearMissAndPassesItsRepair(string invariant)
    {
        var vocabulary = InvariantGuard.Vocabulary();

        var refused = InvariantGuard.Scan(
            new[] { ($"Invariants/{invariant}-near-miss.txt", InvariantGuard.Fixture($"{invariant}-near-miss.txt")) },
            vocabulary,
            Array.Empty<InvariantGuard.Departure>());

        Assert.NotEmpty(refused.Findings);
        Assert.All(refused.Findings, finding => Assert.Equal(invariant, finding.Invariant));

        var repaired = InvariantGuard.Scan(
            new[] { ($"Invariants/{invariant}-near-miss-repaired.txt", InvariantGuard.Fixture($"{invariant}-near-miss-repaired.txt")) },
            vocabulary,
            Array.Empty<InvariantGuard.Departure>());

        Assert.Empty(repaired.Findings.Select(finding =>
            $"the repaired fixture for {invariant} still trips {finding.Id} at line {finding.Line}."));
    }

    /// <summary>
    /// The departure leg proven the same way. One declared departure covers the call and removes
    /// the finding; a second one names a rule the file does not carry and is reported as dangling.
    /// The tree declares none today, so this is where that leg is exercised.
    /// </summary>
    [Fact]
    public void ADepartureCoversItsCallAndOneThatCoversNothingIsRefused()
    {
        var sources = new[] { ("Invariants/injected-clock-near-miss.txt", InvariantGuard.Fixture("injected-clock-near-miss.txt")) };
        var vocabulary = InvariantGuard.Vocabulary();

        var covered = InvariantGuard.Scan(
            sources,
            vocabulary,
            new[] { new InvariantGuard.Departure("Invariants/injected-clock-near-miss.txt", "clock-datetime-utcnow", "a reason") });

        Assert.Empty(covered.Findings);
        Assert.Empty(covered.Dangling);

        var stale = InvariantGuard.Scan(
            sources,
            vocabulary,
            new[] { new InvariantGuard.Departure("Invariants/injected-clock-near-miss.txt", "clock-tick-count", "a reason") });

        var dangling = Assert.Single(stale.Dangling);
        Assert.Equal("clock-tick-count", dangling.Id);
    }

    /// <summary>
    /// The scan reads the tree rather than a list of file names, so a source is covered the moment
    /// it is written and nobody has to remember to add it. It also refuses an empty source set: a
    /// scan that reaches nothing reports no findings, which is indistinguishable from a clean tree
    /// and is the way a guard like this dies quietly.
    ///
    /// The set is the one the storage identity guard already reads, taken from it rather than
    /// derived a second time. Two readings of what counts as this plugin's own sources would
    /// eventually disagree, and the invariant a file falls out of would be whichever guard was
    /// written second.
    /// </summary>
    [Fact]
    public void TheScanReadsTheTreeAndRefusesToJudgeNothing()
    {
        var root = InvariantGuard.RepositoryRoot();
        var scanned = InvariantGuard.PluginSources(root).Select(source => source.Path).ToList();

        Assert.NotEmpty(scanned);
        Assert.Contains("Jellyfin.Plugin.WatchSync/Plugin.cs", scanned);
    }

    /// <summary>
    /// The register and the guards cannot drift. Every entry names a guard that carries at least
    /// one rule for it, and every rule carried belongs to an invariant the register names.
    /// </summary>
    [Fact]
    public void TheRegisterAndTheGuardsThatCarryItNameTheSameInvariants()
    {
        var drift = InvariantGuard.Compare(InvariantGuard.Register(), InvariantGuard.Carried());

        Assert.Empty(drift.Uncarried);
        Assert.Empty(drift.Unregistered);
    }

    /// <summary>
    /// The closure above proven to bite, in both of the directions it claims. An entry whose guard
    /// carries nothing for it is reported, and a rule no entry names is reported, and neither is
    /// inferred from the other.
    /// </summary>
    [Fact]
    public void AnInvariantNothingCarriesFailsAndSoDoesARuleNothingRegisters()
    {
        var register = new[] { new InvariantGuard.Invariant("named", "1", "the plugin's own sources", "SomeGuard") };

        var uncarried = InvariantGuard.Compare(
            register,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["SomeGuard"] = Array.Empty<string>() });

        Assert.Single(uncarried.Uncarried);
        Assert.Empty(uncarried.Unregistered);

        var unregistered = InvariantGuard.Compare(
            register,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["SomeGuard"] = new[] { "named", "unnamed" } });

        Assert.Empty(unregistered.Uncarried);
        Assert.Single(unregistered.Unregistered);
    }

    /// <summary>
    /// The invariants are argued in `docs/invariants.md` and the register is what a machine reads.
    /// Two lists of the same thing drift, and a document describing a register it has fallen
    /// behind is worse than no document because somebody reads it and believes it.
    /// </summary>
    [Fact]
    public void TheInvariantDocumentAndTheRegisterNameTheSameInvariants()
    {
        var documented = InvariantGuard.DocumentedInvariantIds();
        var registered = InvariantGuard.Register().Select(entry => entry.Id).ToList();

        Assert.NotEmpty(documented);
        Assert.Empty(documented.Except(registered, StringComparer.Ordinal));
        Assert.Empty(registered.Except(documented, StringComparer.Ordinal));
    }

    /// <summary>
    /// Every rule names the issue its invariant was argued in, and names the same one the register
    /// does. A reader who has found the pattern can then find the argument, which is the whole
    /// reason the field is on the rule as well as on the entry.
    /// </summary>
    [Fact]
    public void EveryRuleNamesTheIssueItsInvariantWasArguedIn()
    {
        var register = InvariantGuard.Register().ToDictionary(entry => entry.Id, entry => entry.Issue, StringComparer.Ordinal);

        Assert.All(InvariantGuard.Vocabulary(), rule =>
        {
            Assert.True(register.TryGetValue(rule.Invariant, out var issue), $"{rule.Id} belongs to {rule.Invariant}, which the register does not name.");
            Assert.Equal(issue, rule.Issue);
        });
    }

    /// <summary>
    /// A vocabulary entry with a missing field would refuse a call and say nothing useful about
    /// it, and two entries sharing an identifier would make a departure cover a call nobody meant
    /// to except. The register has the same requirement for the same reason.
    /// </summary>
    [Fact]
    public void EveryEntryIsCompleteAndNamedOnce()
    {
        var vocabulary = InvariantGuard.Vocabulary();

        Assert.NotEmpty(vocabulary);
        Assert.All(vocabulary, rule =>
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Id));
            Assert.False(string.IsNullOrWhiteSpace(rule.Invariant));
            Assert.False(string.IsNullOrWhiteSpace(rule.Issue));
            Assert.False(string.IsNullOrWhiteSpace(rule.Does));
            Assert.False(string.IsNullOrWhiteSpace(rule.Instead));
        });

        Assert.Equal(vocabulary.Count, vocabulary.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count());

        var register = InvariantGuard.Register();

        Assert.NotEmpty(register);
        Assert.All(register, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Id));
            Assert.False(string.IsNullOrWhiteSpace(entry.Issue));
            Assert.False(string.IsNullOrWhiteSpace(entry.Subject));
            Assert.False(string.IsNullOrWhiteSpace(entry.Carrier));
        });

        Assert.Equal(register.Count, register.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count());
    }

    internal static class InvariantGuard
    {
        /// <summary>
        /// The name this guard is known by in the register.
        /// </summary>
        internal const string ThisGuard = "InvariantGuardTests";

        private const string Separator = " :: ";

        private const string TestProject = "Jellyfin.Plugin.WatchSync.Tests";

        private const string StorageIdentityGuard = "StorageIdentityGuardTests";

        private const string StorageIdentityInvariant = "storage-identity";

        private const string DocumentSection = "## The register";

        internal sealed record Invariant(string Id, string Issue, string Subject, string Carrier);

        internal sealed record Rule(string Id, string Invariant, string Issue, Regex Pattern, string Does, string Instead);

        internal sealed record Departure(string Path, string Id, string Reason);

        internal sealed record Finding(string Path, int Line, string Id, string Invariant, string Issue, string Does, string Instead);

        internal sealed record Report(IReadOnlyList<Finding> Findings, IReadOnlyList<Departure> Dangling);

        internal sealed record Drift(IReadOnlyList<string> Uncarried, IReadOnlyList<string> Unregistered);

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

                        findings.Add(new Finding(path, index + 1, rule.Id, rule.Invariant, rule.Issue, rule.Does, rule.Instead));
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
            var sources = PluginSources(RepositoryRoot());

            Assert.True(sources.Count > 0, "No plugin sources were found. A scan that reaches nothing reports nothing, which reads exactly like a clean tree.");

            return Scan(sources, Vocabulary(), Departures());
        }

        /// <summary>
        /// Compares the register against what the guards carry, in both directions and without
        /// inferring either from the other.
        /// </summary>
        /// <param name="register">The invariants the register names.</param>
        /// <param name="carried">The invariants each guard carries at least one rule for.</param>
        /// <returns>What each side holds that the other does not.</returns>
        internal static Drift Compare(
            IReadOnlyList<Invariant> register,
            IReadOnlyDictionary<string, IReadOnlyList<string>> carried)
        {
            var uncarried = register
                .Where(entry => !carried.TryGetValue(entry.Carrier, out var ids)
                    || !ids.Contains(entry.Id, StringComparer.Ordinal))
                .Select(entry => $"{entry.Id} names {entry.Carrier}, which carries no rule for it.")
                .ToList();

            var registered = new HashSet<string>(register.Select(entry => entry.Id), StringComparer.Ordinal);

            var unregistered = carried
                .SelectMany(guard => guard.Value.Select(id => (Guard: guard.Key, Id: id)))
                .Where(rule => !registered.Contains(rule.Id))
                .Select(rule => $"{rule.Guard} carries {rule.Id}, which the register does not name.")
                .ToList();

            return new Drift(uncarried, unregistered);
        }

        /// <summary>
        /// What each guard carries, as invariants rather than as rule identifiers.
        ///
        /// This guard's set is derived from its own vocabulary. The storage identity guard's is
        /// derived from its vocabulary too rather than asserted: its five rules are five ways of
        /// deriving a key from where a file is kept and all five belong to the one invariant, so
        /// what is written here is the mapping and not the contents. A guard emptied of its rules
        /// stops carrying its invariant and the register fails, which is the direction that
        /// matters.
        /// </summary>
        /// <returns>The invariants each guard carries.</returns>
        internal static IReadOnlyDictionary<string, IReadOnlyList<string>> Carried()
        {
            var storage = StorageIdentityGuardTests.StorageIdentityGuard.Vocabulary().Count == 0
                ? (IReadOnlyList<string>)Array.Empty<string>()
                : new[] { StorageIdentityInvariant };

            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [ThisGuard] = Vocabulary().Select(rule => rule.Invariant).Distinct(StringComparer.Ordinal).ToList(),
                [StorageIdentityGuard] = storage,
            };
        }

        /// <summary>
        /// The repository root, taken from the guard that already finds it rather than found a
        /// second time.
        /// </summary>
        /// <returns>The repository root.</returns>
        internal static string RepositoryRoot() => HeadlessGuardTests.HeadlessGuard.RepositoryRoot();

        /// <summary>
        /// The plugin's own sources, taken from the guard that already reads them.
        /// </summary>
        /// <param name="root">The repository root.</param>
        /// <returns>The path and text of each source to scan.</returns>
        internal static IReadOnlyList<(string Path, string Text)> PluginSources(string root) =>
            StorageIdentityGuardTests.StorageIdentityGuard.PluginSources(root);

        /// <summary>
        /// Reads the register from the tracked file rather than from a copy in the output
        /// directory, because a copy proves the state of the file on the day it was copied.
        /// </summary>
        /// <returns>The invariants.</returns>
        internal static IReadOnlyList<Invariant> Register() =>
            Entries("register.txt", 4)
                .Select(fields => new Invariant(fields[0], fields[1], fields[2], fields[3]))
                .ToList();

        /// <summary>
        /// Reads the vocabulary.
        /// </summary>
        /// <returns>The rules.</returns>
        internal static IReadOnlyList<Rule> Vocabulary() =>
            Entries("vocabulary.txt", 6)
                .Select(fields => new Rule(
                    fields[0],
                    fields[1],
                    fields[2],
                    new Regex(fields[3], RegexOptions.CultureInvariant),
                    fields[4],
                    fields[5]))
                .ToList();

        /// <summary>
        /// Reads the declared departures.
        /// </summary>
        /// <returns>The departures.</returns>
        internal static IReadOnlyList<Departure> Departures() =>
            Entries("exceptions.txt", 3)
                .Select(fields => new Departure(fields[0], fields[1], fields[2]))
                .ToList();

        /// <summary>
        /// Reads one of the fixtures.
        /// </summary>
        /// <param name="name">The fixture file name.</param>
        /// <returns>Its text.</returns>
        internal static string Fixture(string name) => File.ReadAllText(DataFile(name));

        /// <summary>
        /// Reads the invariant identifiers out of the one section of the document that names them.
        /// The read is scoped to that section rather than to the whole file, because the document
        /// carries other tables and prose mentioning an invariant elsewhere is not the same as
        /// declaring it.
        /// </summary>
        /// <returns>The identifiers the document names.</returns>
        internal static IReadOnlyList<string> DocumentedInvariantIds()
        {
            var document = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "invariants.md"));
            var start = document.IndexOf(DocumentSection, StringComparison.Ordinal);

            Assert.True(start >= 0, $"docs/invariants.md carries no section headed \"{DocumentSection}\", so there is nothing to hold the register against.");

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
            Path.Combine(RepositoryRoot(), TestProject, "Invariants", name);
    }
}
