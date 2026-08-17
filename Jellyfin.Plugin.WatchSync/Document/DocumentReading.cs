namespace Jellyfin.Plugin.WatchSync.Document;

/// <summary>
/// What one attempt to read a document in this plugin's store came back with.
///
/// A refused document is one this type holds nothing of. There is no member on it carrying the
/// document that was refused, so a caller that meant to write it back has nothing to write, and
/// the second rule of #69 is a property of the type rather than a discipline somebody keeps.
/// </summary>
public sealed class DocumentReading
{
    private DocumentReading(
        DocumentAnswer answer,
        StoredDocument? document,
        int? foundVersion,
        int expectedVersion)
    {
        Answer = answer;
        Document = document;
        FoundVersion = foundVersion;
        ExpectedVersion = expectedVersion;
    }

    /// <summary>
    /// Gets what the document turned out to be.
    /// </summary>
    public DocumentAnswer Answer { get; }

    /// <summary>
    /// Gets the document, or null where it was refused or was never one.
    /// </summary>
    public StoredDocument? Document { get; }

    /// <summary>
    /// Gets the version the document carried, or null where it carried none.
    ///
    /// It is here for the refusal rather than for the reader. #69 asks that a refusal say what
    /// it found and what it expected, and #62 is the status page that says it, so both numbers
    /// leave this type as numbers rather than as a sentence somebody assembled here.
    /// </summary>
    public int? FoundVersion { get; }

    /// <summary>
    /// Gets the version this code writes, which is what the reading expected.
    /// </summary>
    public int ExpectedVersion { get; }

    /// <summary>
    /// Gets a value indicating whether this reading refuses the document.
    ///
    /// Refusing stops the pairing the document belongs to rather than the plugin. A store
    /// holding one document from the future is not a reason to stop syncing every other
    /// pairing on the server, and #46 is where doing nothing and saying why is argued.
    /// </summary>
    public bool IsRefused =>
        Answer is DocumentAnswer.FromTheFuture or DocumentAnswer.NotADocument;

    internal static DocumentReading Current(StoredDocument document, int expectedVersion) =>
        new DocumentReading(DocumentAnswer.Current, document, document.Version, expectedVersion);

    internal static DocumentReading OlderThanThisCode(
        StoredDocument document,
        int expectedVersion) =>
        new DocumentReading(
            DocumentAnswer.OlderThanThisCode,
            document,
            document.Version,
            expectedVersion);

    internal static DocumentReading FromTheFuture(int foundVersion, int expectedVersion) =>
        new DocumentReading(DocumentAnswer.FromTheFuture, null, foundVersion, expectedVersion);

    internal static DocumentReading NotADocument(int expectedVersion) =>
        new DocumentReading(DocumentAnswer.NotADocument, null, null, expectedVersion);
}
