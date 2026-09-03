using System;

namespace Jellyfin.Plugin.WatchSync.Api;

/// <summary>
/// When the last exchange ran, read from the point the peer last confirmed in the agreed record.
///
/// A pairing that has never exchanged and a pairing whose last exchange was three weeks ago are
/// the two cases a status surface exists to separate, and a moment alone collapses them onto
/// whatever the empty value happens to be. So the state is carried beside the moment, which is
/// what the watermark's own reading answers.
/// </summary>
public sealed class LastExchangeStatus
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LastExchangeStatus"/> class.
    /// </summary>
    /// <param name="reading">What the read of the agreed record came back with.</param>
    /// <param name="hasEverExchanged">Whether the two sides have confirmed a point at all.</param>
    /// <param name="confirmedAt">When this server confirmed the last point, where it has.</param>
    /// <param name="agreedItems">How many items the two sides have agreed, where the record was read.</param>
    public LastExchangeStatus(
        RecordReading reading,
        bool hasEverExchanged,
        DateTimeOffset? confirmedAt,
        int? agreedItems)
    {
        Reading = reading;
        HasEverExchanged = hasEverExchanged;
        ConfirmedAt = confirmedAt;
        AgreedItems = agreedItems;
    }

    /// <summary>
    /// Gets what the read of the agreed record came back with.
    /// </summary>
    public RecordReading Reading { get; }

    /// <summary>
    /// Gets a value indicating whether the two sides have confirmed a point at all. False is a
    /// pairing still in its first exchange, or one that has never run.
    /// </summary>
    public bool HasEverExchanged { get; }

    /// <summary>
    /// Gets when this server confirmed the last point, or null where it never has.
    /// </summary>
    public DateTimeOffset? ConfirmedAt { get; }

    /// <summary>
    /// Gets how many items the two sides have agreed, or null where the record was not read.
    /// </summary>
    public int? AgreedItems { get; }
}
