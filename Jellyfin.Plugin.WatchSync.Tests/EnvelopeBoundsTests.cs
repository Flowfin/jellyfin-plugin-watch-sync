using System;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.WatchSync.Model;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What one envelope may carry, which is #19.
///
/// Each bound is driven at its boundary and one unit past it, because a bound written with the
/// wrong one of two operators answers every other value correctly and is wrong only at the one
/// value anybody would use to probe it. The four are also driven together, so that the order
/// they are asked in is a fact rather than an accident of which one happened to be first.
/// </summary>
public class EnvelopeBoundsTests
{
    private static EnvelopeBounds Within(
        int envelopes = 0,
        long? bytes = null,
        int changes = 1,
        int longestString = 1) =>
        EnvelopeBounds.Judge(
            envelopes,
            bytes ?? 1,
            changes,
            longestString);

    /// <summary>
    /// An envelope inside every bound is not refused, and carries no bound and no count.
    ///
    /// Without this the four refusals below could all be produced by a rule that refused
    /// everything, and each of them would still pass.
    /// </summary>
    [Fact]
    public void AnEnvelopeInsideEveryBoundIsWithin()
    {
        var answer = Within(
            envelopes: EnvelopeBounds.MaximumEnvelopesInAWindow - 1,
            bytes: EnvelopeBounds.MaximumBytes,
            changes: EnvelopeBounds.MaximumChanges,
            longestString: EnvelopeBounds.LongestStringLength);

        Assert.Equal(EnvelopeBoundsAnswer.Within, answer.Answer);
        Assert.True(answer.MayBeRead);
        Assert.Null(answer.Bound);
        Assert.Null(answer.Counted);
    }

    /// <summary>
    /// Each of the four refuses at one unit past its bound, and each refusal is its own answer.
    ///
    /// The four are asserted in one fact rather than four because what is being asserted is
    /// that they are four different answers. A rule that answered every crossing with the same
    /// value would pass four separate facts that each only checked that something was refused.
    /// </summary>
    [Fact]
    public void EachBoundIsRefusedOnePastItAndEachRefusalIsItsOwnAnswer()
    {
        Assert.Equal(
            EnvelopeBoundsAnswer.TooManyEnvelopesInTheWindow,
            Within(envelopes: EnvelopeBounds.MaximumEnvelopesInAWindow).Answer);

        Assert.Equal(
            EnvelopeBoundsAnswer.TooManyBytes,
            Within(bytes: EnvelopeBounds.MaximumBytes + 1).Answer);

        Assert.Equal(
            EnvelopeBoundsAnswer.TooManyChanges,
            Within(changes: EnvelopeBounds.MaximumChanges + 1).Answer);

        Assert.Equal(
            EnvelopeBoundsAnswer.AStringIsTooLong,
            Within(longestString: EnvelopeBounds.LongestStringLength + 1).Answer);
    }

    /// <summary>
    /// Each of the four accepts the value at its bound.
    ///
    /// The bound is the largest value that is still allowed, so this is where a comparison
    /// written one unit out shows and nowhere else. The window is asserted at one short of its
    /// bound, because the count handed in excludes the envelope being judged and what is held
    /// to the bound is the count including it.
    /// </summary>
    [Fact]
    public void EachBoundAcceptsTheValueAtIt()
    {
        Assert.True(Within(envelopes: EnvelopeBounds.MaximumEnvelopesInAWindow - 1).MayBeRead);
        Assert.True(Within(bytes: EnvelopeBounds.MaximumBytes).MayBeRead);
        Assert.True(Within(changes: EnvelopeBounds.MaximumChanges).MayBeRead);
        Assert.True(Within(longestString: EnvelopeBounds.LongestStringLength).MayBeRead);
    }

    /// <summary>
    /// A peer over its rate is refused before anything about the envelope is looked at.
    ///
    /// The envelope driven here crosses all three of the other bounds as well. The point of the
    /// order is that a peer that is looping costs this server the rate check and nothing else,
    /// so a rule that asked the byte length first would be paying to find out something it was
    /// going to refuse anyway.
    /// </summary>
    [Fact]
    public void APeerOverItsRateIsRefusedBeforeTheEnvelopeIsLookedAt()
    {
        var answer = EnvelopeBounds.Judge(
            EnvelopeBounds.MaximumEnvelopesInAWindow,
            EnvelopeBounds.MaximumBytes + 1,
            EnvelopeBounds.MaximumChanges + 1,
            EnvelopeBounds.LongestStringLength + 1);

        Assert.Equal(EnvelopeBoundsAnswer.TooManyEnvelopesInTheWindow, answer.Answer);
    }

    /// <summary>
    /// The byte length is asked before the change count and the string length.
    ///
    /// It is the only one of the three that is knowable without parsing, so a caller that
    /// stopped at it has refused before allocating, which is this issue's second condition. A
    /// rule that answered the change count first would be telling the caller something it could
    /// only have learned by doing the work the bound exists to avoid.
    /// </summary>
    [Fact]
    public void TheByteLengthIsAskedBeforeAnythingThatNeedsTheEnvelopeParsed()
    {
        var answer = EnvelopeBounds.Judge(
            0,
            EnvelopeBounds.MaximumBytes + 1,
            EnvelopeBounds.MaximumChanges + 1,
            EnvelopeBounds.LongestStringLength + 1);

        Assert.Equal(EnvelopeBoundsAnswer.TooManyBytes, answer.Answer);
    }

    /// <summary>
    /// Every refusal carries the bound it crossed and what was counted against it.
    ///
    /// #19 asks that a refusal be recorded with the peer, the bound and the count. The peer
    /// belongs to whoever holds the pairing; these two belong here, and a refusal carrying only
    /// its answer would leave an operator unable to tell one change over the line from a peer
    /// sending a hundred times the limit.
    /// </summary>
    [Fact]
    public void EveryRefusalCarriesTheBoundAndWhatWasCountedAgainstIt()
    {
        var changes = Within(changes: EnvelopeBounds.MaximumChanges + 7);

        Assert.Equal(EnvelopeBounds.MaximumChanges, changes.Bound);
        Assert.Equal(EnvelopeBounds.MaximumChanges + 7, changes.Counted);

        var bytes = Within(bytes: EnvelopeBounds.MaximumBytes * 100L);

        Assert.Equal(EnvelopeBounds.MaximumBytes, bytes.Bound);
        Assert.Equal(EnvelopeBounds.MaximumBytes * 100L, bytes.Counted);

        var strings = Within(longestString: EnvelopeBounds.LongestStringLength + 1);

        Assert.Equal(EnvelopeBounds.LongestStringLength, strings.Bound);
        Assert.Equal(EnvelopeBounds.LongestStringLength + 1, strings.Counted);
    }

    /// <summary>
    /// A refused window carries the number this envelope would have made rather than the number
    /// the caller passed.
    ///
    /// The count handed in excludes the envelope being judged, so a refusal reporting it back
    /// would be off by one in the direction that reads as though the peer was refused while
    /// still inside its bound, which is the reading an operator would take as a defect here.
    /// </summary>
    [Fact]
    public void ARefusedWindowCountsTheEnvelopeItRefused()
    {
        var answer = Within(envelopes: EnvelopeBounds.MaximumEnvelopesInAWindow);

        Assert.Equal(EnvelopeBounds.MaximumEnvelopesInAWindow, answer.Bound);
        Assert.Equal(EnvelopeBounds.MaximumEnvelopesInAWindow + 1L, answer.Counted);
    }

    /// <summary>
    /// This plugin's two bounds sit strictly below the ceilings the layer beneath imposes.
    ///
    /// A byte bound at or above the transport's own would never bind, because the transport
    /// refuses first and this plugin's refusal, its code and the record #19 asks for would all
    /// be unreachable. A rate bound near the freshness budget would spend a pairing's budget on
    /// syncing and refuse traffic that has nothing to do with this plugin, so it is held to a
    /// fraction of it rather than merely below it.
    ///
    /// Both ceilings are readings of another tree at one commit, recorded on the members that
    /// carry them. This holds the relation between the numbers and not the numbers, so it says
    /// nothing about whether either reading is still true.
    /// </summary>
    [Fact]
    public void ThisPluginsBoundsSitUnderTheCeilingsTheLayerBelowImposes()
    {
        Assert.True(
            EnvelopeBounds.MaximumBytes < EnvelopeBounds.TransportBodyCeilingBytes,
            $"the byte bound is {EnvelopeBounds.MaximumBytes} and the transport refuses above {EnvelopeBounds.TransportBodyCeilingBytes}, so nothing here would ever bind");

        Assert.True(
            EnvelopeBounds.MaximumEnvelopesInAWindow * 4 < EnvelopeBounds.FreshnessBudgetPerPairing,
            $"the rate bound is {EnvelopeBounds.MaximumEnvelopesInAWindow} against a shared budget of {EnvelopeBounds.FreshnessBudgetPerPairing}, which is not a fraction of it");
    }

    /// <summary>
    /// The two bounds that are reached together are reachable together.
    ///
    /// A change is a match key, four field values and a date. If the byte bound divided by the
    /// change bound left less room than that, one of the two would be unreachable and the pair
    /// would be a single bound wearing two names, which is the state a pair of numbers written
    /// separately usually ends up in.
    /// </summary>
    [Fact]
    public void TheByteBoundLeavesRoomForTheChangesTheChangeBoundAllows()
    {
        Assert.True(
            EnvelopeBounds.MaximumBytes / EnvelopeBounds.MaximumChanges >= 200,
            $"{EnvelopeBounds.MaximumBytes} bytes over {EnvelopeBounds.MaximumChanges} changes leaves {EnvelopeBounds.MaximumBytes / EnvelopeBounds.MaximumChanges} bytes each, which is less than one change is");
    }

    /// <summary>
    /// A refusal is a refusal and never a truncation, which is a property of the type rather
    /// than a rule anybody keeps.
    ///
    /// The answer has three members and none of them can carry a shortened envelope, so a
    /// caller that wanted to keep the first thousand changes of a refused envelope has nothing
    /// to take them out of. A rule that returned a shortened value would need a member for it,
    /// and this refuses one arriving.
    /// </summary>
    [Fact]
    public void ARefusalHasNowhereToCarryAShortenedEnvelope()
    {
        var carried = typeof(EnvelopeBounds)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "Answer", "Bound", "Counted", "MayBeRead" }, carried);
    }

    /// <summary>
    /// A count or a length below zero is refused rather than judged.
    ///
    /// None of the four is a quantity that can be negative, so a negative one is a caller that
    /// computed it wrongly, and every comparison here would answer that the envelope is within
    /// its bounds.
    /// </summary>
    [Fact]
    public void ACountOrALengthBelowZeroIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EnvelopeBounds.Judge(-1, 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnvelopeBounds.Judge(0, -1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnvelopeBounds.Judge(0, 1, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnvelopeBounds.Judge(0, 1, 1, -1));
    }
}
