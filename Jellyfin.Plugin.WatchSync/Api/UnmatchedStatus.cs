using System.Collections.Generic;

namespace Jellyfin.Plugin.WatchSync.Api;

/// <summary>
/// How many items produced no match, with the reasons that account for most of them, read from
/// the unmatched record.
///
/// The count is the record's own count and the reasons are grouped out of the record's own
/// entries, so the number on the page is the number the matcher wrote and never a second walk
/// made for display. What that count is bounded by is the record's own cap, and a library with
/// more unmatchable items than the cap shows the cap; <c>docs/unmatched.md</c> says so.
/// </summary>
public sealed class UnmatchedStatus
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnmatchedStatus"/> class.
    /// </summary>
    /// <param name="reading">What the read of the record came back with.</param>
    /// <param name="count">How many items are recorded as unmatched.</param>
    /// <param name="reasons">The reasons, most frequent first, at most the top few.</param>
    public UnmatchedStatus(RecordReading reading, int count, IReadOnlyList<UnmatchedReasonCount> reasons)
    {
        Reading = reading;
        Count = count;
        Reasons = reasons;
    }

    /// <summary>
    /// Gets what the read of the record came back with.
    /// </summary>
    public RecordReading Reading { get; }

    /// <summary>
    /// Gets how many items are recorded as unmatched, which is zero where the record is absent
    /// or unreadable and says nothing in the second case.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Gets the reasons that account for the most unmatched items, most frequent first.
    /// </summary>
    public IReadOnlyList<UnmatchedReasonCount> Reasons { get; }
}
