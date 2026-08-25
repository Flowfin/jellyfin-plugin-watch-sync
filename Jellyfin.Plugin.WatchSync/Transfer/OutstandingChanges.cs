using System;
using System.Collections.Generic;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Transfer;

/// <summary>
/// What one reading of this server's own record leaves outstanding against what two servers last
/// agreed.
///
/// This is the second and third conditions of #14 and it is the whole reason the agreed record
/// exists. With two current values and no record of an agreement the only available rule is to
/// overwrite, and the prior art overwrites the wrong side. With the record, a value equal to the
/// last agreed one is a value the peer already has, and a value different from it is something
/// that happened here since.
///
/// It answers one entry per field rather than one per reading. A change is about one field,
/// because the list a peer reads holds at most one entry per pairing, mapped user, item and
/// field, and <see cref="ChangeCollapse"/> is what puts these into that list.
///
/// Where nothing has been agreed the comparison is against the state a work nobody has watched
/// is in. That is not the same as an agreement holding that state, and the difference is kept in
/// <see cref="AgreedRecords.For"/> rather than erased here: the peer's ratchet answers the two
/// differently, which is #34. What it decides here is only what an untouched item offers a peer,
/// and the answer is nothing, because a first exchange that offered every never-watched item
/// would make the list the size of the library instead of the size of the work outstanding. The
/// path that compares both sides for every matched item is the full reconciliation in #52 and it
/// is a different question from this one.
///
/// Nothing here reads a clock. The moment the reading was observed is a parameter, which is the
/// <c>injected-clock</c> invariant, and it becomes the entry's first-observed moment through
/// <see cref="RecordedChange"/>.
/// </summary>
public static class OutstandingChanges
{
    /// <summary>
    /// The state a work nobody has watched is in on this server.
    ///
    /// It is the baseline for a subject nothing has been agreed about. It is written here, at the
    /// one rule that needs it, rather than on <see cref="SyncedState"/>, because a member there
    /// would read as a value two servers can agree, and no exchange ever agrees it: an item in
    /// this state has nothing outstanding, so it never reaches a peer and never comes back as an
    /// agreement.
    /// </summary>
    private static readonly SyncedState _neverWatched = new SyncedState(false, 0, 0, null);

    /// <summary>
    /// What this reading leaves outstanding for one peer.
    /// </summary>
    /// <param name="pairingId">The pairing the entries would be outstanding for.</param>
    /// <param name="agreement">What was last agreed about this subject, or null where nothing was.</param>
    /// <param name="subject">The mapped user and the leaf item.</param>
    /// <param name="local">What this server's own record holds now.</param>
    /// <param name="observedAt">When this server saw the reading.</param>
    /// <returns>One entry per field that has moved since the agreement, in the order the fields are declared.</returns>
    /// <exception cref="ArgumentNullException">The subject or the reading is null.</exception>
    /// <exception cref="ArgumentException">
    /// The agreement is about another subject. Comparing a reading of one item against what was
    /// agreed about another is the mistake this type exists to make impossible to write, and it
    /// is not one a caller could see in the answer, because the answer would look like ordinary
    /// outstanding work.
    /// </exception>
    public static IReadOnlyList<RecordedChange> Since(
        Guid pairingId,
        AgreedRecord? agreement,
        TransferSubject subject,
        SyncedState local,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(local);

        if (agreement is not null
            && (agreement.Subject.MappedUserId != subject.MappedUserId
                || agreement.Subject.ItemId != subject.ItemId))
        {
            throw new ArgumentException(
                "The agreement is about another mapped user or another item than the reading.",
                nameof(agreement));
        }

        var agreed = agreement?.Agreed ?? _neverWatched;
        var outstanding = new List<RecordedChange>();

        foreach (var field in Enum.GetValues<SyncedField>())
        {
            if (HasMoved(field, agreed, local))
            {
                outstanding.Add(
                    new RecordedChange(pairingId, subject, field, local, observedAt));
            }
        }

        return outstanding.AsReadOnly();
    }

    /// <summary>
    /// Whether one field of the local reading differs from what was agreed.
    ///
    /// One arm per member of the enumeration and no default that guesses. A field added to the
    /// moved set with no arm here throws where it is reached rather than answering that nothing
    /// moved, because a silent false is a field that never syncs again and nothing on either
    /// server says why. <c>OutstandingChangesTests</c> drives every member through this, so the
    /// arm is owed at the moment the member is declared.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="agreed">What was agreed, or the never-watched state.</param>
    /// <param name="local">What this server holds now.</param>
    /// <returns>Whether the field has moved since.</returns>
    private static bool HasMoved(SyncedField field, SyncedState agreed, SyncedState local) =>
        field switch
        {
            SyncedField.Played => agreed.Played != local.Played,
            SyncedField.PlayCount => agreed.PlayCount != local.PlayCount,
            SyncedField.PlaybackPositionTicks =>
                agreed.PlaybackPositionTicks != local.PlaybackPositionTicks,
            SyncedField.LastPlayedDate => agreed.LastPlayedDate != local.LastPlayedDate,
            _ => throw new ArgumentOutOfRangeException(
                nameof(field),
                field,
                "This is a field that moves and no rule here says when it has moved, so nothing outstanding would ever be recorded about it."),
        };
}
