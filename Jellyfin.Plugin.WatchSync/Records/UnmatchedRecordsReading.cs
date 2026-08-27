namespace Jellyfin.Plugin.WatchSync.Records;

/// <summary>
/// What reading one document as the unmatched items recorded for a pairing and a mapped user
/// came back with.
/// </summary>
public sealed class UnmatchedRecordsReading
{
    private UnmatchedRecordsReading(UnmatchedRecordsAnswer answer, UnmatchedRecords? records)
    {
        Answer = answer;
        Records = records;
    }

    /// <summary>
    /// Gets what the document turned out to be.
    /// </summary>
    public UnmatchedRecordsAnswer Answer { get; }

    /// <summary>
    /// Gets the records, where the document is one.
    /// </summary>
    public UnmatchedRecords? Records { get; }

    /// <summary>
    /// Gets a value indicating whether the document was refused.
    /// </summary>
    public bool IsRefused => Answer is not UnmatchedRecordsAnswer.Readable;

    /// <summary>
    /// A document that is a record of unmatched items.
    /// </summary>
    /// <param name="records">What it holds.</param>
    /// <returns>The reading.</returns>
    internal static UnmatchedRecordsReading Readable(UnmatchedRecords records) =>
        new UnmatchedRecordsReading(UnmatchedRecordsAnswer.Readable, records);

    /// <summary>
    /// A document that is not a record of unmatched items.
    /// </summary>
    /// <returns>The reading.</returns>
    internal static UnmatchedRecordsReading NotARecordOfUnmatchedItems() =>
        new UnmatchedRecordsReading(UnmatchedRecordsAnswer.NotARecordOfUnmatchedItems, null);
}
