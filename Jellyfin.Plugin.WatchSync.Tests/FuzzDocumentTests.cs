using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the rule tables in <c>docs/fuzz.md</c> to the rules the harness actually carries.
///
/// The document is what somebody reads to decide whether a green run means anything, and a table
/// of rules is exactly the shape that goes stale: a rule added to the oracle and not to the table
/// is a run doing more than it says, and a row for a rule nobody asks any more is a document
/// promising cover that was deleted. The second is the expensive direction, because it is believed.
///
/// The set is derived from the source rather than listed here, for the same reason: a third copy
/// would drift from both.
/// </summary>
public class FuzzDocumentTests
{
    /// <summary>
    /// Every rule the harness can report has a row, and every row names a rule it can report.
    /// </summary>
    [Fact]
    public void TheDocumentAndTheHarnessNameTheSameRules()
    {
        var carried = RulesInTheHarness(Read("Jellyfin.Plugin.WatchSync.Tests/EnvelopeFuzz.cs"));
        var written = RulesInTheDocument(Read("docs/fuzz.md"));

        Assert.NotEmpty(carried);
        Assert.NotEmpty(written);

        Assert.Empty(carried.Except(written, StringComparer.Ordinal));
        Assert.Empty(written.Except(carried, StringComparer.Ordinal));
    }

    /// <summary>
    /// The comparison reads both sides out of the tree, so a file that moved is a red suite rather
    /// than a guard quietly comparing two empty sets.
    ///
    /// It is the failure the shape invites: every assertion above passes over nothing, and the
    /// document could then say anything at all.
    /// </summary>
    [Fact]
    public void BothSidesAreReadFromTheTreeRatherThanAssumed()
    {
        Assert.Contains("new Finding(", Read("Jellyfin.Plugin.WatchSync.Tests/EnvelopeFuzz.cs"), StringComparison.Ordinal);
        Assert.Contains("| rule |", Read("docs/fuzz.md"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The rules the harness reports, taken from the one place a finding can be made.
    /// </summary>
    /// <param name="source">The harness source.</param>
    /// <returns>The rule identifiers.</returns>
    private static IReadOnlyCollection<string> RulesInTheHarness(string source) =>
        Regex.Matches(source, "new Finding\\(\\s*\"([a-z0-9-]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The rules the document carries, taken from the first cell of every table row that names one.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The rule identifiers.</returns>
    private static IReadOnlyCollection<string> RulesInTheDocument(string document) =>
        Regex.Matches(document, "(?m)^\\| `([a-z0-9-]+)` \\|")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static string Read(string path) =>
        File.ReadAllText(Path.Combine(Root(), path.Replace('/', Path.DirectorySeparatorChar)));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var marker = Path.Combine(directory.FullName, ".git");

            if (Directory.Exists(marker) || File.Exists(marker))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"No repository root above {AppContext.BaseDirectory}. Both sides of this comparison are read from the tracked tree.");
    }
}
