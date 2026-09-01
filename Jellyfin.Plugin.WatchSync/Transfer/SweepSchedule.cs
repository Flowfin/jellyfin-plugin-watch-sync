using System;
using Jellyfin.Plugin.WatchSync.Peer;

namespace Jellyfin.Plugin.WatchSync.Transfer;

/// <summary>
/// How often the scheduled sweep runs, which is #55's second condition.
///
/// The sweep is the convergence behind the events. `docs/transfer.md` fixes what one exchange is
/// and this does not restate it; what belongs here is the one number that says how long a change
/// the events missed stays invisible, because that is what the interval decides and it is the
/// only thing an operator can weigh it against. Events are missed for reasons nobody controls -
/// a restart between a change and its enqueue, a handler unregistered while a plugin updates,
/// a notification the pairing plugin sends once and best effort - and nothing is wrong at any
/// single moment while the two sides drift.
///
/// <para>
/// The interval is a setting rather than a constant because the two ends of it are chosen by
/// facts nobody here has: how much a household watches, and how much a pairing's traffic costs on
/// the link between the two servers. Both bounds below are this rule's rather than the operator's,
/// and both are argued against numbers this plugin already declares rather than against a feel for
/// what is reasonable.
/// </para>
///
/// <para>
/// Nothing calls this yet. The task that would read it is the rest of #55, and this is the number
/// it will be written against rather than a claim that a sweep runs.
/// </para>
/// </summary>
public static class SweepSchedule
{
    /// <summary>
    /// Gets how often the sweep runs where nobody has chosen.
    ///
    /// Fifteen minutes. What the number is chosen against is how long a change the events missed
    /// stays invisible to the person who made it: somebody who marks an episode watched on one
    /// server and walks to the other finds it there, and a pass at this interval is not what
    /// drives a pairing's traffic.
    ///
    /// It sits below <see cref="BoundedBackoff.DefaultCeiling"/>, and that is a relation rather
    /// than a coincidence of two numbers. That ceiling's own reason says it is not shorter
    /// because this pass asks anyway, and that a ceiling below the sweep's interval buys nothing
    /// and asks more often. Both halves of that sentence are false if this number rises above it,
    /// so <c>SweepScheduleTests</c> holds the pair rather than leaving the relation in two
    /// comments that can drift apart.
    /// </summary>
    public static TimeSpan DefaultInterval => TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets the longest interval this rule accepts.
    ///
    /// Six hours. Past it the sweep asks a peer less often than the longest wait a peer that is
    /// FAILING is ever made to serve, so a working pairing is reached more slowly than a broken
    /// one and the backoff stops being what spaces retries out. That is the failure
    /// <see cref="BoundedBackoff.LongestCeiling"/> exists against, arrived at from the other side,
    /// and it is why the two numbers are the same one.
    ///
    /// It is declared here rather than read off that member, and the difference is what a guard
    /// can see. Reading it would make the two unable to disagree and would leave the relation
    /// proven by nothing, which is the shape a hand-applied change on #53 found in this tree
    /// already: two clamps that agreed, either of which could be deleted with the suite still
    /// green. Declared, <c>SweepScheduleTests</c> reddens when either number moves without the
    /// other.
    ///
    /// The bound is inclusive, as every bound this document's settings carry is. An interval
    /// exactly at it asks a working peer exactly as often as the worst case for a failing one,
    /// which is the edge of the absurdity rather than inside it.
    /// </summary>
    public static TimeSpan LongestInterval => TimeSpan.FromHours(6);
}
