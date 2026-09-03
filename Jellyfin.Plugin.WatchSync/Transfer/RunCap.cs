using System;

namespace Jellyfin.Plugin.WatchSync.Transfer;

/// <summary>
/// The bound on how much one run may change before it stops and waits for an operator, which is
/// #38.
///
/// It is the only rule in this plan that limits the damage when another rule is wrong. Every
/// other safeguard here assumes its own inputs are right: the matcher assumes the identifiers
/// are right, the resolver assumes the match is right, and the apply path assumes the mapping
/// is right. A bad mapping, a bad match and a peer restored from a backup all arrive at the
/// same place, which is a run about to change a large part of somebody's library, and nothing
/// upstream of this can tell that place apart from a legitimate first reconciliation.
///
/// So the run stops rather than deciding. Beyond either bound it changes nothing, records what
/// it was about to do, and waits to be approved, which is the rest of #38 and is not here.
///
/// Two bounds rather than one, because one of them is blind on every library the other one
/// catches. A count is what stops a run on a large library, where a mass-mark is thousands of
/// items and any proportion of the library is a large number. A share is what stops one on a
/// small library, where the same mistake changes a third of what somebody has and never comes
/// near a count set for the large case.
///
/// The count is judged first, and that ordering is a rule rather than an implementation detail.
/// The share is computed against how many items this person has matched, which is produced by
/// the matcher, and the matcher is one of the things that can be wrong in the way this cap
/// exists to catch. A match index that has gone wrong in the direction that inflates the
/// matched count also softens the share, so the bound that does not read the matcher's output
/// is the one that still binds when the matcher is the problem.
///
/// <c>CappedApply</c> is what asks this, before a decided set reaches the walk, and it is the
/// only route a decided set takes to a write. Both numbers below are what a setting defaults
/// to rather than a setting: where those live is <c>docs/configuration.md</c>, and nothing holds
/// a pairing yet to keep them beside, so they arrive at the caller as parameters.
/// </summary>
public sealed class RunCap
{
    private RunCap(RunCapAnswer answer, int changes, int allowed)
    {
        Answer = answer;
        Changes = changes;
        Allowed = allowed;
    }

    /// <summary>
    /// Gets how many changes one run may make before it stops, where an operator has chosen
    /// nothing.
    ///
    /// A hundred. It is a choice with a reason rather than a measurement, and the reason is the
    /// gap between the two things it has to separate. An incremental exchange carries what
    /// changed since the last one, and a household watching an evening produces tens of changes
    /// across everybody in it, so a hundred leaves room for a busy evening and for a day spent
    /// catching up with a peer that was unreachable. A mass-mark is not near it: it is the
    /// matched part of a library, which is thousands of items on the deployments this plugin is
    /// written for.
    ///
    /// It is deliberately not set where a legitimate first reconciliation would pass it. That
    /// run is the one most likely to be large and is also the one where a wrong mapping does the
    /// most damage, so it is meant to stop and be approved rather than to be let through by a
    /// bound generous enough to cover it.
    /// </summary>
    public static int DefaultMaximumChanges => 100;

    /// <summary>
    /// Gets the greatest share of this person's matched items one run may change, where an
    /// operator has chosen nothing.
    ///
    /// A tenth. What it is for is the library too small for the count to see: two hundred
    /// matched items and a wrong mapping changes eighty of them, which is well under a hundred
    /// and is most of somebody's history.
    ///
    /// A tenth rather than something tighter, because the two mistakes here do not both get
    /// worse in the same direction. A bound that fires on ordinary runs costs one approval each
    /// time and then costs the whole control: an operator who is asked to approve every evening
    /// stops reading what they are approving, and the cap becomes a button rather than a
    /// question. A tenth of a small library is a change nobody makes by watching things.
    /// </summary>
    public static double DefaultMaximumShare => 0.10;

    /// <summary>
    /// Gets the largest count this rule accepts as a setting.
    ///
    /// Ten thousand, and it is a bound on the rule rather than advice to whoever sets it. Above
    /// it the count stops being a cap on the deployments this plugin is written for: a library
    /// of fifty thousand items is a real one, and a count set past a fifth of it would let a
    /// mass-mark through while still reading as a cap on the page.
    ///
    /// The refusal is here rather than only in the document, because a number a document
    /// declares and no code refuses is one a later caller passes straight through.
    /// </summary>
    public static int MaximumConfigurableChanges => 10000;

    /// <summary>
    /// Gets the largest share this rule accepts as a setting.
    ///
    /// A half. Past it the proportion no longer answers the question it exists for, which is
    /// whether a run is about to change a large part of what somebody has: a run allowed to
    /// change more than half of one person's matched items has already done the thing this cap
    /// is written against, and what it would stop is only the case that is worse still.
    ///
    /// This is also where the cap stops being switchable off from the settings page. The bound
    /// is on the setting rather than on the plugin, so an operator can loosen it and cannot
    /// remove it, which is the shape #38 asks for when it says the cap is on by default because
    /// the operator who needs it most has not thought about it.
    /// </summary>
    public static double MaximumConfigurableShare => 0.50;

    /// <summary>
    /// Gets what the cap answered.
    /// </summary>
    public RunCapAnswer Answer { get; }

    /// <summary>
    /// Gets how many changes the run was about to make.
    /// </summary>
    public int Changes { get; }

    /// <summary>
    /// Gets how many changes the bound that was crossed allowed, or how many the tighter of the
    /// two allowed where neither was.
    ///
    /// It is a count in both cases, including where the share is what stopped the run, because
    /// what an operator is deciding about is a number of items rather than a percentage of a
    /// library whose size they would then have to look up.
    /// </summary>
    public int Allowed { get; }

    /// <summary>
    /// Judges one run against both bounds.
    /// </summary>
    /// <param name="changes">How many changes the run would make.</param>
    /// <param name="matched">How many items this person has matched.</param>
    /// <param name="maximumChanges">The count bound in force.</param>
    /// <param name="maximumShare">The share bound in force.</param>
    /// <returns>The verdict.</returns>
    public static RunCap Judge(int changes, int matched, int maximumChanges, double maximumShare)
    {
        if (changes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(changes),
                changes,
                "A run cannot make a negative number of changes, and a caller that computed one has counted something wrong rather than found a small run.");
        }

        if (matched < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(matched),
                matched,
                "A person cannot have a negative number of matched items. Nothing matched is zero, and the share bound then allows nothing, which is the right answer rather than an error.");
        }

        if (maximumChanges < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumChanges),
                maximumChanges,
                "A count bound below one stops every run including the empty one, so it is a plugin turned off by a setting rather than a cap.");
        }

        if (maximumChanges > MaximumConfigurableChanges)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumChanges),
                maximumChanges,
                "The count bound is above what this rule accepts, and a cap that reads as a cap while letting a mass-mark through is worse than none.");
        }

        if (double.IsNaN(maximumShare) || maximumShare <= 0 || maximumShare > MaximumConfigurableShare)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumShare),
                maximumShare,
                "The share bound is outside what this rule accepts. Nothing at or below zero is a cap, and anything above the maximum has already allowed the change this cap is written against.");
        }

        if (changes > maximumChanges)
        {
            return new RunCap(RunCapAnswer.ExceedsCount, changes, maximumChanges);
        }

        var allowedByShare = (int)Math.Floor(matched * maximumShare);

        if (changes > allowedByShare)
        {
            return new RunCap(RunCapAnswer.ExceedsShare, changes, allowedByShare);
        }

        return new RunCap(RunCapAnswer.Within, changes, Math.Min(maximumChanges, allowedByShare));
    }
}
