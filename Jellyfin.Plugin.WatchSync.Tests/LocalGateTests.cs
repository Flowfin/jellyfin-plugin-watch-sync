using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the local gate to the workflows it stands in for, which is #114's first condition.
///
/// The command exists so that a contributor can run what the merge waits for before pushing. What
/// makes that worth anything is that it runs the workflow's own steps rather than a copy of them,
/// and the copy it could drift into is not the command line - <c>gate.py</c> reads each step's
/// script out of the workflow - but the SET of steps. A step added to a job with no line beside it
/// in the register is a step the gate runs and the local command does not, and a contributor who
/// ran it green would then be surprised by exactly the check the command was written to stop
/// surprising them.
///
/// <para>
/// So the register is held to the workflow in both directions: every step it names exists and
/// carries a command, and every step carrying a command is either run or kept out with a reason.
/// <c>gate.py</c> refuses the same disagreement at the moment somebody runs it, and this suite is
/// where it is refused on a machine that never runs the script at all.
/// </para>
///
/// <para>
/// WHAT NEITHER OF THEM CAN SEE is which contexts a merge actually waits for. That is a repository
/// setting, no file in this tree carries it, and a context added to it tomorrow is a leg the
/// register does not hold and nothing here will say so. The register says that in its own header
/// and the note says it to the contributor; this is the same admission from the side that cannot
/// repair it.
/// </para>
/// </summary>
public class LocalGateTests
{
    /// <summary>
    /// The command a contributor runs.
    /// </summary>
    private const string Command = "gate.py";

    /// <summary>
    /// The register naming the legs and their steps.
    /// </summary>
    private const string Register = "gate.txt";

    /// <summary>
    /// The note that sends a contributor to the command.
    /// </summary>
    private const string Note = "CONTRIBUTING.md";

    /// <summary>
    /// Every line of the register places a step in a job that exists and carries a command.
    ///
    /// The set is asserted non-empty first, because a register that stopped being read would
    /// otherwise leave every fact here green over nothing, which is the failure a register check
    /// exists to prevent rather than one it may have.
    /// </summary>
    [Fact]
    public void EveryLineOfTheRegisterNamesAStepThatExistsAndCarriesACommand()
    {
        var root = HeadlessGuardTests.HeadlessGuard.RepositoryRoot();
        var lines = LocalGate.Read(root);

        Assert.NotEmpty(lines);

        var missing = new List<string>();

        foreach (var line in lines)
        {
            var path = Path.Combine(root, line.Workflow.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(path))
            {
                missing.Add($"{Register} names {line.Workflow}, which is not a file in this repository.");
                continue;
            }

            var steps = LocalGate.Steps(File.ReadAllText(path), line.Job);

            if (steps is null)
            {
                missing.Add($"{line.Workflow} carries no job `{line.Job}`, so the leg `{line.Leg}` names a context this repository does not produce.");
                continue;
            }

            var step = steps.FirstOrDefault(candidate => string.Equals(candidate.Name, line.Step, StringComparison.Ordinal));

            if (step is null)
            {
                missing.Add($"{line.Workflow}:{line.Job} carries no step named `{line.Step}`.");
                continue;
            }

            if (!step.HasCommand)
            {
                missing.Add($"{line.Workflow}:{line.Job} carries a step named `{line.Step}` and it has no command, so there is nothing for the local run to do with it.");
            }
        }

        Assert.Empty(missing);
    }

    /// <summary>
    /// Every step of a leg's job that carries a command is either run locally or kept out with a
    /// reason.
    ///
    /// This is the direction that decides whether the command is worth running. The other one
    /// fails loudly the moment somebody runs the script; this one fails silently, because a job
    /// that grew a step is a job the gate runs more of and the local command runs the same amount
    /// of, and nothing about that looks wrong from either file on its own.
    /// </summary>
    [Fact]
    public void EveryCommandInALegsJobIsRunLocallyOrKeptOutWithAReason()
    {
        var root = HeadlessGuardTests.HeadlessGuard.RepositoryRoot();
        var lines = LocalGate.Read(root);

        Assert.NotEmpty(lines);

        var unaccounted = new List<string>();

        foreach (var job in lines.GroupBy(line => (line.Workflow, line.Job)))
        {
            var path = Path.Combine(root, job.Key.Workflow.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(path))
            {
                continue;
            }

            var steps = LocalGate.Steps(File.ReadAllText(path), job.Key.Job);

            if (steps is null)
            {
                continue;
            }

            var named = job.Select(line => line.Step).ToHashSet(StringComparer.Ordinal);

            unaccounted.AddRange(steps
                .Where(step => step.HasCommand && step.Name.Length > 0)
                .Where(step => !named.Contains(step.Name))
                .Select(step => $"{job.Key.Workflow}:{job.Key.Job} runs `{step.Name}` and {Register} accounts for it neither way, so the local command covers less of that job than the gate does and says nothing about it."));

            unaccounted.AddRange(steps
                .Where(step => step.HasCommand && step.Name.Length == 0)
                .Select(_ => $"{job.Key.Workflow}:{job.Key.Job} carries a step with a command and no name, and the register places a step by its name."));
        }

        Assert.Empty(unaccounted);
    }

    /// <summary>
    /// A step kept out of the local run says why, and a step run says nothing extra.
    ///
    /// A step left out in silence is one nobody can tell from a step somebody forgot, and the
    /// register is the only place that difference is written down.
    /// </summary>
    [Fact]
    public void EveryStepKeptOutOfTheLocalRunCarriesItsReason()
    {
        var lines = LocalGate.Read(HeadlessGuardTests.HeadlessGuard.RepositoryRoot());

        Assert.NotEmpty(lines);

        Assert.Empty(lines
            .Where(line => !line.Runs && line.Reason.Length == 0)
            .Select(line => $"{Register} keeps `{line.Step}` out of the run and gives no reason."));
    }

    /// <summary>
    /// The note names the command and the register, and both are in the tree.
    ///
    /// The pair is what a contributor arriving here follows, and either half missing leaves them
    /// with an instruction and nowhere to go.
    /// </summary>
    [Fact]
    public void TheNoteNamesTheCommandAndTheRegisterAndBothExist()
    {
        var root = HeadlessGuardTests.HeadlessGuard.RepositoryRoot();

        Assert.True(File.Exists(Path.Combine(root, Command)), $"There is no {Command} for the note to send anybody to.");
        Assert.True(File.Exists(Path.Combine(root, Register)), $"There is no {Register} for {Command} to read its legs from.");

        var text = File.ReadAllText(Path.Combine(root, Note));

        Assert.Contains($"python {Command}", text, StringComparison.Ordinal);
        Assert.Contains(Register, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The note says what the command does not cover, and that survives an edit that adds to it.
    ///
    /// This fact replaces the one that refused the deletion of the section saying the note carried
    /// no command at all. The direction such a section is edited in has not changed: somebody adds
    /// a leg, deletes the paragraph saying the command is not the gate, and the note then promises
    /// a verdict it cannot give. The two admissions held here are the ones that cost most if they
    /// go: that the required set is a setting no file here reads, and that a machine without both
    /// runtimes goes red on the suite for a reason that is not the tree's.
    /// </summary>
    [Fact]
    public void TheNoteSaysWhatTheLocalCommandDoesNotCover()
    {
        var text = File.ReadAllText(Path.Combine(HeadlessGuardTests.HeadlessGuard.RepositoryRoot(), Note));

        Assert.Contains("What the local gate does not cover", text, StringComparison.Ordinal);
        Assert.Contains("repository setting", text, StringComparison.Ordinal);
        Assert.Contains("no .NET 9 runtime", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the register and the workflow jobs it names.
    ///
    /// The workflow is read as text rather than parsed as YAML, because this project carries no
    /// YAML reader and adding one for four files would be a dependency the suite pays for
    /// everywhere. What is needed is narrower than a parse: the names of a job's steps and whether
    /// each carries a command, off files whose shape this repository's own rules fix.
    /// </summary>
    internal static class LocalGate
    {
        /// <summary>
        /// One line of the register.
        /// </summary>
        /// <param name="Leg">The leg the step belongs to.</param>
        /// <param name="Workflow">The workflow file, relative to the repository root.</param>
        /// <param name="Job">The job id inside that file.</param>
        /// <param name="Runs">Whether the local command runs the step.</param>
        /// <param name="Step">The step's name.</param>
        /// <param name="Reason">Why a step that is not run is kept out.</param>
        internal sealed record Line(string Leg, string Workflow, string Job, bool Runs, string Step, string Reason);

        /// <summary>
        /// One step of one job.
        /// </summary>
        /// <param name="Name">The step's name, empty where it has none.</param>
        /// <param name="HasCommand">Whether the step carries a `run:` command.</param>
        internal sealed record Step(string Name, bool HasCommand);

        /// <summary>
        /// Reads the register, skipping blanks and comments.
        /// </summary>
        /// <param name="root">The repository root.</param>
        /// <returns>The lines, in file order.</returns>
        internal static IReadOnlyList<Line> Read(string root)
        {
            var path = Path.Combine(root, Register);

            Assert.True(File.Exists(path), $"There is no {Register} at {path}.");

            var lines = new List<Line>();

            foreach (var text in File.ReadAllLines(path))
            {
                var trimmed = text.Trim();

                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                var fields = trimmed.Split("::").Select(field => field.Trim()).ToArray();

                Assert.True(fields.Length >= 5, $"{Register} carries a line with {fields.Length} fields and a step needs five: {trimmed}");

                var disposition = fields[3];

                Assert.True(
                    disposition is "run" or "skip",
                    $"{Register} carries the disposition `{disposition}` and the only two are `run` and `skip`.");

                lines.Add(new Line(
                    fields[0],
                    fields[1],
                    fields[2],
                    string.Equals(disposition, "run", StringComparison.Ordinal),
                    fields[4],
                    string.Join(" :: ", fields.Skip(5)).Trim()));
            }

            return lines;
        }

        /// <summary>
        /// Reads one job's steps out of a workflow file.
        /// </summary>
        /// <param name="workflow">The workflow's text.</param>
        /// <param name="job">The job id.</param>
        /// <returns>The steps in file order, or null where the file carries no such job.</returns>
        internal static IReadOnlyList<Step>? Steps(string workflow, string job)
        {
            var lines = workflow.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var opening = Array.FindIndex(lines, line => string.Equals(line.TrimEnd(), "  " + job + ":", StringComparison.Ordinal));

            if (opening < 0)
            {
                return null;
            }

            var steps = new List<Step>();
            var name = string.Empty;
            var command = false;
            var started = false;

            for (var index = opening + 1; index < lines.Length; index++)
            {
                var line = lines[index];
                var text = line.TrimEnd();

                if (text.Length == 0)
                {
                    continue;
                }

                if (Indent(line) <= 2)
                {
                    break;
                }

                var body = text.TrimStart();

                if (body.StartsWith("- ", StringComparison.Ordinal))
                {
                    if (started)
                    {
                        steps.Add(new Step(name, command));
                    }

                    started = true;
                    name = string.Empty;
                    command = false;
                    body = body[2..];
                }

                if (!started)
                {
                    continue;
                }

                if (body.StartsWith("name:", StringComparison.Ordinal))
                {
                    name = body["name:".Length..].Trim().Trim('"', '\'');
                }
                else if (body.StartsWith("run:", StringComparison.Ordinal))
                {
                    command = true;
                }
            }

            if (started)
            {
                steps.Add(new Step(name, command));
            }

            return steps;
        }

        /// <summary>
        /// Counts a line's leading spaces.
        /// </summary>
        /// <param name="line">The line.</param>
        /// <returns>How many spaces it opens with.</returns>
        private static int Indent(string line)
        {
            var count = 0;

            while (count < line.Length && line[count] == ' ')
            {
                count++;
            }

            return count;
        }
    }
}
