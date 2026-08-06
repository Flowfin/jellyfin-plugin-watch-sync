using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the compatibility matrix to the build manifest, and holds every one of its cells to
/// saying something.
///
/// A matrix like this fails in two ways and both are quiet. It falls behind the lines the plugin
/// actually builds, so it describes a version that is no longer shipped. And it acquires a cell
/// that reads as reassurance without being a measurement, which is worse than a blank because a
/// blank is visibly missing. So the row set is derived from the manifest rather than typed, and
/// a cell is either evidence or the exact words that say there is none.
/// </summary>
public class CompatibilityMatrixTests
{
    /// <summary>
    /// The one phrase a cell may use to say nothing was measured. One phrase rather than a
    /// family of them, because "untested", "not yet verified" and "should be fine" are three
    /// different distances from the truth and a reader cannot tell which was meant.
    /// </summary>
    private const string NotEvaluated = "not evaluated";

    /// <summary>
    /// Words that turn a missing measurement into an expectation. Each is refused wherever it
    /// appears in a cell. This is a floor rather than a guarantee: it holds the shapes that have
    /// been written, and a new one has to be added the first time somebody writes it.
    /// </summary>
    private static readonly string[] SoftWords =
    {
        "probably",
        "presumably",
        "should work",
        "should be fine",
        "expected to",
        "assumed",
        "likely",
        "untested",
    };

    private const string Document = "docs/compatibility.md";

    /// <summary>
    /// The matrix names exactly the lines the manifest declares. A target added to the manifest
    /// with no row leaves a shipped line undescribed, and a row naming a target the manifest does
    /// not carry describes a line nobody ships.
    /// </summary>
    [Fact]
    public void TheMatrixNamesEveryDeclaredTargetAndNoOther()
    {
        var rows = Rows();
        var declared = BuildTargetsTests.BuildFacts.DeclaredTargets();

        Assert.Equal(
            declared.Select(target => target.Framework).OrderBy(name => name, StringComparer.Ordinal),
            rows.Select(row => row["framework"]).OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// The ABI in a row is the one the manifest declares for that framework, and the manifest's
    /// own value is held against the assembly the target compiled against by the tests next to
    /// this one. So the chain from this table to the bytes is closed and no link in it is a
    /// number somebody remembered.
    /// </summary>
    [Fact]
    public void EveryRowCarriesTheAbiTheManifestDeclaresForItsFramework()
    {
        var declared = BuildTargetsTests.BuildFacts.DeclaredTargets()
            .ToDictionary(target => target.Framework, target => target.TargetAbi, StringComparer.Ordinal);

        Assert.Empty(Rows()
            .Where(row => !declared.TryGetValue(row["framework"], out var abi) || abi != row["declared ABI"])
            .Select(row => $"{row["framework"]} carries the ABI {row["declared ABI"]}, which is not what build.yaml declares for it."));
    }

    /// <summary>
    /// No blank cell, and no cell that says nothing was measured in words a reader could take
    /// for a measurement. The phrase is refused as a fragment as well, so a cell reading
    /// "not evaluated, but it loads" is refused rather than counted as a disclosure.
    /// </summary>
    [Fact]
    public void NoCellIsBlankAndNoCellSoftensAMissingMeasurement()
    {
        var wrong = new List<string>();

        foreach (var row in Rows())
        {
            foreach (var (column, value) in row)
            {
                if (value.Length == 0)
                {
                    wrong.Add($"{row["framework"]} has a blank cell under '{column}'. Every cell says what proved it or says '{NotEvaluated}'.");
                    continue;
                }

                if (value.Contains(NotEvaluated, StringComparison.OrdinalIgnoreCase) && !string.Equals(value, NotEvaluated, StringComparison.Ordinal))
                {
                    wrong.Add($"{row["framework"]} qualifies '{NotEvaluated}' under '{column}'. It is the whole cell or it is not there.");
                }

                wrong.AddRange(SoftWords
                    .Where(word => value.Contains(word, StringComparison.OrdinalIgnoreCase))
                    .Select(word => $"{row["framework"]} says '{word}' under '{column}', which reads as a measurement and is not one."));
            }
        }

        Assert.Empty(wrong);
    }

    /// <summary>
    /// Support is derived rather than declared. A line nobody has run this on is unsupported,
    /// however well it builds, and this refuses a row that says otherwise while its own person
    /// column still admits nothing was read.
    /// </summary>
    [Fact]
    public void NoRowClaimsSupportForALineNobodyHasRunItOn()
    {
        Assert.Empty(Rows()
            .Where(row => string.Equals(row["read by a person on a running server"], NotEvaluated, StringComparison.Ordinal))
            .Where(row => !string.Equals(row["supported"], "no", StringComparison.OrdinalIgnoreCase))
            .Select(row => $"{row["framework"]} claims supported '{row["supported"]}' while nobody has run it on that line."));
    }

    /// <summary>
    /// Reads the first table in the document into one dictionary per row, keyed by the header
    /// text. Keying by header rather than by position means a column inserted in the middle
    /// moves nothing here, and a column renamed fails loudly on the lookup rather than silently
    /// comparing the wrong cell.
    /// </summary>
    /// <returns>The rows.</returns>
    private static IReadOnlyList<IReadOnlyDictionary<string, string>> Rows()
    {
        var path = Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), Document);
        Assert.True(File.Exists(path), $"{Document} was not found at {path}.");

        var table = File.ReadAllLines(path)
            .Select(line => line.Trim())
            .SkipWhile(line => !line.StartsWith("| plugin version ", StringComparison.Ordinal))
            .TakeWhile(line => line.StartsWith('|'))
            .ToList();

        Assert.True(table.Count >= 3, $"{Document} carries no matrix with a header, a separator and at least one row.");

        var headers = Cells(table[0]);
        var rows = table.Skip(2).Select(Cells).ToList();

        Assert.All(rows, cells => Assert.True(
            cells.Count == headers.Count,
            $"A row of the matrix has {cells.Count} cells where the header has {headers.Count}."));

        return rows
            .Select(cells => (IReadOnlyDictionary<string, string>)headers
                .Select((header, index) => (header, value: cells[index]))
                .ToDictionary(pair => pair.header, pair => pair.value, StringComparer.Ordinal))
            .ToList();
    }

    private static IReadOnlyList<string> Cells(string line) =>
        line.Trim('|')
            .Split('|')
            .Select(cell => cell.Trim().Trim('`').Trim())
            .ToList();
}
