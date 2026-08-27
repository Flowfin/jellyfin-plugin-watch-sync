using System;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Records;

/// <summary>
/// One value this plugin wrote into a person's record, as it is written down: which pairing and
/// which mapped user it was for, which peer user it came from, which item and which moved field,
/// what this server held immediately before the write, what was written, and when.
///
/// This is what #44 asks for. Decision 5 on the pairing board is that on revocation what came
/// from the peer is deleted, and a value this plugin wrote is indistinguishable from one the
/// server wrote itself unless something recorded that this plugin wrote it. Without this record
/// the strict answer is not available at all, and the plan pays for it here rather than making
/// the decision impossible to carry out later.
///
/// <para>
/// It carries the value that was replaced and the value that was written, and the second of those
/// is not in the sentence #44's body uses to describe this record. It is here because of that
/// issue's third condition: the undo skips a value the person changed after this plugin wrote it,
/// and the only way to notice such a change is to compare what the record is standing on now
/// against what this plugin left there. A record holding the replaced value alone cannot tell a
/// person's later action from its own write, so an undo driven by it would overwrite that action
/// silently, which is the failure the whole plan is written against in the direction nobody looks.
/// </para>
///
/// <para>
/// It carries identifiers, the field and two numbers, and nothing else, which is
/// <see cref="ConflictRecord"/>'s rule for the same reason and one more of its own. Watch history
/// is personal data, and this record is the one kind in the store that is a copy of what somebody
/// watched rather than a note about it. A title or a path here would make it readable as a
/// viewing history by anyone who could reach it, and a string a peer chose would arrive in a
/// record that is meant to be shown on a page and written into a log.
/// <c>ProvenanceRecordTests</c> refuses a member of any type outside the declared set, so the
/// rule is a property of the type rather than a habit of whoever adds the next field.
/// </para>
///
/// <para>
/// Both values are ticks or counts rather than the field's own type, because one record type
/// holds all four rows and what a number means is decided by <see cref="Field"/>: a played state
/// is zero or one, a play count is the count, a position is its ticks, and a last played date is
/// the ticks of its UTC moment. That is <see cref="ConflictRecord"/>'s convention and the two are
/// read side by side by anyone answering why a value on this server is what it is.
/// </para>
///
/// <para>
/// What this does not decide: nothing writes one yet. #44's first condition asks that every write
/// path record provenance, asserted over the whole apply surface, and there is no apply surface.
/// <c>IUserDataGateway.Write</c> is the one place anything this plugin decides reaches a person's
/// record and nothing calls it, so there is no caller for that condition to be true of.
/// </para>
/// </summary>
public sealed class ProvenanceRecord
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProvenanceRecord"/> class.
    /// </summary>
    /// <param name="pairingId">The pairing the value came in on.</param>
    /// <param name="mappedUserId">The local user the mapping named.</param>
    /// <param name="peerUserId">The peer user the value came from, as the peer names them.</param>
    /// <param name="itemId">The local item the value was written against.</param>
    /// <param name="field">The moved field that was written.</param>
    /// <param name="before">
    /// What this server held immediately before the write, or <c>null</c> where it held nothing.
    /// </param>
    /// <param name="written">What this plugin wrote.</param>
    /// <param name="writtenAt">When it was written.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="peerUserId"/> is empty, or <paramref name="written"/> is what was already
    /// there.
    /// </exception>
    public ProvenanceRecord(
        Guid pairingId,
        Guid mappedUserId,
        Guid peerUserId,
        Guid itemId,
        SyncedField field,
        long? before,
        long written,
        DateTimeOffset writtenAt)
    {
        if (peerUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "A record of provenance says which peer user a value came from, and this one names nobody, so an undo bounded by a mapping could not decide whether it is in scope.",
                nameof(peerUserId));
        }

        if (before == written)
        {
            throw new ArgumentException(
                "The value written is the value that was already there, so nothing was replaced and there is nothing for an undo to put back.",
                nameof(written));
        }

        PairingId = pairingId;
        MappedUserId = mappedUserId;
        PeerUserId = peerUserId;
        ItemId = itemId;
        Field = field;
        Before = before;
        Written = written;
        WrittenAt = writtenAt;
    }

    /// <summary>
    /// Gets the pairing the value came in on.
    /// </summary>
    public Guid PairingId { get; }

    /// <summary>
    /// Gets the local user the mapping named.
    /// </summary>
    public Guid MappedUserId { get; }

    /// <summary>
    /// Gets the peer user the value came from, as the peer names them.
    ///
    /// It is the pairing that a revocation ends and the mapping that says whose record was
    /// touched, and neither of those says which account on the other machine the value came out
    /// of. A mapping that is removed and made again is the case this member is for: the pairing
    /// is the same and the person on the other side may not be.
    /// </summary>
    public Guid PeerUserId { get; }

    /// <summary>
    /// Gets the local item the value was written against.
    /// </summary>
    public Guid ItemId { get; }

    /// <summary>
    /// Gets the moved field that was written.
    /// </summary>
    public SyncedField Field { get; }

    /// <summary>
    /// Gets what this server held immediately before the write, read as <see cref="Field"/>
    /// decides, or <c>null</c> where it held nothing.
    ///
    /// Nothing and zero are different states and an undo has to tell them apart. A position of
    /// zero is somebody who started the work and stopped at the beginning; no position at all is
    /// somebody who never opened it, and restoring the first where the second was true leaves a
    /// resume point on an item the person has not touched.
    /// </summary>
    public long? Before { get; }

    /// <summary>
    /// Gets what this plugin wrote, read as <see cref="Field"/> decides.
    ///
    /// This is what an undo compares the current value against. Where they differ the person
    /// changed the value after this plugin wrote it, their action outranks the undo, and the skip
    /// is recorded rather than forced.
    /// </summary>
    public long Written { get; }

    /// <summary>
    /// Gets the moment the value was written, which the caller supplies rather than this type
    /// reading a clock, because a rule in this plugin reads the injected clock and nothing else.
    /// </summary>
    public DateTimeOffset WrittenAt { get; }
}
