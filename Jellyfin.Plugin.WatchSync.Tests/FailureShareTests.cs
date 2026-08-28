using System;
using Jellyfin.Plugin.WatchSync.Apply;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The rule that separates a walk whose items are failing from a walk that is failing, which is
/// #54's third condition.
///
/// The two failures it sits between are opposite and it can produce either of them itself. Too
/// loose and a mapping pointing at somebody else's record grinds through a whole envelope, one
/// recorded refusal at a time, and does it again on the next exchange. Too tight and one deleted
/// film stops an exchange that would have written everything else, which is the all-or-nothing
/// outcome the rest of #54 exists to refuse, produced by the rule that was added to bound it.
///
/// So the facts here are about the two ends as much as about the middle: what the rule refuses to
/// be set to, and what it declines to judge at all.
/// </summary>
public class FailureShareTests
{
    /// <summary>
    /// The floor. One item attempted and refused is a share of one, which is above every share
    /// this rule accepts, so an arithmetic answer here would stop a walk over a single missing film
    /// and report a systematic failure.
    ///
    /// This is the fact the floor exists for, and it is the smallest envelope there is rather than
    /// a contrived one.
    /// </summary>
    [Fact]
    public void OneRefusedItemIsNotASystematicFailure()
    {
        var verdict = FailureShare.Judge(1, 1, FailureShare.DefaultMaximumShare);

        Assert.Equal(FailureShareAnswer.TooFewToJudge, verdict.Answer);
        Assert.Equal(0, verdict.Allowed);
    }

    /// <summary>
    /// The floor is where it says it is, read at the attempt on either side of it.
    ///
    /// One attempt short of the floor the rule declines however many failed; at the floor the same
    /// failures are judged. Without both halves a floor set one out is invisible, and a floor one
    /// out is a rule that decides a walk nobody has enough of yet.
    /// </summary>
    [Fact]
    public void TheFloorIsReachedAtTheAttemptItNames()
    {
        var below = FailureShare.SmallestJudgeableAttempts - 1;

        Assert.Equal(
            FailureShareAnswer.TooFewToJudge,
            FailureShare.Judge(below, below, FailureShare.DefaultMaximumShare).Answer);

        Assert.Equal(
            FailureShareAnswer.Systematic,
            FailureShare
                .Judge(
                    FailureShare.SmallestJudgeableAttempts,
                    FailureShare.SmallestJudgeableAttempts,
                    FailureShare.DefaultMaximumShare)
                .Answer);
    }

    /// <summary>
    /// Failures at the share are ordinary and failures past it are not, read at the two counts
    /// either side of the same walk.
    ///
    /// The comparison is on being above rather than on reaching, which is one character and decides
    /// the walk where exactly half of what was attempted failed. Half is what the default permits,
    /// so that walk carries on, and the pair here is what says so rather than a comment.
    /// </summary>
    [Fact]
    public void TheShareIsCrossedByBeingAboveItAndNotByReachingIt()
    {
        var atTheShare = FailureShare.Judge(4, 8, FailureShare.DefaultMaximumShare);

        Assert.Equal(FailureShareAnswer.Within, atTheShare.Answer);
        Assert.Equal(4, atTheShare.Allowed);

        var pastIt = FailureShare.Judge(5, 8, FailureShare.DefaultMaximumShare);

        Assert.Equal(FailureShareAnswer.Systematic, pastIt.Answer);
        Assert.Equal(4, pastIt.Allowed);
        Assert.Equal(5, pastIt.Failed);
        Assert.Equal(8, pastIt.Attempted);
    }

    /// <summary>
    /// The share is taken over everything that was attempted rather than over what failed, so items
    /// that were written pull it back down.
    ///
    /// It is the arm that decides whether this rule is about a proportion or about a count of bad
    /// items in a row. Five refusals stop a walk of eight and do not stop a walk of twelve, because
    /// what the rule is looking for is a side that has stopped accepting writes rather than a
    /// library with things missing from it.
    /// </summary>
    [Fact]
    public void ItemsThatWereWrittenCountTowardsTheShare()
    {
        var verdict = FailureShare.Judge(5, 12, FailureShare.DefaultMaximumShare);

        Assert.Equal(FailureShareAnswer.Within, verdict.Answer);
        Assert.Equal(6, verdict.Allowed);
    }

    /// <summary>
    /// A share tighter than the rule accepts is refused, and that is the direction this bound
    /// exists in.
    ///
    /// The change cap beside this one refuses a setting for being loose. Here the dangerous end is
    /// the tight one: a share near zero means the first refused item stops the walk and every
    /// exchange after it, and an operator meeting that sees a plugin that has stopped working
    /// rather than a setting they chose.
    /// </summary>
    [Fact]
    public void AShareTighterThanTheRuleAcceptsIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FailureShare.Judge(1, 10, FailureShare.SmallestConfigurableShare - 0.01));

        Assert.Throws<ArgumentOutOfRangeException>(() => FailureShare.Judge(1, 10, 0));
    }

    /// <summary>
    /// A share looser than the rule accepts is refused, which is where this rule stops being
    /// switchable off from a settings page.
    ///
    /// The bound is on the setting rather than on the plugin, so an operator may loosen this and
    /// may not remove it. A share of one is the spelling that removes it: no walk fails more than
    /// everything it attempted, so nothing would ever be above it.
    /// </summary>
    [Fact]
    public void AShareLooserThanTheRuleAcceptsIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FailureShare.Judge(1, 10, FailureShare.LargestConfigurableShare + 0.01));

        Assert.Throws<ArgumentOutOfRangeException>(() => FailureShare.Judge(1, 10, 1));
    }

    /// <summary>
    /// A share that is not a number is refused rather than compared.
    ///
    /// Every comparison against it answers false, so a walk handed one would be judged by a rule
    /// that says yes to everything, and nothing about the run would look wrong.
    /// </summary>
    [Fact]
    public void AShareThatIsNotANumberIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FailureShare.Judge(1, 10, double.NaN));
    }

    /// <summary>
    /// Counts that cannot have come from one walk are refused rather than judged.
    ///
    /// More failures than attempts is two counts taken from different places, and a share computed
    /// from them describes neither walk. A negative count is the same defect one step earlier.
    /// </summary>
    [Fact]
    public void CountsThatCannotHaveComeFromOneWalkAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FailureShare.Judge(11, 10, FailureShare.DefaultMaximumShare));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => FailureShare.Judge(-1, 10, FailureShare.DefaultMaximumShare));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => FailureShare.Judge(0, -1, FailureShare.DefaultMaximumShare));
    }

    /// <summary>
    /// The default sits between the two bounds, which is the property that makes the default usable
    /// as a setting rather than only as a constant.
    ///
    /// A default outside its own bounds is refused the moment anything passes it in, and what an
    /// operator would meet is a plugin that refuses its own value.
    /// </summary>
    [Fact]
    public void TheDefaultIsAValueTheRuleAccepts()
    {
        Assert.InRange(
            FailureShare.DefaultMaximumShare,
            FailureShare.SmallestConfigurableShare,
            FailureShare.LargestConfigurableShare);
    }
}
