using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the release runbook to the answer #104 took: the two-server harness gates a release,
/// and a release that cannot run it waits rather than shipping unproven.
///
/// The reason this is a fact rather than a paragraph is the shape of the edit it refuses. Both
/// near-misses beside it leave the section in place, leave its heading unchanged and leave a
/// reader skimming the runbook with the impression that the gate is still there. One makes the
/// gate follow the runtime, which turns a green release into one that means either "the harness
/// passed" or "the harness never ran"; the other keeps the gate and turns the wait into a ship.
/// Neither fails anything else in this repository, and both are one line.
///
/// What this cannot judge is whether the harness exists or ever runs. It reads a document. The
/// runbook says at that section that #88 has not landed and that nothing runs this gate today,
/// and that admission is the kind of sentence a later edit removes once the harness arrives,
/// which is why it is written where the rule is rather than in an index of what is not yet true.
/// </summary>
public class ReleaseHarnessGateTests
{
    /// <summary>
    /// The gate itself, with no condition on it. The third answer #104 refuses is a gate that
    /// disables itself where its runtime is missing, and that answer is spelled as a condition
    /// on this line rather than as a different word.
    /// </summary>
    [Fact]
    public void TheRunbookRequiresTheHarnessGate()
    {
        Assert.Equal("required", Runbook.GateOfThisRepository());
    }

    /// <summary>
    /// What a missing runtime costs. It is the half that decides whether the gate means anything
    /// on a machine that cannot run it, and it is the half a release under time pressure edits.
    /// </summary>
    [Fact]
    public void TheRunbookSaysAReleaseWaitsWhereNoRuntimeAnswers()
    {
        Assert.Equal("the release waits", Runbook.WaitOfThisRepository());
    }

    /// <summary>
    /// The first guard proven by the mistake it exists for. The near-miss is the section with the
    /// gate made conditional on a runtime being present, which is the answer that reads as a
    /// description of reality. The repair is those five words and nothing else.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAConditionalGateAndPassesItsRepair()
    {
        Assert.Equal(
            "required where a container runtime is present",
            Runbook.Gate(Runbook.Fixture("harness-gate-near-miss.txt")));

        Assert.Equal("required", Runbook.Gate(Runbook.Fixture("harness-gate-near-miss-repaired.txt")));
    }

    /// <summary>
    /// The second guard proven the same way, on the edit that leaves the gate line untouched.
    /// A reader who checks that the gate is still required finds it is, and the release ships
    /// anyway.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAReleaseThatShipsUnprovenAndPassesItsRepair()
    {
        Assert.Equal(
            "the release ships unproven",
            Runbook.Wait(Runbook.Fixture("harness-wait-near-miss.txt")));

        Assert.Equal("the release waits", Runbook.Wait(Runbook.Fixture("harness-wait-near-miss-repaired.txt")));
    }

    /// <summary>
    /// Reads the two declared lines out of the release runbook. The read is anchored on the shape
    /// those lines have rather than on a Markdown parse, for the reason the other document reads
    /// in this project are: one dependency for two lines, in a file every reader of the release
    /// route already reads by eye.
    ///
    /// Both reads fail loudly on finding nothing. A line renamed out of the runbook would
    /// otherwise turn each assertion above into a comparison against an empty string, which is
    /// the state the whole section going missing leaves.
    /// </summary>
    internal static class Runbook
    {
        private const string Path = "docs/RELEASING.md";

        /// <summary>
        /// Reads the gate this repository ships rather than a copy of it.
        /// </summary>
        /// <returns>What the runbook declares the gate to be.</returns>
        internal static string GateOfThisRepository() => Gate(OfThisRepository());

        /// <summary>
        /// Reads the wait this repository ships rather than a copy of it.
        /// </summary>
        /// <returns>What the runbook declares a missing runtime to cost.</returns>
        internal static string WaitOfThisRepository() => Wait(OfThisRepository());

        /// <summary>
        /// Reads the declared gate out of runbook text.
        /// </summary>
        /// <param name="text">The runbook text.</param>
        /// <returns>What it declares the gate to be.</returns>
        internal static string Gate(string text) => Declared(text, "Harness gate");

        /// <summary>
        /// Reads the declared answer for a missing runtime out of runbook text.
        /// </summary>
        /// <param name="text">The runbook text.</param>
        /// <returns>What it declares a missing runtime to cost.</returns>
        internal static string Wait(string text) => Declared(text, "No container runtime");

        /// <summary>
        /// Reads a fixture from the tracked file rather than from a copy in the output directory,
        /// because a copy proves the state of the file on the day it was copied.
        /// </summary>
        /// <param name="name">The file name.</param>
        /// <returns>The fixture text.</returns>
        internal static string Fixture(string name) =>
            File.ReadAllText(System.IO.Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                "Jellyfin.Plugin.WatchSync.Tests",
                "Release",
                name));

        private static string OfThisRepository() =>
            File.ReadAllText(System.IO.Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                Path));

        private static string Declared(string text, string key)
        {
            var match = Regex.Match(text, @"(?m)^\s+" + Regex.Escape(key) + @": (?<answer>[^\r\n]+?)\s*$");

            if (!match.Success)
            {
                Assert.Fail($"No line declaring {key} was found. The read is anchored on that key in the release runbook and the shape has changed, so nothing here is judging what the runbook says.");
            }

            return match.Groups["answer"].Value;
        }
    }
}
