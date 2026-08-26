using System;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Records;

/// <summary>
/// One conflict, as it is written down: which pairing and which mapped user it was about, which
/// item and which moved field, which rule decided, what each side held, which side's reading did
/// not survive, and when.
///
/// A conflict is a moment where this plugin discarded something a person did, which is
/// defensible when the rule is right and indefensible when nobody can find out that it happened.
/// An operator asking why an episode is marked watched has to be able to answer it from the
/// server rather than from a hypothesis, and this is what they are answering it from. That is
/// the second condition of #36.
///
/// <para>
/// It carries identifiers, the field, the rule and two numbers, and nothing else. Watch history
/// is personal data and this record is a diagnostic rather than an archive of what somebody
/// watched, so a title, a path or any string a peer chose has no place in it: the first two
/// would make the record readable as a viewing history by anyone who could reach it, and the
/// third is a string from a machine this server does not administer, arriving in a record that
/// is meant to be shown on a page and written into a log.
/// <c>ConflictRecordTests</c> refuses a member of any type outside the declared set, so the
/// rule is a property of the type rather than a habit of whoever adds the next field.
/// </para>
///
/// <para>
/// Both readings are ticks or counts rather than the field's own type, because one record type
/// holds all four rows. What a number means is decided by <see cref="Field"/>: a played state is
/// zero or one, a play count is the count, a position is its ticks, and a last played date is
/// the ticks of its UTC moment. A record type per field would be four types that differ in one
/// property and four places to forget a rule; a record carrying the value as text would be the
/// free text this type refuses.
/// </para>
///
/// <para>
/// What this does not decide: nothing writes one yet. The first condition of #36 asks that every
/// branch of the resolver write a record, and the three rules on the mainline are reached
/// independently with nothing driving them together, so there is no caller to hold to that. The
/// fourth condition asks that a record survive a restart, and the store that would carry it is
/// on the mainline while the document shape for this record is not written here. Both are named
/// in #36 rather than answered by this type.
/// </para>
/// </summary>
public sealed class ConflictRecord
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictRecord"/> class.
    /// </summary>
    /// <param name="pairingId">The pairing the conflict arose on.</param>
    /// <param name="mappedUserId">The local user the mapping named.</param>
    /// <param name="itemId">The local item the values were about.</param>
    /// <param name="field">The moved field the two readings are of.</param>
    /// <param name="rule">The rule that decided.</param>
    /// <param name="here">What this server held, or <c>null</c> where it held nothing.</param>
    /// <param name="atThePeer">What the peer offered, or <c>null</c> where it offered nothing.</param>
    /// <param name="discarded">Which side's reading did not survive.</param>
    /// <param name="recordedAt">When the rule decided.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="discarded"/> names a side that held no reading. A side cannot lose a value
    /// it never had, and a record saying it did is one an operator would read as a value this
    /// plugin threw away.
    /// </exception>
    public ConflictRecord(
        Guid pairingId,
        Guid mappedUserId,
        Guid itemId,
        SyncedField field,
        ConflictRule rule,
        long? here,
        long? atThePeer,
        ConflictSide discarded,
        DateTimeOffset recordedAt)
    {
        if (discarded == ConflictSide.Here && here is null)
        {
            throw new ArgumentException(
                "This server is recorded as having lost a reading and held none, so there is nothing the record says was discarded.",
                nameof(discarded));
        }

        if (discarded == ConflictSide.AtThePeer && atThePeer is null)
        {
            throw new ArgumentException(
                "The peer is recorded as having lost a reading and offered none, so there is nothing the record says was discarded.",
                nameof(discarded));
        }

        PairingId = pairingId;
        MappedUserId = mappedUserId;
        ItemId = itemId;
        Field = field;
        Rule = rule;
        Here = here;
        AtThePeer = atThePeer;
        Discarded = discarded;
        RecordedAt = recordedAt;
    }

    /// <summary>
    /// Gets the pairing the conflict arose on.
    /// </summary>
    public Guid PairingId { get; }

    /// <summary>
    /// Gets the local user the mapping named.
    /// </summary>
    public Guid MappedUserId { get; }

    /// <summary>
    /// Gets the local item the two readings were about.
    /// </summary>
    public Guid ItemId { get; }

    /// <summary>
    /// Gets the moved field the two readings are of.
    /// </summary>
    public SyncedField Field { get; }

    /// <summary>
    /// Gets the rule that decided, which is the row of the conflict table this conflict was
    /// answered by.
    /// </summary>
    public ConflictRule Rule { get; }

    /// <summary>
    /// Gets what this server held, read as <see cref="Field"/> decides, or <c>null</c> where it
    /// held nothing.
    /// </summary>
    public long? Here { get; }

    /// <summary>
    /// Gets what the peer offered, read as <see cref="Field"/> decides, or <c>null</c> where it
    /// offered nothing.
    /// </summary>
    public long? AtThePeer { get; }

    /// <summary>
    /// Gets the side whose reading did not survive, which is
    /// <see cref="ConflictSide.Neither"/> where the rule discards nothing.
    /// </summary>
    public ConflictSide Discarded { get; }

    /// <summary>
    /// Gets the moment the rule decided, which the caller supplies rather than this type reading
    /// a clock, because a rule in this plugin reads the injected clock and nothing else.
    /// </summary>
    public DateTimeOffset RecordedAt { get; }
}
