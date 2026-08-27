using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// #77's first condition, made decidable: a case written with the two-server harness fits on a
/// screen.
///
/// The condition reads as a matter of taste and it is not one. The harness exists so that a
/// property about two servers is an ordinary case rather than a page of setup, and a case that
/// does not fit is the measurement that says the harness is missing something the case had to do
/// for itself. Left to a reading, that measurement is taken once, by whoever wrote the harness,
/// on the day the harness had nothing written against it.
///
/// What it cannot see: how much a case body says per line. A case can be inside the bound and
/// still be three cases wearing one name, and no reading of the source separates those. The
/// review is where that is caught, which is the same place every judgement about meaning in this
/// repository is caught.
/// </summary>
public sealed class HarnessCaseSizeTests
{
    /// <summary>
    /// The whole point, run against the tree as it is.
    /// </summary>
    [Fact]
    public void EveryCaseWrittenWithTheHarnessFitsOnAScreen()
    {
        var report = HarnessCases.ScanTheTree();

        Assert.Empty(report.TooLong.Select(finding =>
            $"{finding.Path}:{finding.Line} {finding.Name} is {finding.Lines} lines, and the bound is {finding.Bound}."));
    }

    /// <summary>
    /// A guard that found nothing because it was looking at nothing is a guard that passes
    /// forever. The subject is a call rather than a file name, so this asserts the scan actually
    /// reached the cases it is for.
    /// </summary>
    [Fact]
    public void TheScanReachesTheCasesWrittenWithTheHarness()
    {
        var report = HarnessCases.ScanTheTree();

        Assert.Contains(
            report.Examined,
            examined => examined.Path.EndsWith("LinkFaultTests.cs", StringComparison.Ordinal));

        Assert.Contains(
            report.Examined,
            examined => examined.Name == "ADroppedBodyNeverArrivesOnAnyDelivery");
    }

    /// <summary>
    /// The guard proven by the mistake somebody actually makes. The near-miss is a case that is
    /// one line over the bound and correct in every other way, which is what makes it a near-miss
    /// rather than an obviously broken file, and the repair is the assertion about what the link
    /// dropped moved to the case that is about dropping.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheNearMissAndPassesItsRepair()
    {
        var refused = HarnessCases.Scan(
            new[] { ("Harness/near-miss.txt", HarnessCases.Fixture("near-miss.txt")) },
            HarnessCases.Subject(),
            HarnessCases.Bound());

        var finding = Assert.Single(refused.TooLong);
        Assert.Equal(HarnessCases.Bound() + 1, finding.Lines);

        var repaired = HarnessCases.Scan(
            new[] { ("Harness/near-miss-repaired.txt", HarnessCases.Fixture("near-miss-repaired.txt")) },
            HarnessCases.Subject(),
            HarnessCases.Bound());

        Assert.Empty(repaired.TooLong);
        Assert.NotEmpty(repaired.Examined);
    }

    /// <summary>
    /// A case that does not use the harness is not this guard's business, and a guard that
    /// bounded every case in the suite would be this file deciding how the rest of it is written.
    /// </summary>
    [Fact]
    public void ACaseThatDoesNotUseTheHarnessIsNotExamined()
    {
        var report = HarnessCases.Scan(
            new[] { ("Harness/near-miss.txt", HarnessCases.Fixture("near-miss.txt")) },
            "SomethingElse.Create(",
            HarnessCases.Bound());

        Assert.Empty(report.Examined);
        Assert.Empty(report.TooLong);
    }

    /// <summary>
    /// The two values the guard runs on come out of the file beside the harness rather than out
    /// of this source, so raising the bound is an edit somebody makes where the reason for the
    /// number is written.
    /// </summary>
    [Fact]
    public void TheSubjectAndTheBoundAreReadFromTheFileThatArguesThem()
    {
        Assert.Equal(24, HarnessCases.Bound());
        Assert.EndsWith(".Create(", HarnessCases.Subject(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the cases a file holds and how long each one is.
    ///
    /// The reading is line-based rather than a parse. Every source in this project is laid out
    /// the same way, a case is a method at one indentation inside a class, and a parser would be
    /// a dependency and a second definition of what a case is. What it costs is written where the
    /// scan is: a file laid out some other way is read as holding no case at all, which is why
    /// <see cref="TheScanReachesTheCasesWrittenWithTheHarness"/> asserts the scan found the ones
    /// this repository has rather than only that it found nothing wrong.
    /// </summary>
    internal static class HarnessCases
    {
        private const string TestProject = "Jellyfin.Plugin.WatchSync.Tests";
        private const string CaseMarker = "[Fact]";
        private const string BodyOpens = "    {";
        private const string BodyCloses = "    }";

        /// <summary>
        /// One case that is longer than the bound.
        /// </summary>
        /// <param name="Path">The file it is in.</param>
        /// <param name="Name">What the case is called.</param>
        /// <param name="Line">The line its body opens on.</param>
        /// <param name="Lines">How long the body is.</param>
        /// <param name="Bound">What the bound was.</param>
        internal sealed record Finding(string Path, string Name, int Line, int Lines, int Bound);

        /// <summary>
        /// One case the scan looked at.
        /// </summary>
        /// <param name="Path">The file it is in.</param>
        /// <param name="Name">What the case is called.</param>
        /// <param name="Lines">How long the body is.</param>
        internal sealed record Examined(string Path, string Name, int Lines);

        /// <summary>
        /// What one scan found.
        /// </summary>
        /// <param name="Examined">Every case written with the harness that the scan reached.</param>
        /// <param name="TooLong">The ones over the bound.</param>
        internal sealed record Report(IReadOnlyList<Examined> Examined, IReadOnlyList<Finding> TooLong);

        /// <summary>
        /// Runs the scan over the tracked test sources of this repository.
        /// </summary>
        /// <returns>The report.</returns>
        internal static Report ScanTheTree() =>
            Scan(
                HeadlessGuardTests.HeadlessGuard.TestSources(HeadlessGuardTests.HeadlessGuard.RepositoryRoot()),
                Subject(),
                Bound());

        /// <summary>
        /// The call that says a case was written with the harness.
        /// </summary>
        /// <returns>The subject.</returns>
        internal static string Subject() => Field("subject");

        /// <summary>
        /// How many lines a case body may hold.
        /// </summary>
        /// <returns>The bound.</returns>
        internal static int Bound() => int.Parse(Field("bound"), CultureInfo.InvariantCulture);

        /// <summary>
        /// Reads one of the fixtures beside the harness.
        /// </summary>
        /// <param name="name">The fixture's file name.</param>
        /// <returns>Its text.</returns>
        internal static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(
                HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                TestProject,
                "Harness",
                name));

        /// <summary>
        /// Reads every case a set of sources holds and measures the ones written with the
        /// harness.
        /// </summary>
        /// <param name="sources">The path and text of each source.</param>
        /// <param name="subject">The call that says a case was written with the harness.</param>
        /// <param name="bound">How many lines a case body may hold.</param>
        /// <returns>The report.</returns>
        internal static Report Scan(
            IEnumerable<(string Path, string Text)> sources,
            string subject,
            int bound)
        {
            ArgumentNullException.ThrowIfNull(sources);

            var examined = new List<Examined>();
            var tooLong = new List<Finding>();

            foreach (var source in sources)
            {
                foreach (var found in CasesIn(source.Text))
                {
                    if (!found.Body.Contains(subject, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    examined.Add(new Examined(source.Path, found.Name, found.Lines));

                    if (found.Lines > bound)
                    {
                        tooLong.Add(new Finding(source.Path, found.Name, found.Line, found.Lines, bound));
                    }
                }
            }

            return new Report(examined, tooLong);
        }

        private static IEnumerable<(string Name, string Body, int Line, int Lines)> CasesIn(string text)
        {
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

            for (var at = 0; at < lines.Length; at++)
            {
                if (lines[at].Trim() != CaseMarker)
                {
                    continue;
                }

                var opens = IndexOfLine(lines, at + 1, BodyOpens);
                var closes = opens < 0 ? -1 : IndexOfLine(lines, opens + 1, BodyCloses);

                if (closes < 0)
                {
                    continue;
                }

                yield return (
                    NameBetween(lines, at + 1, opens),
                    string.Join("\n", lines[(opens + 1)..closes]),
                    opens + 1,
                    closes - opens - 1);
            }
        }

        private static int IndexOfLine(string[] lines, int from, string exactly)
        {
            for (var at = from; at < lines.Length; at++)
            {
                if (lines[at].TrimEnd() == exactly)
                {
                    return at;
                }
            }

            return -1;
        }

        private static string NameBetween(string[] lines, int from, int to)
        {
            for (var at = from; at < to; at++)
            {
                var opens = lines[at].IndexOf('(', StringComparison.Ordinal);
                var words = lines[at][..(opens < 0 ? lines[at].Length : opens)].Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (opens >= 0 && words.Length > 0)
                {
                    return words[^1];
                }
            }

            return "an unnamed case";
        }

        private static string Field(string name)
        {
            var wanted = name + " :: ";

            var line = File.ReadAllLines(Path.Combine(
                    HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
                    TestProject,
                    "Harness",
                    "case-bound.txt"))
                .Select(each => each.Trim())
                .FirstOrDefault(each => each.StartsWith(wanted, StringComparison.Ordinal));

            Assert.NotNull(line);

            return line![wanted.Length..];
        }
    }
}
