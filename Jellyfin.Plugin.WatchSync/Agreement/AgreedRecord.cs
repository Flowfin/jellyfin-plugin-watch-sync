using System;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Agreement;

/// <summary>
/// What this server and one peer last agreed about one mapped user and one leaf item.
///
/// Without it there are two current values and no history, and the only rule available over two
/// current values is to overwrite. #14 is where that is argued and the prior art is where it is
/// visible: a tool that always sends its own user data to the other server overwrites whatever
/// was there, and it cannot do anything else, because it has nothing to tell a local change from
/// a value that arrived from the peer three minutes ago.
///
/// It holds the three things that question needs and nothing else: the state as both sides last
/// agreed it, when the agreement was reached by this server's clock, and the version of the
/// envelope that carried it. The envelope version is here rather than derivable because a record
/// agreed under an older version was agreed about the fields that version carried, so a reader
/// asking why a field never moved has the answer in the record instead of in a release history.
///
/// The moment is a parameter and never a clock this type reads. Waiting and reading time are on
/// the injected clock in #86, which the <c>injected-clock</c> invariant refuses a departure from.
///
/// One record per subject is the whole of #14's fourth condition, and it is a property of
/// <see cref="AgreedRecords"/> rather than of this type: an evening of playback agrees the same
/// subject repeatedly and replaces one entry each time.
/// </summary>
public sealed class AgreedRecord
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AgreedRecord"/> class.
    /// </summary>
    /// <param name="subject">The mapped user and the leaf item the agreement is about.</param>
    /// <param name="agreed">The state as both sides last agreed it.</param>
    /// <param name="agreedAt">When the agreement was reached, by this server's clock.</param>
    /// <param name="envelopeVersion">The version of the envelope that carried it.</param>
    /// <exception cref="ArgumentNullException">The subject or the state is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The envelope version is not a whole number above zero, or the state carries a position or
    /// a play count below zero. An agreement is a reading of what two servers settled on, and
    /// neither side produces either of those, so a record carrying one is a caller that assembled
    /// a state rather than one that agreed it.
    /// </exception>
    public AgreedRecord(
        TransferSubject subject,
        SyncedState agreed,
        DateTimeOffset agreedAt,
        int envelopeVersion)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(agreed);
        ArgumentOutOfRangeException.ThrowIfLessThan(envelopeVersion, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(agreed.PlaybackPositionTicks, nameof(agreed));
        ArgumentOutOfRangeException.ThrowIfNegative(agreed.PlayCount, nameof(agreed));

        Subject = subject;
        Agreed = agreed;
        AgreedAt = agreedAt;
        EnvelopeVersion = envelopeVersion;
    }

    /// <summary>
    /// Gets the mapped user and the leaf item this agreement is about.
    ///
    /// The pairing is not part of it. A record sits in <see cref="AgreedRecords"/>, which is per
    /// pairing and per mapped user, so carrying the pairing here would be a second copy of a
    /// fact the collection already decides, and two copies of one fact disagree.
    /// </summary>
    public TransferSubject Subject { get; }

    /// <summary>
    /// Gets the state as both sides last agreed it.
    /// </summary>
    public SyncedState Agreed { get; }

    /// <summary>
    /// Gets when the agreement was reached, by this server's clock.
    ///
    /// By this server's clock, which is what makes it usable without the tolerated skew #32
    /// bounds: it orders this server's own agreements against each other and is never compared
    /// against a moment a peer stamped.
    /// </summary>
    public DateTimeOffset AgreedAt { get; }

    /// <summary>
    /// Gets the version of the envelope that carried the agreement.
    /// </summary>
    public int EnvelopeVersion { get; }
}
