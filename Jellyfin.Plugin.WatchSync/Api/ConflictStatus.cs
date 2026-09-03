using System;

namespace Jellyfin.Plugin.WatchSync.Api;

/// <summary>
/// How many conflicts have been resolved and recorded, read from the conflict record.
///
/// The count is the record's own and it is bounded by the record's own retention and cap, so
/// what it answers is how many decisions are still in the record rather than how many were ever
/// taken. The newest moment beside it is what tells an operator whether those decisions are
/// recent.
/// </summary>
public sealed class ConflictStatus
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictStatus"/> class.
    /// </summary>
    /// <param name="reading">What the read of the record came back with.</param>
    /// <param name="count">How many conflicts the record holds.</param>
    /// <param name="newestRecordedAt">When the most recent of them was decided, where there is one.</param>
    public ConflictStatus(RecordReading reading, int count, DateTimeOffset? newestRecordedAt)
    {
        Reading = reading;
        Count = count;
        NewestRecordedAt = newestRecordedAt;
    }

    /// <summary>
    /// Gets what the read of the record came back with.
    /// </summary>
    public RecordReading Reading { get; }

    /// <summary>
    /// Gets how many conflicts the record holds, which is zero where the record is absent or
    /// unreadable and says nothing in the second case.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Gets when the most recent conflict was decided, or null where there is none.
    /// </summary>
    public DateTimeOffset? NewestRecordedAt { get; }
}
