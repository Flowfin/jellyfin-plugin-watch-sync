using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.WatchSync.Conflict;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Transfer;

/// <summary>
/// At most one recorded change per pairing, mapped user, item and field, holding the latest
/// value and the earliest time at which it moved.
///
/// Watching one film produces a start, a stream of progress reports and a finish. Only the
/// last state matters to a peer, and without this an evening of viewing becomes hundreds of
/// entries that each contradict the next. The collapse happens on the way in, when the change
/// is observed, rather than when a peer reads the list, so what the list holds is the state
/// outstanding rather than the events that produced it: a peer that reads twice with nothing
/// in between sees the same entry both times.
///
/// Two changes to one field collapse into the later one, and that is the whole of the rule for
/// three of the four fields. The exception is where the collapse meets the conflict table: a
/// completion answers an outstanding position, because <c>docs/conflicts.md</c> gives
/// <c>Played</c> the <c>ratchet</c> rule and a position offered against a completion is
/// discarded. The exception is asked of <see cref="PlayedRatchet"/> rather than decided here,
/// so there is one rule with two callers instead of two implementations that agree until one
/// of them is edited.
///
/// The later observation wins on a single field even when it is weaker, which is the opposite
/// of what the ratchet does and is not an inconsistency. The ratchet holds two SERVERS' states
/// against each other, where an old value must not beat a completion. These are one server's
/// own successive readings of one record, where the later one is simply what this server now
/// holds: a person who marks a work unwatched has changed what is true here, and an entry
/// saying otherwise would offer a peer a value this server no longer carries.
///
/// <c>docs/transfer.md</c> fixes what one exchange is, who starts it, what it may cover,
/// whether two may overlap and what a failed one leaves behind. This type points at that
/// document rather than restating it, and #49 is the rule.
/// </summary>
public static class ChangeCollapse
{
    /// <summary>
    /// Gets the fields an arriving change of which answers an outstanding change to another
    /// field.
    ///
    /// Derived from <see cref="Supersedes"/> rather than written beside it, because a second
    /// list is the drift this plugin refuses everywhere else. <c>ChangeCollapseTests</c> holds
    /// it to the rows <c>docs/conflicts.md</c> gives the <c>ratchet</c> rule to, in both
    /// directions, so a field that gains or loses that rule in the table reddens the suite
    /// rather than leaving this answering what used to be written down.
    /// </summary>
    public static IReadOnlyList<SyncedField> SupersedingFields { get; } =
        Array.AsReadOnly(Enum.GetValues<SyncedField>()
            .Where(arriving => Enum.GetValues<SyncedField>()
                .Any(outstanding => Supersedes(arriving, outstanding)))
            .ToArray());

    /// <summary>
    /// Whether an arriving change to one field can answer an outstanding change to another.
    ///
    /// One pair and its direction. A completion can answer an outstanding position; an
    /// arriving position never answers an outstanding completion, because a position recorded
    /// after a completion is a rewatch in progress and both statements are true of the record
    /// at once.
    ///
    /// This says only that the pair is the one the ratchet decides. Whether it decides it in
    /// favour of the completion is the ratchet's answer and is asked per entry, because a
    /// position the ratchet discards nothing for is an entry with nothing to remove.
    /// </summary>
    /// <param name="arriving">The field the arriving change is about.</param>
    /// <param name="outstanding">The field the standing entry is about.</param>
    /// <returns>Whether the arriving field can answer the outstanding one.</returns>
    public static bool Supersedes(SyncedField arriving, SyncedField outstanding) =>
        arriving == SyncedField.Played && outstanding == SyncedField.PlaybackPositionTicks;

    /// <summary>
    /// Records one observed change into the list a peer reads, collapsing on the way in.
    ///
    /// The list is returned rather than mutated, so a caller holding the previous one still
    /// holds what it had. Ordering is by the moment each subject's first outstanding entry
    /// was recorded and it is never disturbed: an entry that collapses stays where it was, and
    /// a subject nothing outstanding is about is appended. So changes to different items keep
    /// the order they arrived in, whatever happens to the entries of one of them.
    /// </summary>
    /// <param name="recorded">The list as it stands.</param>
    /// <param name="arriving">The change that was observed.</param>
    /// <returns>The list with the change recorded.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentException">The list holds a null entry.</exception>
    public static IReadOnlyList<RecordedChange> Record(
        IReadOnlyList<RecordedChange> recorded,
        RecordedChange arriving)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(arriving);

        var collapsed = new List<RecordedChange>(recorded.Count + 1);
        var replaced = false;

        foreach (var standing in recorded)
        {
            if (standing is null)
            {
                throw new ArgumentException(
                    "The list a peer reads holds a null entry, which is a caller that recorded nothing and called it a change.",
                    nameof(recorded));
            }

            if (!standing.IsAboutTheSameSubjectAs(arriving))
            {
                collapsed.Add(standing);
                continue;
            }

            if (standing.Field == arriving.Field)
            {
                collapsed.Add(arriving.ObservedSince(
                    Earlier(standing.FirstObservedAt, arriving.FirstObservedAt)));
                replaced = true;
                continue;
            }

            if (IsAnsweredBy(standing, arriving))
            {
                continue;
            }

            collapsed.Add(standing);
        }

        if (!replaced)
        {
            collapsed.Add(arriving);
        }

        return collapsed.AsReadOnly();
    }

    /// <summary>
    /// Whether the arriving change answers a standing entry about another field, so that the
    /// standing entry has nothing left to offer a peer.
    ///
    /// The pair is named by <see cref="Supersedes"/> and the answer is the conflict table's
    /// own rule, asked in the argument order that makes it directional: the standing entry is
    /// the first side, so what the rule reports as discarded there is what this entry would
    /// have carried.
    ///
    /// Both halves bite. An unmark arriving against a position recorded while the work was
    /// played leaves the ratchet answering that a completion stands and discarding nothing,
    /// and that entry is kept: it is #34's case, the deliberate unplayed, and removing a
    /// position for it here would be this rule taking a decision it has no agreed record to
    /// take. A position of zero is discarded by nothing for the same reason the ratchet gives,
    /// which is that it is not something a person did, so there is no loss to collapse and the
    /// entry stands until a peer's own ratchet answers it.
    /// </summary>
    /// <param name="standing">The entry already in the list.</param>
    /// <param name="arriving">The change that was observed.</param>
    /// <returns>Whether the standing entry is answered.</returns>
    private static bool IsAnsweredBy(RecordedChange standing, RecordedChange arriving)
    {
        if (!Supersedes(arriving.Field, standing.Field))
        {
            return false;
        }

        var held = PlayedRatchet.Hold(standing.Observed, arriving.Observed);

        return held.Answer == RatchetAnswer.PlayedStands
            && held.PositionDiscardedHere is not null;
    }

    /// <summary>
    /// The earlier of two moments.
    /// </summary>
    /// <param name="first">One moment.</param>
    /// <param name="second">The other.</param>
    /// <returns>The earlier of the two.</returns>
    private static DateTimeOffset Earlier(DateTimeOffset first, DateTimeOffset second) =>
        first <= second ? first : second;
}
