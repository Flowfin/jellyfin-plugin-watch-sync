using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchSync.Matching;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the operator's guide for an item that did not match to the reasons the sources
/// carry.
///
/// The guide is the answer to the question this plugin will be asked most, and it is only
/// worth trusting if it covers every reason an item can stop at. Two lists of one thing
/// drift, and this pair drifts in the direction that costs an operator an evening: a reason
/// added to the code is a reason nothing tells them what to do about, and they meet it on
/// their own server with a guide that does not admit it exists.
///
/// Three sets are held equal here rather than two. The reasons in the sources, the sections
/// of the guide, and the rows of the table that says when a repair takes effect. A section
/// without a row is a repair nobody is told the timing of, and a row without a section is a
/// timing for a repair nobody described.
/// </summary>
public class UnmatchedGuideTests
{
    /// <summary>
    /// The whole point, run against the guide as it is. Every reason has a section, no
    /// section names something the sources do not carry, and nothing has two sections.
    ///
    /// The set is asserted non-empty first. A regular expression that stopped matching
    /// would otherwise report every reason as missing, which is a red suite naming nine
    /// documentation defects when the defect is one character of a pattern.
    /// </summary>
    [Fact]
    public void TheGuideHasASectionForEveryUnmatchedReasonExactlyOnce()
    {
        var sections = UnmatchedGuide.Sections(UnmatchedGuide.Text());

        Assert.NotEmpty(sections);

        var report = UnmatchedGuide.Check(
            sections,
            UnmatchedGuide.Vocabulary());

        Assert.Empty(report.Missing.Select(reason =>
            $"{reason} is a reason the sources carry and the guide has no section for, so an operator meeting it is told nothing."));

        Assert.Empty(report.Unknown.Select(reason =>
            $"{reason} has a section and is not a reason the sources carry, so the section is about nothing."));

        Assert.Empty(report.Repeated.Select(reason =>
            $"{reason} has more than one section, so which of them an operator should follow is undefined."));
    }

    /// <summary>
    /// The same closure over the table that says when a repair takes effect. It is a
    /// separate leg because the two halves of the document are edited separately, and a
    /// section added without its row is the way this file will actually go wrong.
    /// </summary>
    [Fact]
    public void TheEffectTableNamesEveryUnmatchedReasonExactlyOnce()
    {
        var report = UnmatchedGuide.Check(
            UnmatchedGuide.Rows(UnmatchedGuide.Text()).Select(row => row.Reason).ToList(),
            UnmatchedGuide.Vocabulary());

        Assert.Empty(report.Missing.Select(reason =>
            $"{reason} has no row, so nothing says when its repair takes effect."));

        Assert.Empty(report.Unknown.Select(reason =>
            $"{reason} has a row and is not a reason the sources carry."));

        Assert.Empty(report.Repeated.Select(reason =>
            $"{reason} has more than one row, and the two can disagree."));
    }

    /// <summary>
    /// The effect column is a closed set, and the set is read out of the document rather
    /// than restated here. A row carrying a word the document never declared is a timing
    /// somebody decided in the table and explained nowhere.
    /// </summary>
    [Fact]
    public void EveryRowCarriesAnEffectTheDocumentDeclares()
    {
        var text = UnmatchedGuide.Text();
        var declared = UnmatchedGuide.DeclaredEffects(text);
        var rows = UnmatchedGuide.Rows(text);

        Assert.NotEmpty(declared);
        Assert.NotEmpty(rows);

        Assert.Empty(rows
            .Where(row => !declared.Contains(row.Effect, StringComparer.Ordinal))
            .Select(row => $"{row.Reason} carries the effect {row.Effect}, which the document does not declare."));
    }

    /// <summary>
    /// The other direction of the same closure. An effect the document declares and no row
    /// uses is a rule removed from the table and left in the prose, which is where a reader
    /// looks first.
    /// </summary>
    [Fact]
    public void NoDeclaredEffectHasOutlivedTheRowsThatUsedIt()
    {
        var text = UnmatchedGuide.Text();
        var used = UnmatchedGuide.Rows(text).Select(row => row.Effect).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(UnmatchedGuide
            .DeclaredEffects(text)
            .Where(effect => !used.Contains(effect))
            .Select(effect => $"{effect} is declared and no row uses it."));
    }

    /// <summary>
    /// A row with an empty repair column is a reason the table claims to answer and answers
    /// nothing for, which is worse than leaving it out: an operator reading the table would
    /// believe a repair exists and stop looking.
    /// </summary>
    [Fact]
    public void EveryRowNamesARepair()
    {
        var rows = UnmatchedGuide.Rows(UnmatchedGuide.Text());

        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.False(
            string.IsNullOrWhiteSpace(row.Repair),
            $"{row.Reason} has a row and names no repair."));
    }

    /// <summary>
    /// The guard proven by deleting it, on the mistake a document of nine similar names
    /// actually produces. The near-miss misspells one reason by one character in its
    /// heading, so the section is present, its prose is right and its row is right, and the
    /// reason the heading claims to be about does not exist. The repair is that one
    /// character and nothing else.
    ///
    /// The fixture carries its own vocabulary. A fixture judged against the real
    /// enumerations would prove the state of the tree on the day it ran rather than proving
    /// the guard.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheNearMissAndPassesItsRepair()
    {
        var vocabulary = new[] { "NoIdentifierAtAll", "NoEpisodeNumber", "Ambiguous" };

        var refused = UnmatchedGuide.Check(
            UnmatchedGuide.Sections(UnmatchedGuide.Fixture("near-miss.txt")),
            vocabulary);

        Assert.Equal("NoEpisodeNumber", Assert.Single(refused.Missing));
        Assert.Equal("NoEpisodeNumbers", Assert.Single(refused.Unknown));
        Assert.Empty(refused.Repeated);

        var repaired = UnmatchedGuide.Check(
            UnmatchedGuide.Sections(UnmatchedGuide.Fixture("near-miss-repaired.txt")),
            vocabulary);

        Assert.Empty(repaired.Missing);
        Assert.Empty(repaired.Unknown);
        Assert.Empty(repaired.Repeated);
    }

    /// <summary>
    /// The near-miss read the other way. Its table is correct while its headings are not,
    /// so the row leg passes on the same file the section leg refuses, which is what says
    /// the two legs are about different halves of the document rather than one thing
    /// counted twice.
    /// </summary>
    [Fact]
    public void TheRowsOfTheNearMissArePassedByTheLegThatIsNotAboutThem()
    {
        var report = UnmatchedGuide.Check(
            UnmatchedGuide.Rows(UnmatchedGuide.Fixture("near-miss.txt")).Select(row => row.Reason).ToList(),
            new[] { "NoIdentifierAtAll", "NoEpisodeNumber", "Ambiguous" });

        Assert.Empty(report.Missing);
        Assert.Empty(report.Unknown);
        Assert.Empty(report.Repeated);
    }

    /// <summary>
    /// A reason named twice is the other way a hand-maintained document goes wrong, and the
    /// two sections can give different advice. This drives the leg on the fixture, because
    /// the real guide has no repeat and a leg exercised only by the tree stops being
    /// exercised the moment the tree is right.
    /// </summary>
    [Fact]
    public void ARepeatedReasonIsRefused()
    {
        var sections = UnmatchedGuide.Sections(UnmatchedGuide.Fixture("near-miss-repaired.txt"));

        var report = UnmatchedGuide.Check(
            sections.Concat(new[] { sections[0] }).ToList(),
            new[] { "NoIdentifierAtAll", "NoEpisodeNumber", "Ambiguous" });

        Assert.Equal("NoIdentifierAtAll", Assert.Single(report.Repeated));
    }

    /// <summary>
    /// Both reads survive a checkout whose lines end in a carriage return.
    ///
    /// This is the mistake the first version of the guard made. The heading pattern ended
    /// in a run of spaces and tabs before the line end, which matches nothing on a Windows
    /// checkout, so the read returned no section at all and the suite reported every reason
    /// as undocumented. The line ending is a property of the machine rather than of the
    /// file, so it is varied here rather than carried in a fixture the repository would
    /// normalise on the way in.
    /// </summary>
    [Fact]
    public void BothReadsSurviveACarriageReturnAtTheEndOfEveryLine()
    {
        var text = UnmatchedGuide.Text().Replace("\r\n", "\n", StringComparison.Ordinal);
        var carriageReturned = text.Replace("\n", "\r\n", StringComparison.Ordinal);

        Assert.Equal(
            UnmatchedGuide.Sections(text),
            UnmatchedGuide.Sections(carriageReturned));

        Assert.Equal(
            UnmatchedGuide.Rows(text).Select(row => row.Reason),
            UnmatchedGuide.Rows(carriageReturned).Select(row => row.Reason));

        Assert.NotEmpty(UnmatchedGuide.Sections(carriageReturned));
    }

    /// <summary>
    /// The vocabulary is derived rather than listed, so this asserts what it is derived
    /// from. The two members that are not a reason are the two that mean nothing went
    /// wrong, and a member added to either enumeration joins the set without anybody
    /// editing this file.
    /// </summary>
    [Fact]
    public void TheVocabularyIsEveryRefusalAndEveryAnswerThatIsNotAMatch()
    {
        var vocabulary = UnmatchedGuide.Vocabulary();

        Assert.DoesNotContain(nameof(MatchKeyRefusal.None), vocabulary);
        Assert.DoesNotContain(nameof(MatchAnswer.Matched), vocabulary);
        Assert.Contains(nameof(MatchKeyRefusal.NoIdentifierAtAll), vocabulary);
        Assert.Contains(nameof(MatchAnswer.NoMatch), vocabulary);

        Assert.Equal(
            Enum.GetNames<MatchKeyRefusal>().Length + Enum.GetNames<MatchAnswer>().Length - 2,
            vocabulary.Count);
    }

    internal static class UnmatchedGuide
    {
        /// <summary>
        /// A row of the effect table: the reason it is about, when a repair takes effect,
        /// and what the repair is.
        /// </summary>
        /// <param name="Reason">The reason the row names.</param>
        /// <param name="Effect">The effect column.</param>
        /// <param name="Repair">What the operator does.</param>
        internal sealed record Row(string Reason, string Effect, string Repair);

        /// <summary>
        /// What a set of names and a vocabulary disagree about.
        /// </summary>
        /// <param name="Missing">Reasons the vocabulary has and the names do not carry.</param>
        /// <param name="Unknown">Names that are not in the vocabulary.</param>
        /// <param name="Repeated">Names carried more than once.</param>
        internal sealed record Report(
            IReadOnlyList<string> Missing,
            IReadOnlyList<string> Unknown,
            IReadOnlyList<string> Repeated);

        /// <summary>
        /// Compares a set of names against a vocabulary. Pure, so the fixtures run through
        /// the same code the document does rather than through a second implementation of
        /// it.
        /// </summary>
        /// <param name="named">The names to judge.</param>
        /// <param name="vocabulary">The vocabulary they are judged against.</param>
        /// <returns>What the two disagree about.</returns>
        internal static Report Check(IReadOnlyList<string> named, IReadOnlyList<string> vocabulary)
        {
            var known = new HashSet<string>(vocabulary, StringComparer.Ordinal);

            var missing = vocabulary
                .Where(reason => !named.Contains(reason, StringComparer.Ordinal))
                .ToList();

            var unknown = named
                .Where(reason => !known.Contains(reason))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var repeated = named
                .GroupBy(reason => reason, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            return new Report(missing, unknown, repeated);
        }

        /// <summary>
        /// The reasons an item can stop at, read off the two enumerations that carry them
        /// rather than out of a list somebody maintains here. A member added to either one
        /// joins this set, which is what makes the check a check.
        /// </summary>
        /// <returns>Every refusal and every answer that is not a match.</returns>
        internal static IReadOnlyList<string> Vocabulary() =>
            Enum.GetNames<MatchKeyRefusal>()
                .Where(name => !string.Equals(name, nameof(MatchKeyRefusal.None), StringComparison.Ordinal))
                .Concat(Enum.GetNames<MatchAnswer>()
                    .Where(name => !string.Equals(name, nameof(MatchAnswer.Matched), StringComparison.Ordinal)))
                .ToList();

        /// <summary>
        /// Reads the reason sections out of a document. A heading that is exactly a code
        /// span names a reason, which the document states as a rule about itself, so prose
        /// mentioning a reason does not count as giving it a section.
        /// </summary>
        /// <param name="text">The document text.</param>
        /// <returns>The reasons the headings name, in the order they appear.</returns>
        internal static IReadOnlyList<string> Sections(string text) =>
            Regex
                .Matches(text, @"(?m)^###[ \t]+`(?<reason>[A-Za-z0-9]+)`\s*$")
                .Select(match => match.Groups["reason"].Value)
                .ToList();

        /// <summary>
        /// Reads the rows of the effect table. The read is table rows rather than a parse of
        /// the whole file, for the same reason the section read is anchored on a heading.
        /// </summary>
        /// <param name="text">The document text.</param>
        /// <returns>The rows.</returns>
        internal static IReadOnlyList<Row> Rows(string text) =>
            Regex
                .Matches(text, @"(?m)^\|\s*`(?<reason>[A-Za-z0-9]+)`\s*\|\s*(?<effect>[a-z]+)\s*\|(?<repair>[^|]*)\|\s*$")
                .Select(match => new Row(
                    match.Groups["reason"].Value,
                    match.Groups["effect"].Value,
                    match.Groups["repair"].Value.Trim()))
                .ToList();

        /// <summary>
        /// Reads the effects the document declares, which are the leading code spans of the
        /// list that introduces them. Reading them rather than restating them here is what
        /// makes this a check on the document instead of a second copy of it.
        /// </summary>
        /// <param name="text">The document text.</param>
        /// <returns>The declared effects.</returns>
        internal static IReadOnlyList<string> DeclaredEffects(string text) =>
            Regex
                .Matches(text, "(?m)^- `(?<effect>[a-z]+)`[,.]")
                .Select(match => match.Groups["effect"].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

        /// <summary>
        /// Reads the guide from the tracked tree rather than from a copy in the output
        /// directory, because a copy proves the state of the file on the day it was copied.
        /// </summary>
        /// <returns>Its text.</returns>
        internal static string Text() =>
            File.ReadAllText(Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), "docs", "unmatched.md"));

        /// <summary>
        /// Reads one of the two fixtures.
        /// </summary>
        /// <param name="name">The fixture file name.</param>
        /// <returns>Its text.</returns>
        internal static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                "Jellyfin.Plugin.WatchSync.Tests",
                "Unmatched",
                name));
    }
}
