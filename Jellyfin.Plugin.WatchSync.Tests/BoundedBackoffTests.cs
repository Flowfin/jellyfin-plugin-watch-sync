using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Peer;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The wait between attempts on a peer that is failing, which is the second condition of #53: a
/// sequence of failures reaches the ceiling and stays there.
///
/// The condition names two properties that fail in opposite directions and a set written against
/// only one of them passes while the other is broken. A wait that grows without a ceiling is
/// caught by asking whether anything ever exceeds it. A wait that stops growing BEFORE the
/// ceiling, which is what an implementation that abandons the doubling as soon as the next one
/// would pass it produces, is invisible to that question and is what the reached assertions are
/// for.
///
/// Nothing here sleeps and nothing here reads a clock. The run of failures is a count and the
/// answer is a span, which is the headless rule in
/// <c>Jellyfin.Plugin.WatchSync.Tests/headless-rule.md</c>: a backoff proven by waiting for one is
/// the test that gets deleted the first time it is flaky.
/// </summary>
public class BoundedBackoffTests
{
    private static readonly TimeSpan _first = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _ceiling = TimeSpan.FromMinutes(30);

    /// <summary>
    /// A peer with nothing behind it is not backing off, and that is a different answer from a
    /// wait of no time.
    ///
    /// The two are collapsed by any rule that returns a span alone, and what is lost is the case
    /// worth seeing: a retry loop running with no interval in it answers zero as well, and a
    /// caller reading only the span cannot tell it from a peer that is working.
    /// </summary>
    [Fact]
    public void NothingHasFailedIsNotAWaitOfNoTime()
    {
        var answered = BoundedBackoff.After(0, _first, _ceiling);

        Assert.Equal(BackoffAnswer.NothingHasFailed, answered.Answer);
        Assert.Equal(TimeSpan.Zero, answered.Wait);
        Assert.False(answered.AtTheCeiling);
    }

    /// <summary>
    /// The first failure waits the first wait, rather than a double of it.
    ///
    /// It is the off-by-one the doubling invites, and it is invisible in every other fact here: a
    /// sequence starting one step along still grows, still reaches the ceiling and still stays
    /// there, and the only thing that moves is the interval a peer that failed once is asked
    /// again at, which doubles.
    /// </summary>
    [Fact]
    public void TheFirstFailureWaitsTheFirstWait()
    {
        var answered = BoundedBackoff.After(1, _first, _ceiling);

        Assert.Equal(_first, answered.Wait);
        Assert.Equal(BackoffAnswer.Growing, answered.Answer);
    }

    /// <summary>
    /// The whole condition, driven as a sequence rather than asserted at points.
    ///
    /// Forty consecutive failures, which is far past where the ceiling arrives, so what the fact
    /// reads is the shape of the run and not three sampled values. Four properties are asked of
    /// it: nothing exceeds the ceiling, the wait grows strictly while it is below it, the ceiling
    /// is reached exactly, and every failure after that is answered with the same wait.
    ///
    /// The third is the one a ceiling assertion alone does not carry. An implementation that stops
    /// doubling as soon as the next double would pass the ceiling never exceeds it and never
    /// reaches it, so it settles at whatever interval happened to be the last one underneath -
    /// here that would be sixteen minutes rather than thirty, and a failing peer would be asked
    /// nearly twice as often as anybody chose.
    /// </summary>
    [Fact]
    public void TheWaitDoublesUntilItReachesTheCeilingAndThenStaysThere()
    {
        var waits = Enumerable
            .Range(1, 40)
            .Select(failures => BoundedBackoff.After(failures, _first, _ceiling))
            .ToList();

        Assert.All(waits, answered => Assert.True(
            answered.Wait <= _ceiling,
            $"a run of failures was answered with {answered.Wait}, which is past the ceiling of {_ceiling}"));

        var reached = waits.FindIndex(answered => answered.AtTheCeiling);

        Assert.True(reached >= 0, $"forty consecutive failures never reached the ceiling of {_ceiling}, so the wait settles below it at whatever the last double under it was");

        Assert.Equal(_ceiling, waits[reached].Wait);

        for (var i = 1; i < reached; i++)
        {
            Assert.True(
                waits[i].Wait > waits[i - 1].Wait,
                $"failure {i + 1} was answered with {waits[i].Wait} and failure {i} with {waits[i - 1].Wait}, so the wait is not growing below the ceiling");
        }

        Assert.All(waits.Skip(reached), answered =>
        {
            Assert.Equal(_ceiling, answered.Wait);
            Assert.Equal(BackoffAnswer.AtTheCeiling, answered.Answer);
        });
    }

    /// <summary>
    /// The ceiling is reached exactly where it is not a doubling of the first wait.
    ///
    /// Thirty seconds doubling towards thirty minutes passes through 960 seconds and would reach
    /// 1920, so the ceiling is never a value the doubling lands on and the clamp is what puts it
    /// there. A fact driven only against a ceiling the doubling happens to hit would be green
    /// under an implementation with no clamp in it at all.
    /// </summary>
    [Fact]
    public void TheCeilingIsReachedEvenThoughTheDoublingWouldStepOverIt()
    {
        var lastBelow = BoundedBackoff.After(6, _first, _ceiling);
        var atIt = BoundedBackoff.After(7, _first, _ceiling);

        Assert.Equal(TimeSpan.FromSeconds(960), lastBelow.Wait);
        Assert.Equal(BackoffAnswer.Growing, lastBelow.Answer);

        Assert.Equal(_ceiling, atIt.Wait);
        Assert.Equal(BackoffAnswer.AtTheCeiling, atIt.Answer);
    }

    /// <summary>
    /// A count no peer will reach is still answered with the ceiling.
    ///
    /// This is the arithmetic rather than the rule, and it is the failure the shorter spelling
    /// produces: a wait computed as the first one shifted by the count wraps at the sixty-fourth
    /// failure, and a peer that has been unreachable for a week is then answered with a wait of no
    /// time and asked as fast as the loop goes round. Nothing about the sequence facts above sees
    /// it, because none of them counts that high.
    /// </summary>
    [Fact]
    public void ARunNobodyWillReachIsStillAnsweredWithTheCeiling()
    {
        foreach (var failures in new[] { 63, 64, 65, 1000, int.MaxValue })
        {
            var answered = BoundedBackoff.After(failures, _first, _ceiling);

            Assert.Equal(_ceiling, answered.Wait);
            Assert.Equal(BackoffAnswer.AtTheCeiling, answered.Answer);
        }
    }

    /// <summary>
    /// A success puts the peer back where it started, because the rule holds nothing between
    /// calls.
    ///
    /// The reset is the caller passing zero rather than an operation here, and that is what makes
    /// the rule the same shape as every other one in this tree: the state lives with whatever
    /// keeps the peer, and this answers a question about it.
    /// </summary>
    [Fact]
    public void ASuccessAfterAFailingRunIsAnsweredAsAPeerWithNothingBehindIt()
    {
        Assert.Equal(_ceiling, BoundedBackoff.After(20, _first, _ceiling).Wait);
        Assert.Equal(TimeSpan.Zero, BoundedBackoff.After(0, _first, _ceiling).Wait);
    }

    /// <summary>
    /// The defaults reach the ceiling at the seventh failure, which is what the document states.
    ///
    /// The number is here rather than only in prose because it is the one an operator reasons
    /// about: it is how long a peer has to be unreachable before this plugin settles into asking
    /// once every half hour, and it is a little over half an hour of failures.
    /// </summary>
    [Fact]
    public void TheDefaultsSettleAtTheSeventhConsecutiveFailure()
    {
        Assert.False(BoundedBackoff
            .After(6, BoundedBackoff.DefaultFirstWait, BoundedBackoff.DefaultCeiling)
            .AtTheCeiling);

        Assert.True(BoundedBackoff
            .After(7, BoundedBackoff.DefaultFirstWait, BoundedBackoff.DefaultCeiling)
            .AtTheCeiling);
    }

    /// <summary>
    /// The default first wait spends a small share of what the layer below admits.
    ///
    /// The plane admits a bounded number of arrivals per claimed pairing identifier inside a
    /// window of its own, refuses the rest before it verifies anything, and counts every request
    /// type against the same allowance. So the shortest wait this rule produces is the one that
    /// decides whether a failing pairing spends its own allowance: at a wait near the plane's
    /// window, retrying is a fraction of what is admitted, and at a wait far below it the pairing
    /// is refused for arriving too often and an operator meets a refusal that says nothing about a
    /// peer being down.
    ///
    /// It is held against the SHORTEST wait rather than against the ceiling, because the ceiling
    /// is the easy direction: a longer wait is always further under the allowance. The reading of
    /// the plane's two numbers lives on <see cref="EnvelopeBounds"/> with the commit it was taken
    /// at, and is not copied here.
    ///
    /// THE ALLOWANCE IS A DEFAULT AND THIS SAYS NOTHING ABOUT A PEER THAT HAS MOVED IT. The
    /// operator of the receiving server may set it as low as one arrival an hour, and no wait
    /// chosen here sits under that. What is held is the relation at the published default, which
    /// is the configuration a peer that has chosen nothing is in.
    /// </summary>
    [Fact]
    public void TheDefaultFirstWaitSpendsASmallShareOfThePlanesDefaultArrivalAllowance()
    {
        var retriesInThePlanesWindow =
            EnvelopeBounds.TransportArrivalWindowSeconds / BoundedBackoff.DefaultFirstWait.TotalSeconds;

        Assert.True(
            retriesInThePlanesWindow * 8 <= EnvelopeBounds.TransportArrivalsPerPairing,
            string.Create(
                CultureInfo.InvariantCulture,
                $"a first wait of {BoundedBackoff.DefaultFirstWait.TotalSeconds} seconds is {retriesInThePlanesWindow} retries inside the plane's {EnvelopeBounds.TransportArrivalWindowSeconds} second window, against {EnvelopeBounds.TransportArrivalsPerPairing} arrivals it admits there, which is not a small share of it"));
    }

    /// <summary>
    /// A first wait below the shortest one the rule accepts is refused rather than quietly raised.
    ///
    /// Raising it would be the rule deciding, in passing, what an operator meant by a number they
    /// typed, which is the shape #61 exists against everywhere else in this tree.
    /// </summary>
    [Fact]
    public void AFirstWaitBelowTheShortestOneIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BoundedBackoff.After(
            1,
            BoundedBackoff.ShortestFirstWait - TimeSpan.FromTicks(1),
            _ceiling));

        Assert.Equal(
            BoundedBackoff.ShortestFirstWait,
            BoundedBackoff.After(1, BoundedBackoff.ShortestFirstWait, _ceiling).Wait);
    }

    /// <summary>
    /// A ceiling above the longest one the rule accepts is refused.
    ///
    /// The far end is the one that reads as caution and is not. A ceiling of a day means a peer
    /// that came back a minute into the wait syncs tomorrow, which is the failure the ceiling
    /// exists against reached through the ceiling rather than around it.
    /// </summary>
    [Fact]
    public void ACeilingAboveTheLongestOneIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BoundedBackoff.After(
            1,
            _first,
            BoundedBackoff.LongestCeiling + TimeSpan.FromTicks(1)));

        Assert.Equal(
            BoundedBackoff.LongestCeiling,
            BoundedBackoff.After(99, _first, BoundedBackoff.LongestCeiling).Wait);
    }

    /// <summary>
    /// A ceiling below the first wait is refused as a pair that cannot both be meant.
    ///
    /// It is the pair a caller reaches by moving one number and not the other, and it is not
    /// nonsense on its face: it answers, and what it answers is the ceiling at every failure, so
    /// the doubling this rule is has no effect anybody could observe.
    /// </summary>
    [Fact]
    public void ACeilingBelowTheFirstWaitIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BoundedBackoff.After(
            1,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(1)));

        Assert.Equal(
            BackoffAnswer.AtTheCeiling,
            BoundedBackoff.After(1, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5)).Answer);
    }

    /// <summary>
    /// A negative count is refused rather than read as a peer with nothing behind it.
    ///
    /// It is what a caller that subtracted from a count instead of resetting it hands over, and
    /// reading it as zero would answer a peer that is failing as a peer that is working.
    /// </summary>
    [Fact]
    public void ANegativeRunOfFailuresIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BoundedBackoff.After(-1, _first, _ceiling));
    }

    /// <summary>
    /// The two numbers the transfer document states are the two numbers the rule uses.
    ///
    /// #53 asks that the backoff have a ceiling and that its default be documented, and a default
    /// written in two places drifts in the direction that costs most: the number a person reads
    /// before deciding whether to change anything is the one in the document, and the number a
    /// server behaves by is the one in the type. The row is resolved rather than read, so a
    /// document saying one thing while the rule does another reddens here instead of being found
    /// by a reader.
    /// </summary>
    [Fact]
    public void TheTransferDocumentStatesTheNumbersTheRuleUses()
    {
        var stated = BackoffRows();

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["the first wait"] = "30 seconds",
                ["the ceiling"] = "30 minutes",
            },
            stated);
    }

    /// <summary>
    /// The numbers the document states are the numbers the type declares, resolved rather than
    /// compared as text.
    ///
    /// The fact above holds the document still and this one holds it against the rule, and the two
    /// together are what stops either end moving alone. A default changed in the type with the
    /// table left alone reddens here; a table edited with the type left alone reddens above.
    /// </summary>
    [Fact]
    public void TheStatedNumbersAreTheOnesTheRuleDeclares()
    {
        var stated = BackoffRows();

        Assert.Equal(
            string.Create(CultureInfo.InvariantCulture, $"{BoundedBackoff.DefaultFirstWait.TotalSeconds:0} seconds"),
            stated["the first wait"]);

        Assert.Equal(
            string.Create(CultureInfo.InvariantCulture, $"{BoundedBackoff.DefaultCeiling.TotalMinutes:0} minutes"),
            stated["the ceiling"]);
    }

    /// <summary>
    /// Reads the backoff table out of the transfer document, scoped to the section that declares
    /// it.
    ///
    /// The read is scoped to the section rather than to the file, because that document carries
    /// several tables and a row elsewhere naming a span is not this declaration.
    /// </summary>
    /// <returns>What each named number is stated as.</returns>
    private static Dictionary<string, string> BackoffRows()
    {
        const string Heading = "## The wait after a failure";

        var document = File.ReadAllText(Path.Combine(
            InvariantGuardTests.InvariantGuard.RepositoryRoot(),
            "docs",
            "transfer.md"));

        var start = document.IndexOf(Heading, StringComparison.Ordinal);

        Assert.True(start >= 0, $"docs/transfer.md carries no section headed \"{Heading}\", so the numbers the rule uses are stated nowhere an operator reads.");

        var rest = document[(start + Heading.Length)..];
        var end = rest.IndexOf("\n## ", StringComparison.Ordinal);
        var section = end < 0 ? rest : rest[..end];

        return Regex
            .Matches(section, "(?m)^\\|\\s*(?<name>[^|]+?)\\s*\\|\\s*(?<value>[^|]+?)\\s*\\|")
            .Where(match => match.Groups["name"].Value is "the first wait" or "the ceiling")
            .ToDictionary(
                match => match.Groups["name"].Value,
                match => match.Groups["value"].Value,
                StringComparer.Ordinal);
    }
}
