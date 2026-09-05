using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the disposition register in <c>docs/code-scanning.md</c> to the rule set the security
/// page carries, which is #368's second condition.
///
/// The register is one entry per rule the page has carried, each naming what was decided about
/// the rule and the argument for it. A register somebody maintains by remembering to is a list,
/// and a list drifts in two directions that are invisible from inside it: a rule the page raises
/// with no entry is a class of finding nobody decided, and an entry naming a rule the page has
/// never carried is an argument about nothing that a reader still believes. So the comparison
/// fails in both directions, naming what each side holds that the other does not.
///
/// <para>
/// THE SUITE DOES NOT READ THE PAGE. It is headless and reaches no network, so what it reads is
/// <c>CodeScanning/page-rules.txt</c>, which is the page's rule set as one command returned it,
/// with that command and the day in its header. The register says the same thing where a reader
/// of the register meets it. A rule raised after that file was written is unread by anything here
/// until the command is re-run and its output committed, and this guard is then what reports the
/// entry the register lacks.
/// </para>
///
/// <para>
/// The third condition is the sentence a dismissal quotes. An entry declining a rule carries the
/// one sentence every dismissal of that rule on the page carries as its comment, so the page and
/// the register are two copies of one sentence rather than two paraphrases. A decline with nothing
/// to quote is refused here; whether the page actually carries the sentence is read by hand.
/// </para>
/// </summary>
public class CodeScanningRegisterTests
{
    /// <summary>
    /// A rule the page carries with no entry is a class of finding nobody decided.
    /// </summary>
    [Fact]
    public void EveryRuleThePageCarriesHasAnEntry()
    {
        var drift = CodeScanningRegister.Compare(
            CodeScanningRegister.Entries(CodeScanningRegister.DocumentText()),
            CodeScanningRegister.PageRules(CodeScanningRegister.ReadingText()));

        Assert.True(
            drift.RulesWithoutAnEntry.Count == 0,
            $"The security page carries rules the register in {CodeScanningRegister.Document} has no entry for: {string.Join(", ", drift.RulesWithoutAnEntry)}. Each needs a heading under {CodeScanningRegister.Heading} naming the rule in backticks, with a Disposition line.");
    }

    /// <summary>
    /// An entry naming a rule the page has never carried is an argument about nothing.
    /// </summary>
    [Fact]
    public void EveryEntryNamesARuleThePageCarries()
    {
        var drift = CodeScanningRegister.Compare(
            CodeScanningRegister.Entries(CodeScanningRegister.DocumentText()),
            CodeScanningRegister.PageRules(CodeScanningRegister.ReadingText()));

        Assert.True(
            drift.EntriesWithoutARule.Count == 0,
            $"The register in {CodeScanningRegister.Document} carries entries for rules {CodeScanningRegister.Reading} does not: {string.Join(", ", drift.EntriesWithoutARule)}. Either the page never carried the rule, or the reading is older than the page and its command has to be re-run.");
    }

    /// <summary>
    /// An entry with no disposition is a heading, and a heading decides nothing.
    /// </summary>
    [Fact]
    public void EveryEntryCarriesADisposition()
    {
        var entries = CodeScanningRegister.Entries(CodeScanningRegister.DocumentText());

        Assert.Empty(entries.Where(entry => entry.Disposition is null).Select(entry => entry.Rule));
    }

    /// <summary>
    /// A decline carries the sentence a dismissal on the page quotes, so the two cannot drift into
    /// disagreeing about why an alert stands.
    /// </summary>
    [Fact]
    public void EveryDeclineSaysWhatADismissalQuotes()
    {
        var entries = CodeScanningRegister.Entries(CodeScanningRegister.DocumentText());

        Assert.Empty(CodeScanningRegister.DeclinesWithNothingToQuote(entries).Select(entry => entry.Rule));
    }

    /// <summary>
    /// The reading is a copy of the page and says so: its header carries the command that produced
    /// it, so the next person can re-run it rather than trust it.
    /// </summary>
    [Fact]
    public void TheReadingCarriesTheCommandThatProducedIt()
    {
        var text = CodeScanningRegister.ReadingText();

        Assert.Contains("code-scanning/alerts", text, StringComparison.Ordinal);
        Assert.Contains("gh api", text, StringComparison.Ordinal);
        Assert.NotEmpty(CodeScanningRegister.PageRules(text));
    }

    /// <summary>
    /// The guard proven by the mistake that will actually be made: the page raises a rule and
    /// nobody writes an entry. The repair is the entry.
    /// </summary>
    [Fact]
    public void TheGuardRefusesARuleWithNoEntryAndPassesItsRepair()
    {
        var page = CodeScanningRegister.PageRules(CodeScanningRegister.Fixture("reading-of-three-rules.txt"));

        var mistake = CodeScanningRegister.Compare(
            CodeScanningRegister.Entries(CodeScanningRegister.Fixture("register-of-two-entries.txt")),
            page);

        Assert.Equal(new[] { "cs/gamma" }, mistake.RulesWithoutAnEntry);
        Assert.Empty(mistake.EntriesWithoutARule);

        var repaired = CodeScanningRegister.Compare(
            CodeScanningRegister.Entries(CodeScanningRegister.Fixture("register-of-three-entries.txt")),
            page);

        Assert.Empty(repaired.RulesWithoutAnEntry);
        Assert.Empty(repaired.EntriesWithoutARule);
    }

    /// <summary>
    /// The other direction: an entry survives the rule it was written for. The repair is removing
    /// the entry, or re-running the reading if the page still carries the rule.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAnEntryNamingNoRuleAndPassesItsRepair()
    {
        var page = CodeScanningRegister.PageRules(CodeScanningRegister.Fixture("reading-of-two-rules.txt"));

        var mistake = CodeScanningRegister.Compare(
            CodeScanningRegister.Entries(CodeScanningRegister.Fixture("register-of-three-entries.txt")),
            page);

        Assert.Equal(new[] { "cs/gamma" }, mistake.EntriesWithoutARule);
        Assert.Empty(mistake.RulesWithoutAnEntry);

        var repaired = CodeScanningRegister.Compare(
            CodeScanningRegister.Entries(CodeScanningRegister.Fixture("register-of-two-entries.txt")),
            page);

        Assert.Empty(repaired.EntriesWithoutARule);
        Assert.Empty(repaired.RulesWithoutAnEntry);
    }

    /// <summary>
    /// A heading under a later section is not an entry. Both register fixtures carry one after
    /// the section ends, and the two proofs above pass only because it is not read.
    /// </summary>
    [Fact]
    public void AnEntryUnderALaterSectionIsNotRead()
    {
        var entries = CodeScanningRegister.Entries(CodeScanningRegister.Fixture("register-of-two-entries.txt"));

        Assert.DoesNotContain(entries, entry => string.Equals(entry.Rule, "cs/delta", StringComparison.Ordinal));
    }

    /// <summary>
    /// The one-word mistake: a disposition that declines, with the quote line left out or left
    /// empty. The repair is the sentence.
    /// </summary>
    [Fact]
    public void TheGuardRefusesADeclineWithNothingToQuoteAndPassesItsRepair()
    {
        var mistake = CodeScanningRegister.Entries(CodeScanningRegister.Fixture("a-decline-with-nothing-to-quote-near-miss.txt"));

        Assert.Equal(new[] { "cs/alpha", "cs/beta" }, CodeScanningRegister.DeclinesWithNothingToQuote(mistake).Select(entry => entry.Rule));

        var repaired = CodeScanningRegister.Entries(CodeScanningRegister.Fixture("a-decline-with-nothing-to-quote-near-miss-repaired.txt"));

        Assert.Empty(CodeScanningRegister.DeclinesWithNothingToQuote(repaired));
    }

    /// <summary>
    /// A heading with nothing under it is refused as an entry with no disposition rather than
    /// passed as one.
    /// </summary>
    [Fact]
    public void AnEntryWithNoDispositionLineIsReadAsHavingNone()
    {
        var entries = CodeScanningRegister.Entries(CodeScanningRegister.Fixture("a-decline-with-nothing-to-quote-near-miss.txt"));

        Assert.Contains(entries, entry => string.Equals(entry.Rule, "cs/gamma", StringComparison.Ordinal) && entry.Disposition is null);
    }

    /// <summary>
    /// Reads the register out of the document and the rule set out of the reading, and compares
    /// them. Anchored on headings and on two labelled lines rather than on a Markdown parse, which
    /// is what every other read of a document in this suite does and for the same reason: one
    /// dependency for one section, in a file its readers read by eye.
    /// </summary>
    internal static class CodeScanningRegister
    {
        /// <summary>
        /// Where the register lives, relative to the repository root.
        /// </summary>
        internal const string Document = "docs/code-scanning.md";

        /// <summary>
        /// Where the reading of the page lives, relative to the repository root.
        /// </summary>
        internal const string Reading = "Jellyfin.Plugin.WatchSync.Tests/CodeScanning/page-rules.txt";

        /// <summary>
        /// The heading the register sits under. The section ends at the next heading of the same
        /// level.
        /// </summary>
        internal const string Heading = "## The disposition register";

        /// <summary>
        /// The line every entry carries.
        /// </summary>
        private const string DispositionLabel = "Disposition:";

        /// <summary>
        /// The line every decline carries.
        /// </summary>
        private const string QuotedLabel = "Quoted by a dismissal:";

        /// <summary>
        /// An entry heading: the rule id in backticks and nothing else.
        /// </summary>
        private static readonly Regex EntryHeading = new Regex("^### `(?<rule>[^`]+)`\\s*$");

        /// <summary>
        /// The one word that makes an entry a decline.
        /// </summary>
        private static readonly Regex Declined = new Regex("\\bdeclined\\b", RegexOptions.IgnoreCase);

        /// <summary>
        /// Reads the register the tree carries.
        /// </summary>
        /// <returns>The document text.</returns>
        internal static string DocumentText() =>
            File.ReadAllText(Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), Document));

        /// <summary>
        /// Reads the reading the tree carries.
        /// </summary>
        /// <returns>The reading text.</returns>
        internal static string ReadingText() =>
            File.ReadAllText(Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), Reading));

        /// <summary>
        /// Reads a fixture from the tracked file rather than from a copy in the output directory,
        /// because a copy proves the state of the file on the day it was written.
        /// </summary>
        /// <param name="name">The file name.</param>
        /// <returns>The fixture text.</returns>
        internal static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                "Jellyfin.Plugin.WatchSync.Tests",
                "CodeScanning",
                name));

        /// <summary>
        /// Reads every entry under the register heading. A document with no such heading is
        /// refused rather than read as an empty register, because an empty register and a missing
        /// one are different states and only one of them is a decision.
        /// </summary>
        /// <param name="text">The document text.</param>
        /// <returns>The entries, in the order they are written.</returns>
        internal static IReadOnlyList<Entry> Entries(string text)
        {
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var start = Array.FindIndex(lines, line => string.Equals(line.TrimEnd(), Heading, StringComparison.Ordinal));

            Assert.True(start >= 0, $"No heading `{Heading}` was found, so there is no register to hold to the page.");

            var entries = new List<Entry>();
            string? rule = null;
            string? disposition = null;
            string? quoted = null;

            void Close()
            {
                if (rule is not null)
                {
                    entries.Add(new Entry(rule, disposition, quoted));
                }

                rule = null;
                disposition = null;
                quoted = null;
            }

            for (var index = start + 1; index < lines.Length; index++)
            {
                var line = lines[index];

                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    break;
                }

                var heading = EntryHeading.Match(line);

                if (heading.Success)
                {
                    Close();
                    rule = heading.Groups["rule"].Value;
                    continue;
                }

                if (rule is null)
                {
                    continue;
                }

                if (line.StartsWith(DispositionLabel, StringComparison.Ordinal))
                {
                    disposition = line[DispositionLabel.Length..].Trim();
                }
                else if (line.StartsWith(QuotedLabel, StringComparison.Ordinal))
                {
                    quoted = line[QuotedLabel.Length..].Trim();
                }
            }

            Close();

            return entries;
        }

        /// <summary>
        /// Reads the distinct rule ids out of the reading: the first column of every line that is
        /// neither blank nor a comment.
        /// </summary>
        /// <param name="text">The reading text.</param>
        /// <returns>The rule ids, sorted.</returns>
        internal static IReadOnlyList<string> PageRules(string text) =>
            text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .Select(line => line.Split('\t')[0].Trim())
                .Where(rule => rule.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(rule => rule, StringComparer.Ordinal)
                .ToList();

        /// <summary>
        /// Compares the entries against the rules, in both directions.
        /// </summary>
        /// <param name="entries">The register's entries.</param>
        /// <param name="rules">The rules the page carries.</param>
        /// <returns>What each side holds and the other does not, sorted.</returns>
        internal static Drift Compare(IEnumerable<Entry> entries, IEnumerable<string> rules)
        {
            var named = entries.Select(entry => entry.Rule).ToList();
            var carried = rules.ToList();

            return new Drift(
                carried.Except(named, StringComparer.Ordinal).OrderBy(rule => rule, StringComparer.Ordinal).ToList(),
                named.Except(carried, StringComparer.Ordinal).OrderBy(rule => rule, StringComparer.Ordinal).ToList());
        }

        /// <summary>
        /// Every entry whose disposition declines the rule and which carries no sentence for a
        /// dismissal to quote.
        /// </summary>
        /// <param name="entries">The register's entries.</param>
        /// <returns>The entries, in the order they are written.</returns>
        internal static IReadOnlyList<Entry> DeclinesWithNothingToQuote(IEnumerable<Entry> entries) =>
            entries
                .Where(entry => entry.Disposition is not null && Declined.IsMatch(entry.Disposition))
                .Where(entry => string.IsNullOrWhiteSpace(entry.Quoted))
                .ToList();

        /// <summary>
        /// One entry of the register.
        /// </summary>
        /// <param name="Rule">The rule id the heading names.</param>
        /// <param name="Disposition">The disposition line, or null where the entry has none.</param>
        /// <param name="Quoted">The sentence a dismissal quotes, or null where the entry has none.</param>
        internal sealed record Entry(string Rule, string? Disposition, string? Quoted);

        /// <summary>
        /// What each side holds and the other does not.
        /// </summary>
        /// <param name="RulesWithoutAnEntry">Rules the page carries that the register does not name.</param>
        /// <param name="EntriesWithoutARule">Rules the register names that the page does not carry.</param>
        internal sealed record Drift(IReadOnlyList<string> RulesWithoutAnEntry, IReadOnlyList<string> EntriesWithoutARule);
    }
}
