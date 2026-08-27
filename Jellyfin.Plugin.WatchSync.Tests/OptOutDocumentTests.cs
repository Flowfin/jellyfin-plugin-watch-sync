using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchSync.Model;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the wording a person is offered the opt-out in, which is the fourth condition of #60.
///
/// One thing here is decidable and one is not, and the document says which is which rather than
/// leaving a reader to assume the whole of it is held. What a machine can read is the table of
/// fields the choice stops: it has to be exactly the moved set, in both directions, because a
/// field added to what moves and not named there is a field somebody was not told about when
/// they were asked to decide, and a row for a field that does not move is a promise to stop
/// something that was never happening.
///
/// Whether three sentences of prose say what the rules above them say they say is a judgement
/// about meaning, and nothing here makes one. The near miss this set is written against is the
/// one the drift actually takes, which is the table falling behind the moved set rather than
/// somebody rewriting the sentences.
/// </summary>
public class OptOutDocumentTests
{
    /// <summary>
    /// Where the wording lives, relative to the repository root.
    /// </summary>
    private const string Document = "docs/opt-out.md";

    /// <summary>
    /// Every field this plugin moves is named in the table a person reads before they decide.
    ///
    /// This is the direction that costs somebody something. A field that moves and is not in the
    /// table is one a person has not been told about, and they made their choice against a list
    /// that was short.
    /// </summary>
    [Fact]
    public void EveryMovedFieldIsNamedInTheTableThePersonDecidesAgainst()
    {
        var named = OptOutDocument.NamedFields(OptOutDocument.Text());

        Assert.NotEmpty(named);

        Assert.Empty(Enum.GetNames<SyncedField>()
            .Where(field => !named.Contains(field, StringComparer.Ordinal))
            .Select(field =>
                $"{field} moves and {Document} does not name it, so somebody choosing to stop their history moving was shown a list that was short by one field."));
    }

    /// <summary>
    /// The other direction. A row for something that does not move is a promise to stop
    /// something that was never happening, which is the kind of sentence a person later finds
    /// out was not true.
    /// </summary>
    [Fact]
    public void NoRowNamesSomethingThatDoesNotMove()
    {
        var moved = Enum.GetNames<SyncedField>();

        Assert.Empty(OptOutDocument.NamedFields(OptOutDocument.Text())
            .Where(field => !moved.Contains(field, StringComparer.Ordinal))
            .Select(field =>
                $"{Document} says the choice stops {field} and nothing by that name moves, so the wording promises to stop something that was never happening."));
    }

    /// <summary>
    /// The two claims the wording exists to carry, present as claims rather than judged as
    /// prose. The first is that what already moved is not taken back, and the second is that
    /// the choice is honoured in both directions.
    ///
    /// This is a weaker fact than the two above and is worth having anyway: it refuses the
    /// deletion of either sentence, which is how a wording is shortened, and it refuses nothing
    /// about a rewrite that keeps the words and changes what they mean. That bound is written
    /// here rather than left for a reader to find out by trusting the fact.
    /// </summary>
    [Theory]
    [InlineData("This does not delete anything")]
    [InlineData("in both directions")]
    public void TheWordingStillCarriesTheClaimItIsWrittenAround(string claim)
    {
        Assert.Contains(claim, OptOutDocument.Text(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the wording document.
    /// </summary>
    internal static class OptOutDocument
    {
        /// <summary>
        /// The fields the table names, read as table rows rather than as a search over the whole
        /// document, so prose mentioning a field does not count as giving it a row.
        ///
        /// The row ends on optional whitespace rather than on the bar itself, which is the same
        /// spelling <c>SyncModelDocumentTests</c> uses and is not a stylistic echo of it. The
        /// checkout carries a carriage return before the newline on one of the three platforms
        /// the suite runs on, so a pattern anchored straight after the bar matches every row on
        /// two platforms and no row on the third, and a table read as empty is a document that
        /// names no field at all.
        /// </summary>
        /// <param name="text">The document text.</param>
        /// <returns>The field named by each row.</returns>
        internal static IReadOnlyList<string> NamedFields(string text) =>
            Regex
                .Matches(
                    text,
                    @"^\|\s*`(?<field>[A-Za-z]+)`\s*\|(?<what>[^|]+)\|\s*$",
                    RegexOptions.Multiline,
                    TimeSpan.FromSeconds(5))
                .Select(match => match.Groups["field"].Value)
                .ToList();

        /// <summary>
        /// The document as it stands in the tree.
        /// </summary>
        /// <returns>The text.</returns>
        internal static string Text() =>
            File.ReadAllText(Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                "docs",
                "opt-out.md"));
    }
}
