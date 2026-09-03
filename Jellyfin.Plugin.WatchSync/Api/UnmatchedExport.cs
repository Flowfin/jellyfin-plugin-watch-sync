using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.WatchSync.Api;

/// <summary>
/// The unmatched items of one pairing for one person, whole, which is #62's third condition.
///
/// The status shows a count and the top reasons; this is every entry, because the fix for most
/// unmatched items is metadata work an operator does elsewhere and a count is not a work list.
/// It is the unmatched record read out entry by entry and never a second walk, so what an
/// operator exports is what the matcher recorded.
/// </summary>
public sealed class UnmatchedExport
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnmatchedExport"/> class.
    /// </summary>
    /// <param name="pairingId">The pairing.</param>
    /// <param name="mappedUserId">The person, as this server names them.</param>
    /// <param name="reading">What the read of the record came back with.</param>
    /// <param name="items">Every unmatched item, in the record's own order.</param>
    public UnmatchedExport(
        Guid pairingId,
        Guid mappedUserId,
        RecordReading reading,
        IReadOnlyList<UnmatchedExportEntry> items)
    {
        PairingId = pairingId;
        MappedUserId = mappedUserId;
        Reading = reading;
        Items = items;
    }

    /// <summary>
    /// Gets the pairing.
    /// </summary>
    public Guid PairingId { get; }

    /// <summary>
    /// Gets the person, as this server names them.
    /// </summary>
    public Guid MappedUserId { get; }

    /// <summary>
    /// Gets what the read of the record came back with. An empty list beside
    /// <see cref="RecordReading.Unreadable"/> is not a library where everything matched.
    /// </summary>
    public RecordReading Reading { get; }

    /// <summary>
    /// Gets how many items are in the export.
    /// </summary>
    public int Count => Items.Count;

    /// <summary>
    /// Gets every unmatched item, in the record's own order.
    /// </summary>
    public IReadOnlyList<UnmatchedExportEntry> Items { get; }
}
