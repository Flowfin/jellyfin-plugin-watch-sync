using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchSync.Model;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the sync model document, the server's per-user record and this plugin's moved set to
/// one another.
///
/// The document says, per property of the server's record, whether the value moves between two
/// servers or never leaves the one it is on. That table is a list of the same thing the server
/// declares, and two lists of one thing drift. The drift is silent in the direction that costs
/// most: a property added upstream is a property the document says nothing about, and a value
/// nobody decided about is a value that moves or fails to move by accident.
///
/// The moved set is the other end of the same drift. A row saying a field moves and a type with
/// no member for it is a promise the code cannot keep, and a member with no row is a field
/// crossing between two servers on nobody's decision.
///
/// The record is read by reflection off the assembly this project compiles against, and the
/// suite builds once per target, so the table is judged against both supported server lines
/// rather than against whichever one was built.
/// </summary>
public class SyncModelDocumentTests
{
    /// <summary>
    /// The whole point, run against the table as it is. Every property of the record has a row,
    /// no row names something that is not a property, and nothing is named twice.
    /// </summary>
    [Fact]
    public void TheTableNamesEveryPropertyOfTheRecordExactlyOnce()
    {
        var report = SyncModelDocument.Check(
            SyncModelDocument.Rows(SyncModelDocument.Text()),
            SyncModelDocument.RecordProperties());

        Assert.Empty(report.Missing.Select(property =>
            $"{property} is a property of the server's record the table does not name, so nothing says whether it moves."));

        Assert.Empty(report.Unknown.Select(property =>
            $"{property} is named by the table and is not a property of the server's record, so its row is about nothing."));

        Assert.Empty(report.Repeated.Select(property =>
            $"{property} has more than one row, so which of them holds is undefined."));
    }

    /// <summary>
    /// The disposition column is a closed set, and the set is read out of the document rather
    /// than restated here. A row carrying a word the document never declared is a decision
    /// somebody made in the table and explained nowhere.
    /// </summary>
    [Fact]
    public void EveryRowCarriesADispositionTheDocumentDeclares()
    {
        var text = SyncModelDocument.Text();
        var declared = SyncModelDocument.DeclaredDispositions(text);
        var rows = SyncModelDocument.Rows(text);

        Assert.NotEmpty(declared);

        Assert.Empty(rows
            .Where(row => !declared.Contains(row.Disposition, StringComparer.Ordinal))
            .Select(row => $"{row.Property} carries the disposition {row.Disposition}, which the document does not declare."));
    }

    /// <summary>
    /// The other direction of the same closure. A disposition the document declares and no row
    /// uses is a rule that was removed from the table and left in the prose, which is where a
    /// reader looks first.
    /// </summary>
    [Fact]
    public void NoDeclaredDispositionHasOutlivedTheRowsThatUsedIt()
    {
        var text = SyncModelDocument.Text();
        var used = SyncModelDocument.Rows(text).Select(row => row.Disposition).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(SyncModelDocument
            .DeclaredDispositions(text)
            .Where(disposition => !used.Contains(disposition))
            .Select(disposition => $"{disposition} is declared and no row uses it."));
    }

    /// <summary>
    /// A row with an empty reason column is a field whose disposition the document asserts and
    /// gives no reason for, which is the shape that reads as an oversight and gets filled in by
    /// the next person to notice the gap.
    /// </summary>
    [Fact]
    public void EveryRowGivesAReason()
    {
        var rows = SyncModelDocument.Rows(SyncModelDocument.Text());

        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.False(
            string.IsNullOrWhiteSpace(row.Why),
            $"{row.Property} carries a disposition and no reason for it."));
    }

    /// <summary>
    /// The table and the type are the same decision written twice, so they are held to each
    /// other. A moved row with no member is a field the document promises to carry and nothing
    /// can; a member with no moved row is a field crossing between two servers that the document
    /// does not admit to moving.
    /// </summary>
    [Fact]
    public void TheMovedRowsAndTheMovedSetAreTheSameFields()
    {
        var report = SyncModelDocument.CheckMovedSet(
            SyncModelDocument.Rows(SyncModelDocument.Text()),
            SyncModelDocument.MovedSetMembers());

        Assert.NotEmpty(SyncModelDocument.MovedSetMembers());

        Assert.Empty(report.RowsWithoutAMember.Select(property =>
            $"{property} is moved by the table and is not a member of {nameof(SyncedState)}, so nothing can carry it."));

        Assert.Empty(report.MembersWithoutARow.Select(member =>
            $"{member} is a member of {nameof(SyncedState)} and the table does not move it."));
    }

    /// <summary>
    /// The first Done-when condition of #12 in the one form a test can read: the moved set is
    /// this plugin's own type and not the server's record wearing a different name. A member
    /// typed as anything out of the server's assembly would carry that assembly's shape into
    /// whatever an envelope is made of, and the field table above would stop being the thing
    /// that decides what moves.
    /// </summary>
    [Fact]
    public void TheMovedSetIsNotTheServersRecord()
    {
        Assert.False(
            typeof(SyncedState).IsAssignableFrom(typeof(UserItemData)),
            $"{nameof(SyncedState)} is satisfied by the server's record, so the record can be used where the moved set is expected.");

        var serverAssembly = typeof(UserItemData).Assembly;

        Assert.Empty(typeof(SyncedState)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType.Assembly == serverAssembly)
            .Select(property => $"{property.Name} is typed as {property.PropertyType.FullName}, which comes from the server's own assembly."));
    }

    /// <summary>
    /// The guard proven by deleting it, on the mistake a table of long similar property names
    /// actually produces. The near-miss misspells one property by one character, so the row is
    /// present, its disposition is right and its reason is right, and the property it claims to
    /// be about does not exist. The repair is that one character and nothing else.
    ///
    /// The fixture carries its own vocabulary. A fixture judged against the real record would
    /// prove the state of the referenced package on the day it ran rather than proving the
    /// guard.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheNearMissAndPassesItsRepair()
    {
        var vocabulary = new[] { "Played", "PlaybackPositionTicks", "Key" };

        var refused = SyncModelDocument.Check(
            SyncModelDocument.Rows(SyncModelDocument.Fixture("near-miss.txt")),
            vocabulary);

        Assert.Equal("PlaybackPositionTicks", Assert.Single(refused.Missing));
        Assert.Equal("PlaybackPositionTick", Assert.Single(refused.Unknown));
        Assert.Empty(refused.Repeated);

        var repaired = SyncModelDocument.Check(
            SyncModelDocument.Rows(SyncModelDocument.Fixture("near-miss-repaired.txt")),
            vocabulary);

        Assert.Empty(repaired.Missing);
        Assert.Empty(repaired.Unknown);
        Assert.Empty(repaired.Repeated);
    }

    /// <summary>
    /// A property named twice is the other way a hand-maintained table goes wrong, and the two
    /// rows can disagree about whether the field moves. This drives the leg on the fixture,
    /// because the real table has no repeat and a leg exercised only by the tree is one that
    /// stops being exercised the moment the tree is right.
    /// </summary>
    [Fact]
    public void ARepeatedPropertyIsRefused()
    {
        var rows = SyncModelDocument.Rows(SyncModelDocument.Fixture("near-miss-repaired.txt"));
        var repeatedRows = rows.Concat(new[] { rows[0] }).ToList();

        var report = SyncModelDocument.Check(repeatedRows, new[] { "Played", "PlaybackPositionTicks", "Key" });

        Assert.Equal("Played", Assert.Single(report.Repeated));
    }

    /// <summary>
    /// The moved-set leg, driven both ways off the fixture for the same reason: the tree agrees
    /// today, so a leg that only ever sees the tree proves nothing about what it would refuse.
    /// </summary>
    [Fact]
    public void TheMovedSetLegRefusesEitherSideMissingAField()
    {
        var rows = SyncModelDocument.Rows(SyncModelDocument.Fixture("near-miss-repaired.txt"));

        var agreed = SyncModelDocument.CheckMovedSet(rows, new[] { "Played", "PlaybackPositionTicks" });

        Assert.Empty(agreed.RowsWithoutAMember);
        Assert.Empty(agreed.MembersWithoutARow);

        var memberMissing = SyncModelDocument.CheckMovedSet(rows, new[] { "Played" });

        Assert.Equal("PlaybackPositionTicks", Assert.Single(memberMissing.RowsWithoutAMember));
        Assert.Empty(memberMissing.MembersWithoutARow);

        var rowMissing = SyncModelDocument.CheckMovedSet(
            rows,
            new[] { "Played", "PlaybackPositionTicks", "IsFavorite" });

        Assert.Empty(rowMissing.RowsWithoutAMember);
        Assert.Equal("IsFavorite", Assert.Single(rowMissing.MembersWithoutARow));
    }

    internal static class SyncModelDocument
    {
        /// <summary>
        /// The disposition a field has to carry for the moved set to be expected to hold it.
        /// </summary>
        internal const string Moved = "moved";

        /// <summary>
        /// A row of the table: the property it is about, its disposition, and the reason.
        /// </summary>
        /// <param name="Property">The property of the server's record the row names.</param>
        /// <param name="Disposition">The disposition column.</param>
        /// <param name="Why">The reason column.</param>
        internal sealed record Row(string Property, string Disposition, string Why);

        /// <summary>
        /// What the table and a vocabulary of property names disagree about.
        /// </summary>
        /// <param name="Missing">Properties the vocabulary has and the table does not name.</param>
        /// <param name="Unknown">Properties the table names and the vocabulary does not have.</param>
        /// <param name="Repeated">Properties the table names more than once.</param>
        internal sealed record Report(
            IReadOnlyList<string> Missing,
            IReadOnlyList<string> Unknown,
            IReadOnlyList<string> Repeated);

        /// <summary>
        /// What the moved rows and the members of the moved set disagree about.
        /// </summary>
        /// <param name="RowsWithoutAMember">Fields the table moves and the type has no member for.</param>
        /// <param name="MembersWithoutARow">Members of the type the table does not move.</param>
        internal sealed record MovedSetReport(
            IReadOnlyList<string> RowsWithoutAMember,
            IReadOnlyList<string> MembersWithoutARow);

        /// <summary>
        /// Compares a set of rows against a vocabulary. Pure, so the fixtures run through the
        /// same code the document does rather than through a second implementation of it.
        /// </summary>
        /// <param name="rows">The rows to judge.</param>
        /// <param name="properties">The vocabulary they are judged against.</param>
        /// <returns>What the two disagree about.</returns>
        internal static Report Check(IReadOnlyList<Row> rows, IReadOnlyList<string> properties)
        {
            var named = rows.Select(row => row.Property).ToList();
            var vocabulary = new HashSet<string>(properties, StringComparer.Ordinal);

            var missing = properties
                .Where(property => !named.Contains(property, StringComparer.Ordinal))
                .ToList();

            var unknown = named
                .Where(property => !vocabulary.Contains(property))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var repeated = named
                .GroupBy(property => property, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            return new Report(missing, unknown, repeated);
        }

        /// <summary>
        /// Compares the moved rows against the members of a moved set. Pure, for the same reason
        /// <see cref="Check"/> is.
        /// </summary>
        /// <param name="rows">The rows to judge.</param>
        /// <param name="members">The members of the moved set.</param>
        /// <returns>What the two disagree about.</returns>
        internal static MovedSetReport CheckMovedSet(IReadOnlyList<Row> rows, IReadOnlyList<string> members)
        {
            var moved = rows
                .Where(row => string.Equals(row.Disposition, Moved, StringComparison.Ordinal))
                .Select(row => row.Property)
                .ToList();

            var held = new HashSet<string>(members, StringComparer.Ordinal);

            return new MovedSetReport(
                moved.Where(property => !held.Contains(property)).Distinct(StringComparer.Ordinal).ToList(),
                members.Where(member => !moved.Contains(member, StringComparer.Ordinal)).ToList());
        }

        /// <summary>
        /// The properties the server's own per-user record carries, read off the referenced
        /// assembly rather than out of a list somebody maintains here. Raising the package
        /// raises what this is judged against, which is the point, and the suite builds once per
        /// target so both server lines are judged.
        /// </summary>
        /// <returns>Every public instance property of the record.</returns>
        internal static IReadOnlyList<string> RecordProperties() =>
            typeof(UserItemData)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .ToList();

        /// <summary>
        /// The members of this plugin's moved set, read by reflection for the same reason the
        /// record's properties are: a list written here would be the drift this test exists to
        /// refuse, one level further in.
        /// </summary>
        /// <returns>Every public instance property of the moved set.</returns>
        internal static IReadOnlyList<string> MovedSetMembers() =>
            typeof(SyncedState)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .ToList();

        /// <summary>
        /// Reads the rows of the table out of a document. The read is table rows rather than a
        /// parse of the whole file, so prose naming a property does not count as giving it a row.
        /// </summary>
        /// <param name="text">The document text.</param>
        /// <returns>The rows.</returns>
        internal static IReadOnlyList<Row> Rows(string text) =>
            Regex
                .Matches(text, @"(?m)^\|\s*`(?<property>[A-Za-z0-9]+)`\s*\|\s*(?<disposition>[a-z]+)\s*\|(?<why>[^|]*)\|\s*$")
                .Select(match => new Row(
                    match.Groups["property"].Value,
                    match.Groups["disposition"].Value,
                    match.Groups["why"].Value.Trim()))
                .ToList();

        /// <summary>
        /// Reads the dispositions the document declares, which are the leading code spans of the
        /// list that introduces them. Reading them rather than restating them here is what makes
        /// this a check on the document instead of a second copy of it.
        /// </summary>
        /// <param name="text">The document text.</param>
        /// <returns>The declared dispositions.</returns>
        internal static IReadOnlyList<string> DeclaredDispositions(string text) =>
            Regex
                .Matches(text, "(?m)^- `(?<disposition>[a-z]+)`[,.]")
                .Select(match => match.Groups["disposition"].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

        /// <summary>
        /// Reads the sync model document from the tracked tree rather than from a copy in the
        /// output directory, because a copy proves the state of the file on the day it was
        /// copied.
        /// </summary>
        /// <returns>Its text.</returns>
        internal static string Text() =>
            File.ReadAllText(Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), "docs", "sync-model.md"));

        /// <summary>
        /// Reads one of the two fixtures.
        /// </summary>
        /// <param name="name">The fixture file name.</param>
        /// <returns>Its text.</returns>
        internal static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                "Jellyfin.Plugin.WatchSync.Tests",
                "Model",
                name));
    }
}
