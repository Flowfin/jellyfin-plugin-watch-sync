namespace Jellyfin.Plugin.WatchSync.Records;

/// <summary>
/// What reading one document as the conflicts recorded for a pairing and a mapped user came
/// back with.
/// </summary>
public sealed class ConflictRecordsReading
{
    private ConflictRecordsReading(ConflictRecordsAnswer answer, ConflictRecords? records)
    {
        Answer = answer;
        Records = records;
    }

    /// <summary>
    /// Gets what the document turned out to be.
    /// </summary>
    public ConflictRecordsAnswer Answer { get; }

    /// <summary>
    /// Gets the records, where the document is one.
    /// </summary>
    public ConflictRecords? Records { get; }

    /// <summary>
    /// Gets a value indicating whether the document was refused.
    /// </summary>
    public bool IsRefused => Answer is not ConflictRecordsAnswer.Readable;

    /// <summary>
    /// A document that is a record of conflicts.
    /// </summary>
    /// <param name="records">What it holds.</param>
    /// <returns>The reading.</returns>
    internal static ConflictRecordsReading Readable(ConflictRecords records) =>
        new ConflictRecordsReading(ConflictRecordsAnswer.Readable, records);

    /// <summary>
    /// A document that is not a record of conflicts.
    /// </summary>
    /// <returns>The reading.</returns>
    internal static ConflictRecordsReading NotARecordOfConflicts() =>
        new ConflictRecordsReading(ConflictRecordsAnswer.NotARecordOfConflicts, null);
}
