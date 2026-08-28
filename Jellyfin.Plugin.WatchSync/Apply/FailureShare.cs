using System;

namespace Jellyfin.Plugin.WatchSync.Apply;

/// <summary>
/// The rule that stops a walk whose failures have stopped being about the items, which is #54's
/// third condition.
///
/// One item refused is ordinary. The library no longer holds it, or the person's record for it is
/// gone, and the walk records the item and steps over it, which is the whole of the rest of #54.
/// Most of an envelope refused is not a larger version of that: it is this side's database
/// unavailable, or a mapping pointing at the wrong person's record, or a record this account may
/// not write. None of those is repaired by attempting the remaining items, and each of them
/// produces the same run again on the next exchange, with the same refusals.
///
/// So the walk stops and says it stopped, and what it has already written stands. Nothing is
/// unwound, which is <c>docs/transfer.md</c>'s rule and is not softened here: a stop is the walk
/// declining to attempt what is left, never a second pass over what it did.
///
/// <para>
/// THIS IS NOT THE CAP IN <see cref="Transfer.RunCap"/> AND THE TWO ARE NOT INTERCHANGEABLE. That
/// one bounds how much a run may CHANGE and is judged before anything is written, because what it
/// exists to catch is a run that is about to be too large. This one reads what a run has already
/// FAILED to change, and it can only be judged while the walk is running. A run inside the cap
/// reaches this rule, and a run this rule stops was inside the cap when it began.
/// </para>
///
/// <para>
/// WHY A NUMBER MAY BE CHOSEN HERE WITHOUT THE DISTRIBUTION OF REAL ENVELOPES, which is the
/// reading this condition was held under. That reading is right that nobody has measured how often
/// an ordinary envelope carries an item that fails, and wrong that the number therefore cannot be
/// fixed. The two cases this rule separates are not near each other. Items disappearing from a
/// library between an exchange deciding and a walk writing is a handful across an envelope of up
/// to <see cref="Model.EnvelopeBounds.MaximumChanges"/>. A database that is down, an account that
/// may not write, or a mapping naming somebody else's record fails nearly every item it is handed.
/// A threshold anywhere in the middle separates them, and the middle is wide enough that measuring
/// would move the number without moving a verdict. That is the argument the change cap makes about
/// a busy evening and a mass-mark, made here about a different pair rather than borrowed.
/// </para>
///
/// <para>
/// Nothing is remembered between calls. It is handed what has been attempted and what has failed
/// and answers, which is <see cref="Model.PositionThreshold"/>'s shape and is what lets a caller
/// ask after every item without carrying a rule's state around its loop.
/// </para>
/// </summary>
public sealed class FailureShare
{
    private FailureShare(FailureShareAnswer answer, int failed, int attempted, int allowed)
    {
        Answer = answer;
        Failed = failed;
        Attempted = attempted;
        Allowed = allowed;
    }

    /// <summary>
    /// Gets the greatest share of the items it attempted that one walk may fail before it stops,
    /// where an operator has chosen nothing.
    ///
    /// A half. It sits in the gap the summary above describes rather than at a measured point in
    /// it, and the reason for the middle rather than either end is that both ends cost something
    /// and neither cost is recovered by the other. Set low, an evening with a few missing items
    /// stops runs that would have written everything else, and this rule becomes the all-or-nothing
    /// outcome the rest of #54 exists to refuse. Set high, a mapping pointing at the wrong record
    /// has to fail nearly every item before anything notices, which on a large envelope is hundreds
    /// of refusals recorded one at a time.
    ///
    /// A half is also what is easy to state to whoever reads a stopped run: every second write this
    /// side was asked for was refused, which nobody reaches by having items missing from a library.
    /// </summary>
    public static double DefaultMaximumShare => 0.50;

    /// <summary>
    /// Gets the smallest share this rule accepts as a setting.
    ///
    /// A quarter, and the refusal is in the low direction, which is the opposite of the change cap
    /// beside it. There a setting is dangerous when it is loose; here it is dangerous when it is
    /// tight, because a share near zero means the first refused item stops the walk and every
    /// exchange after it, and what an operator gets is an exchange that writes nothing whenever one
    /// film has been deleted. That failure reads as the plugin not working rather than as a setting
    /// being wrong, and it is the outcome this whole issue is written against.
    /// </summary>
    public static double SmallestConfigurableShare => 0.25;

    /// <summary>
    /// Gets the largest share this rule accepts as a setting.
    ///
    /// Nine tenths. It is where the rule stops being switchable off from a settings page, which is
    /// the shape the change cap takes and is taken here for its reason: the bound is on the setting
    /// rather than on the plugin, so an operator may loosen this rule and may not remove it. Above
    /// nine tenths what is left fires only once essentially everything has failed, which is a run
    /// nobody needed a rule to notice.
    /// </summary>
    public static double LargestConfigurableShare => 0.90;

    /// <summary>
    /// Gets how many items a walk has to have attempted before a share of them is read as anything.
    ///
    /// Eight. Below it the arithmetic answers confidently about too few points: one item attempted
    /// and refused is a share of one, which is above every share this rule accepts, so without this
    /// floor a walk over a single missing film would stop for a systematic failure and report one.
    /// That is the rule producing the outcome it exists to prevent, on the smallest envelope there
    /// is.
    ///
    /// Eight rather than three or four, because the floor has to leave room for the default share
    /// to be crossed by something other than a run of bad luck. At eight attempts a stop takes five
    /// refusals, and five of a person's items refused while three others were written is already
    /// the shape this rule is looking for.
    /// </summary>
    public static int SmallestJudgeableAttempts => 8;

    /// <summary>
    /// Gets what the rule answered.
    /// </summary>
    public FailureShareAnswer Answer { get; }

    /// <summary>
    /// Gets how many of the attempted items failed.
    /// </summary>
    public int Failed { get; }

    /// <summary>
    /// Gets how many items the walk had attempted when it asked.
    /// </summary>
    public int Attempted { get; }

    /// <summary>
    /// Gets how many failures the share allowed at this many attempts.
    ///
    /// A count rather than a proportion, because what an operator reading a stopped run is told is
    /// that this walk failed more items than a walk of this length may, and a percentage leaves
    /// them to do the arithmetic against a length they would then have to look up. It is zero where
    /// the rule declined to judge, and no caller may read that as a walk being allowed no failures.
    /// </summary>
    public int Allowed { get; }

    /// <summary>
    /// Judges the failures one walk has recorded so far.
    /// </summary>
    /// <param name="failed">How many of the attempted items were not written.</param>
    /// <param name="attempted">How many items the walk has attempted, written and refused alike.</param>
    /// <param name="maximumShare">The share bound in force.</param>
    /// <returns>The verdict.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A count is negative, more items failed than were attempted, or the share is outside what
    /// this rule accepts. A caller that produced one of the first two has counted something wrong
    /// rather than found an unusual walk.
    /// </exception>
    public static FailureShare Judge(int failed, int attempted, double maximumShare)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(failed);
        ArgumentOutOfRangeException.ThrowIfNegative(attempted);

        if (failed > attempted)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failed),
                failed,
                "More items failed than were attempted, so the two counts came from different places and a share computed from them is about neither walk.");
        }

        if (double.IsNaN(maximumShare)
            || maximumShare < SmallestConfigurableShare
            || maximumShare > LargestConfigurableShare)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumShare),
                maximumShare,
                "The share bound is outside what this rule accepts. Below the smallest it stops a walk for one missing item, which is the all-or-nothing exchange this rule sits inside the refusal of, and above the largest it fires only once everything has already failed.");
        }

        if (attempted < SmallestJudgeableAttempts)
        {
            return new FailureShare(FailureShareAnswer.TooFewToJudge, failed, attempted, 0);
        }

        var allowed = (int)Math.Floor(attempted * maximumShare);

        return failed > allowed
            ? new FailureShare(FailureShareAnswer.Systematic, failed, attempted, allowed)
            : new FailureShare(FailureShareAnswer.Within, failed, attempted, allowed);
    }
}
