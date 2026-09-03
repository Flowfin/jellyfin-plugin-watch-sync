using System;
using System.Collections.Generic;

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
/// last confirmed; what the last run did is the run record the scheduled sweep keeps; the
/// unmatched count and its reasons are the unmatched record; the conflicts are the conflict
/// record; and whether a run was stopped by the cap is the plan the stop recorded. Whether the
/// peer is unreachable and the last refusal with its reason have no record yet, and no member
/// here pretends to one: a number invented for display is the thing #62's own rule refuses. The
/// queue depth is not here because there is no queue, which was decided on #47 and #48.
/// </para>
///
/// <para>
/// The sweep run is the one member that is the server's rather than the pairing's. The sweep
/// walks the records the store holds rather than pairs today, so one run is over every pairing
/// and every person at once and every status answers the same run; <see cref="LastSweepStatus"/>
/// says so and says what a restart does to it.
/// </para>
///
/// <para>
/// The envelope versions this server speaks are the other server-wide member, and they are the
/// end of a closure #18 asks for: the supported set is declared in one place, and the refusal,
/// the negotiation and the dashboard read that one place. This is what the dashboard reads, and
/// it is the declaration itself rather than a copy of it, so a test can hold the two to be one
/// object. An operator holding two servers that refuse each other's envelopes reads it on both
/// to learn which of them to move.
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
    /// <param name="lastSweep">What the last sweep did.</param>
    /// <param name="unmatched">How many items produced no match, and why.</param>
    /// <param name="conflicts">How many conflicts are recorded.</param>
    /// <param name="envelopeVersionsSpoken">The envelope versions this server speaks, as declared.</param>
    public SyncStatus(
        Guid pairingId,
        Guid mappedUserId,
        StoppedRunStatus stoppedRun,
        LastExchangeStatus lastExchange,
        LastSweepStatus lastSweep,
        UnmatchedStatus unmatched,
        ConflictStatus conflicts,
        IReadOnlyList<int> envelopeVersionsSpoken)
    {
        PairingId = pairingId;
        MappedUserId = mappedUserId;
        StoppedRun = stoppedRun;
        LastExchange = lastExchange;
        LastSweep = lastSweep;
        Unmatched = unmatched;
        Conflicts = conflicts;
        EnvelopeVersionsSpoken = envelopeVersionsSpoken;
    }

    /// <summary>
    /// Gets a value indicating whether something on this status needs an operator rather than
    /// being a line among others: a run the cap stopped, a sweep that stopped short, or a record
    /// that could not be read.
    ///
    /// It is first and it is derived, so a page reads one member before anything else and
    /// cannot show a stopped run as a row somewhere below the counts. That is #62's second
    /// condition and #38's fifth. A sweep that stopped short is in it for the reason
    /// <c>SweepRun</c> was written: its counts look like a run that finished, and an operator
    /// reading them alone concludes that everything was looked at.
    /// </summary>
    public bool NeedsAttention =>
        StoppedRun.Reading != RecordReading.Absent
        || LastSweep.StoppedShort
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
    /// Gets what the last sweep did, from the run record the sweep keeps. It is the server's run
    /// and not this pairing's.
    /// </summary>
    public LastSweepStatus LastSweep { get; }

    /// <summary>
    /// Gets how many items produced no match and why, from the unmatched record.
    /// </summary>
    public UnmatchedStatus Unmatched { get; }

    /// <summary>
    /// Gets how many conflicts are recorded, from the conflict record.
    /// </summary>
    public ConflictStatus Conflicts { get; }

    /// <summary>
    /// Gets the envelope versions this server speaks, oldest first, from the one place they are
    /// declared. It is this server's and not the pairing's, and a peer refusing an envelope names
    /// its own set the same way, so the two can be compared.
    /// </summary>
    public IReadOnlyList<int> EnvelopeVersionsSpoken { get; }
}
