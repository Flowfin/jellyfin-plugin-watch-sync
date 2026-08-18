using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchSync.Model;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the conflict table and this plugin's moved set to one another, which is the first
/// condition of #30.
///
/// The table says, per field this plugin syncs, what happens when the two sides disagree, what
/// evidence the rule uses, what becomes of the value that lost, and which failure the rule
/// prevents. The set of fields it is about is decided in <c>docs/sync-model.md</c> and carried by
/// <see cref="SyncedState"/>, so the table is a second list of one thing, and two lists of one
/// thing drift. The drift is silent in the expensive direction: a field added to the moved set
/// with no row is a value crossing between two servers under no rule anybody wrote down.
///
/// The rule column is closed against the rules the document declares, in both directions, for the
/// same reason the disposition column of the sync model document is: a word invented in a row is
/// a decision explained nowhere, and a rule left in the prose after its row is gone is a rule a
/// reader will act on.
///
/// What this cannot judge is whether a row's rule is the right rule. That is a reading at review,
/// and it is the bound every document check in this repository carries.
/// </summary>
public class ConflictTableTests
{
    /// <summary>
    /// The whole point, run against the table as it stands. Every moved field has a row, no row
    /// names something outside the moved set, and nothing is named twice.
    /// </summary>
    [Fact]
    public void TheTableNamesEveryMovedFieldExactlyOnce()
    {
        var report = ConflictDocument.Check(
            ConflictDocument.Rows(ConflictDocument.Text()),
            ConflictDocument.MovedSetMembers());

        Assert.NotEmpty(ConflictDocument.MovedSetMembers());

        Assert.Empty(report.Missing.Select(field =>
            $"{field} is a member of {nameof(SyncedState)} and the conflict table has no row for it, so nothing says what happens when the two sides disagree about it."));

        Assert.Empty(report.Unknown.Select(field =>
            $"{field} has a row in the conflict table and is not a member of {nameof(SyncedState)}, so the row is about a field that does not move."));

        Assert.Empty(report.Repeated.Select(field =>
            $"{field} has more than one row, so which rule holds is undefined."));
    }

    /// <summary>
    /// The rule column is a closed set and the set is read out of the document rather than
    /// restated here. A row carrying a word the document never declared is a rule somebody
    /// invented in the table and argued nowhere.
    /// </summary>
    [Fact]
    public void EveryRowCarriesARuleTheDocumentDeclares()
    {
        var text = ConflictDocument.Text();
        var declared = ConflictDocument.DeclaredRules(text);

        Assert.NotEmpty(declared);

        Assert.Empty(ConflictDocument
            .Rows(text)
            .Where(row => !declared.Contains(row.Rule, StringComparer.Ordinal))
            .Select(row => $"{row.Field} carries the rule {row.Rule}, which the document does not declare."));
    }

    /// <summary>
    /// The other direction of the same closure. A rule declared in the prose and used by no row
    /// is a rule that was taken out of the table and left where a reader looks first.
    /// </summary>
    [Fact]
    public void NoDeclaredRuleHasOutlivedTheRowsThatUsedIt()
    {
        var text = ConflictDocument.Text();
        var used = ConflictDocument.Rows(text)
            .Select(row => row.Rule)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(ConflictDocument
            .DeclaredRules(text)
            .Where(rule => !used.Contains(rule))
            .Select(rule => $"{rule} is declared and no row uses it."));
    }

    /// <summary>
    /// A row that asserts a rule and leaves one of the three argument columns empty is the shape
    /// that reads as an oversight and gets filled in by whoever notices it next. The loser column
    /// is the one that matters most, because a value discarded in silence is what the whole
    /// document is written against.
    /// </summary>
    [Fact]
    public void EveryRowNamesItsEvidenceItsLoserAndTheFailureItPrevents()
    {
        var rows = ConflictDocument.Rows(ConflictDocument.Text());

        Assert.NotEmpty(rows);

        Assert.All(rows, row => Assert.False(
            string.IsNullOrWhiteSpace(row.Evidence),
            $"{row.Field} carries a rule and does not say what evidence it reads."));

        Assert.All(rows, row => Assert.False(
            string.IsNullOrWhiteSpace(row.Loser),
            $"{row.Field} carries a rule and does not say what happens to the value that lost."));

        Assert.All(rows, row => Assert.False(
            string.IsNullOrWhiteSpace(row.Failure),
            $"{row.Field} carries a rule and names no failure it prevents."));
    }

    /// <summary>
    /// The guard proven by deleting it, on the mistake a table of long similar field names
    /// actually produces. The near-miss misspells one field by one character, so the row is
    /// present, its rule is right and its argument is right, and the field it claims to be about
    /// is not one that moves. The repair is that character and nothing else.
    ///
    /// The fixture carries its own vocabulary, because a fixture judged against the real moved
    /// set would prove the state of that type on the day it ran rather than proving the guard.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheNearMissAndPassesItsRepair()
    {
        var vocabulary = new[] { "Played", "PlaybackPositionTicks", "LastPlayedDate" };

        var refused = ConflictDocument.Check(
            ConflictDocument.Rows(ConflictDocument.Fixture("near-miss.txt")),
            vocabulary);

        Assert.Equal("PlaybackPositionTicks", Assert.Single(refused.Missing));
        Assert.Equal("PlaybackPositionTick", Assert.Single(refused.Unknown));
        Assert.Empty(refused.Repeated);

        var repaired = ConflictDocument.Check(
            ConflictDocument.Rows(ConflictDocument.Fixture("near-miss-repaired.txt")),
            vocabulary);

        Assert.Empty(repaired.Missing);
        Assert.Empty(repaired.Unknown);
        Assert.Empty(repaired.Repeated);
    }

    /// <summary>
    /// A field named twice is the other way a hand-maintained table goes wrong, and the two rows
    /// can carry different rules. This leg is driven off the fixture, because the tree has no
    /// repeat and a leg exercised only by the tree stops being exercised the moment the tree is
    /// right.
    /// </summary>
    [Fact]
    public void ARepeatedFieldIsRefused()
    {
        var rows = ConflictDocument.Rows(ConflictDocument.Fixture("near-miss-repaired.txt"));
        var repeatedRows = rows.Concat(new[] { rows[0] }).ToList();

        var report = ConflictDocument.Check(
            repeatedRows,
            new[] { "Played", "PlaybackPositionTicks", "LastPlayedDate" });

        Assert.Equal("Played", Assert.Single(report.Repeated));
    }

    internal static class ConflictDocument
    {
        /// <summary>
        /// A row of the table: the field it is about, its rule, and the three columns that argue
        /// for the rule.
        /// </summary>
        /// <param name="Field">The moved field the row names.</param>
        /// <param name="Rule">The rule column.</param>
        /// <param name="Evidence">What the rule reads.</param>
        /// <param name="Loser">What becomes of the value that did not win.</param>
        /// <param name="Failure">The failure the rule prevents.</param>
        internal sealed record Row(
            string Field,
            string Rule,
            string Evidence,
            string Loser,
            string Failure);

        /// <summary>
        /// What the table and a vocabulary of field names disagree about.
        /// </summary>
        /// <param name="Missing">Fields the vocabulary has and the table does not name.</param>
        /// <param name="Unknown">Fields the table names and the vocabulary does not have.</param>
        /// <param name="Repeated">Fields the table names more than once.</param>
        internal sealed record Report(
            IReadOnlyList<string> Missing,
            IReadOnlyList<string> Unknown,
            IReadOnlyList<string> Repeated);

        /// <summary>
        /// Compares a set of rows against a vocabulary. Pure, so the fixtures run through the
        /// same code the document does rather than through a second implementation of it.
        /// </summary>
        /// <param name="rows">The rows to judge.</param>
        /// <param name="fields">The vocabulary they are judged against.</param>
        /// <returns>What the two disagree about.</returns>
        internal static Report Check(IReadOnlyList<Row> rows, IReadOnlyList<string> fields)
        {
            var named = rows.Select(row => row.Field).ToList();
            var vocabulary = new HashSet<string>(fields, StringComparer.Ordinal);

            var missing = fields
                .Where(field => !named.Contains(field, StringComparer.Ordinal))
                .ToList();

            var unknown = named
                .Where(field => !vocabulary.Contains(field))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var repeated = named
                .GroupBy(field => field, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            return new Report(missing, unknown, repeated);
        }

        /// <summary>
        /// The members of this plugin's moved set, read by reflection rather than out of a list
        /// kept here, because a list here would be the drift this test exists to refuse, one
        /// level further in.
        /// </summary>
        /// <returns>Every public instance property of the moved set.</returns>
        internal static IReadOnlyList<string> MovedSetMembers() =>
            typeof(SyncedState)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .ToList();

        /// <summary>
        /// Reads the rows of the table out of a document. Table rows rather than a parse of the
        /// whole file, so prose naming a field does not count as giving it a row, and five
        /// columns rather than the sync model table's three, so a row of one shape is not a row
        /// of the other.
        /// </summary>
        /// <param name="text">The document text.</param>
        /// <returns>The rows.</returns>
        internal static IReadOnlyList<Row> Rows(string text) =>
            Regex
                .Matches(
                    text,
                    @"(?m)^\|\s*`(?<field>[A-Za-z0-9]+)`\s*\|\s*(?<rule>[a-z]+)\s*\|(?<evidence>[^|]*)\|(?<loser>[^|]*)\|(?<failure>[^|]*)\|\s*$")
                .Select(match => new Row(
                    match.Groups["field"].Value,
                    match.Groups["rule"].Value,
                    match.Groups["evidence"].Value.Trim(),
                    match.Groups["loser"].Value.Trim(),
                    match.Groups["failure"].Value.Trim()))
                .ToList();

        /// <summary>
        /// Reads the rules the document declares, which are the leading code spans of the list
        /// that introduces them. Reading them rather than restating them here is what makes this
        /// a check on the document instead of a second copy of it.
        /// </summary>
        /// <param name="text">The document text.</param>
        /// <returns>The declared rules.</returns>
        internal static IReadOnlyList<string> DeclaredRules(string text) =>
            Regex
                .Matches(text, "(?m)^- `(?<rule>[a-z]+)`[,.]")
                .Select(match => match.Groups["rule"].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

        /// <summary>
        /// Reads the conflict document from the tracked tree rather than from a copy in the
        /// output directory, because a copy proves the state of the file on the day it was
        /// copied.
        ///
        /// The parts are joined rather than combined. Path.Combine drops every earlier argument
        /// when a later one is rooted, which is the whole of the alert class docs/code-scanning.md
        /// argues site by site; Path.Join never drops one, so this site cannot join the class.
        /// </summary>
        /// <returns>Its text.</returns>
        internal static string Text() =>
            File.ReadAllText(Path.Join(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), "docs", "conflicts.md"));

        /// <summary>
        /// Reads one of the two fixtures, joined rather than combined for the reason
        /// <see cref="Text"/> gives.
        /// </summary>
        /// <param name="name">The fixture file name.</param>
        /// <returns>Its text.</returns>
        internal static string Fixture(string name) =>
            File.ReadAllText(Path.Join(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                "Jellyfin.Plugin.WatchSync.Tests",
                "Conflict",
                name));
    }
}
