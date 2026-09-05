using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.WatchSync.Model;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What the suite proves about the fuzz harness, which is #102, and what it deliberately does
/// not.
///
/// It proves the machinery: that the seed corpus and the bodies the envelope cases hand the
/// reader are one set of bytes, that every rule the oracle carries refuses a reader that breaks
/// it, and that a sweep reports what it found and reproduces from its two numbers.
///
/// It does not sweep the reader this plugin ships beyond the seeds. A sweep in the suite would
/// be a fuzz run gating every merge, which is what #102's second condition refuses, and it would
/// redden a pull request that touched none of this. So a crasher is found by the scheduled run
/// and never here, and nothing in this file is evidence that the reader has none.
/// </summary>
public class EnvelopeFuzzTests
{
    /// <summary>
    /// The corpus is the bodies the envelope cases already hand the reader, and nothing else.
    ///
    /// Closed in both directions. A body added to a case and not seeded is a fact the harness
    /// starts blind to; a seed no case hands over is a second set of bytes drifting from the
    /// first, which is what this issue asks be avoided by lifting the corpus out of the cases
    /// rather than writing one beside them.
    /// </summary>
    [Fact]
    public void TheCorpusAndTheEnvelopeCasesAreOneSetOfBytes()
    {
        var seeded = EnvelopeCorpus.Seeds();
        var handed = EnvelopeCorpus.BodiesIn(EnvelopeCorpus.EnvelopeCaseSource());

        Assert.NotEmpty(seeded);
        Assert.NotEmpty(handed);

        Assert.Empty(handed.Except(seeded, StringComparer.Ordinal));
        Assert.Empty(seeded.Except(handed, StringComparer.Ordinal));
    }

    /// <summary>
    /// The corpus guard proven by the mistake somebody makes: a case gains a body and the corpus
    /// does not, so a run starts from a set the suite has already moved past.
    ///
    /// Both fixtures are data rather than source, for the reason the headless guard's are: a
    /// fixture written as a case would be scanned as one, and the near-miss would then be a
    /// finding against the tree instead of against the fixture.
    /// </summary>
    [Fact]
    public void TheCorpusGuardRefusesTheNearMissAndPassesItsRepair()
    {
        var seeded = EnvelopeCorpus.Seeds();

        var refused = EnvelopeCorpus.BodiesIn(EnvelopeCorpus.Fixture("near-miss.txt"))
            .Except(seeded, StringComparer.Ordinal)
            .ToList();

        var body = Assert.Single(refused);
        Assert.Contains("aBodyTheCorpusDoesNotCarry", body, StringComparison.Ordinal);

        var repaired = EnvelopeCorpus.BodiesIn(EnvelopeCorpus.Fixture("near-miss-repaired.txt"))
            .Except(seeded, StringComparer.Ordinal);

        Assert.Empty(repaired);
    }

    /// <summary>
    /// The guard reads the cases out of the tree rather than assuming a file name, so a case
    /// file that moved is a red suite rather than a guard quietly judging an empty string.
    /// </summary>
    [Fact]
    public void TheGuardReadsTheCasesFromTheTreeRatherThanAssumingThem()
    {
        var source = EnvelopeCorpus.EnvelopeCaseSource();

        Assert.NotEmpty(source);
        Assert.Contains("Envelope.Read(", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every rule the oracle carries, proven on a reader that breaks exactly that rule.
    ///
    /// This is the leg that decides whether a run is worth anything. An oracle is only ever
    /// exercised by inputs that satisfy it, so one that has quietly stopped asking looks exactly
    /// like a surface with no defects, and ten million inputs report the same clean sheet either
    /// way.
    /// </summary>
    /// <param name="rule">The rule the broken reader is meant to trip.</param>
    [Theory]
    [InlineData("reader-threw")]
    [InlineData("reader-answered-nothing")]
    [InlineData("refused-carries-an-envelope")]
    [InlineData("readable-carries-no-envelope")]
    [InlineData("reading-names-no-supported-set")]
    [InlineData("version-not-supported-names-no-version")]
    [InlineData("member-missing-names-no-member")]
    [InlineData("member-carried-twice-names-no-member")]
    [InlineData("not-an-envelope-names-a-version")]
    [InlineData("readable-version-is-not-spoken")]
    [InlineData("readable-keeps-the-version-member")]
    [InlineData("readable-misses-a-required-member")]
    public void EveryOracleRuleRefusesAReaderThatBreaksIt(string rule)
    {
        var judged = EnvelopeFuzz.Judge(BrokenReaders.BodyFor(rule), BrokenReaders.For(rule));

        Assert.Contains(rule, judged.Findings.Select(finding => finding.Rule), StringComparer.Ordinal);
    }

    /// <summary>
    /// The bounds rules proven the same way, on a body whose own measurements disagree with the
    /// answer a reader would have to accept.
    ///
    /// The bounds are arithmetic and cannot be handed a broken implementation the way the reader
    /// can, so what is proven here is the other half: that the harness measures the three
    /// quantities off the bytes and asks, rather than passing zeroes and always agreeing.
    /// </summary>
    [Fact]
    public void TheBoundsLegMeasuresTheBytesRatherThanAssumingThem()
    {
        var overTheStringBound = "{\"version\":1,\"changes\":[\""
            + new string('k', EnvelopeBounds.LongestStringLength + 1)
            + "\"]}";

        var judged = EnvelopeFuzz.Judge(overTheStringBound, EnvelopeFuzz.TheRealReader());

        Assert.Empty(judged.Findings);
        Assert.Equal(EnvelopeBoundsAnswer.AStringIsTooLong.ToString(), BoundsAnswerIn(judged.Answer));

        var within = EnvelopeFuzz.Judge("{\"version\":1,\"changes\":[]}", EnvelopeFuzz.TheRealReader());

        Assert.Equal(EnvelopeBoundsAnswer.Within.ToString(), BoundsAnswerIn(within.Answer));
    }

    /// <summary>
    /// The same oracle over the reader this plugin ships, on the seeds and on nothing generated.
    ///
    /// It is the positive leg of the pair above: the rules that refuse eleven broken readers
    /// refuse nothing here, so a finding out of a scheduled run is a statement about the input
    /// rather than about an oracle that refuses whatever it meets.
    /// </summary>
    [Fact]
    public void TheRealReaderBreaksNoneOfThoseRulesOnTheSeeds()
    {
        var findings = EnvelopeFuzz
            .Run(EnvelopeCorpus.Seeds(), 0, 1, EnvelopeFuzz.TheRealReader())
            .Findings;

        Assert.Empty(findings.Select(finding => $"{finding.Rule}: {finding.Detail} on {finding.Body}"));
    }

    /// <summary>
    /// A sweep judges the seeds and its mutations, carries what it found out of the loop, and
    /// keeps a corpus.
    ///
    /// The reader is a broken one on purpose, so this leg is about the loop and can never redden
    /// because the plugin's own reader changed. What a run finds against the real reader is the
    /// scheduled job's business.
    /// </summary>
    [Fact]
    public void ASweepJudgesTheSeedsAndItsMutationsAndCarriesWhatItFound()
    {
        var seeds = EnvelopeCorpus.Seeds();

        var sweep = EnvelopeFuzz.Run(seeds, 200, 7, BrokenReaders.For("reader-threw"));

        Assert.Equal(seeds.Count + 200, sweep.Inputs);
        Assert.NotEmpty(sweep.Findings);
        Assert.All(sweep.Findings, finding => Assert.Equal("reader-threw", finding.Rule));
        Assert.NotEmpty(sweep.Corpus);
    }

    /// <summary>
    /// A run reaches the byte bound, and reaches it on that bound alone.
    ///
    /// The fifth condition of #19 asks that the refusal path be exercised by the harness rather
    /// than only by the cases, and <c>TooManyBytes</c> was the member of that set no run could
    /// produce. What kept it out of reach was the mutation set rather than the iteration count:
    /// every other shape is capped well below a quarter of a mebibyte by its own arithmetic, so a
    /// longer run met the same ceiling.
    ///
    /// The second half is what stops this being satisfied by a body that is simply enormous. The
    /// byte bound is judged before the change count and before the string length, so a body past
    /// all three answers <c>TooManyBytes</c> without the byte bound having decided anything. The
    /// body found here is under the other two, which is asserted rather than described.
    /// </summary>
    [Fact]
    public void ARunReachesTheByteBoundAndReachesItOnThatBoundAlone()
    {
        var sweep = EnvelopeFuzz.Run(EnvelopeCorpus.Seeds(), 64, 3, EnvelopeFuzz.TheRealReader());

        var reached = sweep.Corpus
            .Where(body => string.Equals(
                BoundsAnswerIn(EnvelopeFuzz.Judge(body, EnvelopeFuzz.TheRealReader()).Answer),
                EnvelopeBoundsAnswer.TooManyBytes.ToString(),
                StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(reached);

        var found = reached[0];

        Assert.True(Encoding.UTF8.GetByteCount(found) > EnvelopeBounds.MaximumBytes);

        using var read = JsonDocument.Parse(found);

        var changes = read.RootElement.GetProperty("changes");

        Assert.True(changes.GetArrayLength() <= EnvelopeBounds.MaximumChanges);

        foreach (var change in changes.EnumerateArray())
        {
            foreach (var member in change.EnumerateObject())
            {
                Assert.True(member.Value.GetString()!.Length <= EnvelopeBounds.LongestStringLength);
            }
        }
    }

    /// <summary>
    /// Every rule the BODY oracle carries, proven on a reader that breaks exactly that rule.
    ///
    /// The same leg as the one above and for the same reason. These rules are the ones about how
    /// much of what a peer is sending this side takes at all, which is #19's second condition, and
    /// an oracle over them that has quietly stopped asking reports the same clean sheet as a
    /// bounded reader.
    /// </summary>
    /// <param name="rule">The rule the broken reader is meant to trip.</param>
    [Theory]
    [InlineData("body-reader-threw")]
    [InlineData("body-reader-answered-nothing")]
    [InlineData("refused-body-carries-text")]
    [InlineData("read-body-carries-no-text")]
    [InlineData("the-declaration-was-not-carried")]
    [InlineData("too-many-bytes-names-no-bound")]
    [InlineData("bound-named-where-no-bound-refused")]
    [InlineData("declared-past-the-bound-was-read-anyway")]
    [InlineData("a-body-inside-the-bound-was-refused-for-its-length")]
    [InlineData("the-text-is-not-the-bytes-that-arrived")]
    [InlineData("body-read-past-the-bound")]
    public void EveryBodyOracleRuleRefusesAReaderThatBreaksIt(string rule)
    {
        var judged = EnvelopeFuzz.Judge(
            BrokenBodyReaders.BodyFor(rule),
            EnvelopeFuzz.TheRealReader(),
            BrokenBodyReaders.For(rule));

        Assert.Contains(rule, judged.Findings.Select(finding => finding.Rule), StringComparer.Ordinal);
    }

    /// <summary>
    /// The two rules a body of a known length cannot reach, proven on the leg that judges the peer
    /// that never stops sending.
    ///
    /// A reader whose stopping condition is the end of the stream rather than the bound passes
    /// every finite input, so this is the one place where the ceiling on what this side holds is
    /// asked at all.
    /// </summary>
    /// <param name="rule">The rule the broken reader is meant to trip.</param>
    [Theory]
    [InlineData("body-read-past-the-bound")]
    [InlineData("an-endless-body-was-not-refused")]
    [InlineData("body-reader-threw")]
    public void EveryCeilingRuleRefusesAReaderThatBreaksIt(string rule)
    {
        var findings = EnvelopeFuzz.JudgeTheCeiling(BrokenBodyReaders.For(rule));

        Assert.Contains(rule, findings.Select(finding => finding.Rule), StringComparer.Ordinal);
    }

    /// <summary>
    /// The body reader this plugin ships is refused by none of those, on a body that never ends.
    ///
    /// The positive half of the pair above: the ceiling leg refuses three broken readers and
    /// refuses nothing here, so a finding out of a scheduled run says something about the reader
    /// rather than about a leg that refuses whatever it meets.
    /// </summary>
    [Fact]
    public void TheRealBodyReaderIsRefusedByNoCeilingRule()
    {
        Assert.Empty(EnvelopeFuzz.JudgeTheCeiling(EnvelopeFuzz.TheRealBodyReader()));
    }

    /// <summary>
    /// A run reaches both refusals of the body reader, which is the fifth condition of #19 for the
    /// path that landed under its second.
    ///
    /// The two are reached from opposite ends and neither depends on the input, which is why they
    /// are asked of every one rather than waited for. A peer that declares more than the bound is
    /// refused with nothing read off it at all, and bytes that are not text are refused rather than
    /// repaired into text; before this the harness reached neither, and both were exercised only by
    /// the cases beside the reader.
    /// </summary>
    [Fact]
    public void ARunReachesBothRefusalsOfTheBodyReader()
    {
        var answers = BodyAnswersIn(
            EnvelopeFuzz.Judge("{\"version\":1,\"changes\":[]}", EnvelopeFuzz.TheRealReader()).Answer);

        Assert.Contains(EnvelopeBodyAnswer.Read.ToString(), answers, StringComparer.Ordinal);
        Assert.Contains(EnvelopeBodyAnswer.TooManyBytes.ToString(), answers, StringComparer.Ordinal);
        Assert.Contains(EnvelopeBodyAnswer.NotText.ToString(), answers, StringComparer.Ordinal);
    }

    /// <summary>
    /// A run reproduces from its two numbers, which is what a finding is reported with.
    ///
    /// A harness whose inputs cannot be reproduced hands whoever has to fix a crasher a body and
    /// no way back to it, and the first thing they do is write that body into a case, which is
    /// the second corpus the guard above exists to prevent.
    /// </summary>
    [Fact]
    public void TheSameNumbersProduceTheSameInputsAndDifferentOnesDoNot()
    {
        var seeds = EnvelopeCorpus.Seeds();

        var first = Mutations(seeds, 64, 11);
        var again = Mutations(seeds, 64, 11);
        var other = Mutations(seeds, 64, 12);

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
    }

    /// <summary>
    /// An empty corpus is a defect rather than a clean run.
    ///
    /// It is the trap this shape falls into: a harness handed nothing judges nothing, reports no
    /// finding, and is indistinguishable in every log from one that judged everything.
    /// </summary>
    [Fact]
    public void AnEmptyCorpusIsRefusedRatherThanSweptAndReportedClean()
    {
        Assert.Throws<ArgumentException>(
            () => EnvelopeFuzz.Run(Array.Empty<string>(), 10, 1, EnvelopeFuzz.TheRealReader()));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => EnvelopeFuzz.Run(EnvelopeCorpus.Seeds(), -1, 1, EnvelopeFuzz.TheRealReader()));
    }

    /// <summary>
    /// The bounds answer out of the key one input is archived under.
    ///
    /// The key names what every leg answered, so a reader of one leg takes its own field rather
    /// than matching the end of the string. Matching the end is what these two facts did until the
    /// body leg landed behind the bounds leg, and it is the shape that goes quietly wrong when a
    /// fifth leg arrives rather than failing.
    /// </summary>
    /// <param name="key">The answer key.</param>
    /// <returns>The bounds answer.</returns>
    private static string BoundsAnswerIn(string key) => key.Split('/')[2];

    /// <summary>
    /// The body answers out of the same key, one per declaration the harness hands over.
    /// </summary>
    /// <param name="key">The answer key.</param>
    /// <returns>The answers.</returns>
    private static IReadOnlyList<string> BodyAnswersIn(string key) => key.Split('/')[3].Split('|');

    private static IReadOnlyList<string> Mutations(IReadOnlyList<string> seeds, int count, int seed)
    {
        var random = new Random(seed);

        return Enumerable.Range(0, count).Select(_ => EnvelopeFuzz.Mutate(seeds, random)).ToList();
    }

    /// <summary>
    /// One reader per rule the oracle carries, each breaking that rule and honest otherwise.
    ///
    /// They are what the oracle is proven on. A rule nobody has watched refuse anything is a
    /// rule that may have stopped asking, and the run it sits in reports the same clean sheet
    /// either way.
    /// </summary>
    internal static class BrokenReaders
    {
        private const string Changes = "changes";

        private const string Version = "version";

        /// <summary>
        /// A body that reaches the rule's branch in a reader that is otherwise honest.
        /// </summary>
        /// <param name="rule">The rule.</param>
        /// <returns>The body.</returns>
        internal static string BodyFor(string rule) => rule switch
        {
            "version-not-supported-names-no-version" => "{\"version\":4,\"changes\":[]}",
            "member-missing-names-no-member" => "{\"version\":1}",
            "member-carried-twice-names-no-member" => "{\"version\":1,\"changes\":[],\"changes\":[]}",
            "not-an-envelope-names-a-version" => "{}",
            _ => "{\"version\":1,\"changes\":[]}",
        };

        /// <summary>
        /// The reader that breaks one rule.
        /// </summary>
        /// <param name="rule">The rule to break.</param>
        /// <returns>The reader.</returns>
        /// <exception cref="ArgumentOutOfRangeException">A rule nothing here breaks.</exception>
        internal static EnvelopeFuzz.Reader For(string rule) => rule switch
        {
            "reader-threw" => (_, _) => throw new InvalidOperationException("the reader gave way"),

            "reader-answered-nothing" => (_, _) => null!,

            "refused-carries-an-envelope" => (_, versions) => new EnvelopeFuzz.Observation(
                nameof(EnvelopeAnswer.VersionNotSupported), true, true, 4, null, null, versions, 4, new[] { Changes }),

            "readable-carries-no-envelope" => (_, versions) => new EnvelopeFuzz.Observation(
                nameof(EnvelopeAnswer.Readable), false, false, 1, null, null, versions, null, Array.Empty<string>()),

            "reading-names-no-supported-set" => (_, _) => new EnvelopeFuzz.Observation(
                nameof(EnvelopeAnswer.Readable), false, true, 1, null, null, Array.Empty<int>(), 1, new[] { Changes }),

            "version-not-supported-names-no-version" => (_, versions) => new EnvelopeFuzz.Observation(
                nameof(EnvelopeAnswer.VersionNotSupported), true, false, null, null, null, versions, null, Array.Empty<string>()),

            "member-missing-names-no-member" => (_, versions) => new EnvelopeFuzz.Observation(
                nameof(EnvelopeAnswer.MemberMissing), true, false, 1, string.Empty, null, versions, null, Array.Empty<string>()),

            "member-carried-twice-names-no-member" => (_, versions) => new EnvelopeFuzz.Observation(
                nameof(EnvelopeAnswer.MemberCarriedTwice), true, false, null, null, string.Empty, versions, null, Array.Empty<string>()),

            "not-an-envelope-names-a-version" => (_, versions) => new EnvelopeFuzz.Observation(
                nameof(EnvelopeAnswer.NotAnEnvelope), true, false, 1, null, null, versions, null, Array.Empty<string>()),

            "readable-version-is-not-spoken" => (_, versions) => new EnvelopeFuzz.Observation(
                nameof(EnvelopeAnswer.Readable), false, true, 99, null, null, versions, 99, new[] { Changes }),

            "readable-keeps-the-version-member" => (_, versions) => new EnvelopeFuzz.Observation(
                nameof(EnvelopeAnswer.Readable), false, true, 1, null, null, versions, 1, new[] { Version, Changes }),

            "readable-misses-a-required-member" => (_, versions) => new EnvelopeFuzz.Observation(
                nameof(EnvelopeAnswer.Readable), false, true, 1, null, null, versions, 1, Array.Empty<string>()),

            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "No broken reader is written for that rule."),
        };
    }

    /// <summary>
    /// One body reader per rule the body oracle carries, each breaking that rule.
    ///
    /// Most of them are the reader this plugin ships with one thing about its answer changed,
    /// rather than an answer invented from nothing. A reader written from scratch to trip a rule
    /// tends to trip four, and then the fact that it trips this one says less than it looks like
    /// it says.
    /// </summary>
    internal static class BrokenBodyReaders
    {
        /// <summary>
        /// A body that reaches the rule's branch in a reader that is otherwise honest.
        ///
        /// One rule needs a body past the bound, and it is the rule about how far a reader goes:
        /// a body with an end that is inside the bound cannot tell a reader that stops at the
        /// bound from one that stops at the end of the stream, because the two stop in the same
        /// place. Every other rule is reached by an ordinary envelope.
        /// </summary>
        /// <param name="rule">The rule.</param>
        /// <returns>The body.</returns>
        internal static string BodyFor(string rule) => rule switch
        {
            "body-read-past-the-bound" =>
                "{\"version\":1,\"changes\":[\"" + new string('k', EnvelopeBounds.MaximumBytes) + "\"]}",
            _ => "{\"version\":1,\"changes\":[]}",
        };

        /// <summary>
        /// The reader that breaks one rule.
        /// </summary>
        /// <param name="rule">The rule to break.</param>
        /// <returns>The reader.</returns>
        /// <exception cref="ArgumentOutOfRangeException">A rule nothing here breaks.</exception>
        internal static EnvelopeFuzz.BodyReader For(string rule)
        {
            var honest = EnvelopeFuzz.TheRealBodyReader();

            return rule switch
            {
                "body-reader-threw" => (_, _) => throw new InvalidOperationException("the body reader gave way"),

                "body-reader-answered-nothing" => (_, _) => null!,

                "refused-body-carries-text" => (body, declared) =>
                {
                    var seen = honest(body, declared);

                    return seen.IsRefused ? seen with { Text = "what was refused" } : seen;
                },

                "read-body-carries-no-text" => (body, declared) =>
                {
                    var seen = honest(body, declared);

                    return seen.IsRefused ? seen : seen with { Text = null };
                },

                "the-declaration-was-not-carried" => (body, declared) =>
                    honest(body, declared) with { DeclaredBytes = -1 },

                "too-many-bytes-names-no-bound" => (body, declared) =>
                {
                    var seen = honest(body, declared);

                    return seen.Bound is null ? seen : seen with { Bound = null };
                },

                "bound-named-where-no-bound-refused" => (body, declared) =>
                {
                    var seen = honest(body, declared);

                    return seen.Bound is null ? seen with { Bound = 1 } : seen;
                },

                // It takes the body off the stream before it asks anything about the declaration,
                // which is the whole failure: the size is discovered by reading rather than by
                // being told, and the refusal then arrives with the body already in memory.
                "declared-past-the-bound-was-read-anyway" => (body, declared) =>
                {
                    Drain(body);

                    return honest(body, declared);
                },

                "a-body-inside-the-bound-was-refused-for-its-length" => (_, declared) =>
                    new EnvelopeFuzz.BodyObservation(
                        nameof(EnvelopeBodyAnswer.TooManyBytes),
                        true,
                        null,
                        EnvelopeBounds.MaximumBytes,
                        declared,
                        0),

                "the-text-is-not-the-bytes-that-arrived" => (body, declared) =>
                {
                    var seen = honest(body, declared);

                    return seen.IsRefused ? seen : seen with { Text = seen.Text + "and one character nobody sent" };
                },

                // The reader that stops at the end of the stream rather than at the bound. Every
                // body with an end passes it, which is why the ceiling leg exists.
                "body-read-past-the-bound" => (body, declared) =>
                {
                    Drain(body);

                    return honest(body, declared);
                },

                "an-endless-body-was-not-refused" => (_, declared) => new EnvelopeFuzz.BodyObservation(
                    nameof(EnvelopeBodyAnswer.Read),
                    false,
                    string.Empty,
                    null,
                    declared,
                    0),

                _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "No broken body reader is written for that rule."),
            };
        }

        private static void Drain(Stream body)
        {
            var scratch = new byte[4096];

            while (body.Read(scratch, 0, scratch.Length) > 0)
            {
                // Reading until the stream says it is done, which is the mistake being made.
            }
        }
    }
}
