namespace Jellyfin.Plugin.WatchSync.Storage;

/// <summary>
/// What one attempt to write a document into this plugin's store came back with.
///
/// The three are separated because their repairs are different, and a caller that collapsed any
/// pair of them would report the wrong one to an operator. A refusal is never a partial write:
/// whichever of the two refusals is answered, the document on disk is the one that was there
/// before the attempt, which is the whole of #70's third condition.
/// </summary>
public enum DocumentWriteOutcome
{
    /// <summary>
    /// The document on disk is the one that was handed over, whole.
    /// </summary>
    Written,

    /// <summary>
    /// A document written by a version this code does not know was already there, and nothing
    /// was written over it.
    ///
    /// This is #69's refusal reaching the write path rather than a second decision about it.
    /// A reader that refuses a document from the future and a writer that overwrites one are
    /// the same defect seen from two sides: the fields the newer version needed are gone
    /// either way, and the write is the side that destroys them.
    /// </summary>
    RefusedByADocumentFromTheFuture,

    /// <summary>
    /// The filesystem refused the bytes, so nothing was replaced.
    ///
    /// A full disk is the case this exists for and it is the one that decides the shape of the
    /// write: the bytes go to a file beside the document and the document is replaced only once
    /// all of them are down, so a write that runs out of room leaves the previous document
    /// where it was rather than half of a new one in its place.
    /// </summary>
    RefusedByTheFilesystem,
}
