using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.WatchSync.Api;

/// <summary>
/// What this plugin's store holds about one person, in a form that can be read and handed over,
/// which is the word #74 uses.
///
/// The document is carried as the JSON the store keeps rather than as a shape declared here. A
/// second shape would be a second answer to what a record is, and it would go stale against the
/// record that decides: a field added to a stored document would be held by this plugin and
/// absent from what somebody was told is everything.
/// </summary>
public sealed class HeldRecordsReport
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HeldRecordsReport"/> class.
    /// </summary>
    /// <param name="mappedUserId">The person the report is about.</param>
    /// <param name="records">The records.</param>
    public HeldRecordsReport(Guid mappedUserId, IReadOnlyList<HeldRecord> records)
    {
        MappedUserId = mappedUserId;
        Records = records;
    }

    /// <summary>
    /// Gets the person the report is about, as this server names them.
    /// </summary>
    public Guid MappedUserId { get; }

    /// <summary>
    /// Gets how many records this plugin holds about them.
    /// </summary>
    public int Count => Records.Count;

    /// <summary>
    /// Gets the records.
    /// </summary>
    public IReadOnlyList<HeldRecord> Records { get; }
}
