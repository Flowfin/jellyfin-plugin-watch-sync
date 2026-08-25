using System;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Transfer;

/// <summary>
/// One entry of the list a peer reads when it asks what changed.
///
/// Data is pulled, which is decision 3 in #1 answered on 2026-08-08, so what this side keeps
/// is not an outbound queue it sends. It is the outstanding state a peer collects on demand,
/// and #49 is the rule that keeps it the size of the work outstanding rather than the size of
/// the evening. <c>docs/transfer.md</c> fixes what one exchange is and who starts it, and this
/// type points at that document rather than restating it.
///
/// An entry names one field and carries the whole reading it was observed in. Carrying the
/// reading rather than one value is what lets the collapse ask the conflict table's own rule
/// whether an outstanding entry has been answered: <see cref="Conflict.PlayedRatchet"/> is
/// written in terms of <see cref="SyncedState"/>, so an entry holding a bare number would have
/// to reassemble a reading before it could ask, and a reassembled reading is a second copy of
/// the three fields it did not carry.
///
/// The time is the earliest moment this side saw the field move, not the latest. A peer asks
/// for what changed since its watermark, and an entry stamped with the last moment it was
/// touched would fall out of that question every time a later report arrived while the peer
/// was away. #51 is the watermark and #52 is what happens when it is not recognised.
/// </summary>
public sealed class RecordedChange
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RecordedChange"/> class.
    /// </summary>
    /// <param name="pairingId">The pairing this entry is outstanding for.</param>
    /// <param name="subject">The mapped user and the leaf item the entry is about.</param>
    /// <param name="field">Which of the moved fields moved.</param>
    /// <param name="observed">The reading this side held when it saw the field move.</param>
    /// <param name="firstObservedAt">The earliest moment this side saw the field move.</param>
    /// <exception cref="ArgumentNullException">The subject or the reading is null.</exception>
    /// <exception cref="ArgumentException">
    /// The pairing is empty, or the field is not a member of <see cref="SyncedField"/>. A
    /// pairing is what the entry is outstanding for, so an empty one is an entry no exchange
    /// can ever collect.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The reading carries a position or a play count below zero. This is a reading of this
    /// server's own record, which produces neither, and the rules the collapse asks refuse
    /// both rather than treating them as ordinary values. What arrives from a peer is bounded
    /// and refused one layer earlier, which is #19.
    /// </exception>
    public RecordedChange(
        Guid pairingId,
        TransferSubject subject,
        SyncedField field,
        SyncedState observed,
        DateTimeOffset firstObservedAt)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(observed);

        if (pairingId == Guid.Empty)
        {
            throw new ArgumentException(
                "A recorded change is outstanding for one pairing and the pairing is empty.",
                nameof(pairingId));
        }

        if (!Enum.IsDefined(field))
        {
            throw new ArgumentException(
                "A recorded change is about one of the moved fields and this is not one of them.",
                nameof(field));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(observed.PlaybackPositionTicks, nameof(observed));
        ArgumentOutOfRangeException.ThrowIfNegative(observed.PlayCount, nameof(observed));

        PairingId = pairingId;
        Subject = subject;
        Field = field;
        Observed = observed;
        FirstObservedAt = firstObservedAt;
    }

    /// <summary>
    /// Gets the pairing this entry is outstanding for.
    ///
    /// It is part of the key rather than a property beside it, because two pairings of one
    /// server hold their own agreed record and their own watermark, so a change outstanding
    /// for one of them says nothing about the other.
    /// </summary>
    public Guid PairingId { get; }

    /// <summary>
    /// Gets the mapped user and the leaf item this entry is about.
    /// </summary>
    public TransferSubject Subject { get; }

    /// <summary>
    /// Gets which of the moved fields moved.
    /// </summary>
    public SyncedField Field { get; }

    /// <summary>
    /// Gets the reading this side held when it saw the field move.
    /// </summary>
    public SyncedState Observed { get; }

    /// <summary>
    /// Gets the earliest moment this side saw the field move.
    /// </summary>
    public DateTimeOffset FirstObservedAt { get; }

    /// <summary>
    /// Whether two entries are about the same pairing, the same mapped user and the same item.
    ///
    /// The field is deliberately not part of this. What the collapse does with two entries
    /// about one subject depends on whether they name one field or two, so an answer that
    /// folded the field in would decide that question here instead.
    /// </summary>
    /// <param name="other">The other entry.</param>
    /// <returns>Whether the two are about the same pairing, user and item.</returns>
    /// <exception cref="ArgumentNullException">The other entry is null.</exception>
    public bool IsAboutTheSameSubjectAs(RecordedChange other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return PairingId == other.PairingId
            && Subject.MappedUserId == other.Subject.MappedUserId
            && Subject.ItemId == other.Subject.ItemId;
    }

    /// <summary>
    /// This entry with an earlier first-observed moment.
    ///
    /// It is how the collapse keeps the latest value and the earliest time in one entry: the
    /// arriving change carries the value, and the entry it replaces carries the moment the
    /// field first moved.
    /// </summary>
    /// <param name="moment">The earlier moment.</param>
    /// <returns>An entry carrying this reading and that moment.</returns>
    public RecordedChange ObservedSince(DateTimeOffset moment) =>
        moment == FirstObservedAt
            ? this
            : new RecordedChange(PairingId, Subject, Field, Observed, moment);
}
