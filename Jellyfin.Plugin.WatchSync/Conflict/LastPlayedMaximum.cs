using System;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Conflict;

/// <summary>
/// When the person last watched the work, held at the later of the two sides.
///
/// This is the <c>LastPlayedDate</c> row of <c>docs/conflicts.md</c>, and it is the row that
/// document declared and the sources decided nowhere. It is the smallest of the four rules and
/// it is the one whose absence costs the most, because this field is what the position rule
/// reads: a resolution that lowered it would decide the next exchange as well as this one.
///
/// The evidence is both dates and nothing else, which is the row's own column and is a
/// restriction rather than an omission. Nothing here reads the played state, the position, the
/// count or a clock. Two of those would change the answer for a pair the other rules already
/// decide, and the third is the reason this rule cannot bound anything: a maximum over two
/// moments compared against each other has no present moment to measure them from, and a peer
/// claiming a play in the future is refused where that comparison is actually made, which is
/// <see cref="PositionRecency"/> and its own answer for it.
///
/// The earlier date is not carried out as a loser, and that is the row's loser column rather
/// than a simplification. A moment that happened is not undone by a later one; the later date
/// already accounts for it, and there is nothing an operator would do about the earlier one.
/// So this rule never produces a conflict for #36 to record, and it is the only one of the four
/// that cannot.
///
/// <c>docs/sync-model.md</c> fixes the fields that move, the unit one transfer is about and the
/// record of what two sides last agreed. This type points at that document rather than
/// restating it.
/// </summary>
public sealed class LastPlayedMaximum
{
    private LastPlayedMaximum(LastPlayedAnswer answer, DateTime? lastPlayedDate)
    {
        Answer = answer;
        LastPlayedDate = lastPlayedDate;
    }

    /// <summary>
    /// Gets what the rule answered.
    /// </summary>
    public LastPlayedAnswer Answer { get; }

    /// <summary>
    /// Gets the date that stands, or null where neither side ever played the work.
    /// </summary>
    public DateTime? LastPlayedDate { get; }

    /// <summary>
    /// Takes the later of the two dates.
    ///
    /// The answer is the same whichever way round the two sides are passed, and there is no
    /// branch that reads one side rather than the other, so the rule has no direction to get
    /// wrong. That is worth stating because the failure this row names is directional: a sync
    /// moving somebody's last played date backwards is what a rule taking whichever side it
    /// happened to be handed second produces, and it looks correct in one of the two runs.
    /// </summary>
    /// <param name="here">The state this server holds for the mapped user and the item.</param>
    /// <param name="atThePeer">The state the peer holds for the same pair.</param>
    /// <returns>The resolution.</returns>
    /// <exception cref="ArgumentNullException">Either side is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A date that is not UTC. It is the local zone read somewhere upstream, and this rule is
    /// the one place two dates are compared against each other and one of them answered with,
    /// so a date that lost its zone here is compared as a wall clock number and the answer is
    /// wrong by the offset rather than refused. <see cref="PositionRecency"/> refuses the same
    /// thing of the present moment it is handed, for the same reason.
    /// </exception>
    public static LastPlayedMaximum Take(SyncedState here, SyncedState atThePeer)
    {
        ArgumentNullException.ThrowIfNull(here);
        ArgumentNullException.ThrowIfNull(atThePeer);

        Utc(here.LastPlayedDate, nameof(here));
        Utc(atThePeer.LastPlayedDate, nameof(atThePeer));

        if (here.LastPlayedDate is not DateTime playedHere)
        {
            return atThePeer.LastPlayedDate is DateTime onlyAtThePeer
                ? new LastPlayedMaximum(LastPlayedAnswer.TheLaterDateStands, onlyAtThePeer)
                : new LastPlayedMaximum(LastPlayedAnswer.NeitherSideHasPlayed, null);
        }

        if (atThePeer.LastPlayedDate is not DateTime playedAtThePeer)
        {
            return new LastPlayedMaximum(LastPlayedAnswer.TheLaterDateStands, playedHere);
        }

        return new LastPlayedMaximum(
            LastPlayedAnswer.TheLaterDateStands,
            playedHere > playedAtThePeer ? playedHere : playedAtThePeer);
    }

    /// <summary>
    /// Refuses a date that is not the instant the server stores.
    /// </summary>
    /// <param name="date">The date, or null.</param>
    /// <param name="side">Which side it came from.</param>
    /// <exception cref="ArgumentOutOfRangeException">The date is not UTC.</exception>
    private static void Utc(DateTime? date, string side)
    {
        if (date is DateTime moment && moment.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentOutOfRangeException(
                side,
                moment.Kind,
                "A last played date reaches this rule in UTC, because it is compared against the other side's and answered with.");
        }
    }
}
