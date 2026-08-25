namespace Jellyfin.Plugin.WatchSync.Agreement;

/// <summary>
/// What reading one document as an agreed record came back with.
/// </summary>
public sealed class AgreedRecordsReading
{
    private AgreedRecordsReading(AgreedRecordsAnswer answer, AgreedRecords? records)
    {
        Answer = answer;
        Records = records;
    }

    /// <summary>
    /// Gets what the document turned out to be.
    /// </summary>
    public AgreedRecordsAnswer Answer { get; }

    /// <summary>
    /// Gets the record, where the document is one.
    /// </summary>
    public AgreedRecords? Records { get; }

    /// <summary>
    /// Gets a value indicating whether the document was refused.
    /// </summary>
    public bool IsRefused => Answer is not AgreedRecordsAnswer.Readable;

    /// <summary>
    /// A document that is an agreed record.
    /// </summary>
    /// <param name="records">What it holds.</param>
    /// <returns>The reading.</returns>
    internal static AgreedRecordsReading Readable(AgreedRecords records) =>
        new AgreedRecordsReading(AgreedRecordsAnswer.Readable, records);

    /// <summary>
    /// A document that is not an agreed record.
    /// </summary>
    /// <returns>The reading.</returns>
    internal static AgreedRecordsReading NotAnAgreedRecord() =>
        new AgreedRecordsReading(AgreedRecordsAnswer.NotAnAgreedRecord, null);
}
