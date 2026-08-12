using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the mutation run to what it says it covers, and to the two things it may never
/// become.
///
/// A mutation run is an instrument rather than a gate, and an instrument has two ways of
/// going quiet. It can be pointed somewhere else, which is what happens when a component
/// lands under a name the scope does not match and the score goes on being reported for the
/// half that is still covered. And it can be turned into a threshold on a merge, which is a
/// number people tune until it passes rather than a fault they fix.
///
/// So the scope lives in <c>stryker-config.json</c> where a clone reads the same one, the
/// components are declared in <c>Mutation/scope.txt</c>, and these hold the two to each
/// other in both directions. The triage of what the run left alive is
/// <c>docs/mutation.md</c>.
/// </summary>
public class MutationScopeTests
{
    private const string PluginProject = "Jellyfin.Plugin.WatchSync";

    /// <summary>
    /// A component the plugin project gained that neither file mentions.
    ///
    /// This is the failure the whole register exists for. A directory nobody added to either
    /// side is not refused by the mutation run, which simply does not mutate it and reports a
    /// score that reads as if it had.
    /// </summary>
    [Fact]
    public void EveryComponentInThePluginProjectIsDeclared()
    {
        var declared = Register().Select(row => row.Directory).ToHashSet(StringComparer.Ordinal);
        var undeclared = ComponentsInTheTree().Where(name => !declared.Contains(name)).ToList();

        Assert.Empty(undeclared);
    }

    /// <summary>
    /// The configuration and the register name the same components.
    ///
    /// Both directions matter and they fail differently. A component declared as mutated that
    /// no glob reaches is a row saying it is covered when it is not. A glob with no row is a
    /// scope nobody argued, which is how the run quietly grows into code whose survivors
    /// nobody has agreed are worth triaging.
    /// </summary>
    [Fact]
    public void TheConfiguredScopeAndTheRegisterNameTheSameComponents()
    {
        var inScope = Register()
            .Where(row => row.State is "mutated" or "awaited")
            .Select(row => row.Directory)
            .ToHashSet(StringComparer.Ordinal);

        var globbed = ConfiguredScope();

        Assert.Empty(inScope.Except(globbed, StringComparer.Ordinal));
        Assert.Empty(globbed.Except(inScope, StringComparer.Ordinal));
    }

    /// <summary>
    /// A row says what the tree holds rather than what somebody meant.
    ///
    /// The awaited state is the one that has to fail closed. #103 scopes the run to the
    /// matcher and the conflict resolver, and the resolver is not written, so the scope names
    /// a directory that does not exist. The day it appears, this goes red and the row has to
    /// be moved by hand, which is the moment somebody checks that the resolver landed under
    /// the name the scope was already pointing at.
    /// </summary>
    [Fact]
    public void EveryRowSaysWhetherItsComponentIsInTheTree()
    {
        var present = ComponentsInTheTree().ToHashSet(StringComparer.Ordinal);

        foreach (var row in Register())
        {
            var shouldBeThere = row.State is "mutated" or "excused";

            Assert.True(
                present.Contains(row.Directory) == shouldBeThere,
                shouldBeThere
                    ? $"{row.Directory} is declared {row.State} and is not in the plugin project."
                    : $"{row.Directory} is declared awaited and the plugin project now has it, so the row has to say mutated.");
        }
    }

    /// <summary>
    /// A row with no reason is a decision nobody made.
    /// </summary>
    [Fact]
    public void EveryRowCarriesItsReason()
    {
        foreach (var row in Register())
        {
            Assert.False(
                string.IsNullOrWhiteSpace(row.Reason),
                $"{row.Directory} is declared {row.State} and gives no reason.");
        }
    }

    /// <summary>
    /// The run reports and gates nothing.
    ///
    /// A break threshold above zero turns the score into a merge condition, and this is the
    /// one line that does it. #103 refuses that in its own words: a score threshold on a merge
    /// is a number people tune rather than a fault they fix.
    /// </summary>
    [Fact]
    public void TheScoreGatesNothing()
    {
        var thresholds = Configuration().GetProperty("thresholds");

        Assert.Equal(0, thresholds.GetProperty("break").GetInt32());
    }

    /// <summary>
    /// It runs on a schedule and on demand and on nothing else.
    ///
    /// A pull-request trigger is what #103 refuses by asking for the other two by name. It
    /// would also be the way the previous test gets undone without touching it: a run on every
    /// change becomes a check people wait for, and a check people wait for becomes one
    /// somebody makes required.
    /// </summary>
    [Fact]
    public void TheRunIsScheduledAndOnDemandAndIsNotAPullRequestCheck()
    {
        var triggers = WorkflowTriggers();

        Assert.Equal(new[] { "schedule", "workflow_dispatch" }, triggers);
    }

    /// <summary>
    /// The instrument having stopped is a failure, and the verdict is a file a clone runs.
    ///
    /// A run producing no report at all looks exactly like a green one from the outside, which
    /// is the failure #103 names. The check is not written into the workflow, so the same file
    /// judges a report produced by hand.
    /// </summary>
    [Fact]
    public void AMissingReportIsJudgedByAFileRatherThanByTheWorkflow()
    {
        var root = HeadlessGuardTests.HeadlessGuard.RepositoryRoot();
        var guard = Path.Combine(root, ".github", "check-mutation-report.py");

        Assert.True(File.Exists(guard), "the mutation report guard is not in the tree.");

        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "mutation.yaml"));

        Assert.Contains(".github/check-mutation-report.py", workflow, StringComparison.Ordinal);
    }

    private static JsonElement Configuration()
    {
        var path = Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), "stryker-config.json");

        Assert.True(File.Exists(path), "stryker-config.json is not in the tree.");

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement.GetProperty("stryker-config").Clone();
    }

    /// <summary>
    /// The directories the mutate globs reach, read out of the globs rather than restated.
    /// </summary>
    /// <returns>One name per glob.</returns>
    private static IReadOnlyCollection<string> ConfiguredScope()
    {
        var globs = Configuration()
            .GetProperty("mutate")
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToList();

        Assert.All(globs, glob => Assert.EndsWith("/**/*.cs", glob, StringComparison.Ordinal));

        return globs
            .Select(glob => glob[..glob.IndexOf('/', StringComparison.Ordinal)])
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Every directory of the plugin project that holds a source, plus "." where a source sits
    /// in none. Build output is not a component and is skipped by name.
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
    /// The register, read as data. A row the parser cannot read fails rather than being
    /// skipped, which is the difference between a register and a comment.
    /// </summary>
    /// <returns>The rows.</returns>
    private static IReadOnlyList<Row> Register()
    {
        var path = Path.Combine(
            HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
            "Jellyfin.Plugin.WatchSync.Tests",
            "Mutation",
            "scope.txt");

        Assert.True(File.Exists(path), "the mutation scope register is not in the tree.");

        var rows = new List<Row>();

        foreach (var line in File.ReadAllLines(path))
        {
            var text = line.Trim();

            if (text.Length == 0 || text.StartsWith('#'))
            {
                continue;
            }

            var fields = text.Split(" :: ");

            Assert.True(fields.Length == 3, $"a row of the register has {fields.Length} fields rather than three: {text}");
            Assert.Contains(fields[1], new[] { "mutated", "awaited", "excused" });

            rows.Add(new Row(fields[0].Trim(), fields[1].Trim(), fields[2].Trim()));
        }

        Assert.NotEmpty(rows);

        return rows;
    }

    /// <summary>
    /// The keys under the workflow's own "on:" block, read off the file rather than restated.
    /// </summary>
    /// <returns>The trigger names, in the order the file gives them.</returns>
    private static IReadOnlyList<string> WorkflowTriggers()
    {
        var path = Path.Combine(
            HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
            ".github",
            "workflows",
            "mutation.yaml");

        Assert.True(File.Exists(path), "the mutation workflow is not in the tree.");

        var triggers = new List<string>();
        var inside = false;

        foreach (var line in File.ReadAllLines(path))
        {
            if (string.Equals(line.TrimEnd(), "on:", StringComparison.Ordinal))
            {
                inside = true;

                continue;
            }

            if (!inside)
            {
                continue;
            }

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (!line.StartsWith("  ", StringComparison.Ordinal))
            {
                break;
            }

            if (line.StartsWith("    ", StringComparison.Ordinal) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            triggers.Add(line.Trim().TrimEnd(':'));
        }

        Assert.NotEmpty(triggers);

        return triggers;
    }

    private sealed record Row(string Directory, string State, string Reason)
    {
        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture, $"{Directory} :: {State}");
    }
}
