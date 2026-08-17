namespace Jellyfin.Plugin.WatchSync.Document;

/// <summary>
/// What reading the version off a document in this plugin's store answered.
///
/// The four are what is left once a version is taken as something to be decided on rather
/// than something to be assumed. Two of them are readable, one is refused, and one is not a
/// document at all, and collapsing any pair of them is how a reader starts guessing.
/// </summary>
public enum DocumentAnswer
{
    /// <summary>
    /// The document carries the version this code writes, so it is read as it stands.
    /// </summary>
    Current,

    /// <summary>
    /// The document is older than the version this code writes.
    ///
    /// It is read after being carried forward one version at a time, which is #71. It is a
    /// separate answer from <see cref="Current"/> rather than a detail of it, because a reader
    /// that treated an old document as current would read fields that moved and miss fields
    /// that arrived, and it would do so silently.
    /// </summary>
    OlderThanThisCode,

    /// <summary>
    /// The document was written by a version this code does not know, and it is refused.
    ///
    /// An operator installs an older version over a newer one, restores a backup taken after
    /// an upgrade, or moves a data folder between two servers on different versions. The
    /// quiet failure this refusal exists for is the reader that goes ahead: it ignores the
    /// fields it does not know, writes the document back without them, and destroys the state
    /// the newer version needed. So nothing is read out of the document and nothing is
    /// written back into it, and the two version numbers are carried out so the refusal can
    /// say what it found and what it expected.
    /// </summary>
    FromTheFuture,

    /// <summary>
    /// The bytes are not a versioned document of this plugin's.
    ///
    /// Not readable as an object, or readable and carrying no version, or carrying one that is
    /// not a whole number above zero. It is distinct from <see cref="FromTheFuture"/> because
    /// the repairs differ: a document from the future is repaired by running the version that
    /// wrote it, and a file that is not one of these documents is repaired by finding out what
    /// put it there. Reading it as version zero, or as the oldest version this code knows,
    /// would turn every truncated write and every foreign file into an upgrade.
    /// </summary>
    NotADocument,
}
