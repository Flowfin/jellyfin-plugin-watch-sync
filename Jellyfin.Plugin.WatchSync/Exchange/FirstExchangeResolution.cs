using System;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Exchange;

/// <summary>
/// What a first exchange answered for one item, and what each side would have to change to
/// hold it.
///
/// The two change flags are carried out rather than left to a caller to work out, because the
/// third condition of #37 is a property of a run rather than of a rule: a second exchange after
/// a first one moves nothing, and what makes that true is that the state agreed here is the
/// state both sides then hold. A caller comparing the resolved state against each side for
/// itself would be a second implementation of that comparison, and the two would agree with
/// each other rather than with the record.
/// </summary>
public sealed class FirstExchangeResolution
{
    private FirstExchangeResolution(
        TransferSubject subject,
        ResolutionAnswer answer,
        SyncedState? resolved,
        UndecidedReason? reason,
        bool changesHere,
        bool changesAtThePeer)
    {
        Subject = subject;
        Answer = answer;
        Resolved = resolved;
        Reason = reason;
        ChangesHere = changesHere;
        ChangesAtThePeer = changesAtThePeer;
    }

    /// <summary>
    /// Gets the mapped user and the leaf item this is about.
    /// </summary>
    public TransferSubject Subject { get; }

    /// <summary>
    /// Gets what the exchange answered.
    /// </summary>
    public ResolutionAnswer Answer { get; }

    /// <summary>
    /// Gets the state both sides agree on, or null where there is none.
    ///
    /// It is null for both of the other two answers and for different reasons: an undecided
    /// item has no answer at all, and an item an earlier run agreed has one that this run did
    /// not produce and may not report as its own.
    /// </summary>
    public SyncedState? Resolved { get; }

    /// <summary>
    /// Gets why the item was left standing, or null where it was not.
    /// </summary>
    public UndecidedReason? Reason { get; }

    /// <summary>
    /// Gets a value indicating whether this server holds something other than the resolved
    /// state, which is what the apply path in #50 would write here.
    /// </summary>
    public bool ChangesHere { get; }

    /// <summary>
    /// Gets a value indicating whether the peer holds something other than the resolved state.
    ///
    /// Data is pulled, so this server writes nothing into the peer's records. What this says is
    /// that the peer's own first exchange, reading the same two states, would find something to
    /// write, which is what makes the pair converge rather than one side chasing the other.
    /// </summary>
    public bool ChangesAtThePeer { get; }

    /// <summary>
    /// Gets a value indicating whether this run decided the item.
    /// </summary>
    public bool IsDecided => Answer == ResolutionAnswer.Decided;

    /// <summary>
    /// An item the table answered, with the state both sides agree on.
    /// </summary>
    /// <param name="item">The item and the two readings.</param>
    /// <param name="resolved">The state the table answered with.</param>
    /// <returns>The resolution.</returns>
    /// <exception cref="ArgumentNullException">Either argument is absent.</exception>
    internal static FirstExchangeResolution Decided(ItemOnBothSides item, SyncedState resolved)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(resolved);

        return new FirstExchangeResolution(
            item.Subject,
            ResolutionAnswer.Decided,
            resolved,
            null,
            !Same(resolved, item.Here),
            !Same(resolved, item.AtThePeer));
    }

    /// <summary>
    /// An item the table did not answer, which stays as it is on both sides.
    /// </summary>
    /// <param name="item">The item and the two readings.</param>
    /// <param name="reason">What it was left standing for.</param>
    /// <returns>The resolution.</returns>
    /// <exception cref="ArgumentNullException">The item is absent.</exception>
    internal static FirstExchangeResolution Undecided(ItemOnBothSides item, UndecidedReason reason)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new FirstExchangeResolution(
            item.Subject,
            ResolutionAnswer.Undecided,
            null,
            reason,
            false,
            false);
    }

    /// <summary>
    /// An item an earlier run of this same first exchange already agreed.
    /// </summary>
    /// <param name="item">The item and the two readings.</param>
    /// <returns>The resolution.</returns>
    /// <exception cref="ArgumentNullException">The item is absent.</exception>
    internal static FirstExchangeResolution AlreadyAgreed(ItemOnBothSides item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new FirstExchangeResolution(
            item.Subject,
            ResolutionAnswer.AlreadyAgreed,
            null,
            null,
            false,
            false);
    }

    private static bool Same(SyncedState one, SyncedState other) =>
        one.Played == other.Played
        && one.PlayCount == other.PlayCount
        && one.PlaybackPositionTicks == other.PlaybackPositionTicks
        && one.LastPlayedDate == other.LastPlayedDate;
}
