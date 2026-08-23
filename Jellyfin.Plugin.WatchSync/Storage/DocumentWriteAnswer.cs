using Jellyfin.Plugin.WatchSync.Document;

namespace Jellyfin.Plugin.WatchSync.Storage;

/// <summary>
/// What a write into this plugin's store answered, and the document that stands on disk after it.
///
/// A refusal carries no document, for the reason <see cref="DocumentReading"/> carries none: a
/// caller that was handed something back on a refusal has something to write, and the next thing
/// it writes is the state the refusal existed to protect.
/// </summary>
public sealed class DocumentWriteAnswer
{
    private DocumentWriteAnswer(DocumentWriteOutcome outcome, StoredDocument? document)
    {
        Outcome = outcome;
        Document = document;
    }

    /// <summary>
    /// Gets what the attempt came back with.
    /// </summary>
    public DocumentWriteOutcome Outcome { get; }

    /// <summary>
    /// Gets the document that is on disk, or null where the attempt was refused.
    /// </summary>
    public StoredDocument? Document { get; }

    /// <summary>
    /// Gets a value indicating whether the attempt was refused.
    ///
    /// Refusing stops the write and nothing else. The document that was there is still there and
    /// is still readable, so a caller that retries later is retrying against a store in the state
    /// it was in before, rather than against one this attempt half moved.
    /// </summary>
    public bool IsRefused => Outcome != DocumentWriteOutcome.Written;

    internal static DocumentWriteAnswer Written(StoredDocument document) =>
        new DocumentWriteAnswer(DocumentWriteOutcome.Written, document);

    internal static DocumentWriteAnswer RefusedByADocumentFromTheFuture() =>
        new DocumentWriteAnswer(DocumentWriteOutcome.RefusedByADocumentFromTheFuture, null);

    internal static DocumentWriteAnswer RefusedByTheFilesystem() =>
        new DocumentWriteAnswer(DocumentWriteOutcome.RefusedByTheFilesystem, null);
}
