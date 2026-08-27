namespace Jellyfin.Plugin.WatchSync.Records;

/// <summary>
/// What reading one document as the provenance recorded for a pairing and a mapped user came
/// back with.
/// </summary>
public sealed class ProvenanceRecordsReading
{
    private ProvenanceRecordsReading(ProvenanceRecordsAnswer answer, ProvenanceRecords? records)
    {
        Answer = answer;
        Records = records;
    }

    /// <summary>
    /// Gets what the document turned out to be.
    /// </summary>
    public ProvenanceRecordsAnswer Answer { get; }

    /// <summary>
    /// Gets the records, where the document is one.
    /// </summary>
    public ProvenanceRecords? Records { get; }

    /// <summary>
    /// Gets a value indicating whether the document was refused.
    /// </summary>
    public bool IsRefused => Answer is not ProvenanceRecordsAnswer.Readable;

    /// <summary>
    /// A document that is a record of provenance.
    /// </summary>
    /// <param name="records">What it holds.</param>
    /// <returns>The reading.</returns>
    internal static ProvenanceRecordsReading Readable(ProvenanceRecords records) =>
        new ProvenanceRecordsReading(ProvenanceRecordsAnswer.Readable, records);

    /// <summary>
    /// A document that is not a record of provenance.
    /// </summary>
    /// <returns>The reading.</returns>
    internal static ProvenanceRecordsReading NotARecordOfProvenance() =>
        new ProvenanceRecordsReading(ProvenanceRecordsAnswer.NotARecordOfProvenance, null);
}
