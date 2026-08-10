using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds `docs/logging.md` to the rules a machine actually refuses.
///
/// The document states what may be logged, what may never be logged, and which half of that is
/// carried by a pattern and which half is carried by a reading. The second sentence is the one
/// worth guarding: a document that says a machine refuses something it does not is worse than one
/// that says nothing, because a reader stops looking. So every claim of a rule in the document is
/// held to the invariant vocabulary in both directions, and a row that claims no rule has to say
/// what it waits on instead.
///
/// The vocabulary is read rather than restated. Two lists of the same rules drift, and the one
/// that drifts is always the one nothing runs.
/// </summary>
public class LoggingDocumentTests
{
    /// <summary>
    /// Every rule the document claims is a rule the guard carries, and every rule the guard
    /// carries for this invariant has a row in the document. Either direction failing is a
    /// document and a guard that have come apart.
    /// </summary>
    [Fact]
    public void TheDocumentAndTheVocabularyNameTheSameRules()
    {
        var drift = LoggingDocument.Compare(LoggingDocument.RulesTheDocumentClaims(), LoggingDocument.RulesTheGuardCarries());

        Assert.Empty(drift.Unheld);
        Assert.Empty(drift.Unclaimed);
    }

    /// <summary>
    /// One rule holds one row, and this is the leg the set comparison above cannot make. The way
    /// this document goes wrong is not a rule invented out of nothing; it is a row that nothing
    /// scans being handed the rule from the row above it, because the two look similar and the
    /// name is to hand. The sets stay equal through that edit and the disclosure stops being true,
    /// so the count is asserted rather than the membership.
    /// </summary>
    [Fact]
    public void NoRuleIsClaimedByTwoRows()
    {
        var claimed = LoggingDocument.RulesTheDocumentClaims();

        Assert.Empty(claimed.GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} is claimed by {group.Count()} rows, so at most one of them is disclosed correctly."));
    }

    /// <summary>
    /// The two tables in the document say the same thing. One lists what may never be logged and
    /// what holds it, the other lists the rules and what a matching call does, and a rule reaching
    /// one table without the other is a refusal whose reason or whose subject is missing.
    /// </summary>
    [Fact]
    public void BothTablesInTheDocumentNameTheSameRules()
    {
        var claimed = LoggingDocument.RulesTheDocumentClaims();
        var explained = LoggingDocument.RulesTheDocumentExplains();

        Assert.NotEmpty(explained);
        Assert.Empty(claimed.Except(explained, StringComparer.Ordinal));
        Assert.Empty(explained.Except(claimed, StringComparer.Ordinal));
    }

    /// <summary>
    /// A row that names no rule is a negative disclosure, and it stays negative. It has to say
    /// what holds it instead and name the issue that would change that, so the reason it is
    /// unscanned is one link away rather than a blank the next editor fills in with a claim.
    /// </summary>
    [Fact]
    public void ARowThatNamesNoRuleSaysWhatItWaitsOn()
    {
        var rows = LoggingDocument.NeverLoggedRows();

        Assert.NotEmpty(rows);

        Assert.All(rows, row =>
        {
            if (LoggingDocument.RuleIdsIn(row.HeldBy).Count == 0)
            {
                Assert.Matches("#[0-9]+", row.HeldBy);
            }
        });
    }

    /// <summary>
    /// The permission the document grants is the one the guard leaves alone. The ordinary level
    /// allows the match key, and the rules that refuse a title and a provider identifier have to
    /// pass a call carrying it, or the document permits something the suite refuses and the first
    /// person to write the line finds out.
    /// </summary>
    [Fact]
    public void TheOrdinaryLevelTheDocumentPermitsIsNotRefusedByAnyRuleItNames()
    {
        var permitted = "_logger.LogInformation(\"Applied a played state to {Item} for {User}\", matchKey, userId);";

        Assert.All(LoggingDocument.Rules(), rule => Assert.False(
            rule.Pattern.IsMatch(permitted),
            $"{rule.Id} refuses the ordinary-level call docs/logging.md permits."));
    }

    /// <summary>
    /// The closure proven to bite, in both of the directions it claims, on hand-made sets rather
    /// than by editing the tracked document. A rule claimed with nothing carrying it is reported,
    /// and a rule carried with nothing claiming it is reported, and neither is inferred from the
    /// other.
    /// </summary>
    [Fact]
    public void ARuleClaimedByNothingFailsAndSoDoesARuleCarriedByNothing()
    {
        var unheld = LoggingDocument.Compare(new[] { "log-item-title", "log-invented" }, new[] { "log-item-title" });

        Assert.Single(unheld.Unheld);
        Assert.Empty(unheld.Unclaimed);

        var unclaimed = LoggingDocument.Compare(new[] { "log-item-title" }, new[] { "log-item-title", "log-provider-identifier" });

        Assert.Empty(unclaimed.Unheld);
        Assert.Single(unclaimed.Unclaimed);
    }

    /// <summary>
    /// Reads `docs/logging.md` and the rules it is held against.
    ///
    /// The document is read from the tracked file rather than from a copy in the output directory,
    /// for the reason the other document guards here read theirs that way: a copy proves the state
    /// of the file on the day it was copied.
    /// </summary>
    internal static class LoggingDocument
    {
        /// <summary>
        /// The invariant whose rules this document is about.
        /// </summary>
        internal const string Invariant = "log-holds-no-viewing";

        private const string NeverSection = "## What may never be logged, at any level";

        private const string RuleSection = "## What a machine refuses";

        internal sealed record Row(string Subject, string HeldBy);

        internal sealed record Drift(IReadOnlyList<string> Unheld, IReadOnlyList<string> Unclaimed);

        /// <summary>
        /// The rules the guard carries for this invariant, read from the vocabulary.
        /// </summary>
        /// <returns>The rule identifiers.</returns>
        internal static IReadOnlyList<string> RulesTheGuardCarries() =>
            Rules().Select(rule => rule.Id).ToList();

        /// <summary>
        /// The rules themselves, for the tests that drive a pattern rather than compare a name.
        /// </summary>
        /// <returns>The rules of this invariant.</returns>
        internal static IReadOnlyList<InvariantGuardTests.InvariantGuard.Rule> Rules() =>
            InvariantGuardTests.InvariantGuard.Vocabulary()
                .Where(rule => string.Equals(rule.Invariant, Invariant, StringComparison.Ordinal))
                .ToList();

        /// <summary>
        /// The rules the document claims are held by a machine, taken from the right-hand column
        /// of the table of what may never be logged.
        /// </summary>
        /// <returns>The rule identifiers the document claims.</returns>
        internal static IReadOnlyList<string> RulesTheDocumentClaims() =>
            NeverLoggedRows().SelectMany(row => RuleIdsIn(row.HeldBy)).ToList();

        /// <summary>
        /// The rules the document explains, taken from the left-hand column of the table of what a
        /// machine refuses.
        /// </summary>
        /// <returns>The rule identifiers the document explains.</returns>
        internal static IReadOnlyList<string> RulesTheDocumentExplains() =>
            TableRows(RuleSection).Select(cells => cells[0]).SelectMany(RuleIdsIn).ToList();

        /// <summary>
        /// The rows of the table of what may never be logged.
        /// </summary>
        /// <returns>One row per thing that may never be logged.</returns>
        internal static IReadOnlyList<Row> NeverLoggedRows() =>
            TableRows(NeverSection).Select(cells => new Row(cells[0], cells[1])).ToList();

        /// <summary>
        /// The rule identifiers written in a cell, which are the backticked lower-case words.
        /// </summary>
        /// <param name="cell">The cell text.</param>
        /// <returns>The identifiers it names.</returns>
        internal static IReadOnlyList<string> RuleIdsIn(string cell) =>
            Regex.Matches(cell, "`(?<id>[a-z0-9-]+)`")
                .Select(match => match.Groups["id"].Value)
                .ToList();

        /// <summary>
        /// Compares what the document claims against what the guard carries, in both directions.
        /// </summary>
        /// <param name="claimed">The rules the document claims.</param>
        /// <param name="carried">The rules the guard carries.</param>
        /// <returns>What each side holds and the other does not.</returns>
        internal static Drift Compare(IEnumerable<string> claimed, IEnumerable<string> carried)
        {
            var claimedSet = claimed.ToList();
            var carriedSet = carried.ToList();

            return new Drift(
                claimedSet.Except(carriedSet, StringComparer.Ordinal).ToList(),
                carriedSet.Except(claimedSet, StringComparer.Ordinal).ToList());
        }

        private static IReadOnlyList<string[]> TableRows(string heading)
        {
            var document = File.ReadAllText(
                Path.Combine(InvariantGuardTests.InvariantGuard.RepositoryRoot(), "docs", "logging.md"));

            var start = document.IndexOf(heading, StringComparison.Ordinal);

            Assert.True(start >= 0, $"docs/logging.md carries no section headed \"{heading}\", so there is nothing to read out of it.");

            var rest = document[(start + heading.Length)..];
            var end = rest.IndexOf("\n## ", StringComparison.Ordinal);
            var section = end < 0 ? rest : rest[..end];

            var rows = new List<string[]>();

            foreach (var line in section.Split('\n'))
            {
                var trimmed = line.Trim();

                if (!trimmed.StartsWith('|') || trimmed.Contains("---", StringComparison.Ordinal))
                {
                    continue;
                }

                var cells = trimmed.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();

                if (cells.Length < 2 || cells[0].StartsWith("what ", StringComparison.Ordinal) || string.Equals(cells[0], "rule", StringComparison.Ordinal))
                {
                    continue;
                }

                rows.Add(cells);
            }

            Assert.NotEmpty(rows);

            return rows;
        }
    }
}
