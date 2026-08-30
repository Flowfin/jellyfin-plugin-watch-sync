using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchSync.Configuration;
using Jellyfin.Plugin.WatchSync.Records;
using Jellyfin.Plugin.WatchSync.Storage;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the note a person is handed about their own data, which is #107.
///
/// The note is a summary of three documents and of the sources under them, and a summary is the
/// shape that goes stale without anybody noticing: the thing it summarises moves, the summary
/// does not, and the reader who was handed the summary is the one least able to tell. So the two
/// tables in it are closed against what they describe, in both directions, and the references to
/// the documents it summarises are refused being deleted.
///
/// What is not held is whether the sentences say what they mean. That is a judgement about
/// meaning and no reading of this tree makes one, which the note states about itself rather than
/// leaving a reader to assume the whole of it is machine-kept.
/// </summary>
public class PrivacyNoteTests
{
    /// <summary>
    /// Where the note lives, relative to the repository root.
    /// </summary>
    private const string Document = "docs/privacy.md";

    /// <summary>
    /// What the note says where the sync model says a field moves.
    /// </summary>
    private const string Moves = "moves";

    /// <summary>
    /// What the note says where the sync model says a field never leaves the server it is on.
    /// </summary>
    private const string DoesNotMove = "does not move";

    /// <summary>
    /// Every property the sync model gives a row is in the note, with the disposition that
    /// document declares for it.
    ///
    /// This is the direction that costs somebody something. A field that moves and is missing
    /// here, or is here as one that stays behind, is a field a person was told the wrong thing
    /// about in the one document written for them to read.
    /// </summary>
    [Fact]
    public void EveryFieldTheSyncModelDeclaresIsNamedHereWithTheSameDisposition()
    {
        var declared = PrivacyNote.SyncModelDispositions();
        var named = PrivacyNote.Dispositions(PrivacyNote.Text());

        Assert.NotEmpty(declared);
        Assert.NotEmpty(named);

        Assert.Empty(declared
            .Where(pair => !named.TryGetValue(pair.Key, out var said) || said != pair.Value)
            .Select(pair => named.TryGetValue(pair.Key, out var said)
                ? $"docs/sync-model.md declares {pair.Key} as one that {pair.Value} and {Document} says it {said}."
                : $"docs/sync-model.md declares {pair.Key} and {Document} gives it no row, so a person reading the note was shown a list that was short by one field."));
    }

    /// <summary>
    /// The other direction. A row for a property the sync model does not declare is a statement
    /// about a field of the server's record that does not exist, and the reader has no way to
    /// find that out.
    /// </summary>
    [Fact]
    public void NoRowNamesAPropertyTheSyncModelDoesNotDeclare()
    {
        var declared = PrivacyNote.SyncModelDispositions();

        Assert.Empty(PrivacyNote.Dispositions(PrivacyNote.Text())
            .Where(pair => !declared.ContainsKey(pair.Key))
            .Select(pair => $"{Document} gives {pair.Key} a row and docs/sync-model.md declares no field by that name."));
    }

    /// <summary>
    /// Every kind of document the store holds has a row saying what it holds about a person and
    /// how long it is kept.
    ///
    /// Driven from <see cref="StoredKinds.All"/> rather than from a list here, for the reason
    /// that declaration was written for: a fifth kind added and not written down is a thing held
    /// about somebody that the note answering "what do you hold about me" does not mention.
    /// </summary>
    [Fact]
    public void EveryKindTheStoreHoldsHasARowInTheNote()
    {
        var rows = PrivacyNote.StoreRows(PrivacyNote.Text());

        Assert.NotEmpty(rows);

        Assert.Empty(StoredKinds.All
            .Where(kind => !rows.Any(row => row.Prefix == kind.NamePrefix))
            .Select(kind => $"The store holds documents named {kind.NamePrefix}, declared by {kind.DeclaredBy.Name}, and {Document} gives them no row."));
    }

    /// <summary>
    /// The other direction. A row for a document the store does not hold describes a thing kept
    /// about a person that is not kept, which is the same defect pointing the other way.
    /// </summary>
    [Fact]
    public void NoRowNamesADocumentTheStoreDoesNotHold()
    {
        Assert.Empty(PrivacyNote.StoreRows(PrivacyNote.Text())
            .Where(row => !StoredKinds.All.Any(kind => kind.NamePrefix == row.Prefix))
            .Select(row => $"{Document} says the store holds documents named {row.Prefix} and StoredKinds.All declares no such kind."));
    }

    /// <summary>
    /// A row that states a retention states the two numbers its own source declares, and names a
    /// setting the configuration type actually carries.
    ///
    /// This is the half of the store table that a reader acts on: somebody deciding whether they
    /// mind a record being kept reads the number, and somebody wanting it shorter reads the
    /// setting name. A default moved in the source and not here is a promise about a retention
    /// nobody is keeping.
    /// </summary>
    /// <param name="prefix">The document name prefix the row is for.</param>
    /// <param name="days">The default retention, in days.</param>
    /// <param name="most">The widest retention the rule accepts, in days.</param>
    /// <param name="setting">The setting that carries it.</param>
    [Theory]
    [MemberData(nameof(Retentions))]
    public void ARetentionRowQuotesTheNumbersAndTheSettingItsSourceDeclares(string prefix, int days, int most, string setting)
    {
        var row = PrivacyNote.StoreRows(PrivacyNote.Text()).SingleOrDefault(candidate => candidate.Prefix == prefix);

        Assert.NotNull(row);
        Assert.Equal(setting, row!.Setting);
        Assert.Contains($"{days} days by default", row.Kept, StringComparison.Ordinal);
        Assert.Contains($"at most {most}", row.Kept, StringComparison.Ordinal);
    }

    /// <summary>
    /// The note still points at each document it is a summary of.
    ///
    /// A summary that loses its reference becomes the authority for a rule it only paraphrases,
    /// and the paraphrase is the shorter of the two. This refuses the deletion and refuses
    /// nothing about a rewrite, which is a weaker fact than the tables above and is why the bound
    /// is written here rather than left to be assumed.
    /// </summary>
    /// <param name="reference">The link the note carries.</param>
    [Theory]
    [InlineData("(sync-model.md)")]
    [InlineData("(opt-out.md)")]
    [InlineData("(logging.md)")]
    public void TheNoteStillPointsAtTheDocumentItSummarises(string reference)
    {
        Assert.Contains(reference, PrivacyNote.Text(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets the retention each record type declares, with the setting that carries it.
    ///
    /// The numbers are read off the types rather than written out, so this data set moves with
    /// the sources and the fact above compares the note against the tree rather than against a
    /// second copy of the note.
    /// </summary>
    public static TheoryData<string, int, int, string> Retentions()
    {
        var data = new TheoryData<string, int, int, string>
        {
            {
                PrivacyNote.PrefixOf(typeof(ConflictRecords)),
                (int)ConflictRecords.DefaultRetention.TotalDays,
                (int)ConflictRecords.MaximumRetention.TotalDays,
                nameof(PluginConfiguration.ConflictRetentionDays)
            },
            {
                PrivacyNote.PrefixOf(typeof(ProvenanceRecords)),
                (int)ProvenanceRecords.DefaultRetention.TotalDays,
                (int)ProvenanceRecords.MaximumRetention.TotalDays,
                nameof(PluginConfiguration.ProvenanceRetentionDays)
            },
        };

        return data;
    }

    /// <summary>
    /// Reads the note and the document it is a summary of.
    /// </summary>
    internal static class PrivacyNote
    {
        /// <summary>
        /// A row of the field table: the property, and whether the note says it moves.
        ///
        /// The disposition is more than one word here where the sync model spells it in one, so
        /// the pattern admits spaces and the two vocabularies are joined by the constants above
        /// rather than by looking alike.
        /// </summary>
        private static readonly Regex FieldRow = new(
            @"^\|\s*`(?<property>[A-Za-z0-9]+)`\s*\|\s*(?<disposition>[a-z][a-z ]*[a-z])\s*\|(?<what>[^|]*)\|\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>
        /// A row of the store table: the name prefix, what it holds, how long it is kept, and
        /// the setting that decides.
        ///
        /// The last column admits a setting name in backticks or the word for none, and nothing
        /// else. A row whose fourth column is a sentence would otherwise read as a setting whose
        /// name is that sentence, and the fact below would compare against it and pass on the
        /// rows it was written for while saying nothing about that one.
        /// </summary>
        private static readonly Regex StoreRow = new(
            @"^\|\s*`(?<prefix>[a-z]+-)`\s*\|(?<holds>[^|]*)\|(?<kept>[^|]*)\|\s*(?:`(?<setting>[A-Za-z]+)`|none)\s*\|\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>
        /// A row of the sync model's field table.
        /// </summary>
        private static readonly Regex SyncModelRow = new(
            @"^\|\s*`(?<property>[A-Za-z0-9]+)`\s*\|\s*(?<disposition>[a-z]+)\s*\|(?<why>[^|]*)\|\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>
        /// What the note says about each property it gives a row.
        ///
        /// The read is table rows rather than a search over the document, so prose naming a
        /// property does not count as giving it a row. The row ends on optional whitespace
        /// rather than on the bar, because the checkout carries a carriage return before the
        /// newline on one of the three platforms the suite runs on and a table read as empty is
        /// a document that names no field at all.
        /// </summary>
        /// <param name="text">The note.</param>
        /// <returns>The disposition per property.</returns>
        internal static IReadOnlyDictionary<string, string> Dispositions(string text) =>
            FieldRow
                .Matches(text)
                .ToDictionary(
                    match => match.Groups["property"].Value,
                    match => match.Groups["disposition"].Value,
                    StringComparer.Ordinal);

        /// <summary>
        /// What the sync model declares about each property, in the note's own vocabulary, so the
        /// comparison is between two answers rather than between two spellings.
        /// </summary>
        /// <returns>The disposition per property.</returns>
        internal static IReadOnlyDictionary<string, string> SyncModelDispositions() =>
            SyncModelRow
                .Matches(SyncModelText())
                .ToDictionary(
                    match => match.Groups["property"].Value,
                    match => match.Groups["disposition"].Value == "moved" ? Moves : DoesNotMove,
                    StringComparer.Ordinal);

        /// <summary>
        /// The rows of the store table.
        /// </summary>
        /// <param name="text">The note.</param>
        /// <returns>The rows.</returns>
        internal static IReadOnlyList<StoreTableRow> StoreRows(string text) =>
            StoreRow
                .Matches(text)
                .Select(match => new StoreTableRow(
                    match.Groups["prefix"].Value,
                    match.Groups["holds"].Value.Trim(),
                    match.Groups["kept"].Value.Trim(),
                    match.Groups["setting"].Value.Trim()))
                .ToList();

        /// <summary>
        /// The name prefix the store gives the documents one type writes.
        /// </summary>
        /// <param name="declaredBy">The type that reads and writes them.</param>
        /// <returns>The prefix.</returns>
        internal static string PrefixOf(Type declaredBy) =>
            StoredKinds.All.Single(kind => kind.DeclaredBy == declaredBy).NamePrefix;

        /// <summary>
        /// The note as it stands in the tree.
        /// </summary>
        /// <returns>The text.</returns>
        internal static string Text() =>
            File.ReadAllText(Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                "docs",
                "privacy.md"));

        /// <summary>
        /// The sync model as it stands in the tree.
        /// </summary>
        /// <returns>The text.</returns>
        internal static string SyncModelText() =>
            File.ReadAllText(Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                "docs",
                "sync-model.md"));
    }

    /// <summary>
    /// A row of the store table in the note.
    /// </summary>
    /// <param name="Prefix">The name prefix the documents carry.</param>
    /// <param name="Holds">What the row says the document holds about a person.</param>
    /// <param name="Kept">What the row says about how long it is kept.</param>
    /// <param name="Setting">The setting the row names, empty where the row names none.</param>
    internal sealed record StoreTableRow(string Prefix, string Holds, string Kept, string Setting);
}
