using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the matching document to the server's own vocabulary for what an item is.
///
/// The document says, per item kind, whether watch state moves for it and by which key. That
/// table is a list of the same thing the server enumerates, and two lists of one thing drift.
/// The drift is silent in the direction that matters most: a kind added upstream is a kind the
/// document says nothing about, and an implementation reading the document then has no rule for
/// an item a library can hold.
/// </summary>
public class MatchingDocumentTests
{
    /// <summary>
    /// The whole point, run against the table as it is. Every member of the enumeration has a
    /// row, no row names something that is not a member, and nothing is named twice.
    /// </summary>
    [Fact]
    public void TheTableNamesEveryItemKindTheServerHasExactlyOnce()
    {
        var report = MatchingDocument.Check(
            MatchingDocument.Rows(MatchingDocument.Text()),
            MatchingDocument.ItemKinds());

        Assert.Empty(report.Missing.Select(kind =>
            $"{kind} is a BaseItemKind the table does not name, so nothing says whether it syncs."));

        Assert.Empty(report.Unknown.Select(kind =>
            $"{kind} is named by the table and is not a BaseItemKind, so its row is about nothing."));

        Assert.Empty(report.Repeated.Select(kind =>
            $"{kind} has more than one row, so which of them holds is undefined."));
    }

    /// <summary>
    /// The disposition column is a closed set, and the set is read out of the document rather
    /// than restated here. A row carrying a word the document never declared reads as a decision
    /// somebody made in the table and explained nowhere.
    /// </summary>
    [Fact]
    public void EveryRowCarriesADispositionTheDocumentDeclares()
    {
        var text = MatchingDocument.Text();
        var declared = MatchingDocument.DeclaredDispositions(text);
        var rows = MatchingDocument.Rows(text);

        Assert.NotEmpty(declared);

        Assert.Empty(rows
            .Where(row => !declared.Contains(row.Disposition, StringComparer.Ordinal))
            .Select(row => $"{row.Kind} carries the disposition {row.Disposition}, which the document does not declare."));
    }

    /// <summary>
    /// The other direction of the same closure. A disposition the document declares and no row
    /// uses is a rule that was removed from the table and left in the prose, which is where a
    /// reader looks first.
    /// </summary>
    [Fact]
    public void NoDeclaredDispositionHasOutlivedTheRowsThatUsedIt()
    {
        var text = MatchingDocument.Text();
        var used = MatchingDocument.Rows(text).Select(row => row.Disposition).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(MatchingDocument
            .DeclaredDispositions(text)
            .Where(disposition => !used.Contains(disposition))
            .Select(disposition => $"{disposition} is declared and no row uses it."));
    }

    /// <summary>
    /// A synced kind with an empty reason column is a kind the document claims to carry and names
    /// no key for, which is worse than leaving it out, because an implementation reading the
    /// table would believe a rule exists.
    /// </summary>
    [Fact]
    public void EverySyncedRowNamesAKeyRule()
    {
        var rows = MatchingDocument.Rows(MatchingDocument.Text());

        var synced = rows.Where(row => string.Equals(row.Disposition, "synced", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(synced);
        Assert.All(synced, row => Assert.False(string.IsNullOrWhiteSpace(row.Rule), $"{row.Kind} is synced and names no key rule."));
    }

    /// <summary>
    /// The guard proven by deleting it, on the mistake a long table of similar names actually
    /// produces. The near-miss misspells one kind by one character, so the row is present, its
    /// disposition is right and its reason is right, and the kind it claims to be about does not
    /// exist. The repair is that one character and nothing else.
    ///
    /// The fixture carries its own vocabulary. A fixture judged against the real enumeration
    /// would prove the state of the tree on the day it ran rather than proving the guard.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheNearMissAndPassesItsRepair()
    {
        var vocabulary = new[] { "Movie", "LiveTvChannel", "Season" };

        var refused = MatchingDocument.Check(
            MatchingDocument.Rows(MatchingDocument.Fixture("near-miss.txt")),
            vocabulary);

        Assert.Equal("LiveTvChannel", Assert.Single(refused.Missing));
        Assert.Equal("LiveTvChanel", Assert.Single(refused.Unknown));
        Assert.Empty(refused.Repeated);

        var repaired = MatchingDocument.Check(
            MatchingDocument.Rows(MatchingDocument.Fixture("near-miss-repaired.txt")),
            vocabulary);

        Assert.Empty(repaired.Missing);
        Assert.Empty(repaired.Unknown);
        Assert.Empty(repaired.Repeated);
    }

    /// <summary>
    /// A kind named twice is the other way a hand-maintained table goes wrong, and the two rows
    /// can disagree. This drives the leg on the fixture, because the real table has no repeat and
    /// a leg exercised only by the tree is a leg that stops being exercised the moment the tree
    /// is right.
    /// </summary>
    [Fact]
    public void ARepeatedKindIsRefused()
    {
        var rows = MatchingDocument.Rows(MatchingDocument.Fixture("near-miss-repaired.txt"));
        var repeatedRows = rows.Concat(new[] { rows[0] }).ToList();

        var report = MatchingDocument.Check(repeatedRows, new[] { "Movie", "LiveTvChannel", "Season" });

        Assert.Equal("Movie", Assert.Single(report.Repeated));
    }

    internal static class MatchingDocument
    {
        /// <summary>
        /// A row of the table: the kind it is about, its disposition, and the key rule or the
        /// reason it is not synced.
        /// </summary>
        /// <param name="Kind">The item kind the row names.</param>
        /// <param name="Disposition">The disposition column.</param>
        /// <param name="Rule">The key rule, or the reason the kind is not synced.</param>
        internal sealed record Row(string Kind, string Disposition, string Rule);

        /// <summary>
        /// What the table and a vocabulary disagree about.
        /// </summary>
        /// <param name="Missing">Kinds the vocabulary has and the table does not name.</param>
        /// <param name="Unknown">Kinds the table names and the vocabulary does not have.</param>
        /// <param name="Repeated">Kinds the table names more than once.</param>
        internal sealed record Report(
            IReadOnlyList<string> Missing,
            IReadOnlyList<string> Unknown,
            IReadOnlyList<string> Repeated);

        /// <summary>
        /// Compares a set of rows against a vocabulary. Pure, so the fixtures run through the
        /// same code the document does rather than through a second implementation of it.
        /// </summary>
        /// <param name="rows">The rows to judge.</param>
        /// <param name="kinds">The vocabulary they are judged against.</param>
        /// <returns>What the two disagree about.</returns>
        internal static Report Check(IReadOnlyList<Row> rows, IReadOnlyList<string> kinds)
        {
            var named = rows.Select(row => row.Kind).ToList();
            var vocabulary = new HashSet<string>(kinds, StringComparer.Ordinal);

            var missing = kinds
                .Where(kind => !named.Contains(kind, StringComparer.Ordinal))
                .ToList();

            var unknown = named
                .Where(kind => !vocabulary.Contains(kind))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var repeated = named
                .GroupBy(kind => kind, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            return new Report(missing, unknown, repeated);
        }

        /// <summary>
        /// The vocabulary the server itself carries, read off the referenced assembly rather than
        /// out of a list somebody maintains here. Raising the package raises what this is judged
        /// against, which is the point.
        /// </summary>
        /// <returns>Every member of the item kind enumeration.</returns>
        internal static IReadOnlyList<string> ItemKinds() => Enum.GetNames<BaseItemKind>();

        /// <summary>
        /// Reads the rows of the table out of a document. The read is table rows rather than a
        /// parse of the whole file, so prose naming a kind does not count as giving it a row.
        /// </summary>
        /// <param name="text">The document text.</param>
        /// <returns>The rows.</returns>
        internal static IReadOnlyList<Row> Rows(string text) =>
            Regex
                .Matches(text, @"(?m)^\|\s*`(?<kind>[A-Za-z0-9]+)`\s*\|\s*(?<disposition>[a-z]+)\s*\|(?<rule>[^|]*)\|\s*$")
                .Select(match => new Row(
                    match.Groups["kind"].Value,
                    match.Groups["disposition"].Value,
                    match.Groups["rule"].Value.Trim()))
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
        /// Reads the matching document from the tracked tree rather than from a copy in the
        /// output directory, because a copy proves the state of the file on the day it was
        /// copied.
        /// </summary>
        /// <returns>Its text.</returns>
        internal static string Text() =>
            File.ReadAllText(Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), "docs", "matching.md"));

        /// <summary>
        /// Reads one of the two fixtures.
        /// </summary>
        /// <param name="name">The fixture file name.</param>
        /// <returns>Its text.</returns>
        internal static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                "Jellyfin.Plugin.WatchSync.Tests",
                "Matching",
                name));
    }
}
