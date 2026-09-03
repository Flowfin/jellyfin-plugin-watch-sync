using System;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Records;

/// <summary>
/// One item a stopped run was about to write: what it had decided, and what this server held at
/// the moment the run stopped.
///
/// Both halves are carried and the second is the one that makes an approval safe. #38's third
/// condition asks that an approved plan apply exactly what it recorded, including nothing that
/// changed in the meantime without being noticed, and the only way to notice is to have written
/// down what was there. An approval compares what this server holds now against this and sets
/// the item aside where the two differ, rather than recomputing anything: a plan recomputed at
/// approval is a second run, and the operator approved the first.
///
/// <para>
/// What was held is recorded as a reading and not as a value. A server holding no record for an
/// item and a server holding a record saying the person never watched it are different states,
/// which is <see cref="RecordedValue"/>'s rule, so an absence travels as an absence. And a read
/// this server refused at the moment the run stopped is recorded as unread rather than as
/// nothing, because a plan claiming to know what was there when it did not would be approved
/// against a baseline nobody measured.
/// </para>
/// </summary>
public sealed class StoppedRunItem
{
    private StoppedRunItem(
        TransferSubject subject,
        SyncedState decided,
        SyncedState? held,
        bool heldWasRead)
    {
        Subject = subject;
        Decided = decided;
        Held = held;
        HeldWasRead = heldWasRead;
    }

    /// <summary>
    /// Gets the mapped user and the leaf item the run was about to write.
    /// </summary>
    public TransferSubject Subject { get; }

    /// <summary>
    /// Gets the state the conflict table decided on, which is what an approval writes.
    /// </summary>
    public SyncedState Decided { get; }

    /// <summary>
    /// Gets what this server held when the run stopped, or null where it held nothing or where
    /// the reading was refused. <see cref="HeldWasRead"/> separates the two.
    /// </summary>
    public SyncedState? Held { get; }

    /// <summary>
    /// Gets a value indicating whether what this server held was read at all.
    ///
    /// False is a plan that cannot say what was there, and an approval sets such an item aside
    /// rather than writing it, because the comparison the third condition of #38 asks for has
    /// no baseline to be made against.
    /// </summary>
    public bool HeldWasRead { get; }

    /// <summary>
    /// An item whose current state was read when the run stopped.
    /// </summary>
    /// <param name="subject">The mapped user and the leaf item.</param>
    /// <param name="decided">The state the run was about to write.</param>
    /// <param name="held">What this server held, or null where it held nothing.</param>
    /// <returns>The item.</returns>
    /// <exception cref="ArgumentNullException">The subject or the decided state is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The decided state carries a position or a play count below zero, which no conflict rule
    /// produces and which a plan would otherwise carry into an approval.
    /// </exception>
    public static StoppedRunItem Read(TransferSubject subject, SyncedState decided, SyncedState? held)
    {
        Refuse(subject, decided);

        return new StoppedRunItem(subject, decided, held, true);
    }

    /// <summary>
    /// An item whose current state could not be read when the run stopped.
    /// </summary>
    /// <param name="subject">The mapped user and the leaf item.</param>
    /// <param name="decided">The state the run was about to write.</param>
    /// <returns>The item.</returns>
    /// <exception cref="ArgumentNullException">The subject or the decided state is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The decided state carries a position or a play count below zero.
    /// </exception>
    public static StoppedRunItem Unread(TransferSubject subject, SyncedState decided)
    {
        Refuse(subject, decided);

        return new StoppedRunItem(subject, decided, null, false);
    }

    /// <summary>
    /// Whether two readings of what a server holds are the same reading.
    ///
    /// Two absences are the same, an absence and a state are not, and two states are the same
    /// where every moved field is. It is here rather than on the state itself because the
    /// question is about readings, and an absent reading is the case the state type has no
    /// value for.
    /// </summary>
    /// <param name="one">One reading.</param>
    /// <param name="other">The other.</param>
    /// <returns>Whether they are the same.</returns>
    public static bool SameReading(SyncedState? one, SyncedState? other)
    {
        if (one is null || other is null)
        {
            return one is null && other is null;
        }

        return one.Played == other.Played
            && one.PlayCount == other.PlayCount
            && one.PlaybackPositionTicks == other.PlaybackPositionTicks
            && one.LastPlayedDate == other.LastPlayedDate;
    }

    private static void Refuse(TransferSubject subject, SyncedState decided)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(decided);
        ArgumentOutOfRangeException.ThrowIfNegative(decided.PlaybackPositionTicks, nameof(decided));
        ArgumentOutOfRangeException.ThrowIfNegative(decided.PlayCount, nameof(decided));
    }
}
