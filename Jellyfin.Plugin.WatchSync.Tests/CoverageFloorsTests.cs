using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the coverage floors to the tree, and holds the run that judges them to the
/// project it claims to measure.
///
/// Coverage is an instrument and an instrument goes quiet in two ways. It can stop
/// looking at something, which is what happens when a component lands and nobody
/// enters it: the run reports a number for the components it does know and that
/// number reads exactly like a number for all of them. And it can be pointed at a
/// target the project no longer builds, or fail to be pointed at one it has
/// started building, which leaves a whole server line unmeasured behind a green
/// mark.
///
/// So the floors live in <c>Coverage/floors.txt</c> where a clone reads the same
/// ones, the verdict is <c>.github/check-coverage.py</c> where a clone runs the
/// same one, and these hold the register to the plugin project in both directions.
/// What the register cannot say is whether a floor is the right floor, which is a
/// judgement, and the reason column is where it is argued rather than asserted.
/// </summary>
public class CoverageFloorsTests
{
    private const string PluginProject = "Jellyfin.Plugin.WatchSync";

    private const string TestProject = "Jellyfin.Plugin.WatchSync.Tests";

    private const string Register = "Jellyfin.Plugin.WatchSync.Tests/Coverage/floors.txt";

    private const string Checker = ".github/check-coverage.py";

    /// <summary>
    /// A component the plugin project gained that the register does not name.
    ///
    /// This is the failure the register exists for. Nothing refuses an unmeasured
    /// directory on its own: the run walks what the report holds, reports a
    /// percentage per component it found, and says nothing at all about one nobody
    /// entered.
    /// </summary>
    [Fact]
    public void EveryComponentInThePluginProjectIsDeclared()
    {
        var declared = Rows().Select(row => row.Component).ToHashSet(StringComparer.Ordinal);
        var undeclared = ComponentsInTheTree().Where(name => !declared.Contains(name)).ToList();

        Assert.Empty(undeclared);
    }

    /// <summary>
    /// A row says what the tree holds rather than what somebody meant.
    ///
    /// The awaited state is the one that has to fail closed. Two of the four areas
    /// this register calls critical are not written, and the rows for them name the
    /// directory each is expected to land under. The day one appears, this goes red
    /// and the row has to be moved by hand, which is the moment somebody checks the
    /// area arrived under the name the register was already pointing at.
    /// </summary>
    [Fact]
    public void EveryRowSaysWhetherItsComponentIsInTheTree()
    {
        var present = ComponentsInTheTree().ToHashSet(StringComparer.Ordinal);

        foreach (var row in Rows())
        {
            var shouldBeThere = !string.Equals(row.State, "awaited", StringComparison.Ordinal);

            Assert.True(
                present.Contains(row.Component) == shouldBeThere,
                shouldBeThere
                    ? $"{row.Component} is entered as {row.State} and is not in the plugin project."
                    : $"{row.Component} is entered as awaited and the plugin project now has it, so the row has to be moved.");
        }
    }

    /// <summary>
    /// A row with no reason is a floor nobody argued.
    /// </summary>
    [Fact]
    public void EveryRowCarriesItsReason()
    {
        foreach (var row in Rows())
        {
            Assert.False(
                string.IsNullOrWhiteSpace(row.Reason),
                $"{row.Component} is entered as {row.State} and gives no reason.");
        }
    }

    /// <summary>
    /// Four areas sit above the ordinary floor and not three or five.
    ///
    /// #87 names them: the resolver, the matcher, the queue and the apply path. The
    /// count is asserted rather than the names, because two of the four are not
    /// written and the row that will hold each of them is a prediction of a
    /// directory name. What may not drift is how many areas this repository holds to
    /// the higher floor, and a fifth added quietly is the way the higher floor stops
    /// meaning anything.
    /// </summary>
    [Fact]
    public void FourAreasSitAboveTheOrdinaryFloor()
    {
        var above = Rows()
            .Where(row => row.State is "critical" or "awaited")
            .Select(row => row.Component)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(4, above.Count);
    }

    /// <summary>
    /// There are two floors and they are the same two wherever they are written.
    ///
    /// A number per component is a number somebody lowers one component at a time,
    /// and every lowering looks reasonable on its own. This refuses the third pair
    /// as well as the lowered one: the rows above the ordinary floor all carry one
    /// pair, the rows at it all carry another, and the first pair is higher than the
    /// second in both columns.
    /// </summary>
    [Fact]
    public void TheRegisterCarriesTwoFloorsAndTheHigherOneIsHigher()
    {
        var higher = Rows()
            .Where(row => row.State is "critical" or "awaited")
            .Select(row => (row.Lines, row.Branches))
            .Distinct()
            .ToList();

        var ordinary = Rows()
            .Where(row => row.State is "ordinary" or "empty")
            .Select(row => (row.Lines, row.Branches))
            .Distinct()
            .ToList();

        var critical = Assert.Single(higher);
        var rest = Assert.Single(ordinary);

        Assert.True(
            critical.Lines > rest.Lines && critical.Branches > rest.Branches,
            $"the floor above is {critical.Lines}/{critical.Branches} and the ordinary one is {rest.Lines}/{rest.Branches}, so it is not above it.");
    }

    /// <summary>
    /// Every target the suite builds is measured.
    ///
    /// The facts differ per target, because each one references a different server
    /// line, so a line reachable on one and not on the other is unmeasured wherever
    /// the run did not go. A target added to the project and not to the run leaves a
    /// whole server line behind a green mark, and nothing in the report would say
    /// so: a report for one target is a complete report of that target.
    /// </summary>
    [Fact]
    public void TheRunMeasuresEveryTargetTheSuiteBuilds()
    {
        var project = File.ReadAllText(Path.Combine(
            HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
            TestProject,
            TestProject + ".csproj"));

        var declared = Regex.Match(project, "<TargetFrameworks>([^<]*)</TargetFrameworks>");

        Assert.True(declared.Success, "the test project declares no target framework list.");

        var built = declared.Groups[1].Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(name => name, StringComparer.Ordinal);

        var measured = Regex.Match(WorkflowSteps(), "\n      TARGETS: \"([^\"]*)\"\n");

        Assert.True(measured.Success, "the coverage workflow names no target list.");

        Assert.Equal(
            built,
            measured.Groups[1].Value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// The verdict is a file a clone runs, and the workflow is what calls it.
    ///
    /// A threshold written into the workflow is one a contributor cannot reproduce
    /// without pushing, and one nobody can run against a clone at all. It is also
    /// how a floor ends up written twice: once where the run reads it and once where
    /// a reader looks for it.
    /// </summary>
    [Fact]
    public void TheFloorsAreJudgedByAFileRatherThanByTheWorkflow()
    {
        var root = HeadlessGuardTests.HeadlessGuard.RepositoryRoot();

        Assert.True(
            File.Exists(Path.Combine(root, ".github", "check-coverage.py")),
            "the coverage verdict is not in the tree.");

        var steps = WorkflowSteps();

        Assert.Contains(Checker, steps, StringComparison.Ordinal);
        Assert.Contains(Register, steps, StringComparison.Ordinal);
    }

    /// <summary>
    /// The guard refuses a register one character away from correct, and passes its
    /// repair.
    ///
    /// The fixture carries its own component list, because a fixture judged against
    /// the real plugin project would prove the state of that project on the day it
    /// ran rather than proving the guard. The miss is one letter dropped from one
    /// component name, so every row parses, every state is one this register has and
    /// the count is right: what is wrong is that the matcher is undeclared while a
    /// directory nobody wrote is entered.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheNearMissAndPassesItsRepair()
    {
        var components = new[] { ".", "Conflict", "Matching", "Storage" };

        var refused = Compare(components, Parse(Fixture("near-miss.txt")));

        Assert.Equal("Matching", Assert.Single(refused.Undeclared));
        Assert.Equal("Matchng", Assert.Single(refused.Dangling));

        var repaired = Compare(components, Parse(Fixture("near-miss-repaired.txt")));

        Assert.Empty(repaired.Undeclared);
        Assert.Empty(repaired.Dangling);
    }

    /// <summary>
    /// Which components a register leaves out and which it names that are not there.
    /// </summary>
    /// <param name="components">The components a project holds.</param>
    /// <param name="rows">The register rows.</param>
    /// <returns>The two directions of the disagreement.</returns>
    private static (IReadOnlyList<string> Undeclared, IReadOnlyList<string> Dangling) Compare(
        IReadOnlyCollection<string> components,
        IReadOnlyList<Row> rows)
    {
        var declared = rows.Select(row => row.Component).ToHashSet(StringComparer.Ordinal);

        var awaited = rows
            .Where(row => string.Equals(row.State, "awaited", StringComparison.Ordinal))
            .Select(row => row.Component)
            .ToHashSet(StringComparer.Ordinal);

        return (
            components.Where(name => !declared.Contains(name)).ToList(),
            declared.Where(name => !awaited.Contains(name) && !components.Contains(name)).ToList());
    }

    /// <summary>
    /// Every directory of the plugin project that holds a source, plus "." where a
    /// source sits in none. Build output is not a component and is skipped by name.
    /// </summary>
    /// <returns>The component names.</returns>
    private static IEnumerable<string> ComponentsInTheTree()
    {
        var project = Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), PluginProject);

        if (Directory.EnumerateFiles(project, "*.cs", SearchOption.TopDirectoryOnly).Any())
        {
            yield return ".";
        }

        foreach (var directory in Directory.EnumerateDirectories(project))
        {
            var name = Path.GetFileName(directory);

            if (string.Equals(name, "bin", StringComparison.Ordinal)
                || string.Equals(name, "obj", StringComparison.Ordinal))
            {
                continue;
            }

            if (Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Any())
            {
                yield return name;
            }
        }
    }

    /// <summary>
    /// The coverage workflow, as text.
    /// </summary>
    /// <returns>Its contents, with the line endings a reader of the tree sees.</returns>
    private static string Workflow()
    {
        var path = Path.Combine(
            HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
            ".github",
            "workflows",
            "coverage.yaml");

        Assert.True(File.Exists(path), "the coverage workflow is not in the tree.");

        return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// The coverage workflow with every comment line removed.
    ///
    /// The header of that file explains what the verdict is and where the floors
    /// live, and it names both paths to do it. A search over the whole file is
    /// therefore satisfied by the explanation whether or not any step still makes
    /// the call, which was measured: replacing the call with a path that does not
    /// exist left every fact here green.
    /// </summary>
    /// <returns>The workflow, prose removed.</returns>
    private static string WorkflowSteps() =>
        string.Join(
            '\n',
            Workflow()
                .Split('\n')
                .Where(line => !line.TrimStart().StartsWith('#')));

    /// <summary>
    /// One of the two fixtures beside the register.
    /// </summary>
    /// <param name="name">The fixture file name.</param>
    /// <returns>Its lines.</returns>
    private static IReadOnlyList<string> Fixture(string name) =>
        File.ReadAllLines(Path.Combine(
            HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
            TestProject,
            "Coverage",
            name));

    /// <summary>
    /// The register, read as data.
    /// </summary>
    /// <returns>The rows.</returns>
    private static IReadOnlyList<Row> Rows()
    {
        var path = Path.Combine(
            HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
            TestProject,
            "Coverage",
            "floors.txt");

        Assert.True(File.Exists(path), "the coverage floor register is not in the tree.");

        return Parse(File.ReadAllLines(path));
    }

    /// <summary>
    /// A row the parser cannot read fails rather than being skipped, which is the
    /// difference between a register and a comment.
    ///
    /// The row is split before it is trimmed, and that order is the whole of why
    /// this comment exists. A row whose reason is deleted keeps the separator in
    /// front of the space that is left, so a line trimmed first arrives as four
    /// fields and is refused as malformed, and the fact that names the missing
    /// reason is never reached. It was measured that way: the deletion reddened
    /// every fact that reads the register and none of them the one it was about.
    /// </summary>
    /// <param name="lines">The register's lines.</param>
    /// <returns>The rows.</returns>
    private static IReadOnlyList<Row> Parse(IReadOnlyList<string> lines)
    {
        var rows = new List<Row>();

        foreach (var line in lines)
        {
            var row = line.TrimEnd('\r');
            var text = row.Trim();

            if (text.Length == 0 || text.StartsWith('#'))
            {
                continue;
            }

            var fields = row.Split(" :: ");

            Assert.True(fields.Length == 5, $"a row of the register has {fields.Length} fields rather than five: {text}");
            Assert.Contains(fields[1].Trim(), new[] { "critical", "ordinary", "awaited", "empty" });

            rows.Add(new Row(
                fields[0].Trim(),
                fields[1].Trim(),
                int.Parse(fields[2].Trim(), CultureInfo.InvariantCulture),
                int.Parse(fields[3].Trim(), CultureInfo.InvariantCulture),
                fields[4].Trim()));
        }

        Assert.NotEmpty(rows);

        return rows;
    }

    private sealed record Row(string Component, string State, int Lines, int Branches, string Reason);
}
