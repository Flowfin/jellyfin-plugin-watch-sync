using System;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Conflict;

/// <summary>
/// An unmark held against a completion, decided by what the two sides last agreed.
///
/// Marking something unwatched is a real thing people do, before a rewatch or after somebody
/// else used their account. The ratchet in #31 says a completion never regresses to a
/// position, and read on its own that makes an unmark impossible to carry: the peer holds the
/// work played, the person turns it off here, and the ratchet hands the win back to the peer
/// on every exchange for as long as both servers run. The two rules contradict each other on
/// their face, and #34 is where they are reconciled rather than left to be discovered by
/// somebody reading one row.
///
/// They are reconciled by the agreed record and by nothing else. A transition from played to
/// unplayed since the last agreement is a change with an intent behind it and it carries. A
/// side that never agreed to the completion is not offering an intent, it is offering an old
/// value, and the ratchet answers it.
///
/// THE FAILURE IN THE OTHER DIRECTION IS THE MORE DANGEROUS ONE and it is why this is not
/// softened into letting any unplayed beat any played. A fresh server, or one restored from a
/// backup taken before this history existed, holds every item unplayed and has agreed nothing.
/// Under a rule that reads unplayed as intent without asking whether an agreement stands
/// behind it, first contact with an established server wipes that server's history, and what
/// was overwritten is the part nobody can reconstruct. The absence of an agreement is
/// therefore the first thing this rule asks about and the answer is always
/// <see cref="UnplayedAnswer.NoUnmarkToCarry"/>.
///
/// The rule reads no clock, like the ratchet it is paired with, and for a stronger reason than
/// the ratchet has. The server stores no moment for turning a completion off, so an unmark is
/// untimed and there is nothing to compare a peer's timestamp against. What stands in for the
/// clock is the agreement itself: a side whose count or last played date differs from the
/// agreed one has watched the work again since, and that is the one comparison this rule makes.
///
/// It is asked before the ratchet rather than after it. The ratchet is handed two readings and
/// no agreement, so it cannot tell a deliberate unmark from an old value and does not try;
/// asking it first would answer the pair before the question this rule exists for was put.
/// Nothing drives the two together yet, so that order is a rule a caller is held to rather than
/// one a machine keeps, and it is written in <c>docs/conflicts.md</c> where the pair is argued.
///
/// Nothing derived is carried out of the rule, which is where this departs from
/// <see cref="PlayedRatchet"/> deliberately. The ratchet computes what a completion discarded,
/// so a caller that did not receive it would have to derive it again. Here the loser is a
/// played state the caller already holds, and a property restating it would be a second copy of
/// one fact, which is what #36's record is assembled from rather than what it is assembled by.
/// </summary>
public static class DeliberateUnplayed
{
    /// <summary>
    /// Holds an unmark against a completion.
    ///
    /// The two readings are one server's state each and the answer names the side rather than a
    /// direction, so passing them the other way round answers the mirrored member and never a
    /// different outcome.
    /// </summary>
    /// <param name="here">The state this server holds for the mapped user and the item.</param>
    /// <param name="atThePeer">The state the peer holds for the same pair.</param>
    /// <param name="agreed">
    /// The state as the two sides last agreed it, or null where they have agreed nothing about
    /// this pair. Null is a first exchange, which is a defined state and not a missing one, and
    /// #37 answers it with this same table.
    /// </param>
    /// <returns>The resolution.</returns>
    /// <exception cref="ArgumentNullException">Either reading is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A play count below zero on any of the three, which no server produces. It reaches a rule
    /// only out of an envelope, where #19 bounds and refuses what one may carry, and reading it
    /// as an ordinary count here would let a negative difference read as a work watched again.
    /// </exception>
    public static UnplayedAnswer Reconcile(SyncedState here, SyncedState atThePeer, SyncedState? agreed)
    {
        ArgumentNullException.ThrowIfNull(here);
        ArgumentNullException.ThrowIfNull(atThePeer);
        ArgumentOutOfRangeException.ThrowIfNegative(here.PlayCount, nameof(here));
        ArgumentOutOfRangeException.ThrowIfNegative(atThePeer.PlayCount, nameof(atThePeer));

        if (agreed is null || !agreed.Played)
        {
            return UnplayedAnswer.NoUnmarkToCarry;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(agreed.PlayCount, nameof(agreed));

        if (here.Played == atThePeer.Played)
        {
            return UnplayedAnswer.NoUnmarkToCarry;
        }

        var completion = here.Played ? here : atThePeer;

        if (HasBeenWatchedAgainSince(completion, agreed))
        {
            return UnplayedAnswer.TheCompletionMovedSinceTheAgreement;
        }

        return here.Played
            ? UnplayedAnswer.UnplayedCarriesFromThePeer
            : UnplayedAnswer.UnplayedCarriesFromHere;
    }

    /// <summary>
    /// Whether the side still holding the completion has watched the work again since the
    /// agreement.
    ///
    /// Two readings answer it and both are needed. A count above the agreed one is a play the
    /// person made afterwards. A last played date later than the agreed one is the same event
    /// seen from the other field, and it is read as well because a rewatch that finished the
    /// work again does not always move the count on both server lines, while the date moves
    /// whenever anything was played at all.
    ///
    /// A count BELOW the agreed one is not a rewatch and is deliberately not read as one. That
    /// is a side restored from an older backup, which is the case #33 records as a shortfall,
    /// and reading it here as movement would hand the unmark a loss for the one reason it
    /// should win.
    /// </summary>
    /// <param name="completion">The side still holding the work played.</param>
    /// <param name="agreed">The state the two sides last agreed.</param>
    /// <returns>Whether that side moved after the agreement.</returns>
    private static bool HasBeenWatchedAgainSince(SyncedState completion, SyncedState agreed) =>
        completion.PlayCount > agreed.PlayCount
        || IsLaterThan(completion.LastPlayedDate, agreed.LastPlayedDate);

    /// <summary>
    /// Whether one last played date is later than another, with an absent date read as no
    /// reading rather than as the beginning of time.
    ///
    /// A side holding a date where the agreement holds none has played the work since, because
    /// the agreement is a state both sides settled on and a date that was there would have been
    /// part of it. A side holding none where the agreement holds one has lost a reading rather
    /// than gained a play, and that is not movement.
    /// </summary>
    /// <param name="reading">The side's date.</param>
    /// <param name="agreed">The agreed date.</param>
    /// <returns>Whether the side's date is later.</returns>
    private static bool IsLaterThan(DateTime? reading, DateTime? agreed) =>
        reading is not null && (agreed is null || reading > agreed);
}
