using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.WatchSync.Conflict;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds every row of the conflict table to the facts that drive it, which is the first
/// condition of #81.
///
/// <see cref="ConflictTableTests"/> holds the table to the moved set, so a field that crosses
/// between two servers has a row saying what happens when the two sides disagree about it.
/// That leaves the other half: a row can say anything at all and nothing has to execute it.
/// A table tested by a few examples is a table whose interesting rows are the untested ones,
/// and the row nobody wrote a fact for is the one a later change to make the resolver simpler
/// quietly removes.
///
/// So <c>Conflict/coverage.txt</c> carries one entry per row, naming the classes that drive it
/// and the types in the resolver that decide it, and these refuse the register and the table
/// disagreeing in either direction and the register and the tree disagreeing in either
/// direction.
///
/// What this cannot judge is whether a fact filed under a row is about that row.
/// <see cref="ConflictRuleRemovalTests"/> reaches it, by holding the record of what each rule's
/// removal reddened to the classes this register files under that row. The removal is applied
/// by hand and nothing runs it, which that record says about itself.
///
/// The third condition is reached by <see cref="ConflictOrderedPairTests"/>, over the rows this
/// register enters as covered rather than over the whole table, and the register is what decides
/// which those are.
/// </summary>
public class ConflictRowCoverageTests
{
    /// <summary>
    /// The whole point. A row of the table with no entry is a rule that decides somebody's
    /// history and that no fact executes.
    /// </summary>
    [Fact]
    public void EveryRowOfTheTableHasAnEntry()
    {
        var rows = TableFields();

        Assert.NotEmpty(rows);

        Assert.Empty(Compare(rows, Register()).Unentered
            .Select(field => $"{field} has a row in the conflict table and no entry in the coverage register, so nothing says which facts drive it."));
    }

    /// <summary>
    /// The other direction. An entry for a row that is no longer in the table is a claim about
    /// a rule that was taken out, and it is the shape that keeps a register looking complete
    /// while it describes something else.
    /// </summary>
    [Fact]
    public void NoEntryOutlivesTheRowItIsAbout()
    {
        Assert.Empty(Compare(TableFields(), Register()).Dangling
            .Select(field => $"{field} has an entry in the coverage register and no row in the conflict table."));
    }

    /// <summary>
    /// The guard proven by deleting it, on the mistake a register of long similar field names
    /// actually produces. The near-miss drops one character from one field, so every entry is
    /// present, every state is right and every reason is right, and one row of the table is
    /// driven by nothing while one entry describes a row that does not exist. The repair is
    /// that character and nothing else.
    ///
    /// The fixture carries its own vocabulary, because a fixture judged against the real table
    /// would prove the state of that document on the day it ran rather than proving the guard.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheNearMissAndPassesItsRepair()
    {
        var vocabulary = new[] { "Played", "PlayCount", "PlaybackPositionTicks", "LastPlayedDate" };

        var refused = Compare(vocabulary, Parse(Fixture("coverage-near-miss.txt")));

        Assert.Equal("PlaybackPositionTicks", Assert.Single(refused.Unentered));
        Assert.Equal("PlaybackPositionTick", Assert.Single(refused.Dangling));

        var repaired = Compare(vocabulary, Parse(Fixture("coverage-near-miss-repaired.txt")));

        Assert.Empty(repaired.Unentered);
        Assert.Empty(repaired.Dangling);
    }

    /// <summary>
    /// Two entries claiming one type is the other way a hand-maintained register goes wrong,
    /// and it is how the awaited state would be defeated: an entry that quietly takes the type
    /// a new rule arrived as leaves the row it belongs to still reading as awaited. This leg is
    /// driven off the fixture, because the register has no repeat and a leg exercised only by
    /// the tree stops being exercised the moment the tree is right.
    /// </summary>
    [Fact]
    public void ATypeClaimedByTwoEntriesIsRefused()
    {
        var entries = Parse(Fixture("coverage-near-miss-repaired.txt"));

        Assert.Empty(ClaimedTwice(entries));

        Assert.Equal(
            new[] { "PlayedRatchet", "RatchetAnswer" },
            ClaimedTwice(entries.Append(entries[0]).ToList()).OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// A covered row names classes that are in this assembly and that hold facts. A register
    /// naming a class nobody wrote, or one that was emptied, is a row reading as driven while
    /// nothing runs.
    /// </summary>
    [Fact]
    public void EveryCoveredRowNamesClassesThatHoldFacts()
    {
        var suite = typeof(ConflictRowCoverageTests).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsNested)
            .ToDictionary(type => type.Name, type => type, StringComparer.Ordinal);

        foreach (var entry in Register().Where(entry => entry.IsCovered))
        {
            Assert.NotEmpty(entry.Classes);

            foreach (var name in entry.Classes)
            {
                Assert.True(
                    suite.TryGetValue(name, out var type),
                    $"{entry.Field} names {name} and the suite has no such class.");

                Assert.True(
                    Facts(type!) > 0,
                    $"{entry.Field} names {name} and it holds no fact.");
            }
        }
    }

    /// <summary>
    /// An awaited row claims nothing. A row that says no rule decides it and also names a
    /// class or a type is two answers at once, and the one a reader takes is whichever column
    /// they read first.
    /// </summary>
    [Fact]
    public void AnAwaitedRowNamesNoClassAndNoType()
    {
        foreach (var entry in Register().Where(entry => !entry.IsCovered))
        {
            Assert.Empty(entry.Classes);
            Assert.Empty(entry.Types);
        }
    }

    /// <summary>
    /// Every type the resolver holds belongs to exactly one entry.
    ///
    /// This is what makes the awaited state fail closed, and it is deliberately not keyed on
    /// the name anybody predicted. The day #32 lands the position rule, the type it arrives as
    /// is named by no entry and this goes red, whatever it was called, and the row has to be
    /// moved by hand. That is the moment somebody checks that the rule landed for the row the
    /// register was holding open.
    /// </summary>
    [Fact]
    public void EveryTypeInTheResolverBelongsToOneEntry()
    {
        var entries = Register();
        var claimed = entries.SelectMany(entry => entry.Types).ToList();

        Assert.Empty(ClaimedTwice(entries)
            .Select(name => $"{name} is claimed by more than one entry, so which row it decides is undefined."));

        var present = ResolverTypes();

        Assert.NotEmpty(present);

        Assert.Empty(present
            .Where(name => !claimed.Contains(name, StringComparer.Ordinal))
            .Select(name => $"{name} is in the resolver and no entry of the coverage register names it, so a row declared awaited may now have a rule."));

        Assert.Empty(claimed
            .Where(name => !present.Contains(name, StringComparer.Ordinal))
            .Select(name => $"{name} is named by an entry and is not in the resolver."));
    }

    /// <summary>
    /// An entry with no reason is a decision nobody made, which is the leg the mutation scope
    /// register carries and for the same reason.
    /// </summary>
    [Fact]
    public void EveryEntryCarriesItsReason()
    {
        foreach (var entry in Register())
        {
            Assert.False(
                string.IsNullOrWhiteSpace(entry.Reason),
                $"{entry.Field} is declared {entry.State} and gives no reason.");
        }
    }

    /// <summary>
    /// The public types of the resolver, read out of the assembly the plugin builds rather
    /// than out of a list kept here, because a list here would be the drift these tests exist
    /// to refuse one level further in.
    /// </summary>
    /// <returns>Their names.</returns>
    internal static IReadOnlyList<string> ResolverTypes() =>
        typeof(PlayedRatchet).Assembly
            .GetExportedTypes()
            .Where(type => !type.IsNested)
            .Where(type => string.Equals(type.Namespace, typeof(PlayedRatchet).Namespace, StringComparison.Ordinal))
            .Select(type => type.Name)
            .ToList();

    /// <summary>
    /// The fields the conflict table has a row for, read through the same parse
    /// <see cref="ConflictTableTests"/> uses rather than through a second one.
    /// </summary>
    /// <returns>One name per row.</returns>
    internal static IReadOnlyList<string> TableFields() =>
        ConflictTableTests.ConflictDocument
            .Rows(ConflictTableTests.ConflictDocument.Text())
            .Select(row => row.Field)
            .ToList();

    /// <summary>
    /// What a set of rows and a set of entries disagree about. Pure, so the fixtures run
    /// through the same code the tree does rather than through a second implementation of it.
    /// </summary>
    /// <param name="rows">The fields the table has a row for.</param>
    /// <param name="entries">The entries of the register.</param>
    /// <returns>What the two disagree about.</returns>
    internal static Disagreement Compare(IReadOnlyList<string> rows, IReadOnlyList<Entry> entries)
    {
        var entered = entries.Select(entry => entry.Field).ToHashSet(StringComparer.Ordinal);
        var present = new HashSet<string>(rows, StringComparer.Ordinal);

        return new Disagreement(
            rows.Where(field => !entered.Contains(field)).ToList(),
            entries.Select(entry => entry.Field).Where(field => !present.Contains(field)).ToList());
    }

    /// <summary>
    /// The types more than one entry claims.
    /// </summary>
    /// <param name="entries">The entries of the register.</param>
    /// <returns>Their names.</returns>
    internal static IReadOnlyList<string> ClaimedTwice(IReadOnlyList<Entry> entries) =>
        entries
            .SelectMany(entry => entry.Types)
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

    /// <summary>
    /// Reads one of the two fixtures. The parts are joined rather than combined, for the
    /// reason <see cref="ConflictTableTests.ConflictDocument.Text"/> gives.
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
    /// The register, read as data.
    /// </summary>
    /// <returns>The entries.</returns>
    internal static IReadOnlyList<Entry> Register() =>
        Parse(File.ReadAllLines(Path.Join(
            HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
            "Jellyfin.Plugin.WatchSync.Tests",
            "Conflict",
            "coverage.txt")));

    /// <summary>
    /// Reads entries out of lines. Pure, so the fixtures run through the same code the
    /// register does rather than through a second implementation of it. An entry the parser
    /// cannot read fails rather than being skipped, which is the difference between a register
    /// and a comment.
    /// </summary>
    /// <param name="lines">The lines.</param>
    /// <returns>The entries.</returns>
    internal static IReadOnlyList<Entry> Parse(IEnumerable<string> lines)
    {
        var entries = new List<Entry>();

        foreach (var line in lines)
        {
            var text = line.Trim();

            if (text.Length == 0 || text.StartsWith('#'))
            {
                continue;
            }

            // Split the line rather than the trimmed text, so an entry whose reason was deleted
            // and whose separator was left behind still arrives as five fields with an empty
            // one. Trimmed first it would arrive as four and be refused as malformed, which is
            // a refusal that names the shape of the line instead of the thing that is missing.
            var fields = line.Split(" :: ");

            Assert.True(
                fields.Length == 5,
                $"an entry of the coverage register has {fields.Length} fields rather than five: {text}");

            Assert.Contains(fields[1].Trim(), new[] { "covered", "awaited" });

            entries.Add(new Entry(
                fields[0].Trim(),
                fields[1].Trim(),
                Column(fields[2]),
                Column(fields[3]),
                fields[4].Trim()));
        }

        Assert.NotEmpty(entries);

        return entries;
    }

    /// <summary>
    /// How many facts a class holds. A class is driven by its facts rather than by its
    /// methods, so a class of helpers named in the register is refused as holding none.
    /// </summary>
    /// <param name="type">The class.</param>
    /// <returns>The count.</returns>
    private static int Facts(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Count(method => method.GetCustomAttributes(inherit: false)
                .Any(attribute => attribute.GetType().Name is "FactAttribute" or "TheoryAttribute"));

    /// <summary>
    /// A comma separated column, where "-" is none.
    /// </summary>
    /// <param name="column">The column text.</param>
    /// <returns>Its members.</returns>
    private static IReadOnlyList<string> Column(string column)
    {
        var text = column.Trim();

        if (string.Equals(text, "-", StringComparison.Ordinal))
        {
            return Array.Empty<string>();
        }

        return text
            .Split(',')
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToList();
    }

    /// <summary>
    /// What the table and the register disagree about.
    /// </summary>
    /// <param name="Unentered">Rows the table has and the register does not name.</param>
    /// <param name="Dangling">Fields the register names and the table has no row for.</param>
    internal sealed record Disagreement(
        IReadOnlyList<string> Unentered,
        IReadOnlyList<string> Dangling);

    /// <summary>
    /// One entry of the coverage register.
    /// </summary>
    /// <param name="Field">The field whose row it is about.</param>
    /// <param name="State">Whether a rule decides the row today.</param>
    /// <param name="Classes">The test classes that drive it.</param>
    /// <param name="Types">The types in the resolver that decide it.</param>
    /// <param name="Reason">Why the row is in that state.</param>
    internal sealed record Entry(
        string Field,
        string State,
        IReadOnlyList<string> Classes,
        IReadOnlyList<string> Types,
        string Reason)
    {
        /// <summary>
        /// Gets a value indicating whether a rule decides this row today.
        /// </summary>
        internal bool IsCovered => string.Equals(State, "covered", StringComparison.Ordinal);

        /// <inheritdoc/>
        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture, $"{Field} :: {State}");
    }
}
