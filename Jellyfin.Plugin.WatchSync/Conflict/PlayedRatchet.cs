using System;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Conflict;

/// <summary>
/// Whether the work was finished, held against a position that says it was not.
///
/// Played is stronger than any position. Where one side holds the work played and the other
/// offers a position, played is the answer and the position is discarded, whatever the two
/// last played dates say about which reading is newer.
///
/// The failure this refuses is the one the prior art produces most often. One side has the
/// film finished, the other still carries a stale partial position from before it was
/// finished, and a rule that settles the pair by recency hands the win to the partial
/// position because it happened to be written later or because two home servers disagree
/// about the time. The person then watches the last twenty minutes again, and again after
/// the next run, because nothing about the pair has changed.
///
/// This is why the rule ignores the clock rather than tolerating it. A recency rule that is
/// right in most cases is what produces that loop, and no skew tolerance narrow enough to be
/// useful removes it: the two readings are genuinely minutes apart, and the older of them is
/// still the true one. So the last played dates reach this rule and are never read by it.
/// They are here because #36 records both values of a resolved conflict and when, and a rule
/// that took only the two fields it decides on would leave that record to be assembled
/// somewhere else out of the same two states.
///
/// <c>docs/sync-model.md</c> fixes the fields that move, the unit one transfer is about and
/// the record of what two sides last agreed. This type points at that document rather than
/// restating it, and #31 is the rule.
/// </summary>
public sealed class PlayedRatchet
{
    private PlayedRatchet(
        RatchetAnswer answer,
        long? positionDiscardedHere,
        long? positionDiscardedAtThePeer)
    {
        Answer = answer;
        PositionDiscardedHere = positionDiscardedHere;
        PositionDiscardedAtThePeer = positionDiscardedAtThePeer;
    }

    /// <summary>
    /// Gets what the rule answered.
    /// </summary>
    public RatchetAnswer Answer { get; }

    /// <summary>
    /// Gets the position this server held that the completion discarded, or null where it
    /// discarded none.
    ///
    /// It is null rather than zero for a side that was not played and had not started either.
    /// A position of zero is not something a person did, so recording it as a discarded value
    /// would fill #36's record with rows that name no loss and make the rows that do name one
    /// harder to find.
    /// </summary>
    public long? PositionDiscardedHere { get; }

    /// <summary>
    /// Gets the position the peer held that the completion discarded, or null where it
    /// discarded none.
    /// </summary>
    public long? PositionDiscardedAtThePeer { get; }

    /// <summary>
    /// Gets a value indicating whether this resolution discarded a position somebody reached.
    ///
    /// This is the conflict #36 records with its loser, and the loser is whichever of the two
    /// discarded positions is not null.
    /// </summary>
    public bool IsAResolvedConflict =>
        PositionDiscardedHere is not null || PositionDiscardedAtThePeer is not null;

    /// <summary>
    /// Holds a completion against a position.
    ///
    /// The two arguments are one server's state each, and the answer is the same whichever way
    /// round they are passed. Nothing here reads a clock, an envelope arrival time or a last
    /// played date, so there is no margin by which a position can become newer than a
    /// completion and win.
    ///
    /// The exception this rule does not carry is the deliberate unplayed. Marking something
    /// unwatched is a real thing a person does, and #34 reconciles that with this rule through
    /// the record of what the two sides last agreed: an unplayed that arrived after the last
    /// agreement is an intent, and an unplayed with no agreement behind it is a fresh server
    /// offering an old value. Neither state reaches this rule, which is handed two readings
    /// and no agreement, so it cannot tell those two apart and does not try. Until #34 lands,
    /// a deliberate unplayed held against a peer's played is answered here as
    /// <see cref="RatchetAnswer.PlayedStands"/>, which is the wrong answer for that case and
    /// the right one for every other.
    /// </summary>
    /// <param name="here">The state this server holds for the mapped user and the item.</param>
    /// <param name="atThePeer">The state the peer holds for the same pair.</param>
    /// <returns>The resolution.</returns>
    /// <exception cref="ArgumentNullException">Either side is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A position below zero, which the server does not produce. It reaches this rule only out
    /// of an envelope, where #19 is what bounds and refuses what one may carry, and treating it
    /// as an ordinary reading here would let it be discarded as a position somebody reached.
    /// </exception>
    public static PlayedRatchet Hold(SyncedState here, SyncedState atThePeer)
    {
        ArgumentNullException.ThrowIfNull(here);
        ArgumentNullException.ThrowIfNull(atThePeer);
        ArgumentOutOfRangeException.ThrowIfNegative(here.PlaybackPositionTicks, nameof(here));
        ArgumentOutOfRangeException.ThrowIfNegative(
            atThePeer.PlaybackPositionTicks,
            nameof(atThePeer));

        if (!here.Played && !atThePeer.Played)
        {
            return new PlayedRatchet(RatchetAnswer.NoCompletionToHold, null, null);
        }

        return new PlayedRatchet(
            RatchetAnswer.PlayedStands,
            Discarded(here),
            Discarded(atThePeer));
    }

    /// <summary>
    /// What one side loses to a completion the other side holds.
    ///
    /// A side that holds the work played loses nothing: its own position is where it stopped
    /// on a work it finished, and the answer leaves that side as it was.
    /// </summary>
    /// <param name="side">The state one server holds.</param>
    /// <returns>The discarded position, or null where the side discarded none.</returns>
    private static long? Discarded(SyncedState side) =>
        side.Played || side.PlaybackPositionTicks == 0 ? null : side.PlaybackPositionTicks;
}
