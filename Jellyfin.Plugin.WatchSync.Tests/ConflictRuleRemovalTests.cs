using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the record of what removing each rule of the conflict table reddens, which is the
/// second condition of #81.
///
/// <see cref="ConflictRowCoverageTests"/> holds every row of the table to the facts filed under
/// it, and states its own bound: it reads which classes are named and that they hold facts, and
/// never whether a fact drives the row it is filed under. A class renamed onto the wrong row
/// passes there. This is what reaches that, by taking the rule out and reading which facts
/// redden.
///
/// The taking out is done by hand. Nothing in this suite and nothing in any workflow of this
/// repository applies one of those changes, so what is held here is a record of a run rather
/// than the run, and <c>Conflict/removal.txt</c> says so about itself in its own header. What
/// these legs do refuse is every way that record can go stale or be wrong about the tree: a row
/// with a rule and no entry, an entry for a row with no rule, a fact that is not in the suite,
/// and a fact filed under a row whose classes do not hold it.
///
/// That last one is the condition's own sentence, decided by the coverage register rather than
/// by whoever wrote the entry. Only that rule's tests means the classes
/// <c>Conflict/coverage.txt</c> files under that row, and a removal reaching outside them is a
/// resolver whose rules have grown into each other.
/// </summary>
public class ConflictRuleRemovalTests
{
    /// <summary>
    /// The vocabulary the two fixtures are judged against. It is declared here rather than read
    /// out of the coverage register, because a fixture judged against the real register would
    /// prove the state of that file on the day it ran rather than proving the guard.
    /// </summary>
    private static readonly Dictionary<string, IReadOnlyList<string>> FixtureRows =
        new(StringComparer.Ordinal)
        {
            ["Played"] = new[] { "PlayedRatchetTests", "ConflictOrderedPairTests" },
            ["PlayCount"] = new[] { "PlayCountReconciliationTests", "ConflictOrderedPairTests" },
            ["PlaybackPositionTicks"] = new[] { "PositionRecencyTests", "ConflictOrderedPairTests" },
        };

    /// <summary>
    /// The whole point of the register existing at all. A row whose rule decides somebody's
    /// history and whose removal nobody has ever run is a row where the facts filed under it are
    /// a claim rather than a demonstration.
    /// </summary>
    [Fact]
    public void EveryRowThatHasARuleRecordsItsRemoval()
    {
        var covered = CoveredRows();

        Assert.NotEmpty(covered);

        var recorded = Register().Select(entry => entry.Field).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(covered
            .Where(field => !recorded.Contains(field))
            .Select(field => $"{field} has a rule and no entry in the removal register, so nothing says what taking that rule out reddens."));
    }

    /// <summary>
    /// The other direction, and it is the one that fails closed. An entry for a row the coverage
    /// register calls awaited is a demonstration of removing something that is not there, and it
    /// is how this file would come to read as complete over four rows while three have rules.
    /// </summary>
    [Fact]
    public void NoEntryOutlivesTheRuleItIsAbout()
    {
        var covered = new HashSet<string>(CoveredRows(), StringComparer.Ordinal);

        Assert.Empty(Register()
            .Where(entry => !covered.Contains(entry.Field))
            .Select(entry => $"{entry.Field} has an entry in the removal register and no rule the coverage register calls covered."));
    }

    /// <summary>
    /// A fact named here is a fact that ran. A name resolving to no class, or to a class holding
    /// no such fact, is a record of a run nobody can repeat, and it is what a renamed fact leaves
    /// behind.
    /// </summary>
    [Fact]
    public void EveryFactNamedIsAFactThisSuiteHolds()
    {
        var suite = typeof(ConflictRuleRemovalTests).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsNested)
            .ToDictionary(type => type.Name, type => type, StringComparer.Ordinal);

        foreach (var entry in Register())
        {
            Assert.NotEmpty(entry.Facts);

            foreach (var fact in entry.Facts)
            {
                Assert.True(
                    suite.TryGetValue(fact.Class, out var type),
                    $"{entry.Field} names {fact} and the suite has no class {fact.Class}.");

                Assert.True(
                    HoldsTheFact(type!, fact.Method),
                    $"{entry.Field} names {fact} and {fact.Class} holds no such fact.");
            }
        }
    }

    /// <summary>
    /// The condition itself. Every fact a removal reddened sits in a class the coverage register
    /// files under that row, so only that rule's tests is decided by that register rather than by
    /// whoever wrote the entry.
    /// </summary>
    [Fact]
    public void EveryRemovalReddensOnlyTheFactsOfItsOwnRow()
    {
        Assert.Empty(Compare(Register(), Rows()).Foreign);
    }

    /// <summary>
    /// One shared class is not a demonstration. <see cref="ConflictOrderedPairTests"/> walks every
    /// row that has a rule and is filed under all of them, so an entry naming only facts of that
    /// class would pass the leg above and say nothing about the row it is for. Every entry has to
    /// reach a fact in a class no other row names.
    /// </summary>
    [Fact]
    public void EveryRemovalReachesAFactNoOtherRowClaims()
    {
        Assert.Empty(Compare(Register(), Rows()).WithoutOwnRow);
    }

    /// <summary>
    /// An entry with no change and no reason is a row somebody entered rather than measured, which
    /// is the leg the coverage register carries and for the same reason.
    /// </summary>
    [Fact]
    public void EveryEntryCarriesTheChangeAndItsReason()
    {
        foreach (var entry in Register())
        {
            Assert.False(
                string.IsNullOrWhiteSpace(entry.Change),
                $"{entry.Field} records no change, so what was removed is undefined.");

            Assert.False(
                string.IsNullOrWhiteSpace(entry.Reason),
                $"{entry.Field} gives no reason for the change being the removal for that row.");
        }
    }

    /// <summary>
    /// The guard proven by the mistake it exists for. The near miss pastes one line of the position
    /// row's failure list into the Played entry, so every row is entered, every entry names its own
    /// class, and one entry claims that removing the ratchet reddens a fact of another row. The
    /// repair is that one fact removed.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheNearMissAndPassesItsRepair()
    {
        var refused = Compare(Parse(Fixture("removal-near-miss.txt")), FixtureRows);

        Assert.Equal(
            "Played reddened PositionRecencyTests.TheLaterPlayWinsInBothDirections, and the coverage register files that class under no such row.",
            Assert.Single(refused.Foreign));

        Assert.Empty(refused.WithoutOwnRow);

        var repaired = Compare(Parse(Fixture("removal-near-miss-repaired.txt")), FixtureRows);

        Assert.Empty(repaired.Foreign);
        Assert.Empty(repaired.WithoutOwnRow);
    }

    /// <summary>
    /// The other half of the pair, driven off the repaired fixture because the register has no
    /// entry resting on the shared walk alone and a leg exercised only by the tree stops being
    /// exercised the moment the tree is right.
    /// </summary>
    [Fact]
    public void AnEntryRestingOnTheSharedWalkAloneIsRefused()
    {
        var thinned = Parse(Fixture("removal-near-miss-repaired.txt"))
            .Select(entry => string.Equals(entry.Field, "Played", StringComparison.Ordinal)
                ? entry with
                {
                    Facts = entry.Facts
                        .Where(fact => string.Equals(fact.Class, "ConflictOrderedPairTests", StringComparison.Ordinal))
                        .ToList(),
                }
                : entry)
            .ToList();

        Assert.Equal(
            "Played reddened no fact of a class its own row alone is filed under, so what was removed was held by the shared walk and by nothing about that row.",
            Assert.Single(Compare(thinned, FixtureRows).WithoutOwnRow));
    }

    /// <summary>
    /// What a set of entries and a set of rows disagree about. Pure, so the fixtures run through
    /// the same code the tree does rather than through a second implementation of it.
    /// </summary>
    /// <param name="entries">The entries of the removal register.</param>
    /// <param name="rows">The classes each row is filed under.</param>
    /// <returns>What the two disagree about.</returns>
    internal static Misfiling Compare(
        IReadOnlyList<Entry> entries,
        IReadOnlyDictionary<string, IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(rows);

        var foreign = new List<string>();
        var withoutOwnRow = new List<string>();

        foreach (var entry in entries)
        {
            var own = rows.TryGetValue(entry.Field, out var classes)
                ? classes
                : Array.Empty<string>();

            var elsewhere = rows
                .Where(row => !string.Equals(row.Key, entry.Field, StringComparison.Ordinal))
                .SelectMany(row => row.Value)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var fact in entry.Facts
                .Where(fact => !own.Contains(fact.Class, StringComparer.Ordinal)))
            {
                foreign.Add($"{entry.Field} reddened {fact}, and the coverage register files that class under no such row.");
            }

            var reachesItsOwn = entry.Facts.Any(fact =>
                own.Contains(fact.Class, StringComparer.Ordinal) && !elsewhere.Contains(fact.Class));

            if (!reachesItsOwn)
            {
                withoutOwnRow.Add($"{entry.Field} reddened no fact of a class its own row alone is filed under, so what was removed was held by the shared walk and by nothing about that row.");
            }
        }

        return new Misfiling(foreign, withoutOwnRow);
    }

    /// <summary>
    /// Reads entries out of lines. Pure, for the reason <see cref="Compare"/> gives. An entry the
    /// parser cannot read fails rather than being skipped, which is the difference between a
    /// register and a comment.
    /// </summary>
    /// <param name="lines">The lines.</param>
    /// <returns>The entries.</returns>
    internal static IReadOnlyList<Entry> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var entries = new List<Entry>();

        foreach (var line in lines)
        {
            var text = line.Trim();

            if (text.Length == 0 || text.StartsWith('#'))
            {
                continue;
            }

            // Split the line rather than the trimmed text, for the reason the coverage register's
            // parser gives: an entry whose reason was deleted and whose separator was left behind
            // arrives as four fields with an empty one rather than as three, and the refusal then
            // names the thing that is missing instead of the shape of the line.
            var fields = line.Split(" :: ");

            Assert.True(
                fields.Length == 4,
                $"an entry of the removal register has {fields.Length} fields rather than four: {text}");

            var facts = fields[2]
                .Split(',')
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .Select(Named)
                .ToList();

            Assert.Empty(facts
                .GroupBy(fact => fact.ToString(), StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => $"{fields[0].Trim()} names {group.Key} more than once."));

            entries.Add(new Entry(fields[0].Trim(), fields[1].Trim(), facts, fields[3].Trim()));
        }

        Assert.NotEmpty(entries);

        return entries;
    }

    /// <summary>
    /// The rows the coverage register calls covered, and the classes it files under each. This is
    /// where that rule's tests is decided, and it is read rather than restated.
    /// </summary>
    /// <returns>One entry per covered row.</returns>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> Rows() =>
        ConflictRowCoverageTests.Register()
            .Where(entry => entry.IsCovered)
            .ToDictionary(entry => entry.Field, entry => entry.Classes, StringComparer.Ordinal);

    /// <summary>
    /// The rows the coverage register calls covered.
    /// </summary>
    /// <returns>Their fields.</returns>
    internal static IReadOnlyList<string> CoveredRows() =>
        ConflictRowCoverageTests.Register()
            .Where(entry => entry.IsCovered)
            .Select(entry => entry.Field)
            .ToList();

    /// <summary>
    /// The register, read as data.
    /// </summary>
    /// <returns>The entries.</returns>
    internal static IReadOnlyList<Entry> Register() =>
        Parse(File.ReadAllLines(Path.Join(
            HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
            "Jellyfin.Plugin.WatchSync.Tests",
            "Conflict",
            "removal.txt")));

    /// <summary>
    /// Reads one of the two fixtures.
    /// </summary>
    /// <param name="name">The fixture file name.</param>
    /// <returns>Its lines.</returns>
    internal static IReadOnlyList<string> Fixture(string name) =>
        File.ReadAllLines(Path.Join(
            HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
            "Jellyfin.Plugin.WatchSync.Tests",
            "Conflict",
            name));

    /// <summary>
    /// Reads one named fact out of its text.
    /// </summary>
    /// <param name="text">The class and the method, joined by a dot.</param>
    /// <returns>The fact.</returns>
    private static NamedFact Named(string text)
    {
        var parts = text.Split('.');

        Assert.True(
            parts.Length == 2 && parts.All(part => part.Trim().Length > 0),
            $"a fact of the removal register is not a class and a method joined by a dot: {text}");

        return new NamedFact(parts[0].Trim(), parts[1].Trim());
    }

    /// <summary>
    /// Whether a class holds a fact by that name. A class is driven by its facts rather than by its
    /// methods, so a helper named where a fact was meant is refused.
    /// </summary>
    /// <param name="type">The class.</param>
    /// <param name="method">The method name.</param>
    /// <returns>Whether it is a fact.</returns>
    private static bool HoldsTheFact(Type type, string method) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(candidate => string.Equals(candidate.Name, method, StringComparison.Ordinal))
            .Any(candidate => candidate.GetCustomAttributes(inherit: false)
                .Any(attribute => attribute.GetType().Name is "FactAttribute" or "TheoryAttribute"));

    /// <summary>
    /// What the removal register and the coverage register disagree about.
    /// </summary>
    /// <param name="Foreign">Facts a removal reddened that its own row is not filed under.</param>
    /// <param name="WithoutOwnRow">Rows whose removal reached no class only that row names.</param>
    internal sealed record Misfiling(
        IReadOnlyList<string> Foreign,
        IReadOnlyList<string> WithoutOwnRow);

    /// <summary>
    /// One fact, named as it is in the register.
    /// </summary>
    /// <param name="Class">The test class.</param>
    /// <param name="Method">The method.</param>
    internal sealed record NamedFact(string Class, string Method)
    {
        /// <inheritdoc/>
        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture, $"{Class}.{Method}");
    }

    /// <summary>
    /// One entry of the removal register.
    /// </summary>
    /// <param name="Field">The field whose row it is about.</param>
    /// <param name="Change">The change that takes the rule out.</param>
    /// <param name="Facts">The facts it reddened.</param>
    /// <param name="Reason">Why that change is the removal for this row.</param>
    internal sealed record Entry(
        string Field,
        string Change,
        IReadOnlyList<NamedFact> Facts,
        string Reason)
    {
        /// <inheritdoc/>
        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture, $"{Field} :: {Change}");
    }
}
