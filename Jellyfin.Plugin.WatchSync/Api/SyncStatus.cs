using System;

namespace Jellyfin.Plugin.WatchSync.Api;

/// <summary>
/// The sync status of one pairing for one mapped user, which is #62.
///
/// Every number here is read from the record the code uses and never counted separately for
/// display, so the surface cannot disagree with the behaviour. Each section says what its read
/// came back with, because a record that could not be read and a record that is not there are
/// different facts and both would read as zero otherwise.
///
/// <para>
/// What is shown is what has a record. The moment of the last exchange is the point the peer
/// last confirmed; the unmatched count and its reasons are the unmatched record; the conflicts
/// are the conflict record; and whether a run was stopped by the cap is the plan the stop
/// recorded. What the last exchange did, whether the peer is unreachable, and the last refusal
/// with its reason have no record yet, and no member here pretends to one: a number invented for
/// display is the thing #62's own rule refuses. The queue depth is not here because there is no
/// queue, which was decided on #47 and #48.
/// </para>
///
/// <para>
/// It carries no title, no path and no text that arrived from a peer. Identifiers, counts,
/// moments and the names of enumerations, which is what makes it safe to show beside a person
/// and to write into a log.
/// </para>
/// </summary>
public sealed class SyncStatus
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SyncStatus"/> class.
    /// </summary>
    /// <param name="pairingId">The pairing.</param>
    /// <param name="mappedUserId">The person, as this server names them.</param>
    /// <param name="stoppedRun">Whether a run was stopped by the cap.</param>
    /// <param name="lastExchange">When the last exchange ran.</param>
    /// <param name="unmatched">How many items produced no match, and why.</param>
    /// <param name="conflicts">How many conflicts are recorded.</param>
    public SyncStatus(
        Guid pairingId,
        Guid mappedUserId,
        StoppedRunStatus stoppedRun,
        LastExchangeStatus lastExchange,
        UnmatchedStatus unmatched,
        ConflictStatus conflicts)
    {
        PairingId = pairingId;
        MappedUserId = mappedUserId;
        StoppedRun = stoppedRun;
        LastExchange = lastExchange;
        Unmatched = unmatched;
        Conflicts = conflicts;
    }

    /// <summary>
    /// Gets a value indicating whether something on this status needs an operator rather than
    /// being a line among others: a run the cap stopped, or a record that could not be read.
    ///
    /// It is first and it is derived, so a page reads one member before anything else and
    /// cannot show a stopped run as a row somewhere below the counts. That is #62's second
    /// condition and #38's fifth.
    /// </summary>
    public bool NeedsAttention =>
        StoppedRun.Reading != RecordReading.Absent
        || LastExchange.Reading == RecordReading.Unreadable
        || Unmatched.Reading == RecordReading.Unreadable
        || Conflicts.Reading == RecordReading.Unreadable;

    /// <summary>
    /// Gets the pairing.
    /// </summary>
    public Guid PairingId { get; }

    /// <summary>
    /// Gets the person, as this server names them.
    /// </summary>
    public Guid MappedUserId { get; }

    /// <summary>
    /// Gets whether a run was stopped by the cap, from the plan the stop recorded.
    /// </summary>
    public StoppedRunStatus StoppedRun { get; }

    /// <summary>
    /// Gets when the last exchange ran, from the point the peer last confirmed.
    /// </summary>
    public LastExchangeStatus LastExchange { get; }

    /// <summary>
    /// Gets how many items produced no match and why, from the unmatched record.
    /// </summary>
    public UnmatchedStatus Unmatched { get; }

    /// <summary>
    /// Gets how many conflicts are recorded, from the conflict record.
    /// </summary>
    public ConflictStatus Conflicts { get; }
}
